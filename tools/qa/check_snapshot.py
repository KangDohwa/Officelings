"""Checks the documented interchange contract and scenario intent, not parser internals."""
import datetime as dt
from contextlib import contextmanager
import hashlib
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import time

PROVIDERS = ("codex", "claude", "antigravity")
STATES = ("working", "thinking", "waiting", "reporting", "idle", "gone")


@contextmanager
def hold_reader(path):
    """A documented FILE_SHARE_READ|WRITE|DELETE reader, for the publisher probe."""
    import ctypes as c
    from ctypes import wintypes as w
    kernel = c.WinDLL("kernel32", use_last_error=True)
    kernel.CreateFileW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, c.c_void_p, w.DWORD, w.DWORD, w.HANDLE]
    kernel.CreateFileW.restype = w.HANDLE
    kernel.CloseHandle.argtypes = [w.HANDLE]
    handle = kernel.CreateFileW(os.path.abspath(path), 0x80000000, 7, None, 3, 0x80, None)
    if handle == c.c_void_p(-1).value:
        raise c.WinError(c.get_last_error())
    try:
        yield handle
    finally:
        kernel.CloseHandle(handle)


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def read_json(path):
    def constant(value):
        raise ValueError("Nonfinite JSON constant: " + value)

    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("Duplicate JSON key: " + key)
            result[key] = value
        return result
    if os.name == "nt":
        # Ordinary CRT/Python opens omit FILE_SHARE_DELETE and can break the
        # tracker's atomic replacement. Match the documented Windows reader.
        import ctypes as c
        from ctypes import wintypes as w
        kernel = c.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, c.c_void_p, w.DWORD, w.DWORD, w.HANDLE]
        kernel.CreateFileW.restype = w.HANDLE
        kernel.CloseHandle.argtypes = [w.HANDLE]
        kernel.ReadFile.argtypes = [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), c.c_void_p]
        kernel.ReadFile.restype = w.BOOL
        # realpath/Path.resolve itself opens Windows metadata handles without
        # dependable delete sharing. The caller supplies an already-safe path.
        handle = kernel.CreateFileW(os.path.abspath(path), 0x80000000, 7, None, 3, 0x80, None)
        if handle == c.c_void_p(-1).value:
            raise c.WinError(c.get_last_error())
        try:
            buffer = c.create_string_buffer(65536)
            chunks = []
            while True:
                received = w.DWORD()
                if not kernel.ReadFile(handle, buffer, len(buffer), c.byref(received), None):
                    raise c.WinError(c.get_last_error())
                if not received.value:
                    break
                chunks.append(buffer.raw[:received.value])
        finally:
            kernel.CloseHandle(handle)
        text = b"".join(chunks).decode("utf-8-sig")
    else:
        text = Path(path).read_text(encoding="utf-8-sig")
    return json.loads(text, parse_constant=constant, object_pairs_hook=pairs)


def documented_version(path):
    match = re.search(r"^# Local state contract \(version (\d+)\)", Path(path).read_text(encoding="utf-8"), re.M)
    require(match is not None, "Cannot find schema version in HEAD docs/state-schema.md")
    version = int(match[1])
    require(version in (3, 4, 5), "QA must be updated for documented schema " + str(version))
    return version


def timestamp(value):
    require(isinstance(value, str), "timestamp must be a string")
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    require(parsed.utcoffset() is not None, "timestamp must include a UTC offset")
    return parsed.timestamp()


def finite(value):
    if isinstance(value, float):
        require(math.isfinite(value), "nonfinite number")
    elif isinstance(value, dict):
        for item in value.values():
            finite(item)
    elif isinstance(value, list):
        for item in value:
            finite(item)


def integer(value, low, high, name):
    require(type(value) is int and low <= value <= high, name + " integer bounds")


def number(value, name):
    require(type(value) in (int, float) and math.isfinite(value), name + " finite number")


def relative_file(root, value):
    require(isinstance(value, str) and "\\" not in value and ":" not in value, "relative PNG path grammar")
    path = PurePosixPath(value)
    require(not path.is_absolute() and ".." not in path.parts and value, "unsafe PNG path")
    resolved = (root / value).resolve()
    require(resolved.is_relative_to(root.resolve()) and resolved.is_file(), "missing/escaping PNG " + value)
    return resolved


def validate_snapshot(snapshot, version, pack_root, verify_hashes=False):
    finite(snapshot)
    require(snapshot["schemaVersion"] == version, "schemaVersion differs from HEAD documentation")
    integer(snapshot["sequence"], 1, 2**63 - 1, "sequence")
    timestamp(snapshot["generatedAt"])
    require(isinstance(snapshot["warnings"], list) and all(isinstance(x, str) for x in snapshot["warnings"]), "warnings array")
    atlas = snapshot["atlas"]
    require(atlas["version"] == 2 and atlas["cellWidth"] == 16 and atlas["cellHeight"] == 24, "legacy atlas contract")
    for field in ("width", "height", "rows", "columns"):
        integer(atlas[field], 1, 65536, "atlas." + field)
    require(atlas["width"] == atlas["columns"] * 16 and atlas["height"] == atlas["rows"] * 24, "atlas geometry")
    relative_file(pack_root, atlas["file"])
    office = snapshot["office"]
    require(office["version"] == 1 and office["tileSize"] == 16, "office format/tileSize")
    w, h = office["widthTiles"], office["heightTiles"]
    integer(w, 8, 128, "office width")
    integer(h, 8, 128, "office height")
    require(len(office["tiles"]) == w * h, "row-major tiles")
    for tile in office["tiles"]:
        integer(tile, 0, 2**31 - 1, "tile")
    for axis, limit in (("x", w), ("y", h)):
        integer(office["door"][axis], 0, limit - 1, "door")
    if version >= 4:
        require(office.get("autoDesks") is None, "resolved fixed office cannot auto-expand")
    assets = snapshot["assets"]
    require(assets["version"] == 1, "asset format")
    sheets = {sheet["id"]: sheet for sheet in assets["sheets"]}
    require(len(sheets) == len(assets["sheets"]), "duplicate sheet ID")
    tile_ids = {tile["id"] for tile in assets["tileTypes"]}
    require(all(tile in tile_ids for tile in office["tiles"]), "unknown tile ID")
    for sheet in sheets.values():
        integer(sheet["width"], 1, 65536, "sheet width")
        integer(sheet["height"], 1, 65536, "sheet height")
        require(re.fullmatch(r"[0-9a-fA-F]{64}", sheet["sha256"]) is not None, "sheet hash grammar")
        file = relative_file(pack_root, sheet["file"])
        if verify_hashes:
            require(hashlib.sha256(file.read_bytes()).hexdigest() == sheet["sha256"].lower(), "PNG content hash " + sheet["id"])

    def frame(value):
        if value is None:
            return
        require(value["sheetId"] in sheets, "unknown SpriteFrame sheet")
        sheet = sheets[value["sheetId"]]
        for field in ("row", "column", "x", "y"):
            integer(value[field], 0, 65536, "sprite." + field)
        for field in ("width", "height"):
            integer(value[field], 1, 65536, "sprite." + field)
        require(value["x"] + value["width"] <= sheet["width"] and value["y"] + value["height"] <= sheet["height"], "sprite source bounds")
        for field in ("anchorX", "anchorY", "scale", "offsetX", "offsetY"):
            number(value[field], "sprite." + field)
        require(value["scale"] > 0, "sprite scale")

    furniture = {item["id"]: item for item in office["furniture"]}
    require(len(furniture) == len(office["furniture"]), "duplicate furniture ID")
    for item in furniture.values():
        for axis, limit in (("x", w), ("y", h)):
            integer(item[axis], 0, limit - 1, "furniture position")
        require(item["x"] + item["width"] <= w and item["y"] + item["height"] <= h, "furniture bounds")
        require(item["rotation"] in (0, 90, 180, 270), "furniture rotation")
    for item in snapshot["furnitureSprites"]:
        require(item["id"] in furniture, "unknown furniture sprite instance")
        frame(item["sprite"])
    ids = set()
    for view in snapshot["agents"]:
        agent, pose = view["agent"], view["pose"]
        require(isinstance(agent["id"], str) and agent["id"] not in ids, "duplicate/invalid agent ID")
        ids.add(agent["id"])
        require(agent["provider"] in PROVIDERS and agent["state"] in STATES, "agent enums")
        require(agent["id"].startswith(agent["provider"] + ":"), "provider ID prefix")
        for field in ("isSubagent", "bestEffort"):
            require(type(agent[field]) is bool, "agent boolean " + field)
        for field in ("name", "task"):
            require(isinstance(agent[field], str), "agent string " + field)
        require(len(agent["task"]) <= 240, "prompt excerpt exceeds 240 characters")
        for field in ("parentId", "role", "model", "cwd", "tool", "toolKind"):
            require(agent[field] is None or isinstance(agent[field], str), "nullable agent string " + field)
        timestamp(agent["lastSeen"])
        timestamp(agent["stateSince"])
        require(agent["bestEffort"] == (agent["provider"] == "antigravity"), "bestEffort provider contract")
        for axis in ("x", "y"):
            number(pose[axis], "pose." + axis)
        require(0 <= pose["x"] < w and 0 <= pose["y"] < h, "pose outside map")
        require(pose["facing"] in ("up", "down", "left", "right"), "pose facing")
        require(isinstance(pose["action"], str) and pose["action"], "pose action")
        for field in ("visible", "carryingPaper", "reportOnDesk"):
            require(type(pose[field]) is bool, "pose boolean " + field)
        number(pose["opacity"], "pose opacity")
        require(0 <= pose["opacity"] <= 1, "pose opacity bounds")
        for field in ("spriteRow", "spriteColumn"):
            integer(pose[field], 0, 65536, field)
        frame(pose["sprite"])
        if version >= 4:
            require(pose["workLocation"] in ("desk", "overflow", "queue") and type(pose["queued"]) is bool, "assignment metadata")
        if version >= 5:
            require(pose.get("seatPhase") in (None, "turn", "lower", "seated", "raise"), "seat phase")
            integer(pose.get("seatPhaseFrame", 0), 0, 1, "seat phase frame")
    return snapshot


def check_expectation(snapshot, expected, seen_warnings=(), now=None):
    now = time.time() if now is None else now
    observed = {v["agent"]["id"]: v["agent"] for v in snapshot["agents"]}
    known = {agent["id"]: agent for agent in expected["agents"]}
    counts = {provider: sum(a["provider"] == provider and a["state"] != "gone" for a in observed.values()) for provider in PROVIDERS}
    errors = []
    if "counts" in expected and counts != expected["counts"]:
        errors.append("live counts: expected %s, observed %s" % (expected["counts"], counts))
    for key, agent in observed.items():
        if key not in known:
            errors.append("unexpected identity " + key)
            continue
        intent = known[key]
        if agent["parentId"] != intent["parentId"] or agent["isSubagent"] != intent["isSubagent"]:
            errors.append("parent/isSubagent mismatch " + key)
        if agent["parentId"] and agent["parentId"] == key:
            errors.append("self parent " + key)
        ended = intent["endedAt"]
        if ended is not None and now >= ended + expected["workingSeconds"] + .5 and agent["state"] == "working":
            errors.append("still working after session ended + timeout " + key)
        if expected.get("states") and agent["state"] != "gone" and agent["state"] not in expected["states"]:
            errors.append("state %s not in %s for %s" % (agent["state"], expected["states"], key))
    # Counts alone cannot detect one missing identity being replaced by another.
    for key, intent in known.items():
        if now - intent["lastWrite"] < expected["goneSeconds"] - .5:
            if key not in observed or observed[key]["state"] == "gone":
                errors.append("recent synthetic identity missing/gone " + key)
    needle = expected.get("warning_contains")
    if needle and not any(needle in value for value in [*seen_warnings, *snapshot["warnings"]]):
        errors.append("missing diagnostic containing " + needle)
    return errors
