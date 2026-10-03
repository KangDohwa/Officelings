"""Align prop frames and lock their static RGBA pixels; never rewrite source art.

Import normalize_frames() for a clip or normalize_sheet() for a production sheet.
Only Pillow and numpy are required. See docs/anim-normalize.md.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
PROP_RULES = {
    "tv": "screen", "television": "screen", "desk": "screen",
    "monitor": "screen", "monitors": "screen",
    "coffee-machine": "steam", "water-cooler": "water",
    "server-rack": "leds", "ceiling-light": "glow",
    "ceiling-light-glow": "glow",
    "cat": "full-animation", "dog": "full-animation", "pet": "full-animation",
    "robot-vacuum": "full-animation",
}
QA_PROPS = ("tv", "server-rack", "coffee-machine", "water-cooler")
STATIC_CHOICES = {"variants", "plants", "door", "clock"}


def _name(prop):
    return prop.lower().replace("_", "-")


def _bbox(mask):
    y, x = np.nonzero(mask)
    return [int(x.min()), int(y.min()), int(x.max()) + 1, int(y.max()) + 1] if len(x) else None


def _translate(array, dx, dy):
    """Integer translation with zero fill, never wraparound or resampling."""
    h, w = array.shape[:2]
    result = np.zeros_like(array)
    x0, x1 = max(0, dx), min(w, w + dx)
    y0, y1 = max(0, dy), min(h, h + dy)
    if x0 < x1 and y0 < y1:
        result[y0:y1, x0:x1] = array[y0 - dy:y1 - dy, x0 - dx:x1 - dx]
    return result


def _dilate(mask, radius=1):
    result = mask.copy()
    for _ in range(radius):
        padded = np.pad(result, 1)
        result = np.logical_or.reduce([
            padded[y:y + mask.shape[0], x:x + mask.shape[1]]
            for y in range(3) for x in range(3)
        ])
    return result


def _erode(mask):
    return ~_dilate(~np.pad(mask, 1, constant_values=False))[1:-1, 1:-1]


def _components(mask):
    """8-connected change clusters, using only the standard library and numpy."""
    todo = mask.copy()
    h, w = mask.shape
    for y, x in zip(*np.nonzero(mask)):
        if not todo[y, x]:
            continue
        todo[y, x] = False
        stack, points = [(int(y), int(x))], []
        while stack:
            cy, cx = stack.pop()
            points.append((cy, cx))
            for ny in range(max(0, cy - 1), min(h, cy + 2)):
                for nx in range(max(0, cx - 1), min(w, cx + 2)):
                    if todo[ny, nx]:
                        todo[ny, nx] = False
                        stack.append((ny, nx))
        component = np.zeros_like(mask)
        yy, xx = zip(*points)
        component[yy, xx] = True
        yield component


def detect_shift(reference, frame, max_shift=6, alignment_mask=None):
    """Return the (dx, dy) applied to frame by integer alpha/outline correlation.

    max_shift bounds each axis. Ties prefer the smallest displacement. Opaque
    alpha ignores translucent glows; a nonempty translucent-only sprite falls
    back to its visible alpha. Scores use the original signal energy so clipping
    cannot improve a candidate's score.
    """
    if max_shift < 0 or max_shift >= min(reference.shape[:2]):
        raise ValueError("max_shift must be nonnegative and smaller than the cell")
    signals = []
    for rgba in (reference, frame):
        alpha = rgba[:, :, 3] >= 128
        if not alpha.any():
            alpha = rgba[:, :, 3] > 0
        if not alpha.any():
            raise ValueError("An animation contains an empty frame")
        signals.append((alpha, alpha & ~_erode(alpha)))
    roi = np.ones(reference.shape[:2], dtype=bool) if alignment_mask is None else alignment_mask
    ref_alpha, ref_edge = (s & roi for s in signals[0])
    moving_alpha, moving_edge = signals[1]
    if not ref_alpha.any():
        raise ValueError("Alignment mask excludes the whole reference sprite")
    best = None
    for dy in range(-max_shift, max_shift + 1):
        for dx in range(-max_shift, max_shift + 1):
            alpha = _translate(moving_alpha, dx, dy) & roi
            edge = _translate(moving_edge, dx, dy) & roi
            alpha_score = 2 * np.count_nonzero(ref_alpha & alpha) / (ref_alpha.sum() + moving_alpha.sum())
            edge_score = 2 * np.count_nonzero(ref_edge & edge) / max(1, ref_edge.sum() + moving_edge.sum())
            score = .35 * alpha_score + .65 * edge_score
            key = (score, -abs(dx) - abs(dy), -abs(dy), -abs(dx))
            if best is None or key > best[0]:
                best = (key, (dx, dy))
    return best[1]


def _cool_pixels(rgba):
    r, g, b = np.moveaxis(rgba[:, :, :3].astype(np.int16), 2, 0)
    return (rgba[:, :, 3] > 0) & (((g >= r + 12) & (b >= r + 4)) | (b > np.maximum(r, g) + 14))


def _screen_mask(aligned):
    reference = aligned[0]
    box = _bbox(reference[:, :, 3] > 0)
    upper = np.indices(reference.shape[:2])[0] < box[1] + (box[3] - box[1]) * .7
    # Prefer the accepted frame's screen background. For an off/black screen,
    # use the union of the aligned on-screen colours instead.
    for candidates in (_cool_pixels(reference), np.any([_cool_pixels(f) for f in aligned], axis=0)):
        clusters = [c for c in _components(candidates & upper) if c.sum() >= 8]
        if clusters:
            mask = np.zeros(reference.shape[:2], dtype=bool)
            for c in clusters:
                x0, y0, x1, y1 = _bbox(c)
                mask[y0:y1, x0:x1] = True
            return mask & _erode(reference[:, :, 3] > 0)
    raise ValueError("Screen not detected; supply animated_mask after inspecting this prop")


def _change_mask(aligned):
    """Keep compact, substantial interior changes; reject silhouette drift/noise."""
    reference = aligned[0]
    difference = np.max(np.abs(aligned.astype(np.int16) - reference), axis=(0, 3))
    interior = _erode(reference[:, :, 3] > 0)
    strong = (difference >= 24) & interior
    result = np.zeros(interior.shape, dtype=bool)
    # ponytail: heuristics cannot infer arbitrary animation intent; use a caller
    # mask or a PROP_RULES entry when a new prop has deliberate silhouette motion.
    for component in _components(_dilate(strong)):
        actual = component & strong
        box = _bbox(actual)
        if box is None:
            continue
        x0, y0, x1, y1 = box
        area = (x1 - x0) * (y1 - y0)
        if actual.sum() >= 2 and area <= .35 * max(1, interior.sum()) and actual.sum() / area >= .12:
            result[max(0, y0 - 1):y1 + 1, max(0, x0 - 1):x1 + 1] = True
    return result & interior


def _animated_mask(aligned, strategy):
    reference = aligned[0]
    visible = np.any(aligned[:, :, :, 3] > 0, axis=0)
    if strategy == "full-animation":
        return visible
    if strategy == "screen":
        return _screen_mask(aligned)
    automatic = _change_mask(aligned)
    if strategy == "glow":
        return np.any((aligned[:, :, :, 3] > 0) & (aligned[:, :, :, 3] < 255), axis=0)
    if strategy in {"steam", "water"}:
        box = _bbox(reference[:, :, 3] > 0)
        yy = np.indices(visible.shape)[0]
        upper = yy < box[1] + (box[3] - box[1]) * .45
        changed = np.any(aligned != reference, axis=(0, 3))
        if strategy == "water":
            vessel = np.any([_cool_pixels(f) for f in aligned], axis=0) & upper
            return automatic | (vessel & changed)
        pale = np.any((aligned[:, :, :, :3].min(axis=3) >= 150) & (aligned[:, :, :, 3] > 0), axis=0)
        steam = changed & upper & (pale | ~_erode(reference[:, :, 3] > 0))
        return automatic | steam
    return automatic


def normalize_frames(frames, prop="unknown", *, max_shift=6, animated_mask=None, mode=None):
    """Return (RGBA images, bool animation mask, JSON-compatible report).

    Frame zero is the reference. Input images are never mutated. animated_mask
    is in reference cell coordinates and overrides automatic region detection.
    mode can be 'full-animation' or 'static-body-with-animated-mask'.
    """
    if not frames or len({f.size for f in frames}) != 1:
        raise ValueError("Supply nonempty, equal-sized Pillow frames")
    if mode not in {None, "full-animation", "static-body-with-animated-mask"}:
        raise ValueError("Unknown normalization mode")
    original = np.stack([np.array(f.convert("RGBA")) for f in frames])
    strategy = PROP_RULES.get(_name(prop), "auto")
    if mode == "full-animation":
        strategy = mode
    elif mode == "static-body-with-animated-mask" and strategy == "full-animation":
        strategy = "auto"
    mask = None if animated_mask is None else np.asarray(animated_mask, dtype=bool).copy()
    if mask is not None and mask.shape != original.shape[1:3]:
        raise ValueError("animated_mask must have the frame's (height, width) shape")
    roi = None
    if strategy in {"steam", "glow"}:
        roi = original[0, :, :, 3] >= 128
        if strategy == "steam":
            box = _bbox(roi)
            if box:
                roi[:box[1] + round((box[3] - box[1]) * .3)] = False
        if not roi.any():
            roi = None
    shifts = [(0, 0)] + [detect_shift(original[0], f, max_shift, roi) for f in original[1:]]
    aligned = np.stack([_translate(f, *shift) for f, shift in zip(original, shifts)])
    clipped = [int((f[:, :, 3] > 0).sum() - (a[:, :, 3] > 0).sum()) for f, a in zip(original, aligned)]
    if any(clipped):
        raise ValueError(f"Alignment would clip visible pixels: {clipped}; use a larger source cell")
    if mask is None:
        mask = _animated_mask(aligned, strategy) if len(frames) > 1 else np.zeros(original.shape[1:3], dtype=bool)
    varying_before = np.any(aligned != aligned[0], axis=(0, 3))
    corrected = int(np.count_nonzero(np.any(aligned[1:] != aligned[0], axis=3) & ~mask))
    result = aligned.copy()
    result[:, ~mask] = aligned[0, ~mask]
    static_identical = bool(np.all(result[:, ~mask] == result[0, ~mask]))
    if not static_identical:
        raise AssertionError("Static pixels are not byte-identical")
    report = {
        "prop": prop, "frames": len(frames),
        "mode": "full-animation" if strategy == "full-animation" else "static-body-with-animated-mask",
        "mask_strategy": "caller" if animated_mask is not None else strategy,
        "shifts_applied": [list(s) for s in shifts],
        "max_shift_fixed": max(max(abs(x), abs(y)) for x, y in shifts),
        "pixels_made_identical": corrected,
        "pixels_made_identical_pct": round(100 * corrected / max(1, (len(frames) - 1) * mask.size), 4),
        "static_region_pct": round(100 * (~mask).mean(), 4),
        "animated_region_bbox": _bbox(mask), "animated_region_pixels": int(mask.sum()),
        "varying_pixels_before": int(varying_before.sum()),
        "varying_pixels_after": int(np.any(result != result[0], axis=(0, 3)).sum()),
        "non_animated_pixels_identical": static_identical, "clipped_pixels": clipped,
        "warnings": [],
    }
    if len(frames) > 1 and varying_before.any() and not mask.any():
        report["warnings"].append("No compact animated region detected; inspect or provide animated_mask")
    if len(frames) > 1 and report["max_shift_fixed"] == max_shift and max_shift:
        report["warnings"].append("Shift reached search boundary; inspect or increase max_shift")
    return [Image.fromarray(f) for f in result], mask, report


def _crop_frames(image, rectangles):
    frames = []
    for r in rectangles:
        x, y, w, h = (r[k] for k in ("x", "y", "w", "h"))
        if not all(isinstance(v, int) for v in (x, y, w, h)) or min(x, y) < 0 or min(w, h) < 1 or x + w > image.width or y + h > image.height:
            raise ValueError(f"Invalid frame rectangle: {r}")
        frames.append(image.crop((x, y, x + w, y + h)))
    return frames


def normalize_sheet(image, manifest, *, prop=None, max_shift=6):
    """Return (new RGBA sheet, per-row report), with no filesystem side effects.

    frame_layout.rows is authoritative. Only frames within a row are compared;
    unrelated states, static choices, timing, anchors and trailing cells survive.
    """
    source = image.convert("RGBA")
    layout = manifest["frame_layout"]
    if source.size != (layout["sheetWidth"], layout["sheetHeight"]):
        raise ValueError("Sheet dimensions disagree with the production manifest")
    result = source.copy()
    rows = []
    sheet_prop = prop or manifest["characterId"]
    for state, rectangles in layout["rows"].items():
        if len(rectangles) != manifest["animation"]["rows"][state]["frames"] or not rectangles:
            raise ValueError(f"Invalid frame count for {state}")
        frames = _crop_frames(source, rectangles)
        row_prop = state if _name(sheet_prop) == "props" else sheet_prop
        choices = state == "variants" or (_name(sheet_prop) == "props" and state in STATIC_CHOICES)
        if choices:
            rows.append({"prop": row_prop, "state": state, "frames": len(frames), "status": "static-choices-preserved"})
            continue
        normalized, mask, report = normalize_frames(frames, row_prop, max_shift=max_shift)
        for r, f in zip(rectangles, normalized):
            result.paste(f, (r["x"], r["y"]))  # Copy RGBA bytes, do not alpha-composite.
        report.update(state=state, status="normalized" if len(frames) > 1 else "single-frame-preserved")
        rows.append(report)
    return result, {"prop": sheet_prop, "rows": rows}


def _qa_strip(before, after, title, path):
    """Two labelled rows of 4x nearest-neighbour frames, on a checkerboard."""
    w, h = before[0].size
    canvas = Image.new("RGB", (w * 4 * len(before), (h * 4 + 24) * 2), (239, 232, 214))
    draw = ImageDraw.Draw(canvas)
    for row, frames in enumerate((before, after)):
        top = row * (h * 4 + 24)
        draw.text((8, top + 5), f'{title} / {"BEFORE" if row == 0 else "AFTER"} / {len(frames)} frame(s)', fill=(20, 25, 25))
        for y in range(top + 24, top + 24 + h * 4, 16):
            for x in range(0, canvas.width, 16):
                if ((y - top - 24) // 16 + x // 16) % 2:
                    draw.rectangle((x, y, x + 15, y + 15), fill=(216, 212, 199))
        for index, frame in enumerate(frames):
            enlarged = frame.resize((w * 4, h * 4), Image.Resampling.NEAREST)
            canvas.paste(enlarged, (index * w * 4, top + 24), enlarged)
    canvas.save(path)


def _sources():
    env2 = ROOT / "assets/art/env2/sheets"
    known_props = {p.name.removesuffix(".manifest.json") for p in env2.glob("*.manifest.json")}
    for directory, subdir in [(env2, ""), (ROOT / "assets/art/env3/sheets", "env3"), (ROOT / "assets/art/sheets/locked", "locked")]:
        for path in sorted(directory.glob("*.manifest.json")):
            manifest = json.loads(path.read_text(encoding="utf-8"))
            name = path.name.removesuffix(".manifest.json")
            if subdir == "locked" and name not in known_props | set(PROP_RULES) | {"props"}:
                continue  # Locked human-character sheets are outside this task.
            if manifest["cell"]["width"] != 64 or manifest["cell"]["height"] != 64:
                continue  # Environment tiles/walls are not prop animations.
            yield path, manifest, subdir
    # The current TV and pantry appliances live in the original mixed prop sheet.
    base = ROOT / "assets/art/sheets/props.manifest.json"
    if base.exists():
        yield base, json.loads(base.read_text(encoding="utf-8")), "base"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "assets/art/env2/normalized")
    parser.add_argument("--qa-dir", type=Path, default=Path(os.environ.get("TEMP", "/tmp")) / "anim-qa")
    parser.add_argument("--max-shift", type=int, default=6)
    args = parser.parse_args(argv)
    output = args.output.resolve()
    sources = list(_sources())
    source_dirs = {p.parent.resolve() for p, _, _ in sources}
    if any(output == d or output in d.parents or d in output.parents for d in source_dirs):
        parser.error("Output must be separate from every source sheet directory")
    qa_dir = args.qa_dir.resolve()
    if qa_dir == output or qa_dir in output.parents or output in qa_dir.parents or any(qa_dir == d or qa_dir in d.parents or d in qa_dir.parents for d in source_dirs):
        parser.error("QA directory must be separate from source and output directories")
    output.mkdir(parents=True, exist_ok=True)
    qa_dir.mkdir(parents=True, exist_ok=True)
    report = {"version": 1, "max_shift_search": args.max_shift, "reference_frame": 0,
              "bbox_convention": "cell-local [x0,y0,x1,y1], exclusive upper bounds",
              "sheets": [], "qa_images": {}}
    print(f'{"Prop / state":62} {"shift":>5} {"fixed %":>8} {"animation bbox":>22}  static RGBA')
    for path, manifest, subdir in sources:
        src = (path.parent / manifest["game_input"]).resolve()
        if src.parent != path.parent.resolve():
            raise ValueError(f"Sheet filename must be local to its manifest: {src}")
        source_hash = hashlib.sha256(src.read_bytes()).hexdigest()
        manifest_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        with Image.open(src) as im:
            image = im.convert("RGBA")
        normalized, entry = normalize_sheet(image, manifest, max_shift=args.max_shift)
        dest = output / subdir
        dest.mkdir(parents=True, exist_ok=True)
        normalized.save(dest / src.name)
        new_manifest = copy.deepcopy(manifest)
        for key in ("base_image", "sprite_sheet_alpha_report"):
            if new_manifest.get(key):
                target = (path.parent / new_manifest[key]).resolve()
                new_manifest[key] = Path(os.path.relpath(target, dest)).as_posix()
        new_manifest.update(game_input=src.name, sprite_sheet_alpha=src.name)
        (dest / path.name).write_text(json.dumps(new_manifest, indent=2) + "\n", encoding="utf-8")
        entry.update(source_manifest=path.relative_to(ROOT).as_posix(), source_png=src.relative_to(ROOT).as_posix(),
                     source_sha256=source_hash, manifest_sha256=manifest_hash,
                     output_png=(dest / src.name).relative_to(output).as_posix(), output_manifest=(dest / path.name).relative_to(output).as_posix())
        for row in entry["rows"]:
            label = f'{subdir + "/" if subdir else ""}{entry["prop"]}/{row["state"]}'
            if row["status"] == "static-choices-preserved":
                print(f'{label:62} {"-":>5} {"-":>8} {"-":>22}  CHOICES preserved')
                continue
            check = "PASS" if row["frames"] > 1 else "STATIC (1 frame)"
            print(f'{label:62} {row["max_shift_fixed"]:5} {row["pixels_made_identical_pct"]:8.4f} {str(row["animated_region_bbox"]):>22}  {check}')
            for warning in row["warnings"]:
                print(f'  WARNING: {label}: {warning}')
            qa_prop = _name(row["prop"])
            if qa_prop in QA_PROPS and qa_prop not in report["qa_images"]:
                rectangles = manifest["frame_layout"]["rows"][row["state"]]
                qa_path = qa_dir / f"{qa_prop}-before-after-4x.png"
                _qa_strip(_crop_frames(image, rectangles), _crop_frames(normalized, rectangles), qa_prop, qa_path)
                report["qa_images"][qa_prop] = str(qa_path)
        if hashlib.sha256(src.read_bytes()).hexdigest() != source_hash or hashlib.sha256(path.read_bytes()).hexdigest() != manifest_hash:
            raise RuntimeError(f"Source changed during normalization: {path}")
        entry["source_bytes_unchanged"] = True
        report["sheets"].append(entry)
    report["missing_qa_props"] = sorted(set(QA_PROPS) - report["qa_images"].keys())
    report_path = output / "report.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"\nWrote {len(report['sheets'])} sheets + manifests: {output}")
    print(f"Report: {report_path}; QA: {qa_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
