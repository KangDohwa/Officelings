"""Re-sync body pixels; stop before writing if approved heads or eyes changed.
Run with the sprite-gen venv Python, using -B (no shared bytecode writes).
"""
from pathlib import Path
import hashlib
import json

import numpy as np
from PIL import Image

OUT = Path(__file__).resolve().parent
CID = "dev-hoodie" if OUT.name == "dev-hoodie-v2" else OUT.name
RUN = OUT / "runs/motion-rev1"
FACINGS = ["se", "sw", "ne", "nw"]
BLINK_ROWS = [0, 1, 4, 5, 8, 9, 12, 13, 16, 17, 20, 21, 24, 25,
              26, 27, 28, 29, 30, 31, 34, 35, 36, 37]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def rgba(path):
    with Image.open(path) as im:
        return np.array(im.convert("RGBA"))


def cell(arr, row, phase):
    return arr[row * 64:(row + 1) * 64, phase * 32:(phase + 1) * 32]


def baseline(name):
    return RUN / ("before-" + name if CID == "punk-girl" else "before/" + name)


def main():
    manifest = OUT / (CID + ".iso.json")
    doc = json.loads(manifest.read_text(encoding="utf-8"))
    normal = next(s for s in doc["sheets"] if s["kind"] == "character")
    blink = next(s for s in doc["sheets"] if s["id"] == normal["blink"]["sheetId"])
    olddoc = json.loads(baseline(CID + "-iso.manifest.json").read_text(encoding="utf-8"))
    oldsheet = next(s for s in olddoc["sheets"] if s["kind"] == "character")
    for key in ("headOffsetsByClip", "faceLock", "blink", "identityPalette"):
        assert normal[key] == oldsheet[key], "STOP: changed " + key
    assert normal["blink"]["normalRows"] == BLINK_ROWS
    assert (normal["cellW"], normal["cellH"], len(normal["rows"])) == (32, 64, 38)

    frozen = [manifest, OUT / (CID + "-iso.manifest.json"), OUT / normal["file"]]
    assert frozen[0].read_bytes() == frozen[1].read_bytes(), "manifest aliases differ"
    if CID == "dev-hoodie":
        hashes = json.loads((RUN / "baseline.json").read_text(encoding="utf-8"))["files"]
    elif CID == "punk-girl":
        hashes = json.loads((RUN / "before-hashes.json").read_text(encoding="utf-8"))
    else:
        hashes = json.loads((RUN / "started.json").read_text(encoding="utf-8"))["frozenSha256"]
    for name in [normal["file"], blink["file"]] + [v["eyeMask"] for v in normal["faceLock"].values()]:
        assert sha(baseline(name).read_bytes()) == hashes[name], "STOP: altered baseline " + name

    old = rgba(baseline(normal["file"]))
    current = rgba(OUT / normal["file"])
    approved = rgba(baseline(blink["file"]))
    existing = rgba(OUT / blink["file"])
    assert old.shape == current.shape == (2432, 128, 4)
    assert approved.shape == existing.shape == (1536, 128, 4)
    heads = {}
    for k, facing in enumerate(FACINGS):
        if CID == "professor":
            source = OUT / "runs/v1" / ("head-" + facing + ".png")
            assert source.read_bytes() == (RUN / "before" / source.name).read_bytes(), "STOP: head layer changed"
            assert sha(source.read_bytes()) == hashes[source.relative_to(OUT).as_posix()]
            head = rgba(source)
            frozen.append(source)
        elif CID == "punk-girl":
            source = OUT / "runs" / (facing + "-idle-native.png")
            expected = json.loads((RUN / "motion-proof.json").read_text(encoding="utf-8"))["protectedHashes"]
            assert sha(source.read_bytes()) == expected[source.relative_to(OUT).as_posix()], "STOP: head source changed"
            head = rgba(source)
            head[38:] = 0
            frozen.append(source)
        else:
            # These baked head layers have no standalone PNG; use the original idle.
            head = cell(old, k, 0).copy()
            head[41 if k < 2 else 39:] = 0
        heads[facing] = Image.fromarray(head)

    head_masks = {}
    for clip in normal["rows"]:
        for phase, offset in enumerate(normal["headOffsetsByClip"][clip["name"] + "/" + clip["direction"]]):
            layer = Image.new("RGBA", (32, 64))
            layer.alpha_composite(heads[clip["direction"]], (offset["x"], offset["y"]))
            mask = np.array(layer)[:, :, 3] > 0
            row = clip["row"]
            assert np.array_equal(cell(old, row, phase)[mask], cell(current, row, phase)[mask]), (
                "STOP: motion changed head RGBA", row, phase)
            head_masks[row, phase] = mask

    masks = {}
    for facing, info in normal["faceLock"].items():
        source = OUT / info["eyeMask"]
        assert source.read_bytes() == baseline(source.name).read_bytes(), "STOP: eye mask changed"
        masks[facing] = rgba(source)[:, :, 3] > 0
        frozen.append(source)
    before_hashes = {p: sha(p.read_bytes()) for p in frozen}
    result = np.empty_like(approved)
    changed_cells = []
    for b, row in enumerate(BLINK_ROWS):
        facing = normal["rows"][row]["direction"]
        for phase in range(4):
            eye = cell(masks[facing], row, phase)
            head = head_masks[row, phase]
            src, prior, dst = cell(current, row, phase), cell(approved, b, phase), cell(result, b, phase)
            assert eye.any() and np.all(head[eye]), "STOP: eyes outside canonical head"
            assert np.array_equal(cell(existing, b, phase)[eye], prior[eye]), "STOP: approved closed eyes changed"
            assert np.array_equal(cell(old, row, phase)[~eye], prior[~eye]), "invalid approved blink baseline"
            dst[:] = src
            dst[eye] = prior[eye]
            assert np.array_equal(dst[~eye], src[~eye])
            assert np.array_equal(dst[eye], prior[eye])
            assert np.array_equal(dst[head], prior[head]), "STOP: blink head changed"
            if not np.array_equal(dst, prior):
                changed_cells.append({"blinkRow": b, "normalRow": row, "phase": phase})

    assert all(sha(p.read_bytes()) == h for p, h in before_hashes.items()), "STOP: concurrent input change"
    target = OUT / blink["file"]
    pending = target.with_name(target.stem + ".resync.png")
    Image.fromarray(result).save(pending)
    assert np.array_equal(rgba(pending), result), "PNG roundtrip differs"
    assert all(sha(p.read_bytes()) == h for p, h in before_hashes.items()), "STOP: concurrent input change"
    pending.replace(target)
    proof = {
        "character": OUT.name, "normalHeadCellsByteIdentical": len(head_masks),
        "blinkHeadCellsByteIdentical": 96, "nonEyeCellsByteIdenticalToNewNormal": 96,
        "closedEyeCellsByteIdenticalToApprovedBlink": 96, "headOffsetsUnchanged": True,
        "faceLockAndBlinkMapUnchanged": True, "identityPaletteUnchanged": True,
        "protectedFileSha256": {p.relative_to(OUT).as_posix(): h for p, h in before_hashes.items()},
        "approvedBlinkSha256": sha(baseline(blink["file"]).read_bytes()),
        "resyncedBlinkSha256": sha(target.read_bytes()),
        "changedCellsFromApprovedBlink": changed_cells,
        "rgbaEvidenceSha256": {
            "normalHeadsBefore": sha(b"".join(cell(old, r, p)[m].tobytes() for (r, p), m in head_masks.items())),
            "normalHeadsAfter": sha(b"".join(cell(current, r, p)[m].tobytes() for (r, p), m in head_masks.items())),
            "closedEyesBefore": sha(b"".join(cell(approved, b, p)[cell(masks[normal["rows"][r]["direction"]], r, p)].tobytes() for b, r in enumerate(BLINK_ROWS) for p in range(4))),
            "closedEyesAfter": sha(b"".join(cell(result, b, p)[cell(masks[normal["rows"][r]["direction"]], r, p)].tobytes() for b, r in enumerate(BLINK_ROWS) for p in range(4))),
        },
    }
    (OUT / "blink-resync-proof.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
    print(OUT.name + ": 152 normal heads / 96 blink heads / 96 non-eye cells / 96 approved eye cells identical; "
          + str(len(changed_cells)) + " body cells re-synced")


if __name__ == "__main__":
    main()
