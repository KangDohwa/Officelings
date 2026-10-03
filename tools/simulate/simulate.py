"""Invented provider sessions; Python standard library only. Never reads real sessions."""
import argparse
from contextlib import closing
import datetime as dt
import json
import math
import os
from pathlib import Path
import random
import shutil
import sqlite3
import tempfile
import time
import uuid

PROVIDERS = ("codex", "claude", "antigravity")
MARKER = ".vibedotcom-synthetic-home"
SCENARIOS = Path(__file__).with_name("scenarios")
# Table/column layout inspected through read-only immutable SQLite on 2026-10-04.
# Opaque protobuf fields are deliberately NULL, never borrowed from real sessions.
CONVERSATION_SQL = """
PRAGMA user_version=1;
CREATE TABLE trajectory_meta (trajectory_id text PRIMARY KEY,cascade_id text,trajectory_type integer,source integer);
CREATE TABLE steps (idx integer PRIMARY KEY,step_type integer NOT NULL DEFAULT 0,status integer NOT NULL DEFAULT 0,
 has_subtrajectory numeric NOT NULL DEFAULT false,metadata blob,error_details blob,permissions blob,
 task_details blob,render_info blob,step_payload blob,step_format integer NOT NULL DEFAULT 0);
CREATE TABLE gen_metadata (idx integer PRIMARY KEY,data blob,size integer NOT NULL DEFAULT 0);
CREATE TABLE executor_metadata (idx integer PRIMARY KEY,data blob);
CREATE TABLE parent_references (idx integer PRIMARY KEY,data blob);
CREATE TABLE trajectory_metadata_blob (id text DEFAULT 'main' PRIMARY KEY,data blob);
CREATE TABLE battle_mode_infos (idx integer PRIMARY KEY,data blob);
"""
SUMMARY_SQL = """
PRAGMA user_version=3;
CREATE TABLE conversation_summaries (conversation_id text PRIMARY KEY,title text NOT NULL DEFAULT '',
 preview text NOT NULL DEFAULT '',step_count integer NOT NULL DEFAULT 0,last_modified_time datetime NOT NULL,
 workspace_uris text NOT NULL,status text NOT NULL DEFAULT '',source text NOT NULL DEFAULT '',
 project_id text NOT NULL DEFAULT '',agent_name text NOT NULL DEFAULT '',parent_conversation_id text NOT NULL DEFAULT '',
 nesting_depth integer NOT NULL DEFAULT 0,battle_id text NOT NULL DEFAULT '',winning_conversation_id text NOT NULL DEFAULT '',
 not_fully_idle numeric NOT NULL DEFAULT false,killed numeric NOT NULL DEFAULT false,last_user_input_time datetime NOT NULL,
 last_user_input_step_index integer NOT NULL DEFAULT -1,app_data_dir text NOT NULL DEFAULT '',raw_summary blob,
 group_id text NOT NULL DEFAULT '');
"""


def stamp():
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="microseconds")


def atomic_json(path, value):
    path = Path(path)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")
    os.replace(temporary, path)


def safe_temp_path(path):
    """Restrict every output to TEMP; reject junctions, real homes and application data."""
    path = Path(path).absolute()
    resolved = path.resolve()
    temp = Path(tempfile.gettempdir()).resolve()
    if resolved == temp or not resolved.is_relative_to(temp):
        raise ValueError("Output must be a directory strictly beneath TEMP.")
    for part in (path, *path.parents):
        if part.exists() and (part.is_symlink() or (part.stat().st_file_attributes & 0x400
                                                 if os.name == "nt" else False)):
            raise ValueError("Output must not traverse a symlink or Windows reparse point.")
    real = Path.home().resolve()
    protected = [real / name for name in (".codex", ".claude", ".gemini")]
    protected.append(Path(os.environ.get("LOCALAPPDATA", real / "AppData/Local")) / "Vibedotcom")
    for root in protected:
        root = root.resolve()
        if resolved == root or resolved.is_relative_to(root) or root.is_relative_to(resolved):
            raise ValueError("Output overlaps a protected real provider/application directory.")
    return resolved


def prepare_home(path, reset=False):
    home = safe_temp_path(path)
    if home.exists() and any(home.iterdir()):
        if not reset or not (home / MARKER).is_file() or (home / MARKER).read_text() != "synthetic-only-v1\n":
            raise ValueError("Nonempty home refused; --reset only accepts a simulator-owned home.")
        # Check all existing descendants before recursive removal (including junctions).
        for entry in home.rglob("*"):
            safe_temp_path(entry)
        shutil.rmtree(home)
    home.mkdir(parents=True, exist_ok=True)
    (home / MARKER).write_text("synthetic-only-v1\n")
    for name in (".codex/sessions", ".claude/projects", ".gemini/antigravity/brain"):
        (home / name).mkdir(parents=True)
    return home


def load_scenario(value):
    path = Path(value)
    if not path.is_file():
        path = SCENARIOS / (value + ".json")
    scenario = json.loads(path.read_text(encoding="utf-8"))
    if scenario.get("version") != 1:
        raise ValueError("Scenario version must be 1.")
    duration = scenario["duration"]
    if not isinstance(duration, (int, float)) or not math.isfinite(duration) or duration <= 0:
        raise ValueError("duration must be positive and finite.")
    for provider, count in scenario["supervisors"].items():
        if provider not in PROVIDERS or type(count) is not int or not 0 <= count <= 100:
            raise ValueError("supervisors requires known providers and counts in 0..100.")
    if not 0 < scenario.get("working_seconds", 40) < scenario.get("gone_seconds", 160):
        raise ValueError("Require 0 < working_seconds < gone_seconds.")
    for cp in scenario.get("checkpoints", []):
        if set(cp["counts"]) != set(PROVIDERS) or any(type(n) is not int or n < 0 for n in cp["counts"].values()):
            raise ValueError("Each checkpoint needs independent live counts for all three providers.")
    events = []
    for order, event in enumerate(scenario.get("events", [])):
        at = event["at"]
        every = event.get("every")
        if every is not None and (not math.isfinite(every) or every <= 0):
            raise ValueError("every must be positive and finite.")
        while at <= event.get("until", event["at"]):
            if not math.isfinite(at) or not 0 <= at <= duration:
                raise ValueError("Event is outside duration.")
            events.append(dict(event, at=at, order=order))
            if every is None:
                break
            at += every
    for cp in scenario.get("checkpoints", []):
        if not 0 <= cp["at"] <= duration:
            raise ValueError("Checkpoint is outside duration.")
    return scenario, sorted(events, key=lambda e: (e["at"], e["order"]))


class Simulator:
    def __init__(self, home, scenario, seed=1, speed=1):
        self.home, self.scenario, self.speed = home, scenario, speed
        self.rng = random.Random(seed)
        self.session_rng = random.Random(seed)
        self.sessions = []
        self.pending = []
        self.rotation = 0
        self.summary = home / ".gemini/antigravity/conversation_summaries.db"
        self.summary.parent.mkdir(parents=True, exist_ok=True)
        with closing(sqlite3.connect(self.summary)) as db, db:
            db.executescript(SUMMARY_SQL)

    def uid(self):
        return str(uuid.UUID(int=self.rng.getrandbits(128), version=4))

    def create(self, provider, index, parent=None):
        sid = str(uuid.UUID(int=self.session_rng.getrandbits(128), version=4))
        session = dict(provider=provider, sid=sid, index=index, parent=parent,
                       cwd=r"C:\sim\project-" + chr(97 + index % 26), state="thinking",
                       ended_at=None, last_write=time.time(), step=0, message_id=None, question=None, last_tool_id=None)
        if provider == "codex":
            date = dt.datetime.now(dt.timezone.utc)
            session["path"] = self.home / date.strftime(".codex/sessions/%Y/%m/%d") / (
                "rollout-" + date.strftime("%Y-%m-%dT%H-%M-%S") + "-" + sid + ".jsonl")
            session["key"] = "codex:" + sid
        elif provider == "claude":
            project = self.home / ".claude/projects/C--sim-project-a"
            session["path"] = project / (sid + ".jsonl") if parent is None else (
                project / parent["sid"] / "subagents" / ("agent-" + sid + ".jsonl"))
            session["key"] = "claude:" + (sid if parent is None else parent["sid"] + "/agent-" + sid)
        else:
            root = self.home / ".gemini/antigravity"
            session["path"] = root / "brain" / sid / ".system_generated/logs/transcript_full.jsonl"
            session["db"] = root / "conversations" / (sid + ".db")
            session["db"].parent.mkdir(exist_ok=True)
            with closing(sqlite3.connect(session["db"])) as db, db:
                db.executescript(CONVERSATION_SQL)
                db.execute("INSERT INTO trajectory_meta VALUES (?,?,?,?)", (sid, sid, 1, 1))
            session["key"] = "antigravity:" + sid
        session["path"].parent.mkdir(parents=True, exist_ok=True)
        self.sessions.append(session)
        self.headers(session)
        self.emit(session, "user")
        return session

    def codex(self, s, typ, payload):
        return dict(timestamp=stamp(), type=typ, payload=payload, ordinal=s["step"])

    def claude(self, s, typ, message=None, **extra):
        previous = s["message_id"]
        s["message_id"] = self.uid()
        r = dict(type=typ, timestamp=stamp(), sessionId=(s["parent"] or s)["sid"],
                 uuid=s["message_id"], parentUuid=previous, cwd=s["cwd"],
                 isSidechain=s["parent"] is not None, userType="external", version="synthetic")
        if s["parent"]:
            r["agentId"] = s["sid"]
        if message is not None:
            r["message"] = message
        return dict(r, **extra)

    def headers(self, s):
        if s["provider"] != "codex":
            return
        p = dict(id=s["sid"], session_id=s["sid"], timestamp=stamp(), cwd=s["cwd"],
                 source="cli", originator="synthetic", cli_version="synthetic", model_provider="synthetic")
        if s["parent"]:
            p.update(parent_thread_id=s["parent"]["sid"], agent_nickname="Sim worker " + str(s["index"]),
                     agent_role="reviewer", source={"subagent": {"thread_spawn": {
                         "parent_thread_id": s["parent"]["sid"], "depth": 1, "agent_role": "reviewer"}}})
        self.append(s, [self.codex(s, "session_meta", p), self.codex(s, "turn_context", {
            "model": "sim-codex", "cwd": s["cwd"]})])

    def records(self, s, action, child=None):
        provider = s["provider"]
        text = "Invented task: count the blue blocks in a pretend garden."
        tool_id = "call-" + self.uid()
        if action in ("working", "spawn"):
            s["last_tool_id"] = tool_id
        elif action in ("error", "result"):
            tool_id = s["last_tool_id"] or tool_id
        if provider == "codex":
            event = lambda kind, **kw: self.codex(s, "event_msg", dict(type=kind, **kw))
            item = lambda kind, **kw: self.codex(s, "response_item", dict(type=kind, **kw))
            if action in ("user", "resume"):
                return [event("task_started", turn_id=self.uid()), event("user_message", message=text)]
            if action == "thinking":
                return [item("reasoning", summary=[{"type": "summary_text", "text": "Comparing invented blocks."}], content=None)]
            if action == "working":
                return [item("custom_tool_call", name="exec", call_id=tool_id,
                             input="text(await tools.exec_command({cmd: 'echo synthetic', workdir: 'C:/sim/project-a'}));")]
            if action == "spawn":
                return [item("function_call", name="spawn_agent", call_id=tool_id, arguments=json.dumps({
                    "agent_type": "reviewer", "message": "Check an invented garden."})),
                    item("function_call_output", call_id=tool_id, output=json.dumps({"agent_id": child["sid"]}))]
            if action == "waiting":
                return [item("function_call", name="request_user_input", call_id=tool_id,
                             arguments=json.dumps({"questions": [{"id": "color", "question": "Which invented color?"}]}))]
            if action in ("error", "result"):
                return [item("custom_tool_call_output", call_id=tool_id,
                             output="Synthetic failure: exit code 1." if action == "error" else "Invented blue block: 1.")]
            if action in ("finish", "end"):
                return [item("message", role="assistant", phase="final_answer", content=[{
                    "type": "output_text", "text": "The invented garden is counted."}]), event("task_complete")]
            return [event("token_count", info={"synthetic": True})]
        if provider == "claude":
            assistant = lambda blocks, stop=None: self.claude(s, "assistant", dict(
                type="message", role="assistant", model="sim-claude", content=blocks, stop_reason=stop))
            if action in ("user", "resume"):
                result = []
                if s["question"]:
                    result.append(self.claude(s, "user", {"role": "user", "content": [{
                        "type": "tool_result", "tool_use_id": s["question"], "content": "Invented blue."}]}))
                    s["question"] = None
                return result + [self.claude(s, "user", {"role": "user", "content": text})]
            if action == "thinking":
                return [assistant([{"type": "thinking", "thinking": "Comparing invented blocks.", "signature": "synthetic"}])]
            if action in ("working", "spawn", "waiting"):
                name = {"working": "Bash", "spawn": "Agent", "waiting": "AskUserQuestion"}[action]
                if action == "waiting":
                    s["question"] = tool_id
                return [assistant([{"type": "tool_use", "id": tool_id, "name": name,
                                    "input": {"description": "Invented garden", "command": "echo synthetic"}}], "tool_use")]
            if action in ("error", "result"):
                return [self.claude(s, "user", {"role": "user", "content": [{
                    "type": "tool_result", "tool_use_id": tool_id, "is_error": action == "error",
                    "content": "Synthetic tool failure." if action == "error" else "Invented blue block: 1."}]})]
            if action in ("finish", "end"):
                return [assistant([{"type": "text", "text": "The invented garden is counted."}], "end_turn"),
                        self.claude(s, "system", subtype="turn_duration", durationMs=1234)]
            return [self.claude(s, "progress", data={"type": "synthetic_progress"})]
        r = dict(step_index=s["step"], source="MODEL", created_at=stamp(), status="DONE")
        if action == "error":
            r["status"] = "ERROR"
        if action in ("user", "resume"):
            return [dict(r, source="USER_EXPLICIT", type="USER_INPUT", content=text, media=[])]
        if action in ("working", "spawn"):
            return [dict(r, type="PLANNER_RESPONSE", thinking="Invented comparison.", tool_calls=[{
                "name": "run_command" if action == "working" else "spawn_agent", "args": {"command": "echo synthetic"}}])]
        if action == "waiting":
            return [dict(r, type="PLANNER_RESPONSE", status="WAITING_FOR_USER", tool_calls=[])]
        if action in ("finish", "end"):
            return [dict(r, type="PLANNER_RESPONSE", content="The invented garden is counted.", tool_calls=[])]
        return [dict(r, type="GENERIC", content="Synthetic failure." if action == "error" else "Invented comparison.")]

    def database_event(self, s, action):
        if s["provider"] != "antigravity":
            return
        state = {"user": "thinking", "resume": "thinking", "spawn": "working", "error": "thinking", "result": "thinking",
                 "finish": "idle", "end": "idle"}.get(action, action)
        step_type, status = {"thinking": (15, 9), "working": (5, 2), "waiting": (138, 9)}.get(state, (15, 3))
        if action == "error":
            step_type, status = 5, 7
        with closing(sqlite3.connect(s["db"])) as db, db:
            db.execute("UPDATE steps SET status=3 WHERE status IN (1,2,8,9,11)")
            db.execute("INSERT INTO steps (idx,step_type,status) VALUES (?,?,?)", (s["step"], step_type, status))
        now = stamp()
        with closing(sqlite3.connect(self.summary)) as db, db:
            db.execute("""INSERT OR REPLACE INTO conversation_summaries
             (conversation_id,title,step_count,last_modified_time,workspace_uris,status,agent_name,
              parent_conversation_id,nesting_depth,not_fully_idle,killed,last_user_input_time)
             VALUES (?,?,?,?,?,?,?,?,?,?,?,?)""", (
                s["sid"], "Sim garden " + str(s["index"]), s["step"] + 1, now,
                json.dumps(["file:///" + s["cwd"].replace("\\", "/")]),
                "CASCADE_RUN_STATUS_RUNNING" if state == "working" else "", "Sim worker" if s["parent"] else "Sim supervisor",
                s["parent"]["sid"] if s["parent"] else "", int(s["parent"] is not None), int(state == "working"), 0, now))

    def append(self, s, records):
        with s["path"].open("ab") as stream:
            for record in records:
                stream.write((json.dumps(record, ensure_ascii=False) + "\n").encode("utf-8"))
        s["last_write"] = time.time()
        s["step"] += len(records)

    def emit(self, s, action, child=None):
        self.database_event(s, action)
        self.append(s, self.records(s, action, child))
        s["state"] = {"user": "thinking", "resume": "thinking", "spawn": "thinking", "error": "thinking", "result": "thinking",
                      "finish": "finished", "end": "finished"}.get(action, action)

    def select(self, event):
        target = event.get("target", "supervisors")
        return [s for s in self.sessions if event.get("provider", "all") in ("all", s["provider"])
                and (target == "all" or (target == "workers") == (s["parent"] is not None))
                and ("index" not in event or event["index"] == s["index"])]

    def action(self, event):
        action = event["action"]
        for s in self.select(event):
            if s["ended_at"] is not None and action not in ("resume", "user"):
                continue
            if action == "spawn":
                if s["parent"]:
                    raise ValueError("spawn target must be a supervisor.")
                for _ in range(event.get("count", 1)):
                    child = self.create(s["provider"], len(self.sessions), s)
                    self.emit(s, "spawn", child)
                    self.emit(child, "working")
            elif action == "partial":
                record = self.records(s, "thinking")[0]
                record["synthetic_padding"] = "invented 한글 🌿"
                raw = (json.dumps(record, ensure_ascii=False) + "\n").encode("utf-8")
                split = raw.index("🌿".encode("utf-8")) + 2
                with s["path"].open("ab") as stream:
                    stream.write(raw[:split])
                s["last_write"] = time.time()
                self.pending.append((event["at"] + event.get("delay", 8), s, raw[split:]))
            elif action in ("huge", "malformed"):
                raw = (json.dumps({"type": "synthetic_padding", "text": "x" * event.get("bytes", 3 * 1024 * 1024)})
                       if action == "huge" else '{"type":"synthetic_broken",')
                with s["path"].open("ab") as stream:
                    stream.write((raw + "\n").encode())
                s["last_write"] = time.time()
            elif action in ("truncate", "rotate"):
                if any(p[1] is s for p in self.pending):
                    raise ValueError("Complete a partial write before rotating/truncating its source.")
                if action == "rotate":
                    self.rotation += 1
                    os.replace(s["path"], s["path"].with_suffix(".rotated-" + str(self.rotation)))
                else:
                    s["path"].write_bytes(b"")
                self.headers(s)
                self.emit(s, "resume")
                self.emit(s, "working")
            elif action == "silent_end":
                s["ended_at"] = time.time()  # Simulated crash: no terminal record.
            else:
                if action not in ("user", "resume", "thinking", "working", "waiting", "error", "result", "finish", "end", "idle"):
                    raise ValueError("Unknown action: " + action)
                self.emit(s, action)
                if action == "end":
                    s["ended_at"] = time.time()
                elif action in ("user", "resume"):
                    s["ended_at"] = None
        with (self.home / "events.jsonl").open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(dict(event, written_at=stamp())) + "\n")

    def flush_partial(self, at):
        for due, s, raw in self.pending[:]:
            if at >= due:
                with s["path"].open("ab") as stream:
                    stream.write(raw)
                s["step"] += 1
                s["state"], s["last_write"] = "thinking", time.time()
                self.pending.remove((due, s, raw))

    def heartbeat(self):
        for s in self.sessions:
            if s["ended_at"] is None and s["state"] in ("working", "thinking", "waiting") and not any(p[1] is s for p in self.pending):
                if s["state"] == "waiting" and s["provider"] != "antigravity":
                    self.append(s, self.records(s, "progress"))
                else:
                    self.emit(s, s["state"])

    def expectation(self, checkpoint):
        return dict(checkpoint, written_at=stamp(), agents=[dict(
            id=s["key"], provider=s["provider"], parentId=s["parent"]["key"] if s["parent"] else None,
            isSubagent=s["parent"] is not None, endedAt=s["ended_at"], lastWrite=s["last_write"])
            for s in self.sessions], workingSeconds=self.scenario.get("working_seconds", 40) / self.speed,
            goneSeconds=self.scenario.get("gone_seconds", 160) / self.speed)


def run(args):
    scenario, events = load_scenario(args.scenario)
    home = prepare_home(args.home, args.reset)
    control = safe_temp_path(args.control_dir) if args.control_dir else None
    if control:
        control.mkdir(parents=True, exist_ok=True)
    sim = Simulator(home, scenario, args.seed, args.speed)
    for provider in PROVIDERS:
        for index in range(scenario["supervisors"].get(provider, 0)):
            sim.create(provider, index)
    atomic_json(home / "manifest.json", dict(scenario=scenario, seed=args.seed, speed=args.speed,
                                            home=str(home), created_at=stamp()))
    checkpoints = [dict(at=0, label="startup", counts={p: scenario["supervisors"].get(p, 0) for p in PROVIDERS})]
    checkpoints += scenario.get("checkpoints", [])
    checkpoints.sort(key=lambda c: c["at"])
    started = time.monotonic()
    paused = 0
    next_heartbeat = scenario.get("heartbeat_seconds", 10)
    cp_index = 0
    while True:
        at = (time.monotonic() - started - paused) * args.speed
        if checkpoints:
            at = min(at, checkpoints[0]["at"])
        sim.flush_partial(at)
        while events and events[0]["at"] <= at:
            sim.action(events.pop(0))
        if at >= next_heartbeat:
            sim.heartbeat()
            next_heartbeat = at + scenario.get("heartbeat_seconds", 10)
        if checkpoints and at >= checkpoints[0]["at"]:
            cp = checkpoints.pop(0)
            cp["number"] = cp_index
            expectation = sim.expectation(cp)
            atomic_json(home / ("checkpoint-%03d.json" % cp_index), expectation)
            print(json.dumps(dict(checkpoint=cp_index, label=cp["label"], at=cp["at"], home=str(home))), flush=True)
            if control:
                before = time.monotonic()
                atomic_json(control / ("checkpoint-%03d.json" % cp_index), expectation)
                ack = control / ("ack-%03d" % cp_index)
                keepalive = time.monotonic()
                diagnostic = time.monotonic()
                while not ack.exists():
                    if time.monotonic() - before > 90:
                        raise TimeoutError("QA did not acknowledge checkpoint.")
                    if cp_index == 0 and time.monotonic() >= keepalive:
                        sim.heartbeat()
                        keepalive = time.monotonic() + .5
                    if cp.get("warning_contains") == "Malformed" and time.monotonic() >= diagnostic:
                        # Repeat bad lines until the transient diagnostic is seen.
                        sim.action(dict(at=cp["at"], action="malformed"))
                        diagnostic = time.monotonic() + .1
                    time.sleep(.05)
                late = max(0, before - started - paused - cp["at"] / args.speed)
                paused += time.monotonic() - before + late
            cp_index += 1
        if at >= scenario["duration"] and not checkpoints:
            if sim.pending:
                raise ValueError("Scenario ends with an unfinished partial write.")
            atomic_json(home / "done.json", sim.expectation(dict(label="done", at=at)))
            return
        time.sleep(.02)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", default="quiet", help="Scenario name or JSON file")
    parser.add_argument("--home", default=str(Path(tempfile.gettempdir()) / "vibedotcom-sim-home"))
    parser.add_argument("--speed", type=float, default=1)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--reset", action="store_true", help="Replace only an owned synthetic home")
    parser.add_argument("--control-dir", help="QA checkpoint handshake directory under TEMP")
    args = parser.parse_args()
    if not math.isfinite(args.speed) or args.speed <= 0:
        parser.error("--speed must be positive and finite")
    try:
        run(args)
    except (ValueError, OSError, sqlite3.Error, TimeoutError) as error:
        parser.exit(1, str(error) + "\n")


if __name__ == "__main__":
    main()
