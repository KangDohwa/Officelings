"""Env3 static assembly and byte-level QA. Writes only beside this script.

Use the installed sprite-gen venv's Python. `check` needs only Pillow and the
standard library: no sprite-gen CLI, model, browser or external QA tools.
"""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps

ROOT = Path(__file__).resolve().parent
ENV2 = ROOT.parent / "env2"
CELL = 64
ANCHOR = (32, 61)
PALETTE = [tuple(c) for c in json.loads((ENV2 / "palette.json").read_text(encoding="utf-8"))["colors"]]
C = {
    "outline": (25, 30, 30, 255), "dark": (16, 40, 44, 255),
    "wood": (120, 65, 40, 255), "wood_mid": (154, 92, 58, 255),
    "wood_light": (206, 137, 79, 255),
    "honey": (181, 128, 67, 255), "ivory_dark": (219, 197, 163, 255),
    "ivory": (231, 209, 172, 255), "cream": (250, 236, 201, 255),
    "teal_dark": (29, 58, 57, 255), "teal": (35, 94, 92, 255),
    "code": (93, 177, 165, 255), "bright": (78, 181, 169, 255),
    "blue": (50, 85, 113, 255), "grey": (62, 64, 63, 255),
}


def write_json(relative, data):
    path = ROOT / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def quantize(im):
    cache = {}
    pixels = []
    material_palette = [c[:3] for c in C.values()]
    for r, g, b, a in im.convert("RGBA").get_flattened_data():
        if a < 128:
            pixels.append((0, 0, 0, 0))
            continue
        rgb = (r, g, b)
        if rgb not in cache:
            cache[rgb] = min(material_palette, key=lambda c: sum((x - y) ** 2 for x, y in zip(rgb, c)))
        pixels.append((*cache[rgb], 255))
    out = Image.new("RGBA", im.size)
    out.putdata(pixels)
    return out


def fitted(index, size):
    path = ROOT / "runs/props/frames/static_props" / f"frame-{index}.png"
    im = Image.open(path).convert("RGBA")
    assert im.getbbox(), path
    im = im.crop(im.getbbox())
    scale = min(size[0] / im.width, size[1] / im.height)
    im = im.resize((round(im.width * scale), round(im.height * scale)), Image.Resampling.NEAREST)
    cell = Image.new("RGBA", (CELL, CELL))
    cell.alpha_composite(im, ((CELL - im.width) // 2, ANCHOR[1] - im.height))
    return quantize(cell)


def review_sources():
    out = Image.new("RGB", (768, 300), C["ivory"][:3])
    draw = ImageDraw.Draw(out)
    draw.fontmode = "1"
    for i, (label, size) in enumerate([("desk / right", (42, 44)), ("chair / right", (28, 34)), ("laptop table / right", (36, 36))]):
        im = fitted(i, size)
        draw.text((i * 256 + 8, 4), label, fill=C["outline"])
        out.paste(im.resize((256, 256), Image.Resampling.NEAREST), (i * 256, 24), im.resize((256, 256), Image.Resampling.NEAREST))
        im.save(ROOT / "qa" / f"fitted-{i}.png")
        print(label, "bbox:", im.getbbox())
    out.save(ROOT / "qa/source-review.png")


# Pixel coordinates were reviewed on the canonical fitted sprites.
# These are screen interiors, never whole display/desk bounding boxes.
SCREEN = (38, 26, 39, 34)  # inclusive: two-pixel side plane, 18 pixels
FAR_SCREEN = (32, 20, 33, 28)
SEAT = {"right": (31, 44), "left": (32, 44)}
CHAIR_OFFSET = {"right": (-26, 0), "left": (26, 0)}


def screen_mask(boxes):
    mask = Image.new("L", (CELL, CELL))
    d = ImageDraw.Draw(mask)
    for box in boxes:
        d.rectangle(box, fill=255)
    return mask


def monitor_frame(base, boxes, mode, phase=0):
    out = base.copy()
    d = ImageDraw.Draw(out)
    for x0, y0, x1, y1 in boxes:
        d.rectangle((x0, y0, x1, y1), fill=C["dark"] if mode != "typing" or phase != 1 else C["teal_dark"])
        if mode == "off":
            continue
        for line, y in enumerate(range(y0 + 1, y1, 3)):
            # Short horizontal code dashes seen edge-on; no legible letters.
            length = 1 if (line + phase) % 2 else 2
            color = C["code"] if mode == "on" else (C["teal"], C["bright"], C["code"])[phase]
            d.line((x0, y, min(x1, x0 + length - 1), y), fill=color)
        if mode == "typing":
            d.point((x1, y1 - 1 - phase), fill=(C["code"], C["cream"], C["bright"])[phase])
    return out


def white_top(base):
    out = base.copy()
    for y in range(37, 43):
        for x in range(CELL):
            color = out.getpixel((x, y))
            if color in (C["wood"], C["wood_mid"], C["honey"], C["wood_light"]):
                out.putpixel((x, y), {37: C["ivory"], 38: C["cream"], 39: C["ivory"],
                                     40: C["cream"], 41: C["ivory"], 42: C["ivory_dark"]}[y])
    return out


def double_monitor(base):
    far = Image.new("RGBA", (CELL, CELL))
    far.alpha_composite(base.crop((37, 23, 53, 37)), (31, 17))
    d = ImageDraw.Draw(far)
    d.rectangle((39, 30, 42, 38), fill=C["outline"])
    d.rectangle((40, 30, 41, 37), fill=C["ivory_dark"])
    far.alpha_composite(base)
    return far


def split_chair(chair):
    front_mask = Image.new("L", chair.size)
    d = ImageDraw.Draw(front_mask)
    d.rectangle((29, 39, 39, 41), fill=255)  # near armrest
    d.rectangle((32, 42, 33, 48), fill=255)  # near arm support
    d.rectangle((25, 48, 43, 49), fill=255)  # seat front lip
    back, front = Image.new("RGBA", chair.size), Image.new("RGBA", chair.size)
    back.paste(chair, mask=ImageOps.invert(front_mask))
    front.paste(chair, mask=front_mask)
    return back, front


def sheet(name, rows):
    cols = max(map(len, rows.values()))
    out = Image.new("RGBA", (CELL * cols, CELL * len(rows)))
    m = json.loads((ENV2 / "sheets/desks.manifest.json").read_text(encoding="utf-8"))
    m.update(characterId=name, game_input=name + ".png", sprite_sheet_alpha=name + ".png",
             sprite_sheet_alpha_report="../qa/export-report.json", base_image="../runs/props/base-source.png")
    m["animation"] = {"cellWidth": CELL, "cellHeight": CELL, "columns": cols, "rows": {}}
    m["frame_layout"] = {"sheetWidth": out.width, "sheetHeight": out.height, "cellWidth": CELL, "cellHeight": CELL, "rows": {}}
    for r, (state, frames) in enumerate(rows.items()):
        fps = 3 if state == "typing" else 1
        m["animation"]["rows"][state] = {"row": r, "frames": len(frames), "fps": fps,
            "durations_ms": [round(1000 / fps)] * len(frames), "loop": state == "typing", "frame_variant": "pixel"}
        rects = []
        for i, im in enumerate(frames):
            out.alpha_composite(im, (i * CELL, r * CELL))
            rects.append({"x": i * CELL, "y": r * CELL, "w": CELL, "h": CELL})
        m["frame_layout"]["rows"][state] = rects
    out.save(ROOT / "sheets" / (name + ".png"))
    write_json("sheets/" + name + ".manifest.json", m)


def build():
    (ROOT / "sheets").mkdir(exist_ok=True)
    (ROOT / "qa/masks").mkdir(parents=True, exist_ok=True)
    # Retain env2's palette verbatim; introduce zero additional colors.
    (ROOT / "palette.json").write_bytes((ENV2 / "palette.json").read_bytes())
    desk, chair, laptop = fitted(0, (42, 44)), fitted(1, (28, 34)), fitted(2, (36, 36))
    back, front = split_chair(chair)
    for double in (False, True):
        base = double_monitor(desk) if double else desk
        boxes = [SCREEN, FAR_SCREEN] if double else [SCREEN]
        for material in ("wood", "white"):
            body = white_top(base) if material == "white" else base
            rows = {"off": [monitor_frame(body, boxes, "off")], "on": [monitor_frame(body, boxes, "on")],
                    "typing": [monitor_frame(body, boxes, "typing", phase) for phase in range(3)]}
            for facing in ("right", "left"):
                name = "desk-side-" + ("double-" if double else "") + material + "-" + facing
                mirror = ImageOps.mirror if facing == "left" else lambda im: im
                sheet(name, {state: [mirror(im) for im in frames] for state, frames in rows.items()})
                mirror(screen_mask(boxes)).save(ROOT / "qa/masks" / (name + ".png"))
    # Laptop pair is static idle; no extra animation requested.
    laptop = monitor_frame(laptop, [(38, 28, 39, 34)], "on")
    for facing in ("right", "left"):
        mirror = ImageOps.mirror if facing == "left" else lambda im: im
        for suffix, im in (("", chair), ("-back", back), ("-front", front)):
            sheet("office-chair-side-" + facing + suffix, {"variants": [mirror(im)]})
        sheet("laptop-table-side-" + facing, {"variants": [mirror(laptop)]})
    check()
    contacts()
    preview()


def load_frames(name):
    m = json.loads((ROOT / "sheets" / (name + ".manifest.json")).read_text(encoding="utf-8"))
    im = Image.open(ROOT / "sheets" / m["game_input"]).convert("RGBA")
    rows = {state: [im.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])) for f in frames]
            for state, frames in m["frame_layout"]["rows"].items()}
    return m, rows


def assert_screen_only(reference, frame, mask):
    a, b, allowed = reference.tobytes(), frame.tobytes(), mask.tobytes()
    changed = [i for i in range(CELL * CELL) if a[i * 4:i * 4 + 4] != b[i * 4:i * 4 + 4]]
    outside = [i for i in changed if not allowed[i]]
    assert not outside, f"non-screen RGBA changed at {(outside[0] % CELL, outside[0] // CELL)}"
    assert reference.getchannel("A").tobytes() == frame.getchannel("A").tobytes(), "alpha silhouette changed"
    return len(changed)


def check():
    """Reload final PNGs, compare all RGBA bytes outside precise screen masks."""
    template = json.loads((ENV2 / "sheets/desks.manifest.json").read_text(encoding="utf-8"))
    paths = sorted((ROOT / "sheets").glob("*.manifest.json"))
    assert len(paths) == 16, f"expected 16 sheets, found {len(paths)}"
    report = {"sheets": [], "animations": [], "chair_layer_checks": [], "errors": []}
    all_colors = set()
    for path in paths:
        name = path.name.removesuffix(".manifest.json")
        m, rows = load_frames(name)
        assert set(m) == set(template), path
        assert m["cell"] == template["cell"], path
        im = Image.open(path.with_name(m["game_input"])).convert("RGBA")
        assert im.size == (m["frame_layout"]["sheetWidth"], m["frame_layout"]["sheetHeight"])
        assert im.size == (m["animation"]["columns"] * CELL, len(rows) * CELL)
        for state, frames in rows.items():
            r = m["animation"]["rows"][state]
            assert len(frames) == r["frames"] == len(r["durations_ms"])
            assert r["loop"] == (state == "typing")
            for i, frame in enumerate(frames):
                f = m["frame_layout"]["rows"][state][i]
                assert f == {"x": i * CELL, "y": r["row"] * CELL, "w": CELL, "h": CELL}
                box = frame.getbbox()
                assert box and box[0] >= 2 and box[1] >= 3 and box[2] <= 62 and box[3] <= 61
                for red, green, blue, alpha in frame.get_flattened_data():
                    assert alpha in (0, 255), (name, "partial alpha")
                    if alpha:
                        assert (red, green, blue) in PALETTE, (name, "new palette color")
                        assert not (red > 220 and green < 40 and blue > 220), (name, "chroma residue")
                        all_colors.add((red, green, blue))
                    else:
                        assert red == green == blue == 0, (name, "hidden RGB")
            # Unused static-row cells must stay fully byte-zero.
            for i in range(len(frames), m["animation"]["columns"]):
                assert not any(im.crop((i * CELL, r["row"] * CELL, (i + 1) * CELL, (r["row"] + 1) * CELL)).tobytes())
        report["sheets"].append({"file": m["game_input"], "frames": sum(map(len, rows.values())), "size": im.size})
        if name.startswith("desk-side-"):
            mask = Image.open(ROOT / "qa/masks" / (name + ".png")).convert("L")
            assert mask.size == (CELL, CELL)
            expected = screen_mask([SCREEN, FAR_SCREEN] if "double" in name else [SCREEN])
            if name.endswith("-left"):
                expected = ImageOps.mirror(expected)
            assert mask.tobytes() == expected.tobytes(), (name, "screen mask geometry changed")
            assert sum(bool(v) for v in mask.tobytes()) == (36 if "double" in name else 18)
            assert set(mask.tobytes()) == {0, 255}
            flat = rows["off"] + rows["on"] + rows["typing"]
            changes = [assert_screen_only(flat[0], f, mask) for f in flat]
            assert all(changes[1:]), (name, "unchanging on/typing")
            assert len(set(f.tobytes() for f in flat)) == 5, (name, "duplicate state/frame")
            outside = b"".join(flat[0].tobytes()[i * 4:i * 4 + 4] for i, v in enumerate(mask.tobytes()) if not v)
            report["animations"].append({"sheet": name, "screen_pixels": sum(bool(v) for v in mask.tobytes()),
                "frames_compared": 5, "changed_pixels_vs_off": changes, "non_screen_changed_pixels": 0,
                "non_screen_rgba_sha256": hashlib.sha256(outside).hexdigest(), "alpha_identical": True})
            # Negative control: checker must reject one altered desk-body pixel.
            bad = flat[0].copy()
            bad.putpixel((32, 42), (255, 0, 255, 255))
            try:
                assert_screen_only(flat[0], bad, mask)
            except AssertionError:
                pass
            else:
                raise AssertionError("checker failed to detect planted body jitter")
    for facing in ("right", "left"):
        whole = load_frames("office-chair-side-" + facing)[1]["variants"][0]
        back = load_frames("office-chair-side-" + facing + "-back")[1]["variants"][0]
        front = load_frames("office-chair-side-" + facing + "-front")[1]["variants"][0]
        joined = back.copy()
        joined.alpha_composite(front)
        assert joined.tobytes() == whole.tobytes(), (facing, "chair layers differ")
        assert not any(a and b for a, b in zip(back.getchannel("A").tobytes(), front.getchannel("A").tobytes()))
        report["chair_layer_checks"].append({"facing": facing, "reassembly_byte_identical": True, "seat_anchor": SEAT[facing]})
    for path in paths:
        name = path.name.removesuffix(".manifest.json")
        if "-right" in name:
            other = name.replace("-right", "-left")
            right, left = load_frames(name)[1], load_frames(other)[1]
            for state in right:
                assert all(ImageOps.mirror(r).tobytes() == l.tobytes() for r, l in zip(right[state], left[state]))
    report.update(sheet_count=len(paths), frame_count=sum(s["frames"] for s in report["sheets"]),
                  palette_colors_used=len(all_colors), new_palette_colors=0, planted_body_jitter_detected=True)
    assert report["frame_count"] == 48
    assert (ROOT / "palette.json").read_bytes() == (ENV2 / "palette.json").read_bytes()
    write_json("qa/export-report.json", report)
    print(f"PASS: {report['sheet_count']} sheets / {report['frame_count']} frames; 8 screen-only animations, chair layers and palette checked.")


def silhouette(facing):
    out = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(out)
    color = C["blue"]
    # Deliberately faceless seated shape, pelvis anchor (31,44).
    d.polygon([(27, 23), (34, 23), (37, 26), (37, 29), (39, 30), (37, 31), (36, 33),
               (31, 33), (28, 30), (26, 28), (26, 25)], fill=color)
    d.polygon([(30, 32), (35, 32), (38, 37), (36, 43), (41, 44), (42, 48),
               (30, 47), (28, 43), (28, 36)], fill=color)
    d.line((34, 35, 40, 38, 48, 38), fill=color, width=3)
    d.line((38, 45, 43, 46, 43, 56), fill=color, width=4)
    d.rectangle((42, 56, 45, 59), fill=color)
    d.rectangle((42, 59, 49, 60), fill=color)
    d.line((33, 47, 38, 50, 38, 57), fill=color, width=3)
    d.rectangle((37, 57, 42, 59), fill=color)
    return ImageOps.mirror(out) if facing == "left" else out


def contacts():
    names = sorted(p.name.removesuffix(".manifest.json") for p in (ROOT / "sheets").glob("desk*.manifest.json"))
    for page in range(2):
        out = Image.new("RGB", (768, 576), C["ivory"][:3])
        d = ImageDraw.Draw(out)
        d.fontmode = "1"
        for row, name in enumerate(names[page * 4:page * 4 + 4]):
            frames = load_frames(name)[1]
            d.text((8, row * 144 + 2), name, fill=C["outline"])
            flat = frames["off"] + frames["on"] + frames["typing"]
            for col, (label, im) in enumerate(zip(("off", "on", "typing 0", "typing 1", "typing 2"), flat)):
                d.text((col * 144 + 8, row * 144 + 14), label, fill=C["outline"])
                large = im.resize((128, 128), Image.Resampling.NEAREST)
                out.paste(large, (col * 144, row * 144 + 16), large)
        out.save(ROOT / "qa" / f"contact-desks-{page + 1}.png")
    out = Image.new("RGB", (640, 320), C["ivory"][:3])
    d = ImageDraw.Draw(out)
    d.fontmode = "1"
    for row, facing in enumerate(("left", "right")):
        for col, suffix in enumerate(("", "-back", "-front")):
            im = load_frames("office-chair-side-" + facing + suffix)[1]["variants"][0]
            d.text((col * 128 + 4, row * 160 + 3), facing + (suffix or " full"), fill=C["outline"])
            large = im.resize((128, 128), Image.Resampling.NEAREST)
            out.paste(large, (col * 128, row * 160 + 24), large)
        im = load_frames("laptop-table-side-" + facing)[1]["variants"][0]
        large = im.resize((128, 128), Image.Resampling.NEAREST)
        d.text((388, row * 160 + 3), "laptop " + facing, fill=C["outline"])
        out.paste(large, (384, row * 160 + 24), large)
        joined = load_frames("office-chair-side-" + facing + "-back")[1]["variants"][0].copy()
        joined.alpha_composite(silhouette(facing))
        joined.alpha_composite(load_frames("office-chair-side-" + facing + "-front")[1]["variants"][0])
        large = joined.resize((128, 128), Image.Resampling.NEAREST)
        d.text((516, row * 160 + 3), "seat layers", fill=C["outline"])
        out.paste(large, (512, row * 160 + 24), large)
    out.save(ROOT / "qa/contact-chairs-laptops.png")


def preview():
    canvas = Image.new("RGBA", (640, 352), C["dark"])
    floor = Image.open(ENV2 / "sheets/floor-wood-oak.png").convert("RGBA")
    wall = Image.open(ENV2 / "sheets/wall-navy.png").convert("RGBA").crop((0, 0, 32, 32))
    for y in range(64, 352, 32):
        for x in range(0, 640, 32):
            canvas.alpha_composite(floor, (x, y))
    for x in range(0, 640, 32):
        canvas.alpha_composite(wall, (x, 32))
    placements = []
    d = ImageDraw.Draw(canvas)
    d.fontmode = "1"
    d.text((16, 9), "ENV3 / SIDE WORKSTATIONS / SEATED PLACEHOLDERS / 2x NEAREST", fill=C["cream"])
    for row, facing in enumerate(("left", "right")):
        top = 96 + row * 128
        d.text((16, top - 24), facing.upper() + "-FACING WORKERS", fill=C["outline"])
        choices = [("desk-side-wood-", "on", "WOOD / IDLE"), ("desk-side-white-", "off", "WHITE / OFF"),
                   ("desk-side-double-wood-", "typing", "DUAL CRT / TYPING"), ("laptop-table-side-", "variants", "LAPTOP / OVERFLOW")]
        for col, (prefix, state, label) in enumerate(choices):
            name, x = prefix + facing, 32 + col * 152
            dx, dy = CHAIR_OFFSET[facing]
            chair_at = (x + dx, top + dy)
            back = load_frames("office-chair-side-" + facing + "-back")[1]["variants"][0]
            front = load_frames("office-chair-side-" + facing + "-front")[1]["variants"][0]
            desk = load_frames(name)[1][state][1 if state == "typing" else 0]
            canvas.alpha_composite(back, chair_at)
            canvas.alpha_composite(silhouette(facing), chair_at)
            canvas.alpha_composite(desk, (x, top))
            canvas.alpha_composite(front, chair_at)
            d.text((x - 6, top + 68), label, fill=C["outline"])
            placements.append({"sheet": name, "state": state, "frame": 1 if state == "typing" else 0,
                "desk_cell_top_left": [x, top], "chair_cell_top_left": chair_at,
                "chair_seat_anchor": SEAT[facing], "seat_in_scene": [chair_at[0] + SEAT[facing][0], chair_at[1] + SEAT[facing][1]],
                "facing": facing, "character": "placeholder silhouette", "draw_order": ["chair-back", "placeholder", "desk", "chair-front"]})
    canvas.resize((1280, 704), Image.Resampling.NEAREST).save(ROOT / "preview-env3.png")
    write_json("scene.json", {"native_size": [640, 352], "export_scale": 2, "anchor": ANCHOR,
        "background_sources": ["../env2/sheets/floor-wood-oak.png", "../env2/sheets/wall-navy.png"], "placements": placements})
    print("Preview: 1280x704, nearest-neighbor 2x; two rows, eight seated placeholders.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("build", "check", "review"), nargs="?", default="build")
    args = parser.parse_args()
    (ROOT / "qa").mkdir(exist_ok=True)
    {"build": build, "check": check, "review": review_sources}[args.action]()
