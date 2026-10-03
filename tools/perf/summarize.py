"""Summarize sequential repeats; optionally gate allocation/work-count regressions."""
import argparse
from collections import defaultdict
import json
import math
from pathlib import Path
import statistics


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def stats(values):
    values = sorted(values)
    return dict(n=len(values), median=statistics.median(values),
                p95=values[max(0, math.ceil(.95 * len(values)) - 1)], max=values[-1])


def repeats(groups):
    result = []
    for key, rows in sorted(groups.items()):
        metrics = sorted({metric for row in rows for metric in row["metrics"]})
        minimums = {}
        for metric in metrics:
            values = [row["metrics"][metric]["median"] for row in rows if metric in row["metrics"]]
            if len(values) == len(rows):
                minimums[metric] = min(values)
        result.append(dict(key=key, repeatCount=len(rows), complete=len(rows) == 3,
                           minOfMedians=minimums, repeats=rows))
    return result


def summary(root):
    phases, counts, live, startup = defaultdict(list), defaultdict(list), defaultdict(list), defaultdict(list)
    excluded = []
    for file in sorted(root.rglob("live.json")):
        if (file.parent / "excluded.json").exists():
            excluded.append(str(file.relative_to(root)))
            continue
        data = read(file)
        rows = data["samples"]
        if not rows:
            raise ValueError("Empty measurement: " + str(file))
        metrics = defaultdict(list)
        for row in rows:
            wall, cpu, processors = row["wallSeconds"], row["cpuSeconds"], row["logicalProcessors"]
            if wall <= 0 or processors <= 0:
                raise ValueError("Invalid CPU denominator: " + str(file))
            one_core = 100 * cpu / wall
            if not math.isclose(one_core, row["cpuOneCorePercent"], rel_tol=1e-8, abs_tol=1e-8):
                raise ValueError("Incorrect one-core CPU percentage: " + str(file))
            if not math.isclose(one_core / processors, row["cpuMachinePercent"], rel_tol=1e-8, abs_tol=1e-8):
                raise ValueError("Incorrect machine CPU percentage: " + str(file))
            for field in ("cpuOneCorePercent", "cpuMachinePercent", "workingSetBytes", "privateBytes"):
                metrics[field].append(row[field])
            for field in ("allocatedBytes", "logicalRedrawPixelArea"):
                if row.get(field) is not None:
                    metrics[field + "PerSecond"].append(row[field] / wall)
            wakeups = row.get("appScheduledWakeups", row.get("animationWakeups"))
            if wakeups is not None:
                metrics["appScheduledWakeupsPerSecond"].append(wakeups / wall)
            if row.get("sampler"):
                for field in ("milliseconds", "counterMilliseconds", "memoryMilliseconds", "threadMilliseconds", "allocatedBytesCurrentThread"):
                    metrics["sampler." + field].append(row["sampler"][field])
                if row["sampler"]["cpuSecondsUi"] is not None:
                    metrics["sampler.cpuOneCorePercentUi"].append(100 * row["sampler"]["cpuSecondsUi"] / wall)
            for field, value in row["counters"].items():
                if value is not None:
                    metrics[field + "PerSecond"].append(value / wall)
        if data.get("fixture") == "native-idle-activities" and not data.get("externalCollector"):
            moving = [row for row in rows if row["movingVisibleCount"] > 0]
            metrics["movingVisibleCount"] = [row["movingVisibleCount"] for row in rows]
            if moving:
                metrics["movingCpuOneCorePercent"] = [row["cpuOneCorePercent"] for row in moving]
                metrics["movingCpuMachinePercent"] = [row["cpuMachinePercent"] for row in moving]
        else:
            moving = []
        key = "%s/%s" % (data["mode"], data["count"])
        if data.get("fixture") == "native-idle-activities":
            key += "/mixed-activities"
        elif data.get("fixture") == "empty-home-real-polling":
            key += "/real-polling"
        elif data.get("fixture") == "observer-only-blank":
            key += "/observer-control"
        if data.get("threadCpuEnabled"):
            key += "/thread-diagnostic"
        wall, cpu = sum(row["wallSeconds"] for row in rows), sum(row["cpuSeconds"] for row in rows)
        target = data.get("target")
        if target:
            wall, cpu = target["measuredWallSeconds"], target["cpuSeconds"]
            for field in ("allocatedBytes", "appScheduledWakeups", "logicalRedrawPixelArea"):
                if target.get(field) is not None:
                    metrics[field + "PerSecond"] = [target[field] / wall]
            for field, value in target["counters"].items():
                if value is not None:
                    metrics[field + "PerSecond"] = [value / wall]
        live[key].append(dict(file=str(file.relative_to(root)), warmupSeconds=data["warmupSeconds"],
                              measuredWallSeconds=wall, cpuSeconds=cpu, logicalProcessors=data["logicalProcessors"],
                              aggregateCpuOneCorePercent=100 * cpu / wall,
                              aggregateCpuMachinePercent=100 * cpu / wall / data["logicalProcessors"],
                              movingSamples=len(moving), samples=len(rows),
                              observedTrackerPollTimestamps=len({row["trackerLastPollAt"] for row in rows + [data.get("warm", {}), target or {}] if row.get("trackerLastPollAt")}),
                              collector="external" if target else "internal observer included",
                              uiCpuSeconds=target.get("uiCpuSeconds") if target else None,
                              otherThreadCpuSeconds=target.get("otherThreadCpuSeconds") if target else None,
                              movingAnimationTicks=target.get("movingAnimationTicks") if target else None,
                              maximumMovingVisible=target.get("maximumMovingVisible") if target else None,
                              metrics={field: stats(values) for field, values in metrics.items() if len(values) == len(rows) or field.startswith("movingCpu") or target}))
    for file in sorted(root.rglob("phases-*.json")):
        data = read(file)
        for row in data["rows"]:
            key = "%s/%s" % (data["count"], row["phase"])
            phases[key].append(dict(file=str(file.relative_to(root)), iterations=row["iterations"],
                                   warmupIterations=row["warmupIterations"],
                                   metrics=dict(milliseconds=stats(row["milliseconds"]),
                                                allocatedBytesCurrentThread=stats(row["allocatedBytesCurrentThread"]))))
    for file in sorted(root.rglob("counts-*.json")):
        if (file.parent / "excluded.json").exists():
            excluded.append(str(file.relative_to(root)))
            continue
        data = read(file)
        metrics = dict(coreAllocatedBytesPerUpdate=stats(data["coreAllocatedBytesCurrentThread"]),
                       viewAllocatedBytesPerUpdate=stats(data["viewAllocatedBytesCurrentThread"]),
                       processAllocatedBytesPerUpdate=stats([data["allocatedBytes"] / data["iterations"]]),
                       logicalRedrawPixelAreaPerUpdate=stats([data["logicalRedrawPixelArea"] / data["iterations"]]))
        for field, value in data["counters"].items():
            if value is not None:
                metrics[field] = stats([value])
        key = "%s/%s" % (data["count"], data.get("fixture", "settled-working-default"))
        counts[key].append(dict(file=str(file.relative_to(root)), iterations=data["iterations"],
                                              warmupIterations=data["warmupIterations"], metrics=metrics))
    for file in sorted(root.rglob("startup.json")):
        data = read(file)
        metrics = {field: stats([data[field]]) for field in
                   ("loadOfficeAsyncMs", "constructorMs", "firstContentRenderedMs", "processStartToFirstContentRenderedMs",
                    "decodedAtLoad", "decodedAfterConstructor", "decodedAtFirstFrame")}
        startup["normal-offscreen"].append(dict(file=str(file.relative_to(root)), metrics=metrics))
    result = dict(cpuFormula="cpuOneCorePercent = 100 * cpuSeconds / wallSeconds; cpuMachinePercent = cpuOneCorePercent / logicalProcessors",
                  reduction="Minimum of the three per-repeat medians; raw samples and each median/p95/max retained; timing is descriptive, not a regression gate",
                  phases=repeats(phases), counts=repeats(counts), live=repeats(live), startup=repeats(startup), excluded=excluded)
    all_groups = result["phases"] + result["counts"] + result["live"] + result["startup"]
    result["complete"] = bool(all_groups) and all(row["complete"] for row in all_groups)
    return result


def regression(baseline, current, max_ratio):
    checks = []
    fields = dict(phases=("allocatedBytesCurrentThread",),
                  counts=("coreAllocatedBytesPerUpdate", "viewAllocatedBytesPerUpdate", "processAllocatedBytesPerUpdate",
                          "logicalRedrawPixelAreaPerUpdate", "SceneRenderCount", "RoomBuildCount",
                          "Core.WorkplaceAssignmentCount", "Core.FurnitureRefreshCount"),
                  live=("allocatedBytesPerSecond", "appScheduledWakeupsPerSecond", "logicalRedrawPixelAreaPerSecond"))
    for section, metrics in fields.items():
        old = {row["key"]: row for row in baseline[section]}
        new = {row["key"]: row for row in current[section]}
        for key, before in old.items():
            if section == "phases" and not key.split("/", 1)[1].startswith("Core."):
                continue  # Rendering moved between Update/OnRender; gate their combined count workload instead.
            after = new.get(key)
            if after is None or not before["complete"] or not after["complete"]:
                checks.append(dict(section=section, key=key, passed=False, reason="Missing condition or not exactly three repeats"))
                continue
            if section == "counts":
                for field in ("iterations", "warmupIterations"):
                    expected = {row[field] for row in before["repeats"]}
                    observed = {row[field] for row in after["repeats"]}
                    checks.append(dict(section=section, key=key, metric=field, baseline=sorted(expected),
                                       current=sorted(observed), passed=len(expected) == 1 and observed == expected))
            for metric in metrics:
                value = before["minOfMedians"].get(metric)
                if value is None:
                    continue  # Unsupported baseline counter is never invented as zero.
                observed = after["minOfMedians"].get(metric)
                # Zero-allocation controls can acquire a small fixed reflection/runtime cost.
                allowance = 128 if section == "phases" and metric == "allocatedBytesCurrentThread" else 0
                limit = value * max_ratio + allowance
                checks.append(dict(section=section, key=key, metric=metric, baseline=value, current=observed,
                                   limit=limit, passed=observed is not None and observed <= limit))
    return dict(passed=bool(checks) and all(row["passed"] for row in checks), maxRatio=max_ratio, checks=checks,
                note="No CPU/wall timing gate. Fixed work counts, thread/process allocations, wakeups and logical damage area only; unavailable baseline counters skipped.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("results", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, help="Previously generated summary.json for count/allocation regression checks")
    parser.add_argument("--max-ratio", type=float, default=1.10)
    args = parser.parse_args()
    if not math.isfinite(args.max_ratio) or args.max_ratio < 1:
        parser.error("--max-ratio must be finite and at least 1")
    try:
        result = summary(args.results)
        if args.baseline:
            result["regression"] = regression(read(args.baseline), result, args.max_ratio)
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(result, stream, indent=2, allow_nan=False)
        for section in ("phases", "counts", "live", "startup"):
            for row in result[section]:
                print("%s %s: %d repeats; min(medians)=%s" % (section, row["key"], row["repeatCount"], row["minOfMedians"]))
        if not result["complete"]:
            print("PARTIAL: at least one condition does not have exactly three successful repeats")
        return 1 if args.baseline and not result["regression"]["passed"] else 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("Summary error: " + str(error))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
