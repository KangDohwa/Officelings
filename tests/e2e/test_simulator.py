"""Small independent checks for privacy guards, writer recovery and QA rejection paths."""
import copy
from contextlib import closing
import json
from pathlib import Path
import sqlite3
import sys
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/simulate"))
sys.path.insert(0, str(ROOT / "tools/qa"))
from simulate import Simulator, load_scenario, prepare_home, safe_temp_path
from check_snapshot import check_expectation, hold_reader, read_json


class SimulatorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="officelings-sim-test-")
        self.home = prepare_home(Path(self.temp.name) / "home")
        self.spec, _ = load_scenario("quiet")

    def tearDown(self):
        self.temp.cleanup()

    def test_real_provider_and_unowned_home_are_refused(self):
        with self.assertRaises(ValueError):
            safe_temp_path(Path.home() / ".codex/sessions")
        unowned = Path(self.temp.name) / "unowned"
        unowned.mkdir()
        (unowned / "keep.txt").write_text("keep")
        with self.assertRaises(ValueError):
            prepare_home(unowned, reset=True)
        self.assertEqual((unowned / "keep.txt").read_text(), "keep")

    def test_seed_and_parent_ids_survive_reopen(self):
        sim = Simulator(self.home, self.spec, seed=7)
        parent = sim.create("claude", 0)
        child = sim.create("claude", 1, parent)
        rows = [json.loads(line) for line in child["path"].read_text(encoding="utf-8").splitlines()]
        self.assertTrue(rows[0]["isSidechain"])
        self.assertEqual(rows[0]["sessionId"], parent["sid"])
        self.assertEqual(rows[0]["agentId"], child["sid"])
        sim.action(dict(at=10, action="end", target="workers"))
        sim.action(dict(at=20, action="resume", target="workers"))
        self.assertIsNone(child["ended_at"])
        second_home = prepare_home(Path(self.temp.name) / "second")
        second = Simulator(second_home, self.spec, seed=7)
        self.assertEqual(second.create("claude", 0)["sid"], parent["sid"])

    def test_split_unicode_then_truncate_and_rotate(self):
        sim = Simulator(self.home, self.spec)
        session = sim.create("codex", 0)
        sim.action(dict(at=20, action="partial", delay=8))
        with self.assertRaises(UnicodeDecodeError):
            session["path"].read_bytes().decode("utf-8")
        sim.flush_partial(28)
        lines = session["path"].read_text(encoding="utf-8").splitlines()
        self.assertEqual(json.loads(lines[-1])["synthetic_padding"], "invented 한글 🌿")
        sim.action(dict(at=30, action="truncate"))
        sim.action(dict(at=40, action="rotate"))
        meta = json.loads(session["path"].read_text().splitlines()[0])
        self.assertEqual(meta["payload"]["id"], session["sid"])
        self.assertEqual(len(list(session["path"].parent.glob("*.rotated-*"))), 1)

    def test_antigravity_layout_and_status_rows(self):
        sim = Simulator(self.home, self.spec)
        parent = sim.create("antigravity", 0)
        child = sim.create("antigravity", 1, parent)
        sim.emit(child, "waiting")
        with closing(sqlite3.connect(child["db"])) as db:
            self.assertEqual(db.execute("pragma user_version").fetchone()[0], 1)
            self.assertEqual(len(db.execute("pragma table_info(steps)").fetchall()), 11)
            self.assertEqual(db.execute("select step_type,status from steps order by idx desc limit 1").fetchone(), (138, 9))
        with closing(sqlite3.connect(sim.summary)) as db:
            self.assertEqual(db.execute("pragma user_version").fetchone()[0], 3)
            self.assertEqual(db.execute("select parent_conversation_id,nesting_depth from conversation_summaries where conversation_id=?", (child["sid"],)).fetchone(), (parent["sid"], 1))

    def test_ended_workers_are_not_rewritten_by_later_churn(self):
        sim = Simulator(self.home, self.spec)
        parent = sim.create("codex", 0)
        child = sim.create("codex", 1, parent)
        sim.action(dict(at=10, action="end", target="workers"))
        before = child["path"].read_bytes()
        sim.action(dict(at=20, action="end", target="workers"))
        sim.heartbeat()
        self.assertEqual(child["path"].read_bytes(), before)

    def test_tool_results_link_to_tool_use(self):
        sim = Simulator(self.home, self.spec)
        session = sim.create("claude", 0)
        sim.emit(session, "working")
        sim.emit(session, "result")
        rows = [json.loads(line) for line in session["path"].read_text().splitlines()]
        call = rows[-2]["message"]["content"][0]
        result = rows[-1]["message"]["content"][0]
        self.assertEqual(result["tool_use_id"], call["id"])
        self.assertFalse(result["is_error"])

    def test_oracle_rejects_count_parent_and_stuck_working(self):
        now = time.time()
        intent = dict(counts=dict(codex=1, claude=0, antigravity=0), workingSeconds=2, goneSeconds=8,
                      agents=[dict(id="codex:child", provider="codex", parentId="codex:parent", isSubagent=True,
                                   lastWrite=now - 4, endedAt=now - 4)])
        agent = dict(id="codex:child", provider="codex", parentId="codex:parent", isSubagent=True, state="waiting")
        good = dict(agents=[dict(agent=agent)], warnings=[])
        self.assertEqual(check_expectation(good, intent, now=now), [])
        bad = copy.deepcopy(good)
        bad["agents"][0]["agent"].update(parentId=None, state="working")
        errors = check_expectation(bad, intent, now=now)
        self.assertTrue(any("parent" in e for e in errors))
        self.assertTrue(any("still working" in e for e in errors))
        self.assertTrue(any("live counts" in e for e in check_expectation(dict(agents=[], warnings=[]), intent, now=now)))

    def test_nonfinite_and_duplicate_json_rejected(self):
        file = Path(self.temp.name) / "bad.json"
        for text in ('{"x":NaN}', '{"x":1,"x":2}'):
            file.write_text(text)
            with self.assertRaises(ValueError):
                read_json(file)

    @unittest.expectedFailure
    def test_documented_shared_reader_allows_atomic_replacement(self):
        # Preserved regression for the Windows publisher defect. E2E reports
        # this as FAIL and separately completes lifecycle/soak with guarded reads.
        file = Path(self.temp.name) / "state.json"
        file.write_text('{"sequence":1}')
        with hold_reader(file):
            temporary = file.with_suffix(".tmp")
            temporary.write_text('{"sequence":2}')
            temporary.replace(file)
        self.assertEqual(read_json(file)["sequence"], 2)

    def test_ready_scenarios_expand_repeat_events(self):
        for name in ("quiet", "busy-12", "burst-spawn", "flaky-writer", "long-soak"):
            spec, events = load_scenario(name)
            self.assertEqual(events, sorted(events, key=lambda e: (e["at"], e["order"])))
            self.assertTrue(all(e["at"] <= spec["duration"] for e in events))


if __name__ == "__main__":
    unittest.main()
