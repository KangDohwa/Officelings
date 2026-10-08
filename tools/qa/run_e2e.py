"""Implementation of e2e.ps1. All builds, generated data and logs live under TEMP."""
import argparse
import csv
import datetime as dt
import json
import math
import struct
import os
from pathlib import Path
import re
import shutil
import statistics
import subprocess
import sys
import tempfile
import time

from check_snapshot import check_expectation, documented_version, hold_reader, read_json, timestamp, validate_snapshot

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools/simulate"))
from simulate import atomic_json, load_scenario, safe_temp_path

CREATE_FLAGS = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0


def command(args, cwd, env, log, timeout=120):
    with Path(log).open("w", encoding="utf-8") as output:
        output.write("Command: " + subprocess.list2cmdline([str(a) for a in args]) + "\n")
        output.flush()
        result = subprocess.Popen([str(a) for a in args], cwd=cwd, env=env, stdout=output,
                                  stderr=subprocess.STDOUT, creationflags=CREATE_FLAGS)
        try:
            result.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            stop(result)
            raise
    if result.returncode:
        raise RuntimeError("exit %s; see %s" % (result.returncode, log))


def start(args, cwd, env, log):
    output = Path(log).open("w", encoding="utf-8")
    try:
        process = subprocess.Popen([str(a) for a in args], cwd=cwd, env=env, stdout=output,
                                   stderr=subprocess.STDOUT, creationflags=CREATE_FLAGS)
        return process
    finally:
        output.close()


def stop(process):
    if process is not None and process.poll() is None:
        # Only processes launched by this run. /T also stops dotnet run's apphost child.
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=CREATE_FLAGS)
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            raise RuntimeError("Owned process did not stop: " + str(process.pid))


def row(results, scope, check, status, detail):
    value = dict(scope=scope, check=check, status=status, detail=str(detail))
    results.append(value)
    print("[%s] %s / %s: %s" % (status, scope, check, detail), flush=True)


def tracker_args(worktree, home, output, spec, speed, once=False, direct=False, tracker_binary=None):
    leave_minutes = str(spec.get("gone_seconds", 160) / speed / 60)
    args = ["dotnet", "run", "--disable-build-servers", "--project", str(worktree / "src/Officelings.Tracker"),
            "--no-build", "--no-restore", "--", "--home", str(home), "--output", str(output),
            "--asset-dir", str(worktree / "assets/pack"),
            "--layout", str(output / "layout.json"),
            "--working-seconds", str(spec.get("working_seconds", 40) / speed),
            "--leave-after-idle-minutes", leave_minutes,
            "--leave-after-waiting-minutes", leave_minutes,
            "--scan-seconds", "0.25", "--interval-seconds", "0.2"]
    if direct or tracker_binary:
        args = [str(tracker_binary or worktree / "src/Officelings.Tracker/bin/Debug/net10.0/Officelings.Tracker.exe")] + args[args.index("--") + 1:]
    return args + (["--once"] if once else [])


def wpf_smoke(binary, home, state, assets, output, cwd, env, log):
    snapshot = read_json(state)
    command([binary, "--home", home, "--output", state, "--asset-dir", assets,
             "--qa", "--smoke-test", output], cwd, env, log, 90)
    # Exit success alone does not prove that the supplied snapshot was rendered.
    evidence = (output / "smoke.txt").read_text(encoding="utf-8-sig")
    if ("sourceSequence=%s" % snapshot["sequence"] not in evidence or
            "agents=%s" % len(snapshot["agents"]) not in evidence or "logBytesRead=0" not in evidence):
        raise AssertionError("WPF smoke did not confirm the selected published snapshot")
    for zoom in (1, 2, 3):
        header = (output / ("office-%dx.png" % zoom)).read_bytes()[:24]
        expected = (snapshot["office"]["widthTiles"] * 16 * zoom + 32,
                    snapshot["office"]["heightTiles"] * 16 * zoom + 32)
        if header[:8] != b"\x89PNG\r\n\x1a\n" or len(header) != 24 or struct.unpack(">II", header[16:24]) != expected:
            raise AssertionError("Missing or incorrectly sized WPF %dx capture" % zoom)


def performance(samples, results, name):
    # Discard startup for CPU/handle trend; peak RSS includes startup.
    steady = [s for s in samples if s["elapsed"] >= 20]
    if len(steady) < 6:
        row(results, name, "resource window", "FAIL", "Need at least six samples after 20 s warmup; reduce speed")
        return
    first, last = steady[0], steady[-1]
    active_wall = last["elapsed"] - first["elapsed"] - (last["snapshot_pause_seconds"] - first["snapshot_pause_seconds"])
    if active_wall <= 0 or (name == "quiet" and active_wall < 40):
        row(results, name, "resource window", "FAIL", "Need a positive active CPU interval; quiet requires at least 40 measured seconds")
        return
    cpu_seconds = last["cpu_seconds"] - first["cpu_seconds"]
    cpu_one_core_percent = 100 * cpu_seconds / active_wall
    cpu_limit = .5 if name == "quiet" else 3
    logical_processors = os.cpu_count()
    cpu_machine_percent = cpu_one_core_percent / logical_processors
    peak = max(s["rss_mb"] for s in samples)
    # A Windows File handle count is integer and watcher reads can briefly overlap.
    baseline = statistics.median(s["file_handles"] for s in steady[:3])
    final = statistics.median(s["file_handles"] for s in steady[-3:])
    peak_files = max(s["file_handles"] for s in steady)
    row(results, name, "CPU average", "PASS" if cpu_one_core_percent <= cpu_limit else "FAIL",
        "cpuOneCorePercent=%.3f%% (limit %g%%), cpuMachinePercent=%.3f%%, cpuSeconds=%.6f, wallSeconds=%.6f, logicalProcessors=%d; 100*cpuSeconds/wallSeconds" %
        (cpu_one_core_percent, cpu_limit, cpu_machine_percent, cpu_seconds, active_wall, logical_processors))
    row(results, name, "RSS peak", "PASS" if peak <= 200 else "FAIL", "%.1f MiB (limit 200 MiB)" % peak)
    row(results, name, "open File handles", "PASS" if final <= baseline else "FAIL",
        "median first/last=%g/%g, peak=%g; no median growth" % (baseline, final, peak_files))
    return dict(cpuOneCorePercent=cpu_one_core_percent, cpuMachinePercent=cpu_machine_percent,
                cpuSeconds=cpu_seconds, measuredWallSeconds=active_wall, logicalProcessors=logical_processors,
                cpuLimitPercent=cpu_limit, warmupSeconds=20, sampleCount=len(steady), peakRssMiB=peak,
                fileHandlesFirstMedian=baseline, fileHandlesLastMedian=final, fileHandlesPeak=peak_files,
                formula="100 * cpuSeconds / wallSeconds; one logical core; no observer subtraction")


def quiet_tail(args, worktree, folder, spec, env, version, results):
    """Measure a fresh direct tracker on the completed quiet scenario's same home."""
    from win_metrics import children, sample
    home, output = folder / "home", folder / "tracker"
    process, samples = None, []
    try:
        expected = read_json(home / "done.json")
        (output / "state.json").unlink(missing_ok=True)
        invocation = tracker_args(worktree, home, output, spec, args.speed, direct=True,
                                  tracker_binary=getattr(args, "tracker_binary", None))
        invocation += ["--seconds", str(args.quiet_steady_seconds + 90)]
        process = start(invocation, worktree, env, folder / "tracker-quiet-tail.log")
        ready_until = time.monotonic() + 60
        while not (output / "state.json").exists():
            if process.poll() is not None or time.monotonic() >= ready_until:
                raise RuntimeError("Quiet tail did not publish an initial state within 60 seconds")
            time.sleep(.05)
        pid = next(p for p, exe in children(process.pid) if exe.lower() == "officelings.tracker.exe")
        begin, previous_sequence, steady_begin = time.monotonic(), None, None
        ticks = list(range(0, args.quiet_steady_seconds, 5)) + [args.quiet_steady_seconds]
        for tick in ticks:
            deadline = begin + tick
            if tick == ticks[-1] and steady_begin is not None:
                deadline = max(deadline, steady_begin + args.quiet_steady_seconds - 20) + .1
            time.sleep(max(0, deadline - time.monotonic()))
            if process.poll() is not None:
                raise RuntimeError("Quiet tail tracker exited before measurement completed")
            elapsed = time.monotonic() - begin
            measurement = sample(pid, home / ".officelings-synthetic-home")
            if elapsed >= 20 and steady_begin is None:
                steady_begin = begin + elapsed
            snapshot = read_json(output / "state.json")
            validate_snapshot(snapshot, version, output)
            heartbeat_age = time.time() - timestamp(snapshot["generatedAt"])
            if previous_sequence is not None and snapshot["sequence"] <= previous_sequence:
                raise AssertionError("Quiet tail publication sequence did not advance")
            if elapsed >= 20 and not -.5 <= heartbeat_age <= 4:
                raise AssertionError("Quiet tail heartbeat is stale or in the future: %.3f seconds" % heartbeat_age)
            errors = check_expectation(snapshot, expected)
            if errors:
                raise AssertionError("Quiet tail correctness: " + "; ".join(errors))
            previous_sequence = snapshot["sequence"]
            live_agents = sum(v["agent"]["state"] != "gone" for v in snapshot["agents"])
            if elapsed >= 20 and live_agents:
                raise AssertionError("Quiet steady window requires zero live agents")
            samples.append(dict(elapsed=elapsed, snapshot_pause_seconds=0, **measurement,
                                sequence=snapshot["sequence"], heartbeat_age_seconds=heartbeat_age,
                                live_agents=live_agents))
        metrics = performance(samples, results, "quiet")
        if metrics is None:
            return
        steady = [value for value in samples if value["elapsed"] >= 20]
        metrics.update(firstSequence=steady[0]["sequence"], lastSequence=steady[-1]["sequence"],
                       writesPerSecond=(steady[-1]["sequence"]-steady[0]["sequence"])/metrics["measuredWallSeconds"],
                       maxLiveAgents=max(value["live_agents"] for value in steady),
                       maxHeartbeatAgeSeconds=max(value["heartbeat_age_seconds"] for value in steady),
                       note="Same completed synthetic home, fresh direct tracker; external process metrics; no observer subtraction. Scenario correctness failures remain in results.")
        atomic_json(folder / "cpu-summary.json", metrics)
        row(results, "quiet", "steady publication and correctness", "PASS",
            "sequence %s -> %s; 20 s warmup + %.3f s measured; fresh heartbeat and final scenario intent checked" %
            (samples[0]["sequence"], samples[-1]["sequence"], metrics["measuredWallSeconds"]))
    except (OSError, ValueError, KeyError, AssertionError, RuntimeError, StopIteration) as error:
        row(results, "quiet", "steady resource window", "FAIL", error)
    finally:
        stop(process)
        if samples:
            with (folder / "quiet-tail-samples.csv").open("w", newline="", encoding="utf-8") as stream:
                writer = csv.DictWriter(stream, fieldnames=list(samples[0]))
                writer.writeheader()
                writer.writerows(samples)


def run_scenario(value, args, worktree, run_root, env, renderer, version, results, full_renderer):
    from win_metrics import children, sample, suspended
    spec, _ = load_scenario(value)
    name = spec["name"]
    folder = run_root / ("%02d-" % sum(1 for p in run_root.iterdir() if p.is_dir() and (p / "scenario.json").exists()) + re.sub(r"[^a-zA-Z0-9_-]", "_", name))
    folder.mkdir()
    atomic_json(folder / "scenario.json", spec)
    home, control, output = folder / "home", folder / "control", folder / "tracker"
    control.mkdir()
    simulation = tracker = None
    samples, warnings, seen, seq = [], set(), set(), 0
    best_state = None
    metrics_error = None
    started = time.monotonic()
    deadline = started + (args.soak_cap_seconds if name == "long-soak" else max(90, spec["duration"] / args.speed + 60))
    startup_state = folder / "startup-state.json"
    selected_state = folder / "render-state.json"
    pause_seconds = 0

    def live_snapshot(pid):
        nonlocal pause_seconds
        before = time.monotonic()
        with suspended(pid):
            snapshot = read_json(output / "state.json")
        pause_seconds += time.monotonic() - before
        return snapshot

    def launch_tracker(log):
        (output / "state.json").unlink(missing_ok=True)
        process = start(tracker_args(worktree, home, output, spec, args.speed, direct=args.direct_tracker,
                                     tracker_binary=getattr(args, "tracker_binary", None)), worktree, env, log)
        try:
            ready_until = time.monotonic() + 60
            while not (output / "state.json").exists():
                if process.poll() is not None or time.monotonic() >= ready_until:
                    raise RuntimeError("Tracker startup failed; see " + str(log))
                time.sleep(.05)
            apphost = next(p for p, exe in children(process.pid) if exe.lower() == "officelings.tracker.exe")
            return process, apphost
        except Exception:
            stop(process)
            raise
    try:
        scenario_path = Path(value) if Path(value).is_file() else REPO / "tools/simulate/scenarios" / (value + ".json")
        simulation = start([sys.executable, REPO / "tools/simulate/simulate.py", "--scenario", scenario_path,
                            "--home", home, "--control-dir", control, "--speed", args.speed, "--seed", args.seed],
                           REPO, env, folder / "simulator.log")
        while not (control / "checkpoint-000.json").exists():
            if simulation.poll() is not None or time.monotonic() - started > 30:
                raise RuntimeError("Simulator startup failed; see simulator.log")
            time.sleep(.05)
        # Obtain an actual tracker-produced sequence 1 for the existing renderer suite,
        # whose int64 mutation test specifically searches for '"sequence": 1'.
        command(tracker_args(worktree, home, output, spec, args.speed, once=True, direct=args.direct_tracker,
                             tracker_binary=getattr(args, "tracker_binary", None)), worktree, env,
                folder / "tracker-once.log", 60)
        initial = read_json(output / "state.json")
        validate_snapshot(initial, version, output, verify_hashes=True)
        shutil.copyfile(output / "state.json", startup_state)
        if full_renderer:
            tracker, probe_pid = launch_tracker(folder / "tracker-reader-probe.log")
            probe_before = live_snapshot(probe_pid)["sequence"]
            with hold_reader(output / "state.json"):
                time.sleep(.6)  # Longer than two normal publication intervals.
            try:
                tracker.wait(timeout=2)
                row(results, name, "live shared-reader publisher", "FAIL",
                    "tracker exited %s while a FILE_SHARE_READ|WRITE|DELETE reader was open; see tracker-reader-probe.log" % tracker.returncode)
            except subprocess.TimeoutExpired:
                probe_after = live_snapshot(probe_pid)["sequence"]
                row(results, name, "live shared-reader publisher", "PASS" if probe_after > probe_before else "FAIL",
                    "tracker survived; publication sequence %s -> %s" % (probe_before, probe_after))
            stop(tracker)
        tracker, pid = launch_tracker(folder / "tracker.log")
        next_sample = time.monotonic() + 5
        schema_checks = 0
        observation_log = (folder / "observations.jsonl").open("w", encoding="utf-8")
        try:
            while True:
                now = time.monotonic()
                if now >= deadline:
                    raise TimeoutError("Scenario exceeded wall-clock cap; long-soak maximum is 600 s")
                if tracker.poll() is not None:
                    raise RuntimeError("Tracker exited early; see tracker.log")
                if now >= next_sample:
                    if pid is None:
                        metrics_error = "Could not identify tracker apphost PID"
                    elif metrics_error is None:
                        try:
                            measurement = sample(pid, home / ".officelings-synthetic-home")
                            samples.append(dict(elapsed=now - started, snapshot_pause_seconds=pause_seconds, **measurement))
                        except (OSError, StopIteration) as error:
                            metrics_error = str(error)
                    next_sample += 5  # Never sample the dotnet build/run wrapper instead.
                cp_path = control / ("checkpoint-%03d.json" % len(seen))
                cp = read_json(cp_path) if cp_path.exists() else None
                if cp is not None:
                    snapshot = live_snapshot(pid)
                    # Simulator freezes writes. Retry only to allow one scan/publish to settle.
                    until = time.monotonic() + 2
                    errors = check_expectation(snapshot, cp, warnings)
                    while errors and time.monotonic() < until:
                        time.sleep(.1)
                        snapshot = live_snapshot(pid)
                        validate_snapshot(snapshot, version, output)
                        warnings.update(snapshot["warnings"])
                        errors = check_expectation(snapshot, cp, warnings)
                    if snapshot["sequence"] < seq:
                        raise AssertionError("Snapshot sequence went backwards within a run")
                    seq = snapshot["sequence"]
                    validate_snapshot(snapshot, version, output)
                    schema_checks += 1
                    warnings.update(snapshot["warnings"])
                    live = sum(v["agent"]["state"] != "gone" for v in snapshot["agents"])
                    observation_log.write(json.dumps(dict(sequence=seq, generatedAt=snapshot["generatedAt"],
                        live=live, warnings=snapshot["warnings"])) + "\n")
                    if best_state is None or live > best_state:
                        best_state = live
                        atomic_json(selected_state, snapshot)
                    atomic_json(folder / ("checkpoint-%03d-state.json" % cp["number"]), snapshot)
                    atomic_json(folder / ("checkpoint-%03d-expected.json" % cp["number"]), cp)
                    row(results, name, cp["label"], "FAIL" if errors else "PASS", "; ".join(errors) if errors else cp["counts"])
                    (control / ("ack-%03d" % cp["number"])).write_text("checked\n")
                    seen.add(cp["number"])
                if simulation.poll() is not None:
                    if simulation.returncode:
                        raise RuntimeError("Simulator exited nonzero; see simulator.log")
                    if len(seen) != len(spec.get("checkpoints", [])) + 1:
                        raise RuntimeError("Not all checkpoints were checked")
                    final_intent = read_json(home / "done.json")
                    snapshot = live_snapshot(pid)
                    validate_snapshot(snapshot, version, output)
                    errors = check_expectation(snapshot, final_intent, warnings)
                    row(results, name, "final parents and ended sessions", "FAIL" if errors else "PASS",
                        "; ".join(errors) if errors else "no working agent past end + timeout")
                    break
                time.sleep(.05)
        finally:
            observation_log.close()
        row(results, name, "schema and PNG hashes", "PASS", "%d snapshots; documented v%d; initial PNG hashes checked" % (schema_checks, version))
        if metrics_error:
            row(results, name, "resource probe", "FAIL", metrics_error)
        elif name == "long-soak":
            metrics = performance(samples, results, name)
            if metrics:
                atomic_json(folder / "cpu-summary.json", metrics)
        if samples:
            with (folder / "samples.csv").open("w", newline="", encoding="utf-8") as stream:
                writer = csv.DictWriter(stream, fieldnames=list(samples[0]))
                writer.writeheader()
                writer.writerows(samples)
    except (OSError, ValueError, KeyError, AssertionError, RuntimeError, subprocess.TimeoutExpired) as error:
        row(results, name, "execution", "FAIL", error)
    finally:
        stop(simulation)
        stop(tracker)
        if samples and not (folder / "samples.csv").exists():
            atomic_json(folder / "samples-partial.json", samples)
    if renderer and startup_state.exists():
        if full_renderer:
            try:
                command([renderer, startup_state, output, folder / "renderer-contract"], worktree, env,
                        folder / "renderer-contract.log", 90)
                row(results, name, "Windhawk contract harness", "PASS", "actual tracker sequence-1 snapshot; offscreen")
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                row(results, name, "Windhawk contract harness", "FAIL", error)
        if selected_state.exists():
            try:
                command([renderer, selected_state, output, folder / "renderer", "--render-only"], worktree, env,
                        folder / "renderer.log", 60)
                row(results, name, "Windhawk scenario render", "PASS", "peak live snapshot; offscreen PNGs")
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                row(results, name, "Windhawk scenario render", "FAIL", error)
    return folder


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", nargs="+", default=["quiet", "busy-12", "burst-spawn", "flaky-writer", "long-soak"])
    parser.add_argument("--speed", type=float, default=20)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--soak-cap-seconds", type=int, default=600)
    parser.add_argument("--quiet-steady-seconds", type=int, default=0,
                        help="After quiet correctness/render checks, run a fresh direct tracker on the same fake home for 20 s warmup plus at least 40 s measurement (0 disables; 60..600 seconds)")
    parser.add_argument("--renderer-tests", help="Read-only use of an already compiled renderer_tests.exe")
    parser.add_argument("--direct-tracker", action="store_true", help="Run the fresh worktree apphost directly if SDK launch is blocked")
    parser.add_argument("--workspace", nargs="?", const=str(REPO), type=Path,
                        help="Read current sources (including uncommitted changes); build only under TEMP; no git worktree operations")
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("The .NET Desktop/Windhawk/process metrics QA requires Windows")
    if not math.isfinite(args.speed) or args.speed <= 0 or not 30 <= args.soak_cap_seconds <= 600:
        parser.error("Require positive finite speed and soak cap in 30..600 seconds")
    if args.quiet_steady_seconds and not 60 <= args.quiet_steady_seconds <= 600:
        parser.error("Quiet steady seconds must be 0 or 60..600")
    for value in args.scenario:
        load_scenario(value)
    temp = safe_temp_path(Path(tempfile.gettempdir()) / "officelings-e2e")
    temp.mkdir(exist_ok=True)
    run_root = Path(tempfile.mkdtemp(prefix=dt.datetime.now().strftime("%Y%m%d-%H%M%S-"), dir=temp))
    worktree = args.workspace.resolve(strict=True) if args.workspace else run_root / "head"
    results = []
    env = dict(os.environ, LOCALAPPDATA=str(run_root / "localappdata"), APPDATA=str(run_root / "appdata"),
               DOTNET_CLI_HOME=str(run_root / "dotnet-home"), DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
               DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0",
               MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_UI_LANGUAGE="en")
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="false")
    attached = False
    renderer = None
    print("Logs: " + str(run_root), flush=True)
    try:
        revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True, creationflags=CREATE_FLAGS).strip()
        atomic_json(run_root / "run.json", dict(head=revision, workspace=str(worktree) if args.workspace else None,
                                               includes_uncommitted_changes=bool(args.workspace), scenarios=args.scenario, speed=args.speed,
                                               seed=args.seed, direct_tracker=args.direct_tracker,
                                               quiet_steady_seconds=args.quiet_steady_seconds,
                                               started_at=dt.datetime.now(dt.timezone.utc).isoformat()))
        if not args.workspace:
            command(["git", "worktree", "add", "--detach", worktree, revision], REPO, env, run_root / "worktree-add.log")
            attached = True
        build_flags = ["--nologo", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false", "-p:NuGetAudit=false"]
        if args.workspace:
            build_flags += ["-c", "Release", "--artifacts-path", str(run_root / "build"), "-p:RestoreSources=",
                            "-p:RestorePackagesPath=" + str(run_root / "packages")]
            args.tracker_binary = run_root / "build/bin/Officelings.Tracker/release/Officelings.Tracker.exe"
        command(["dotnet", "build", worktree / "src/Officelings.Tracker", *build_flags], worktree, env,
                run_root / "build-tracker.log", 180)
        row(results, "workspace" if args.workspace else "HEAD", "tracker build", "PASS",
            revision + ("; current sources; TEMP artifacts" if args.workspace else "; build once in TEMP worktree"))
        if args.direct_tracker:
            row(results, "HEAD", "tracker launch", "SKIP", "SDK run launcher bypassed; using the same newly built apphost directly")
        version = documented_version(worktree / "docs/state-schema.md")
        program = (worktree / "src/Officelings.App/Program.cs").read_text(encoding="utf-8-sig")
        smoke_possible = all(flag in program for flag in ('case "--home"', 'case "--output"', "--smoke-test"))
        if not smoke_possible:
            row(results, "WPF", "fake-home smoke", "SKIP", "Selected source lacks --home/--output snapshot smoke CLI")
        else:
            command(["dotnet", "build", worktree / "src/Officelings.App", *build_flags], worktree, env,
                    run_root / "build-app.log", 180)
            app_binary = (run_root / "build/bin/Officelings.App/release/Officelings.App.exe" if args.workspace else
                          worktree / "src/Officelings.App/bin/Debug/net10.0-windows/Officelings.App.exe")
        try:
            if args.renderer_tests:
                renderer = Path(args.renderer_tests).resolve(strict=True)
                row(results, "Windhawk", "compiled test exe", "PASS", "read-only: " + str(renderer))
            elif args.workspace:
                existing = worktree / "windhawk/artifacts/renderer_tests.exe"
                if existing.is_file():
                    renderer = existing
                    row(results, "Windhawk", "compiled test exe", "PASS", "read-only: " + str(renderer))
                else:
                    row(results, "Windhawk", "compiled test exe", "SKIP", "Workspace mode does not build windhawk; pass --renderer-tests")
            else:
                command(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                         worktree / "windhawk/compile.ps1", "-Tests"], worktree, env, run_root / "build-windhawk.log", 600)
                renderer = worktree / "windhawk/artifacts/renderer_tests.exe"
                for name in ("libc++.whl", "libunwind.whl"):
                    shutil.copyfile(Path(r"C:\ProgramData\Windhawk\Engine\Mods\64") / name, renderer.parent / name)
                row(results, "Windhawk", "test harness build", "PASS", "compile.ps1 -Tests only in disposable HEAD worktree")
        except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
            renderer = None
            row(results, "Windhawk", "test harness build", "FAIL", error)
        for index, value in enumerate(args.scenario):
            folder = run_scenario(value, args, worktree, run_root, env, renderer, version, results, index == 0)
            if smoke_possible and (folder / "render-state.json").exists():
                try:
                    wpf_smoke(app_binary, folder / "home", folder / "render-state.json", folder / "tracker",
                              folder / "wpf-smoke", worktree, env, folder / "wpf-smoke.log")
                    row(results, folder.name, "WPF fake-home smoke", "PASS", "selected tracker snapshot; offscreen 1/2/3x; source sequence/count and PNG sizes verified")
                except (OSError, ValueError, KeyError, AssertionError, RuntimeError, subprocess.TimeoutExpired) as error:
                    row(results, folder.name, "WPF fake-home smoke", "FAIL", error)
            elif smoke_possible:
                row(results, folder.name, "WPF fake-home smoke", "FAIL", "No tracker-produced snapshot available")
            spec, _ = load_scenario(value)
            if spec["name"] == "quiet" and args.quiet_steady_seconds:
                quiet_tail(args, worktree, folder, spec, env, version, results)
    except (OSError, RuntimeError, AssertionError, subprocess.SubprocessError) as error:
        row(results, "setup", "execution", "FAIL", error)
    finally:
        if attached:
            # Resolve and check before git's recursive worktree removal. No live checkout cleanup.
            safe_temp_path(worktree)
            if worktree.parent.resolve() != run_root.resolve():
                raise RuntimeError("Unsafe worktree cleanup path")
            try:
                command(["git", "worktree", "remove", "--force", worktree], REPO, env,
                        run_root / "worktree-remove.log", 120)
                row(results, "cleanup", "TEMP worktree", "PASS", "removed; logs and fake homes retained")
            except (OSError, RuntimeError, subprocess.SubprocessError) as error:
                row(results, "cleanup", "TEMP worktree", "FAIL", error)
    atomic_json(run_root / "results.json", results)
    failures = [r for r in results if r["status"] == "FAIL"]
    atomic_json(run_root / "failures.json", failures)
    print("\nScope          Check                                    Result\n" + "-" * 68)
    for value in results:
        print("%-14s %-40s %s" % (value["scope"][:14], value["check"][:40], value["status"]))
    print("\n%d failed; logs: %s" % (len(failures), run_root), flush=True)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
