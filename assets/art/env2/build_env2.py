"""Environment Pack 2: recipes and deterministic assembly of sprite-gen outputs."""
import json
import sys
import math
import shutil
import numpy as np
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
ART = ROOT.parent
KEY = {"name": "magenta", "hex": "#FF00FF", "rgb": [255, 0, 255], "selection": "manual"}
STYLE = (
    "Match the attached existing office props EXACTLY: cozy warm 2D chibi-world game pixel art, "
    "orthographic three-quarter top-down front view with horizontal front edges, NEVER isometric. "
    "True chunky logical pixels, crisp 1 pixel selective dark charcoal/navy outlines, warm walnut/honey wood, "
    "cream, muted teal, dusty blue, brick red, terracotta and olive. Two or three flat shades per material. "
    "No gradients, antialiasing, noise, dithering, text, people, ground shadows or floor patches. "
    "Do not copy the layout guide as boxes. Objects are separate connected silhouettes with huge magenta gaps. "
    "Each stated frame is a separate asset. Preserve readable details at native scale."
)

# State row -> (frame count, action). Native object scale is documented at export.
PROPS = {
    "bookshelf": (2, "Two distinct walnut BOOKSHELVES, each 38x50 logical pixels. Frame 0 tall three-shelf bookcase with teal/red/cream books and a brass bookend. Frame 1 two shelves above two cream cabinet doors, colorful books and a tiny pot attached to the top. All books touch shelves."),
    "pictures": (4, "Four different small FRAMED PICTURES, each 28x22 pixels, straight frontal wall art. 0 cream mountain/sun landscape in walnut frame, 1 teal botanical leaf print in honey frame, 2 sleeping orange cat portrait in walnut frame, 3 abstract red/blue/cream geometric blocks in honey frame. No words, no wall."),
    "boards": (2, "Frame 0 a small round ivory WALL CLOCK with dark rim, black hands at 10:10 and a muted red second hand, 22x22 pixels, no numbers. Frame 1 a horizontal WHITEBOARD in thin walnut frame, 48x28 pixels, with teal/red block diagrams and two tiny markers resting in connected bottom tray, no legible words. Wall mounted, no wall."),
    "storage": (2, "Frame 0 a cream three-drawer FILING CABINET with charcoal outlines, little brass label slots and feet, 28x44 pixels. Frame 1 a compact cream/charcoal desktop PRINTER, 30x22 pixels, with one sheet of paper extending upward and teal ready lamp, all connected. No table."),
    "tables": (3, "Frame 0 BIG WALNUT MEETING TABLE AND FOUR CHAIRS SET, one connected compact grouping, horizontal rectangular table visible from above with two small chairs at each long side, about 60x48 pixels total. Chairs overlap table edge so entire set is a single component. Frame 1 ROUND WOOD TABLE with round honey top and short central pedestal, 36x30 pixels. Frame 2 low rectangular COFFEE TABLE with walnut rim, honey top, visible short feet and two magazines ON top, 46x24 pixels. No floor."),
    "desks": (2, "Frame 0 WOOD DESK, plain walnut 54x30 pixels, large clear honey top surface, slim drawer on left and two visible legs. Frame 1 WHITE DESK same geometry with warm ivory top and walnut legs, little red notebook touching back corner. No monitor, keyboard, chair, person or floor. Separate monitors will be layered onto tops."),
    "chairs": (3, "Three identical ergonomic OFFICE CHAIRS facing up toward a desk, 26x32 pixels: frame 0 muted teal upholstery, 1 dusty blue upholstery, 2 brick-red upholstery. Rounded backrest, seat and charcoal wheeled base. Only upholstery colors differ; identical scale and shape. No people."),
    "monitors": (3, "Three desktop MONITOR assets with integrated little keyboard touching base, front-facing, 30x25 pixels: 0 cream old chunky CRT with dark green pixel terminal, 1 slim charcoal modern screen with teal terminal blocks, 2 two slim adjacent charcoal monitors sharing joined base and keyboard, 42x25 pixels. Clean unlettered screen blocks, no desk."),
    "seating": (3, "Frame 0 brick RED TWO-SEAT SOFA facing DOWN, 58x30 logical pixels, tall back behind two cushions, generous arms, walnut feet and one cream pillow touching cushion. Frame 1 SAME RED TWO-SEAT SOFA facing RIGHT, long axis vertical on screen, 32x50 pixels, backrest along LEFT edge, seats open toward RIGHT, no diagonal rotation. Frame 2 warm cream-and-teal ARMCHAIR facing down, 30x32 pixels with walnut feet and cream pillow. No floor."),
    "rugs": (2, "Two RECTANGULAR RUGS seen straight overhead, each 56x38 pixels, no perspective, shadow or solid surrounding floor: 0 dusty blue field with cream stepped geometric border and a tiny muted teal center diamond, 1 brick red field with honey/cream border and small stepped central diamond. Crisp quiet woven pixel motifs, full connected rectangular silhouette. No fringe separated from rug."),
    "plants": (5, "Five different connected houseplants in terracotta/cream pots: 0 broad-leaf MONSTERA 28x38 pixels, 1 tall narrow SNAKE PLANT 24x40, 2 dense FERN 30x32, 3 tiny round SUCCULENT 20x20, 4 indoor FICUS TREE 32x50 with round layered olive canopy and visible brown trunk. Muted olive, sage and deep teal leaves, cream highlights. Every leaf touches stem/pot, no floating particles."),
    "pantry": (4, "Four kitchen/pantry objects: 0 dark navy VENDING MACHINE 32x52 pixels with cream/red/teal drink rows behind dark glass, coin slot and dispensing opening; 1 warm ivory FRIDGE 30x50 pixels with two doors, dark handles, little teal magnet and red note; 2 walnut KITCHEN COUNTER/SINK 54x32 pixels, ivory countertop, blue-grey sink bowl, connected dark faucet, cream cabinet doors; 3 small ivory MICROWAVE 30x20 pixels with charcoal door, teal status light and two knobs. No floor or people."),
    "small": (3, "Three separate small props: 0 charcoal cylindrical TRASH BIN 18x22 pixels with ivory rim and one cream crumpled paper resting inside; 1 warm ivory MUG 12x12 pixels with brown coffee and handle connected on right, no steam; 2 cozy FLOOR LAMP 22x46 pixels, cream pleated shade, walnut pole and round charcoal base, all connected, no external glow."),
    "light": (1, "One top-down CEILING LIGHT WITH PIXEL GLOW, 48x28 pixels total. Small warm ivory/ochre hanging oval fixture centered within three concentric hard-edged pale honey pixel halos, no gradients. The halo is a compact connected sprite on magenta, intended to have semitransparency applied later. No chain, floor or other object."),
    "server": (3, "Three animation frames of the EXACT SAME dark navy SERVER RACK, 28x50 pixels. Five horizontal units, charcoal vents, little teal/amber LED lights. Frame 0 green LED on upper unit, frame 1 amber LED on middle unit, frame 2 teal LED on lower unit. ONLY the small LED pixels change; every cabinet edge and vent MUST be identical. No floor."),
    "cat_sleep": (1, "One adorable small orange tabby CAT SLEEPING curled into an oval, 24x16 logical pixels, tail wrapped around body, two closed dark eyes, cream muzzle and belly, little triangular ears, dark selective outline. Seen from top/front, no bed, pillow, floor or shadow."),
    "cat_walk": (2, "Two walking contact frames of the SAME adorable orange tabby CAT facing RIGHT, 28x20 pixels, cream muzzle/belly, black tiny eyes, two triangular ears, upright curled tail. Frame 0 near front paw forward and near back paw back; frame 1 near front paw back and near back paw forward. Body/head/size stable, opposite paws alternate, four paws recognizable. No floor, shadow or floating bits."),
    "vacuum": (2, "Two frames of SAME SMALL ROBOT VACUUM, 22x16 pixels, top-down 3/4 view, round ivory body with charcoal side skirt, teal top status LED and small connected side brush. Frame 0 brush points diagonally forward-right, frame 1 brush points directly right and LED changes amber. Keep body exactly same size/position, no floor or shadow."),
}
TILES = {
    "wood": (3, "Three full opaque SQUARE wooden floor textures, each true 32x32 logical pixels seen straight overhead. Four horizontal plank bands 8px high, subtle 1px joins, few short sparse grain lines. 0 honey oak, 1 warm walnut slightly darker, 2 pale honey with staggered plank ends. Use muted warm golds/browns from reference. No frame around tile, no perspective. Left/right edges seamless; top/bottom periodic. Separated square panels on magenta."),
    "carpet": (2, "Two full opaque square 32x32 CARPET TILES: 0 muted dusty BLUE, 1 muted brick RED. Quiet flat field with only a few sparse one-pixel weave dots, no motifs, no border, no outline, no perspective. Seamless across all edges. Separated square panels on magenta."),
    "hard_floor": (3, "Three full opaque square 32x32 floor textures, overhead, no border or outline: 0 BLACK/CREAM CHECKER PANTRY with exactly 4x4 squares each 8px, charcoal black and warm ivory; 1 BATHROOM ceramic tile, pale ivory/blue-grey with 2x2 big squares and 1px muted grout, no speckles; 2 warm grey STONE floor, 2x2 blocks with subtle brown-grey joins and two small texture pixels. All edges repeat seamlessly. Separated square panels on magenta."),
    "wall": (2, "Two full opaque square 32x32 FRONT WALL PANELS, NOT floor or perspective. 0 dark navy painted panel with muted teal inset, thin warm walnut chair rail at y=24 and dark charcoal baseboard last 3 pixels. 1 warm walnut vertical wood panel with sparse darker central seam, honey chair rail at y=24 and same charcoal baseboard last 3 pixels. Flat top edge, no side border. Left and right edges match. No objects or windows; structural variations assembled later from these panels."),
}

def prepare():
    (ROOT / "references").mkdir(parents=True, exist_ok=True)
    for kind, states, cell in [("props", PROPS, 64), ("tiles", TILES, 32)]:
        src = Image.open(ART / "sheets" / f"{kind}.png").convert("RGBA")
        bg = Image.new("RGBA", src.size, (231, 209, 172, 255))
        bg.alpha_composite(src)
        bg.resize((src.width * 3, src.height * 3), Image.Resampling.NEAREST).save(ROOT / "references" / f"{kind}.png")
        request = {
            "version": 1, "kind": "sprite-gen-request", "engine": "component-row",
            "character": {"id": f"env2-{kind}", "description": "Original environment assets; reference is style/palette only, no people", "base_image": "base-source.png"},
            "cell": {"shape": "square", "width": cell, "height": cell, "safe_margin_x": 2 if cell == 64 else 0, "safe_margin_y": 3 if cell == 64 else 0},
            "chroma_key": KEY,
            "states": {k: {"frames": n, "fps": 4 if k in ("cat_walk", "vacuum", "server") else 1, "loop": k in ("cat_walk", "vacuum", "server"), "action": action} for k, (n, action) in states.items()},
            "style": STYLE + (" TRUE 64x64 logical cell grid." if cell == 64 else " TRUE 32x32 logical tile grid; flat full opaque square textures."),
            "layout": "taxonomy/v1",
            "fit": {"pixel_unfake": False, "resample": "kcentroid", "palette_size": 48, "align_x": "bbox-center", "align_y": "bottom" if cell == 64 else "center", "ground_frames": cell == 64},
            "chroma": {"mode": "rgb", "unmix_reach": 4, "spill_max_fraction": 0.005},
        }
        (ROOT / f"request-{kind}.json").write_text(json.dumps(request, indent=2), encoding="utf-8")
    print("Prepared recipes and nearest-neighbor references; all files under env2.")

SPECS = {
    "bookshelf": ("bookshelf", [0, 1], [(38, 50)] * 2),
    "framed-pictures": ("pictures", list(range(4)), [(28, 22)] * 4),
    "wall-clock": ("boards", [0], [(22, 22)]),
    "whiteboard": ("boards", [1], [(48, 28)]),
    "filing-cabinet": ("storage", [0], [(28, 44)]),
    "printer": ("storage", [1], [(30, 22)]),
    "meeting-table-chairs": ("tables", [0], [(60, 48)]),
    "round-table": ("tables", [1], [(36, 30)]),
    "coffee-table": ("tables", [2], [(46, 24)]),
    "desks": ("desks", [0, 1], [(54, 30)] * 2),
    "office-chairs": ("chairs", [0, 1, 2], [(26, 32)] * 3),
    "monitors": ("monitors", [0, 1, 2], [(30, 25), (30, 25), (42, 25)]),
    "sofa-red": ("seating", [0, 1], [(58, 30), (32, 50)]),
    "armchair": ("seating", [2], [(30, 32)]),
    "rugs": ("rugs", [0, 1], [(56, 38)] * 2),
    "plants": ("plants", list(range(5)), [(28, 38), (24, 40), (30, 32), (20, 20), (32, 50)]),
    "vending-machine": ("pantry", [0], [(32, 52)]),
    "fridge": ("pantry", [1], [(30, 50)]),
    "kitchen-counter-sink": ("pantry", [2], [(54, 32)]),
    "microwave": ("pantry", [3], [(30, 20)]),
    "trash-bin": ("small", [0], [(18, 22)]),
    "mug": ("small", [1], [(12, 12)]),
    "lamp": ("small", [2], [(22, 46)]),
    "ceiling-light-glow": ("light", [0], [(48, 28)]),
    "server-rack": ("server", [0, 1, 2], [(28, 50)] * 3),
    "robot-vacuum": ("vacuum", [0, 1], [(22, 16)] * 2),
}

def palette():
    colors = set()
    for name in ["props", "tiles", "worker", "claude-worker-01", "antigravity-worker-01", "icons"]:
        a = np.asarray(Image.open(ART / "sheets" / f"{name}.png").convert("RGBA"))
        colors.update(map(tuple, a[a[:, :, 3] > 0, :3].tolist()))
    accents = [(97, 40, 43), (154, 66, 64), (191, 101, 84), (50, 85, 113), (72, 114, 143), (100, 146, 163)]
    colors.update(accents)
    result = np.array(sorted(colors), dtype=np.int32)
    (ROOT / "palette.json").write_text(json.dumps({"existing_source_sheets": ["props", "tiles", "worker", "claude-worker-01", "antigravity-worker-01", "icons"], "added_requested_red_blue_accents": accents, "colors": result.tolist()}, indent=2), encoding="utf-8")
    return result

def lock_palette(im, pal):
    a = np.array(im.convert("RGBA"))
    rgb = a[:, :, :3].astype(np.int32)
    distance = ((rgb[:, :, None, :] - pal[None, None, :, :]) ** 2).sum(axis=3)
    a[:, :, :3] = pal[distance.argmin(axis=2)]
    a[:, :, 3] = np.where(a[:, :, 3] >= 128, 255, 0)
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a)

def fitted(kind, state, index, size, pal):
    path = ROOT / "runs" / kind / "frames" / state / f"frame-{index}.png"
    im = Image.open(path).convert("RGBA")
    bbox = im.getbbox()
    assert bbox, path
    im = im.crop(bbox)
    if kind == "tiles":
        # Remove the generator's unwanted perimeter frame from the extracted panel.
        a = np.asarray(im)
        edges = np.concatenate((a[0], a[-1], a[:, 0], a[:, -1]))
        if (edges[:, :3].mean(axis=1) < 70).mean() > .7:
            im = im.crop((1, 1, im.width - 1, im.height - 1))
        im = im.resize((32, 32), Image.Resampling.NEAREST)
        im.putalpha(255)
        if state == "hard_floor" and index > 0:
            # Warm grey materials must not quantize into the character's violet ramp.
            allowed = [(137, 141, 123), (139, 147, 144), (92, 92, 90), (231, 209, 172), (250, 236, 201), (251, 248, 228), (219, 197, 163)]
            if index == 1:
                allowed.append((100, 146, 163))
            return lock_palette(im, np.array(allowed, dtype=np.int32))
        return lock_palette(im, pal)
    factor = min(size[0] / im.width, size[1] / im.height)
    im = im.resize((max(1, round(im.width * factor)), max(1, round(im.height * factor))), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (64, 64))
    canvas.alpha_composite(im, ((64 - im.width) // 2, 61 - im.height))
    return lock_palette(canvas, pal)

def sheet(name, rows, cell, kind, fps=1, loops=()):
    cols = max(len(v) for v in rows.values())
    image = Image.new("RGBA", (cell * cols, cell * len(rows)))
    m = json.loads((ART / "sheets" / f"{kind}.manifest.json").read_text(encoding="utf-8"))
    m.update(characterId=name, game_input=f"{name}.png", sprite_sheet_alpha=f"{name}.png", sprite_sheet_alpha_report="../qa/export-report.json", base_image=f"../runs/{kind}/base-source.png", curation_applied=True)
    m["cell"].update(shape="square", width=cell, height=cell, safe_margin_x=2 if cell == 64 else 0, safe_margin_y=3 if cell == 64 else 0)
    m["chroma_key"] = KEY
    m["animation"] = {"cellWidth": cell, "cellHeight": cell, "columns": cols, "rows": {}}
    m["frame_layout"] = {"sheetWidth": image.width, "sheetHeight": image.height, "cellWidth": cell, "cellHeight": cell, "rows": {}}
    for r, (state, frames) in enumerate(rows.items()):
        m["animation"]["rows"][state] = {"row": r, "frames": len(frames), "fps": fps, "durations_ms": [round(1000 / fps)] * len(frames), "loop": state in loops, "frame_variant": "pixel"}
        m["frame_layout"]["rows"][state] = []
        for c, im in enumerate(frames):
            image.alpha_composite(im, (c * cell, r * cell))
            m["frame_layout"]["rows"][state].append({"x": c * cell, "y": r * cell, "w": cell, "h": cell})
    image.save(ROOT / "sheets" / f"{name}.png")
    (ROOT / "sheets" / f"{name}.manifest.json").write_text(json.dumps(m, indent=2), encoding="utf-8")

def export():
    (ROOT / "sheets").mkdir(exist_ok=True)
    (ROOT / "qa").mkdir(exist_ok=True)
    pal = palette()
    for name, (state, ids, sizes) in SPECS.items():
        if not all((ROOT / "runs" / "props" / "frames" / state / f"frame-{i}.png").exists() for i in ids):
            continue
        frames = [fitted("props", state, i, sz, pal) for i, sz in zip(ids, sizes)]
        key = "blink" if name == "server-rack" else "clean" if name == "robot-vacuum" else "variants"
        if name == "server-rack":
            # Preserve one cabinet silhouette; only native LED blocks blink.
            base = frames[0]
            box = base.getbbox()
            frames = []
            for i in range(3):
                f = base.copy()
                d = ImageDraw.Draw(f)
                for j in range(5):
                    x = box[0] + round((box[2] - box[0]) * .64)
                    y = box[1] + round((box[3] - box[1]) * (.20 + j * .16))
                    color = [(93, 177, 165, 255), (236, 166, 98, 255), (78, 181, 169, 255)][i] if j % 3 == i else (29, 58, 57, 255)
                    d.rectangle((x, y, x + 1, y + 1), fill=color)
                frames.append(f)
        if name == "robot-vacuum":
            # A longer brush changed fitted body scale: keep one body, animate its brush/LED.
            base = frames[0].copy()
            a = np.array(base)
            a[56:, 40:] = 0
            a[59:, 38:] = 0
            base = Image.fromarray(a)
            frames = []
            for i in range(2):
                f = base.copy()
                d = ImageDraw.Draw(f)
                d.rectangle((30, 49, 31, 49), fill=(93, 177, 165, 255) if i == 0 else (236, 166, 98, 255))
                d.line((38, 56, 41 if i == 0 else 42, 60 if i == 0 else 56), fill=(62, 64, 63, 255), width=1)
                frames.append(f)
        if name == "ceiling-light-glow":
            a = np.array(frames[0])
            yy, xx = np.indices(a.shape[:2])
            box = frames[0].getbbox()
            distance = ((xx - 32) / 23) ** 2 + ((yy - (box[1] + box[3]) / 2) / 13) ** 2
            fixture = (distance < .3) | (a[:, :, :3].mean(axis=2) < 110)
            a[:, :, 3] = np.where(a[:, :, 3] > 0, np.where(fixture, 255, np.where(distance < .55, 96, 40)), 0)
            frames = [Image.fromarray(a)]
        sheet(name, {key: frames}, 64, "props", fps=3 if key in ("blink", "clean") else 1, loops=(key,) if key in ("blink", "clean") else ())
    if (ROOT / "runs/props/frames/cat_walk/frame-1.png").exists() and (ROOT / "runs/props/frames/cat_sleep/frame-0.png").exists():
        sheet("cat", {"sleep": [fitted("props", "cat_sleep", 0, (24, 16), pal)], "walk_right": [fitted("props", "cat_walk", i, (28, 20), pal) for i in range(2)]}, 64, "props", fps=4, loops=("walk_right",))
    tile_groups = {"wood": ["floor-wood-oak", "floor-wood-walnut", "floor-wood-staggered"], "carpet": ["floor-carpet-blue", "floor-carpet-red"], "hard_floor": ["floor-checker-pantry", "floor-bathroom", "floor-stone"]}
    for state, names in tile_groups.items():
        for i, name in enumerate(names):
            if not (ROOT / "runs/tiles/frames" / state / f"frame-{i}.png").exists():
                continue
            im = fitted("tiles", state, i, (32, 32), pal)
            a = np.array(im)
            if state == "wood":
                for y in (7, 15, 23, 31):
                    a[y, :, :3] = (120, 65, 40) if i != 1 else (76, 42, 35)
                a[:, -1] = a[:, 0]
            elif name == "floor-checker-pantry":
                # Enforce the requested exact 8px checker period after extraction.
                for y in range(32):
                    for x in range(32):
                        a[y, x] = (62, 64, 63, 255) if (x // 8 + y // 8) % 2 else (250, 236, 201, 255)
            elif state == "carpet":
                a[0] = a[1]
                a[-1] = a[0]
                a[:, -1] = a[:, 0]
            elif state == "hard_floor":
                # Restore single-pixel grout that the downsample could lose.
                grout = (137, 141, 123, 255) if i == 1 else (92, 92, 90, 255)
                a[0, :] = a[16, :] = grout
                a[:, 0] = a[:, 16] = grout
            sheet(name, {"tile": [Image.fromarray(a)]}, 32, "tiles")
    for i, name in enumerate(["wall-navy", "wall-wood"]):
        if not (ROOT / "runs/tiles/frames/wall" / f"frame-{i}.png").exists():
            continue
        source = fitted("tiles", "wall", i, (32, 32), pal)
        full = Image.new("RGBA", (32, 32), (39, 40, 77, 255) if i == 0 else (120, 65, 40, 255))
        # Normalize the generated material above its rail to the 24px wall-face contract.
        full.alpha_composite(source.crop((0, 0, 32, 16)).resize((32, 24), Image.Resampling.NEAREST))
        d = ImageDraw.Draw(full)
        if i == 0:
            d.rectangle((4, 5, 27, 20), outline=(50, 85, 113, 255))
        d.rectangle((0, 24, 31, 25), fill=(181, 128, 67, 255))
        d.rectangle((0, 29, 31, 31), fill=(25, 30, 30, 255))
        variants = {"panel": [full]}
        for state, right, inside in [("top_left", False, False), ("top_right", True, False), ("inside_left", False, True), ("inside_right", True, True)]:
            f = full.copy()
            d = ImageDraw.Draw(f)
            x = 28 if right else 0
            d.rectangle((x, 0, x + 3, 31), fill=(25, 30, 30, 255))
            d.rectangle((0, 0 if not inside else 24, 31, 2 if not inside else 26), fill=(37, 18, 14, 255))
            variants[state] = [f]
        door = full.copy()
        d = ImageDraw.Draw(door)
        d.rectangle((5, 2, 26, 31), fill=(181, 128, 67, 255))
        d.rectangle((8, 5, 23, 31), fill=(16, 40, 44, 255))
        variants["door_frame"] = [door]
        win = full.copy()
        d = ImageDraw.Draw(win)
        d.rectangle((4, 3, 27, 22), fill=(120, 65, 40, 255))
        d.rectangle((6, 5, 25, 20), fill=(93, 177, 165, 255))
        d.line((16, 5, 16, 20), fill=(231, 209, 172, 255), width=2)
        d.line((6, 13, 25, 13), fill=(231, 209, 172, 255), width=2)
        d.rectangle((3, 22, 28, 23), fill=(181, 128, 67, 255))
        variants["window_wall"] = [win]
        sheet(name, variants, 32, "tiles")
    validate()
    contacts()

def validate():
    report = {"sheets": [], "errors": []}
    template_keys = set(json.loads((ART / "sheets/props.manifest.json").read_text()))
    for path in sorted((ROOT / "sheets").glob("*.manifest.json")):
        m = json.loads(path.read_text())
        im = Image.open(path.with_name(m["game_input"])).convert("RGBA")
        assert set(m) == template_keys, path
        assert im.size == (m["frame_layout"]["sheetWidth"], m["frame_layout"]["sheetHeight"]), path
        count = 0
        for state, frames in m["frame_layout"]["rows"].items():
            assert len(frames) == m["animation"]["rows"][state]["frames"]
            for f in frames:
                assert 0 <= f["x"] and f["x"] + f["w"] <= im.width and 0 <= f["y"] and f["y"] + f["h"] <= im.height
                crop = im.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"]))
                assert crop.getbbox(), (path, state)
                a = np.asarray(crop)
                assert not np.any((a[:, :, 0] > 220) & (a[:, :, 1] < 40) & (a[:, :, 2] > 220) & (a[:, :, 3] > 0)), path
                if m["cell"]["width"] == 32:
                    assert np.all(a[:, :, 3] == 255), path
                count += 1
        report["sheets"].append({"file": m["game_input"], "frames": count, "size": im.size})
    report["sheet_count"] = len(report["sheets"])
    report["frame_count"] = sum(s["frames"] for s in report["sheets"])
    (ROOT / "qa/export-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Validated {report['sheet_count']} sheets / {report['frame_count']} declared frames.")

def contacts():
    items = []
    for path in sorted((ROOT / "sheets").glob("*.manifest.json")):
        m = json.loads(path.read_text())
        im = Image.open(path.with_name(m["game_input"])).convert("RGBA")
        for state, frames in m["frame_layout"]["rows"].items():
            for i, f in enumerate(frames):
                items.append((path.stem.removesuffix(".manifest") + "/" + state + f"/{i}", im.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"]))))
    for group, subset in [("props", [v for v in items if v[1].width == 64]), ("tiles", [v for v in items if v[1].width == 32])]:
        if not subset:
            continue
        card = (144, 158) if group == "props" else (144, 158)
        out = Image.new("RGB", (6 * card[0], math.ceil(len(subset) / 6) * card[1]), (231, 209, 172))
        draw = ImageDraw.Draw(out)
        for j, (label, im) in enumerate(subset):
            x, y = (j % 6) * card[0], (j // 6) * card[1]
            parts = label.split("/")
            draw.text((x + 3, y + 2), parts[0][:23], fill=(37, 18, 14))
            draw.text((x + 3, y + 12), "/".join(parts[1:]), fill=(37, 18, 14))
            im = im.resize((128, 128), Image.Resampling.NEAREST)
            out.paste(im, (x + 8, y + 24), im)
        out.save(ROOT / "qa" / f"contact-{group}.png")
        if group == "props":
            for p, y in enumerate(range(0, out.height, 632)):
                out.crop((0, y, out.width, min(y + 632, out.height))).save(ROOT / "qa" / f"contact-props-{p + 1}.png")

def refine():
    archive = ROOT / "qa/rejected"
    archive.mkdir(parents=True, exist_ok=True)
    changes = {
        ("props", "plants"): "Five deliberately SIMPLE chunky houseplants. TRUE 64x64 cell art; leaves are broad flat connected blocks, at most 8 leaf clusters per plant, NO veins, individual leaf speckles, noisy mottling, tiny texture or dithering. Use ONLY three foliage shades: deep teal #1D3A39, mid teal #235E5C, sage teal #5DB1A5. Pots use existing warm terracotta or ivory. Left to right: 0 MONSTERA with 5 big split leaves, 28x38; 1 SNAKE PLANT with 5 long spear leaves, 24x40; 2 FERN with 6 bold connected fronds, 30x32; 3 SUCCULENT rosette with 6 fat leaves, 20x20; 4 FICUS TREE with 5 big round leafy clumps and visible walnut trunk, 32x50. All foliage attached to trunk and pot. Quiet simple silhouette matches existing tiny office plants. No brown or yellow leaf texture. No floor or shadow.",
        ("tiles", "wood"): "Three seamless 32x32 wooden FLOOR TEXTURES, NOT objects, not framed panels. Render completely flat naked square surface patches with NO OUTER BORDER on any edge: no black perimeter, no bevel, no highlight perimeter, no framed swatch. Only interior horizontal plank seams at 8px spacing. At most two short grain lines per plank, not noisy texture. Exactly four plank bands each 8px tall. 0 warm honey oak; 1 deeper walnut; 2 light honey with staggered plank ends. Same warm browns as reference. The patch edges flow into the same material on the adjacent tile. All three patches are separated by large magenta gaps. Entire square surface opaque; no padding inside its logical square. No shadow or perspective.",
    }
    for (kind, state), action in changes.items():
        run = ROOT / "runs" / kind
        for file in [run / "raw" / f"{state}.png", run / "reports/gen-set" / f"{state}.json"]:
            if file.exists():
                shutil.copy2(file, archive / f"{kind}-{state}-initial{file.suffix}")
        if kind == "props":
            shutil.copy2(ROOT / "sheets/plants.png", archive / "plants-initial-sheet.png")
        else:
            shutil.copy2(ROOT / "qa/contact-tiles.png", archive / "wood-initial-contact.png")
        q = json.loads((run / "sprite-request.json").read_text())
        q["states"][state]["action"] = action
        (run / "sprite-request.json").write_text(json.dumps(q, indent=2), encoding="utf-8")
        (run / "prompts" / f"{state}.txt").write_text(f"Create exactly {q['states'][state]['frames']} separate sprites in ONE horizontal row, on flat #FF00FF magenta. Reference is STYLE/PALETTE ONLY. Layout guide owns frame count and spacing; do not draw the guide.\n{action}\n{q['style']}\nThe requested simplicity and NO BORDER rules override any visual complexity or outline in the reference. Complete separated silhouettes, no labels or guide marks. Output only the strip.", encoding="utf-8")
    print("Archived initial wood/plants and prepared targeted regeneration prompts.")

def scene():
    dest = ROOT / "qa/scene-assets"
    dest.mkdir(parents=True, exist_ok=True)
    spec = {"kind": "sprite-gen-scene", "version": 1, "canvas": {"width": 704, "height": 448, "background": [16, 40, 44, 255]}, "fps": 1, "duration": 1, "assets": {}, "layers": []}
    def add(name, x, y, index=0, state=None, z=100, old=False, anchor=(32, 61)):
        folder = ART / "sheets" if old else ROOT / "sheets"
        path = folder / f"{name}.manifest.json"
        m = json.loads(path.read_text())
        state = state or next(iter(m["frame_layout"]["rows"]))
        f = m["frame_layout"]["rows"][state][index]
        key = f"{name}-{state}-{index}"
        if key not in spec["assets"]:
            im = Image.open(folder / m["game_input"]).convert("RGBA").crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"]))
            im.save(dest / f"{key}.png")
            spec["assets"][key] = {"source": f"qa/scene-assets/{key}.png"}
        # PNG scene assets default to bottom-center; translate our native anchors.
        spec["layers"].append({"id": f"layer-{len(spec['layers'])}", "asset": key, "at": [x + f["w"] / 2 - anchor[0], y + f["h"] - anchor[1]], "z": z})
    for y in range(96, 416, 32):
        for x in range(32, 672, 32):
            if x < 384:
                name = ["floor-wood-oak", "floor-wood-staggered", "floor-wood-oak"][(x // 32 + y // 32) % 3]
            elif x == 384:
                if 192 <= y < 288:
                    name = "floor-wood-walnut"
                else:
                    continue
            else:
                name = "floor-checker-pantry" if y >= 320 else "floor-carpet-blue"
            add(name, x, y, z=0, anchor=(0, 0))
    for x in range(32, 672, 32):
        if x == 384:
            continue
        name = "wall-navy" if x < 384 else "wall-wood"
        state = "window_wall" if x in (96, 288, 608) else "panel"
        add(name, x, 32, state="panel", z=10, anchor=(0, 0))
        add(name, x, 64, state=state, z=10, anchor=(0, 0))
    # Small divider segments leave a broad passage between both rooms.
    for y in (32, 64, 96, 128, 160, 288, 320, 352, 384):
        add("wall-navy", 384, y, state="inside_left", z=10, anchor=(0, 0))
    for x, i in [(464, 0), (520, 1), (576, 2), (650, 3)]:
        add("framed-pictures", x, 80, i, z=20)
    add("whiteboard", 202, 78, z=20)
    add("wall-clock", 257, 67, z=20)
    add("bookshelf", 64, 150, 0)
    add("bookshelf", 337, 150, 1)
    add("plants", 44, 219, 1)
    add("plants", 355, 371, 4)
    for x, y, desk, monitor, chair in [(112, 196, 0, 0, 0), (214, 196, 1, 1, 1), (308, 217, 0, 2, 2), (92, 291, 1, 1, 2), (227, 291, 0, 2, 0)]:
        add("desks", x, y, desk, z=y)
        add("monitors", x, y - 22, monitor, z=y + 1)
        add("office-chairs", x, y + 34, chair, z=y + 2)
        add("mug", x + 18, y - 15, z=y + 3)
    add("filing-cabinet", 351, 218, z=220)
    add("server-rack", 346, 288, z=288)
    add("printer", 352, 316, z=316)
    add("trash-bin", 56, 299, z=300)
    add("rugs", 156, 399, 1, z=1)
    add("meeting-table-chairs", 151, 379, z=379)
    add("round-table", 276, 384, z=384)
    add("mug", 276, 365, z=385)
    add("plants", 302, 394, 3, z=394)
    add("lamp", 53, 389, z=389)
    add("robot-vacuum", 303, 336, z=336)
    # Lounge: opposing front/side red seating around a low table and a blue rug.
    add("rugs", 540, 252, 0, z=1)
    add("sofa-red", 540, 182, 0, z=182)
    add("sofa-red", 471, 263, 1, z=263)
    add("sofa-red", 540, 291, 0, z=291)
    add("coffee-table", 543, 238, z=238)
    add("mug", 548, 220, z=239)
    add("armchair", 629, 267, z=267)
    add("plants", 646, 176, 0, z=176)
    add("plants", 422, 175, 2, z=175)
    add("plants", 431, 305, 3, z=305)
    add("lamp", 652, 234, z=234)
    add("cat", 528, 176, state="sleep", z=183)
    add("cat", 590, 304, 0, state="walk_right", z=304)
    add("kitchen-counter-sink", 477, 388, z=388)
    add("microwave", 459, 358, z=389)
    add("mug", 492, 367, z=390)
    add("fridge", 566, 395, z=395)
    add("vending-machine", 635, 395, z=395)
    add("trash-bin", 519, 406, z=406)
    add("ceiling-light-glow", 179, 127, z=500)
    add("ceiling-light-glow", 555, 122, z=500)
    # Existing characters are read-only scale references and make the scene inhabited.
    add("codex-worker-01", 112, 221, 1, "typing_back", z=223, old=True, anchor=(16, 60))
    add("claude-worker-02", 214, 221, 0, "typing_back", z=223, old=True, anchor=(16, 60))
    add("antigravity-worker-03", 227, 316, 0, "typing_back", z=318, old=True, anchor=(16, 60))
    add("claude-worker-04", 473, 246, 0, "phone", z=265, old=True, anchor=(16, 60))
    add("codex-worker-02", 627, 255, 0, "coffee", z=269, old=True, anchor=(16, 60))
    add("antigravity-worker-01", 375, 251, 1, "walk_right", z=251, old=True, anchor=(16, 60))
    add("codex-worker-04", 203, 377, 1, "talk", z=381, old=True, anchor=(16, 60))
    (ROOT / "scene.json").write_text(json.dumps(spec, indent=2), encoding="utf-8")
    print(f"Scene spec: {len(spec['layers'])} layers; 704x448 native, preview export 2x.")

def preview():
    im = Image.open(ROOT / "qa/scene-render/frames/frame-00000.png")
    im.resize((im.width * 2, im.height * 2), Image.Resampling.NEAREST).save(ROOT / "preview-env2.png")
    print("Saved 1408x896 nearest-neighbor preview.")

if __name__ == "__main__":
    if sys.argv[1] == "prepare":
        prepare()
    elif sys.argv[1] == "export":
        export()
    elif sys.argv[1] == "scene":
        scene()
    elif sys.argv[1] == "preview":
        preview()
    elif sys.argv[1] == "refine":
        refine()
