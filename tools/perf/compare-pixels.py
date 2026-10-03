"""Compare the nine deterministic raw-pixel hashes emitted by AppHarness pixels."""
import argparse
import json
from pathlib import Path
import struct


def read_capture(path):
    path = Path(path)
    manifest = path if path.is_file() else path / "pixels.json"
    data = json.loads(manifest.read_text(encoding="utf-8-sig"))
    records = {}
    for row in data["records"]:
        key = (row["seconds"], row["zoom"])
        if key in records:
            raise ValueError("Duplicate pixel fixture: %s" % (key,))
        image = manifest.parent / row["file"]
        with image.open("rb") as stream:
            header = stream.read(24)
        if (len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or
                struct.unpack(">II", header[16:24]) != (row["width"], row["height"])):
            raise ValueError("Invalid PNG or dimensions: " + str(image))
        digest = row["pixelSha256"]
        if len(digest) != 64 or any(c not in "0123456789abcdefABCDEF" for c in digest):
            raise ValueError("Invalid raw-pixel hash: " + str(image))
        records[key] = row
    expected = {(seconds, zoom) for seconds in (0, 12, 36) for zoom in (1, 2, 3)}
    if set(records) != expected:
        raise ValueError("Require all nine fixtures: 0/12/36 seconds x 1/2/3 zoom")
    return data, records


def compare(baseline, current):
    before, a = read_capture(baseline)
    after, b = read_capture(current)
    for key in ("epoch", "seed", "stepSeconds", "startSeconds"):
        if before[key] != after[key]:
            raise ValueError("Fixture metadata differs: " + key)
    rows = []
    for seconds, zoom in sorted(a):
        old, new = a[seconds, zoom], b[seconds, zoom]
        fields = ("width", "height", "visible", "pixelSha256")
        changed = [key for key in fields if old[key] != new[key]]
        rows.append(dict(seconds=seconds, zoom=zoom, passed=not changed, changedFields=changed,
                         baseline={key: old[key] for key in fields}, current={key: new[key] for key in fields}))
    return dict(passed=all(row["passed"] for row in rows), baseline=str(baseline), current=str(current),
                comparison="AppHarness decoded Pbgra32 raw-pixel SHA-256; PNG dimensions checked; no screenshot/desktop capture", rows=rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("current", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        report = compare(args.baseline, args.current)
        if args.output:
            with args.output.open("x", encoding="utf-8") as stream:
                json.dump(report, stream, indent=2)
        failed = [row for row in report["rows"] if not row["passed"]]
        print("Pixel fixtures: %d/9 exact matches" % (9 - len(failed)))
        for row in failed:
            print("FAIL %ss/%sx: %s" % (row["seconds"], row["zoom"], ", ".join(row["changedFields"])))
        return 1 if failed else 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("Pixel comparison error: " + str(error))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
