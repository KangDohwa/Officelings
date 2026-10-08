"""Local asset assembly; generation/extraction/recolor remain sprite-gen operations."""
import json
import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image
from sprite_gen.curate.curation import apply_transform, write_curation_atomic
from sprite_gen.spec.runio import publish_guard

ART = Path(__file__).resolve().parent
RUNS = ART / "runs"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def cli(*args):
    result = subprocess.run([sys.executable, "-m", "sprite_gen.cli", *map(str, args)], capture_output=True, text=True, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    print(args[0], "OK", flush=True)


def curate(name, limits, allow_up=()):
    run = RUNS / name
    rows = read(run / "frames/frames-manifest.json")["rows"]
    cell = read(run / "sprite-request.json")["cell"]
    payload = {"version": 1, "kind": "sprite-gen-curation", "states": {}}
    for row in rows:
        if row["state"] not in limits:
            continue
        width, height = limits[row["state"]]
        boxes = [Image.open(run / p).getbbox() for p in row["files"]]
        scale = min(width / max(b[2]-b[0] for b in boxes), height / max(b[3]-b[1] for b in boxes))
        if row["state"] not in allow_up:
            scale = min(1, scale)
        transforms = {}
        for i, path in enumerate(row["files"]):
            image = Image.open(run / path).convert("RGBA")
            transform = {"scale": scale}
            bbox = apply_transform(image, transform, (cell["width"], cell["height"]), snap_scale=1).getbbox()
            transform["dy"] = cell["height"] - cell["safe_margin_y"] - bbox[3]
            transforms[str(i)] = transform
        payload["states"][row["state"]] = {"transforms": transforms}
    with publish_guard(run):
        write_curation_atomic(run, payload)
    cli("compose-atlas", "--run-dir", run)
    cli("export-pngs", "--run-dir", run)


def import_worker():
    source = RUNS / "worker"
    request = read(source / "sprite-request.json")
    back = {"cell": {"width": 32, "height": 64, "safe_margin_x": 3, "safe_margin_y": 10},
            "fit": {"resample": "kcentroid", "align_x": "foot-centroid", "align_y": "bottom", "ground_frames": True},
            "states": {"typing_back": request["states"]["typing_back"]}}
    write(ART / "back-fit-request.json", back)
    normalized = RUNS / "typing-back-fit"
    if not (normalized / "manifest.json").exists():
        cli("prepare", "--out-dir", normalized, "--character-id", "worker", "--base-image", ART / "references/worker-base.png", "--request", ART / "back-fit-request.json", "--chroma-key", "#FF00FF")
        shutil.copy2(source / "raw/typing_back.png", normalized / "raw/typing_back.png")
        cli("extract", "--run-dir", normalized)
    with publish_guard(normalized):
        write_curation_atomic(normalized, {"version": 1, "kind": "sprite-gen-curation", "states": {"typing_back": {"transforms": {str(i): {"dy": 6} for i in range(3)}}}})
    cli("compose-atlas", "--run-dir", normalized)
    cli("export-pngs", "--run-dir", normalized)
    incoming = ART / "assembly/worker"
    (incoming / "_base").mkdir(parents=True, exist_ok=True)
    shutil.copy2(ART / "references/worker-base.png", incoming / "_base/worker.png")
    for state, entry in request["states"].items():
        group = incoming / state
        group.mkdir(parents=True, exist_ok=True)
        for i in range(entry["frames"]):
            origin = normalized if state == "typing_back" else source
            filename = f"frame-{i}.png" if origin == normalized else f"{state}-frame-{i}.png"
            shutil.copy2(origin / "curated" / filename, group / f"{i:02}.png")
        refs = group / "_refs"
        refs.mkdir(exist_ok=True)
        shutil.copy2(source / "raw" / f"{state}.png", refs / "basis-row.png")
    final = RUNS / "worker-final"
    cli("unpack-atlas", "--pngs-dir", incoming, "--out-dir", final, "--force")
    recipe = read(final / "sprite-request.json")
    recipe["states"] = request["states"]
    recipe["states"]["idle"]["fps"] = 1
    write(final / "sprite-request.json", recipe)
    cli("compose-atlas", "--run-dir", final)
    cli("export-pngs", "--run-dir", final)


def rig():
    original = RUNS / "worker-final"
    run = RUNS / "worker-rig"
    incoming = ART / "assembly/rig"
    shutil.copytree(ART / "assembly/worker", incoming, dirs_exist_ok=True)
    for state in ("crown", "lanyard"):
        folder = incoming / state
        folder.mkdir(parents=True, exist_ok=True)
        shutil.copy2(RUNS / "accessories/curated" / f"{state}-frame-0.png", folder / "00.png")
    cli("unpack-atlas", "--pngs-dir", incoming, "--out-dir", run, "--force")
    request = read(run / "sprite-request.json")
    request["states"] = read(original / "sprite-request.json")["states"]
    for state in ("crown", "lanyard"):
        request["states"][state] = {"frames": 1, "fps": 1, "loop": False, "action": "generated wearable overlay", "track": "prop_effect"}
    request["subject"] = "effect"
    frames = read(run / "frames/frames-manifest.json")
    landmarks = {}
    for row in frames["rows"]:
        state = row["state"]
        points = {}
        for i, path in enumerate(row["files"]):
            bbox = Image.open(run / path).getbbox()
            center = (bbox[0] + bbox[2]) // 2
            if state in ("crown", "lanyard"):
                points[str(i)] = {"root": [center, bbox[3]-1] if state == "crown" else [center, bbox[1]]}
            else:
                # Explicit placement recipe reviewed in role contact sheets: hair top and neckline.
                top = bbox[1]
                neckline = top + round((bbox[3]-top) * .57)
                points[str(i)] = {"root": [16, 60], "crown": [center, top+2], "neck": [center, neckline]}
        landmarks[state] = points
    request["rig"] = {"profile": "humanoid_biped", "landmarks": landmarks}
    request["layers"] = {}
    for state in read(original / "sprite-request.json")["states"]:
        for role, accessory, target in (("supervisor", "crown", "crown"), ("subagent", "lanyard", "neck")):
            request["layers"][f"{role}_{state}"] = {"stack": [{"state": state}, {"state": accessory, "to": target}], "fps": request["states"][state]["fps"], "loop": request["states"][state]["loop"]}
    write(run / "sprite-request.json", request)
    cli("compose-layers", "--run-dir", run)
    for role in ("supervisor", "subagent"):
        incoming = ART / "assembly" / role
        (incoming / "_base").mkdir(parents=True, exist_ok=True)
        shutil.copy2(ART / "references/worker-base.png", incoming / "_base/worker.png")
        for state in read(original / "sprite-request.json")["states"]:
            manifest = read(run / "layers" / f"{role}_{state}.manifest.json")
            atlas = Image.open(run / "layers" / f"{role}_{state}.png").convert("RGBA")
            rects = next(iter(manifest["frame_layout"]["rows"].values()))
            group = incoming / state
            group.mkdir(exist_ok=True)
            for i, rect in enumerate(rects):
                atlas.crop((rect["x"], rect["y"], rect["x"]+rect["w"], rect["y"]+rect["h"])).save(group / f"{i:02}.png")
        final = RUNS / f"{role}-final"
        cli("unpack-atlas", "--pngs-dir", incoming, "--out-dir", final, "--force")
        recipe = read(final / "sprite-request.json")
        recipe["states"] = read(original / "sprite-request.json")["states"]
        write(final / "sprite-request.json", recipe)
        cli("compose-atlas", "--run-dir", final)


def hexcolor(color):
    return "#" + "".join(f"{v:02x}" for v in color)


def colors(path):
    return sorted({pixel[:3] for pixel in Image.open(path).convert("RGBA").get_flattened_data() if pixel[3] > 8})


def nearest(color, palette):
    return min(palette, key=lambda p: sum((a-b)**2 for a, b in zip(color, p)))


def package():
    sheets = ART / "sheets"
    sheets.mkdir(exist_ok=True)
    master = read(RUNS / "worker/palette.lock.json")["colors"]
    accessory = colors(RUNS / "accessories/sprite-sheet-alpha.png")
    shirts = {"codex": [(29,58,57),(35,94,92),(52,136,132),(93,177,165)],
              "claude": [(76,42,35),(130,68,44),(185,101,55),(229,151,93)],
              "antigravity": [(39,40,77),(66,63,126),(100,100,184),(156,163,221)]}
    people = {
        "01": ([(206,137,79),(236,166,98),(255,195,129)], [(72,39,29),(120,65,40),(173,99,61)]),
        "02": ([(210,152,120),(241,194,154),(255,224,182)], [(121,82,46),(187,143,76),(227,194,115)]),
        "03": ([(87,48,41),(130,77,57),(168,109,78)], [(33,30,38),(52,49,59),(77,70,74)]),
        "04": ([(142,95,66),(187,140,96),(220,175,120)], [(83,36,34),(132,53,38),(180,82,50)])}
    for role in ("worker", "supervisor", "subagent"):
        run = RUNS / f"{role}-final"
        palette = colors(run / "sprite-sheet-alpha.png")
        mapping = {hexcolor(c): hexcolor(c if c in accessory else nearest(c, master)) for c in palette}
        spec = {"version": 1, "kind": "sprite-gen-recolor", "match": "exact", "tolerance": 0,
                "variants": [{"name": "normalized", "map": mapping}]}
        write(run / "normalize.spec.json", spec)
        cli("recolor", "--run-dir", run, "--spec", run / "normalize.spec.json")
        base = run / "variants/normalized.png"
        variants = []
        for provider, shirt in shirts.items():
            for combo, (skin, hair) in people.items():
                mapping = {}
                for c in colors(base):
                    r, g, b = c
                    target = c
                    if c not in accessory:
                        if g-r >= 14 and abs(g-b) < 15:
                            target = shirt[min(3, max(0, (g-45)//28))]
                        elif r > 195 and g > 110 and b < 140 and r-g > 45:
                            target = skin[0 if g < 150 else 1 if g < 175 else 2]
                        elif 70 < r < 185 and r > g*1.25 and g > b*1.25:
                            target = hair[0 if r < 105 else 1 if r < 150 else 2]
                    mapping[hexcolor(c)] = hexcolor(target)
                variants.append({"name": f"{provider}-{role}-{combo}", "map": mapping})
        write(run / "recolor.spec.json", {**spec, "variants": variants})
        cli("recolor", "--base", base, "--manifest", run / "variants/normalized.manifest.json", "--spec", run / "recolor.spec.json", "--out-dir", sheets, "--report", f"{role}-recolor.report.json")
        alias = "worker" if role == "worker" else f"worker-{role}"
        shutil.copy2(sheets / f"codex-{role}-01.png", sheets / f"{alias}.png")
        manifest = read(sheets / f"codex-{role}-01.manifest.json")
        manifest["game_input"] = manifest["sprite_sheet_alpha"] = f"{alias}.png"
        write(sheets / f"{alias}.manifest.json", manifest)
    for name in ("props", "icons", "accessories"):
        shutil.copy2(RUNS / name / "sprite-sheet-alpha.png", sheets / f"{name}.png")
        manifest = read(RUNS / name / "manifest.json")
        manifest["game_input"] = manifest["sprite_sheet_alpha"] = f"{name}.png"
        write(sheets / f"{name}.manifest.json", manifest)
    tile_palette = [(137,141,123),(116,121,103),(150,152,134),(181,128,67),(168,113,54),(200,151,88),(108,75,47),
                    (231,209,172),(217,189,146),(196,161,113),(156,105,61),(59,48,41),(211,164,101),(187,139,80),(150,99,52),(74,53,39)]
    run = RUNS / "tiles"
    spec = {"version": 1, "kind": "sprite-gen-recolor", "match": "exact", "tolerance": 0,
            "variants": [{"name": "tiles", "map": {hexcolor(c): hexcolor(nearest(c, tile_palette)) for c in colors(run / "sprite-sheet-alpha.png")}}]}
    write(run / "recolor.spec.json", spec)
    cli("recolor", "--run-dir", run, "--spec", run / "recolor.spec.json", "--out-dir", sheets, "--report", "tiles-recolor.report.json")
    for png in sheets.glob("*.png"):
        manifest = read(png.with_suffix(".manifest.json"))
        image = Image.open(png)
        assert image.size == (manifest["frame_layout"]["sheetWidth"], manifest["frame_layout"]["sheetHeight"])
        for rects in manifest["frame_layout"]["rows"].values():
            for rect in rects:
                assert image.crop((rect["x"],rect["y"],rect["x"]+rect["w"],rect["y"]+rect["h"])).getbbox()
    print("Runtime sheets:", len(list(sheets.glob("*.png"))), flush=True)


def scene():
    assets = {}
    layers = []
    folder = ART / "qa/scene-assets"
    folder.mkdir(parents=True, exist_ok=True)

    def asset(sheet, state, frame=0):
        key = f"{sheet}-{state}-{frame}"
        if key not in assets:
            manifest = read(ART / "sheets" / f"{sheet}.manifest.json")
            rect = manifest["frame_layout"]["rows"][state][frame]
            image = Image.open(ART / "sheets" / f"{sheet}.png").convert("RGBA")
            image.crop((rect["x"],rect["y"],rect["x"]+rect["w"],rect["y"]+rect["h"])).save(folder / f"{key}.png")
            assets[key] = {"source": f"qa/scene-assets/{key}.png"}
        return key

    def place(sheet, state, x, y, frame=0, z=10):
        key = asset(sheet, state, frame)
        layers.append({"id": f"layer-{len(layers)}", "asset": key, "at": [x,y], "z": z})

    for y in range(64,352,32):
        for x in range(0,512,32):
            place("tiles", "floor_pantry" if x >= 320 else "floor_office", x+16, y+32, (x//32+y//32)%2, 0)
    for x in range(0,512,32):
        place("tiles", "wall_pantry" if x >= 320 else "wall_office", x+16, 64, z=1)
    # Tile the upper plank band; the baseboard appears only once at the floor line.
    image = Image.open(folder / "tiles-wall_pantry-0.png").crop((0,0,32,24))
    image.save(folder / "plaster-wood.png")
    assets["upper-wood"] = {"source": "qa/scene-assets/plaster-wood.png"}
    for x in range(320,512,32):
        for y in (0,24):
            layers.append({"id": f"layer-{len(layers)}", "asset": "upper-wood", "at": [x+16,y+24], "z": 0})
    place("props", "door", 32, 70, 1, 5)
    place("props", "window", 132, 58, z=5)
    place("props", "window", 420, 58, z=5)
    place("props", "clock", 260, 50, 0, 5)
    place("props", "plants", 300, 98, 1)
    place("props", "plants", 328, 254, 0)
    place("props", "plants", 288, 326, 2)
    for x, frame in ((96,2),(208,1),(292,2)):
        place("props", "desk", x, 150, frame)
        place("props", "chair", x-20, 156, z=11)
    place("codex-supervisor-01", "typing_side", 76, 150, 1, 12)
    place("claude-subagent-02", "typing_side", 188, 150, 2, 12)
    place("antigravity-worker-03", "typing_side", 272, 150, 0, 12)
    for x in (76,188,272):
        place("icons", "bubbles", x, 104, 0, 20)
    for x, frame in ((96,0),(208,1)):
        place("props", "desk", x, 253, frame)
        place("props", "chair", x-20, 263, z=11)
    place("codex-worker-04", "idle", 76, 267, 0, 12)
    place("icons", "bubbles", 76, 218, 1, 20)
    place("claude-supervisor-04", "idle", 224, 285, 0, 12)
    place("claude-subagent-01", "report", 188, 285, 2, 13)
    place("icons", "bubbles", 188, 238, 3, 20)
    place("antigravity-subagent-02", "walk_down", 36, 122, 1, 12)
    place("props", "coffee_machine", 360, 121)
    place("props", "water_cooler", 480, 121)
    place("props", "plants", 428, 118, 2)
    place("codex-worker-03", "coffee", 352, 185, 1, 12)
    place("props", "tv", 456, 187, 1)
    place("props", "sofa", 448, 245, z=11)
    place("antigravity-worker-04", "sleep", 447, 253, 1, 12)
    place("icons", "bubbles", 447, 207, 4, 20)
    place("codex-subagent-02", "talk", 367, 314, 1, 12)
    place("claude-worker-03", "idle", 403, 314, 1, 12)
    place("antigravity-worker-01", "phone", 480, 323, 2, 12)
    spec = {"kind": "sprite-gen-scene", "version": 1,
            "canvas": {"width": 512, "height": 352, "background": [231,209,172,255]},
            "fps": 1, "duration": 1, "assets": assets, "layers": layers}
    write(ART / "scene.json", spec)
    cli("scene-render", "--spec", ART / "scene.json", "--out-dir", ART / "qa/scene-render", "--formats", "png")
    shutil.copy2(ART / "qa/scene-render/frames/frame-00000.png", ART / "preview.png")


def manifest():
    character = read(ART / "sheets/worker.manifest.json")
    lines = ["# Officelings generated office art", "", "Load PNGs under `sheets/` with their sibling `.manifest.json`. All runtime files are RGBA with straight alpha; use nearest-neighbor sampling. Coordinates and row numbers are zero-based. Empty trailing cells stay transparent. `manifest.frame_layout.rows[state][frame]` is authoritative; do not infer cells from alpha.", "",
             "This is a separate art bundle, not the existing `atlas.version == 1` contract in `docs/state-schema.md`. That contract uses 16×24 cells and a different row/column order. No renderer or state-schema code was changed. A developer must map action/facing to the rows below and scale world geometry to 32 px tiles.", "",
             "## Runtime sheet inventory", "", "| Sheet | Cell | Grid / pixels | Meaning |", "|---|---|---|---|"]
    for png in sorted((ART / "sheets").glob("*.png")):
        meta = read(png.with_suffix(".manifest.json"))
        cell = meta["cell"]
        layout = meta["frame_layout"]
        meaning = "Character rows below" if layout["sheetHeight"] == 832 else f"{png.stem} rows below"
        lines.append(f"| `sheets/{png.name}` | {cell['width']}×{cell['height']} | {layout['sheetWidth']//cell['width']}×{layout['sheetHeight']//cell['height']} / {layout['sheetWidth']}×{layout['sheetHeight']} | {meaning} |")
    lines += ["", "`worker.png`, `worker-supervisor.png`, `worker-subagent.png` are convenient aliases of the Codex / combo 01 sheets. All 36 provider/role/appearance variants share the same cell geometry and animation ordering.", "",
              "## Character rows (all character sheets)", "", "Cell **32×64**, four columns, thirteen rows, **128×832**. Body silhouettes are mostly 21–28×41–47 px. Draw at native scale using foot anchor **(16,60)** in the cell. The four-pixel bottom margin is intentional; occupied walking tile remains 32×32. Idle row columns select facing and are not an animation.", "", "| Row | State | Frames / columns | Suggested fps | Playback |", "|---|---|---|---|---|"]
    for state, row in character["animation"]["rows"].items():
        frames = "0 down, 1 left, 2 right, 3 up" if state == "idle" else f"0–{row['frames']-1}"
        lines.append(f"| {row['row']} | `{state}` | {frames} | {'static' if state == 'idle' else row['fps']} | {'loop' if row['loop'] else 'select facing' if state == 'idle' else 'play once, hold final'} |")
    lines += ["", "`typing_back` faces up; `typing_side`, `report`, `coffee`, `phone`, and `talk` face right / down-right. Typing rows contain only the seated person: furniture is a separate layer. `sleep` is a seated, slumped nap suited to a sofa. `surprised` is the body reaction; put bubble frame 5 above it for `!`. Do not mirror whole sheets: choose the generated left/right walk rows. A provider color identifies the agent provider, not its model.", "",
              "| Appearance suffix | Skin | Hair |", "|---|---|---|", "| 01 | warm tan | chestnut |", "| 02 | fair | sandy blond |", "| 03 | deep brown | soft black |", "| 04 | olive / medium brown | auburn |", "",
              "Provider names: `codex` = teal, `claude` = terracotta orange, `antigravity` = violet / blue. Role names: `worker`, `supervisor` (gold crown), `subagent` (navy lanyard / cream ID). Example: `sheets/antigravity-subagent-03.png`. Recolor reports under `sheets/` record exact RGB substitutions and confirm unchanged alpha. Hair and shoes share some warm brown source colors, so footwear also follows the hair palette."]
    descriptions = {
        "props": {"desk": "0 monitor off; 1 on; 2 typing glow (select state; no geometry motion)", "chair": "office chair", "coffee_machine": "coffee machine on walnut cabinet", "water_cooler": "water cooler", "sofa": "two-seat orange sofa", "tv": "3 screen frames with play icon; cycle", "plants": "0 broad-leaf; 1 fern; 2 succulent (static choices)", "window": "wood-framed four-pane window", "door": "0 closed; 1 open (static choices)", "clock": "0 9:00; 1 12:00; 2 3:00 (static choices)"},
        "icons": {"report_item": "paper report", "bubbles": "0 working gear; 1 thinking dots; 2 waiting ?; 3 done check; 4 zzz; 5 surprised ! (static choices)", "role_badges": "0 crown pin; 1 lanyard (optional standalone icons)"},
        "tiles": {"floor_office": "two carpet texture choices", "floor_pantry": "two horizontal wood-plank texture choices", "wall_office": "ivory plaster, wood dado, dark baseboard", "wall_pantry": "honey wood panels, dado, dark baseboard"},
        "accessories": {"crown": "small generated wearable crown; already baked into supervisor sheets", "lanyard": "small generated wearable lanyard; already baked into subagent sheets"}}
    for kind, meaning in descriptions.items():
        meta = read(ART / "sheets" / f"{kind}.manifest.json")
        lines += ["", f"## {kind.title()} rows", "", "| Row | State | Frames | Suggested fps / use |", "|---|---|---|---|"]
        for state, row in meta["animation"]["rows"].items():
            lines.append(f"| {row['row']} | `{state}` | {row['frames']} | {meaning[state]}" + (f"; {row['fps']} fps" if state == "tv" else "; static") + " |")
    lines += ["", "Props use **64×64** cells, bottom anchor **(32,61)**; icons use **32×32**, centered unless used as a status bubble. A bubble's tail is near (9,27), so place it above the hair with a 2–4 px gap. Tiles use **32×32**, no padding and top-left anchor. Only horizontal joins are meaningful for wall tiles; the baseboard is at the bottom. Office floor tiles are intentionally quiet; pantry grain is more visible.", "",
              "## Preview, QA and limits", "", "`preview.png` is a **512×352** composition made from the delivered sprites with `sprite-gen scene-render`. `scene.json` and `qa/scene-render/scene.report.json` preserve placement and source evidence. Enlarge it with nearest-neighbor sampling.", "",
              "Directly reviewed generated rows, extracted frames, role sheets, palette variants and the composed scene. Bad left/right contact poses and raised-fist back typing were regenerated through GPT image_gen; final component extraction was rerun. Side-walk scale and furniture scale were corrected through sprite-gen curation. `qa/rejected/` preserves earlier rejected strips. Final walk contact poses alternate; small one-pixel head/foot changes remain. Back typing is deliberately subtle, and the report handoff is stylized rather than anatomically precise. The lanyard is an orientation-neutral role marker: it is also visible on back views. Walls/floors are basic repeating tiles, not a full terrain autotile set. Crown and lanyard placement were checked across every action; no overlay pixels were clipped.", "",
              "Runtime loading checks: 43 sheets match their manifests, all 1,674 declared frame references are nonempty and in bounds, alpha is unchanged in all 36 palette variants, and all 26 role/action composites have zero clipped overlay pixels (`qa/validation.json`). Animation GIFs and contact sheets live in the run QA/export directories; these are review files, not runtime sheets. Real-time playback inspection was unavailable because no browser was connected. Locomotion continuity remains experimental pending that playback check; the frame-by-frame visual review does not certify seamless runtime motion. No app build/test was run for this asset-only task.", "",
              "## Retained pipeline sheets", "", "These are provenance/intermediate composed sheets; load `sheets/` for the final art. Rows, frames and fps for each are in the linked sibling manifest; layer strip names identify the role and the character state listed above.", "", "| Sheet | Cell | Row/frame/fps source |", "|---|---|---|"]
    for png in sorted(RUNS.rglob("*.png")):
        sibling = png.with_suffix(".manifest.json")
        if png.name == "sprite-sheet-alpha.png":
            sibling = png.parent / "manifest.json"
        if not sibling.is_file():
            continue
        meta = read(sibling)
        if "frame_layout" not in meta:
            continue
        cell = meta["cell"]
        lines.append(f"| `{png.relative_to(ART).as_posix()}` | {cell['width']}×{cell['height']} | `{sibling.relative_to(ART).as_posix()}` |")
    lines += ["", "## Generation and licensing", "", "Generated for Officelings with GPT image_gen / provider `codex`, using sprite-gen **2.20.0**, **component-row still-image pipeline**. No video pipeline or third-party sprite pack was used. Initial generation prompts are in `runs/*/prompts/`; request JSON files record actions, frame counts, fit and style. Targeted native-tool correction prompts and reasons are in `qa/generation-notes.md`. sprite-gen itself is **Apache-2.0**; that software license does not assign a separate license to generated images. Generated-image usage remains governed by the provider's applicable terms. No claim of exclusive copyright or third-party clearance is made.", ""]
    (ART / "MANIFEST.md").write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    if sys.argv[1] == "curate":
        curate("props", {"chair": (27, 32), "coffee_machine": (30, 44), "tv": (48, 42), "plants": (26, 40), "window": (44, 36), "clock": (22, 22)})
        curate("icons", {"report_item": (15, 19)})
        curate("accessories", {"crown": (10, 7), "lanyard": (7, 12)})
    elif sys.argv[1] == "import":
        import_worker()
    elif sys.argv[1] == "rig":
        rig()
    elif sys.argv[1] == "package":
        package()
    elif sys.argv[1] == "fit-worker":
        curate("worker", {"walk_left": (27,44), "walk_right": (27,44), "typing_side": (28,44), "report": (29,44), "coffee": (28,44), "talk": (28,44)}, allow_up=("walk_left", "walk_right"))
    elif sys.argv[1] == "scene":
        scene()
    elif sys.argv[1] == "manifest":
        manifest()
