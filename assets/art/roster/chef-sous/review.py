"""Small review/export helper; canonical extraction and composition stay in sprite-gen."""
import json
import shutil
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
RUN = ROOT / 'run'
REQUEST = json.loads((ROOT / 'request.json').read_text(encoding='utf-8'))
STATES = list(REQUEST['states'])


def review(mode, names):
    pages = []
    for name in names:
        if mode == 'raw':
            source = RUN / 'raw' / (name + '.png')
            if not source.exists():
                continue
            with Image.open(source) as image:
                thumb = image.convert('RGB')
                thumb.thumbnail((640, 210), Image.Resampling.NEAREST)
        else:
            with Image.open(ROOT / 'chef-sous.png') as atlas:
                row = STATES.index(name)
                thumb = Image.new('RGB', (384, 192), '#ded4bf')
                for frame in range(REQUEST['states'][name]['frames']):
                    cell = atlas.crop((frame * 32, row * 64, frame * 32 + 32, row * 64 + 64))
                    cell = cell.resize((96, 192), Image.Resampling.NEAREST)
                    thumb.paste(cell, (frame * 96, 0), cell)
        pages.append((name, thumb))
    per_page = 4 if mode == 'raw' else 5
    for page in range((len(pages) + per_page - 1) // per_page):
        rows = pages[page * per_page:page * per_page + per_page]
        image = Image.new('RGB', (700, sum(im.height + 24 for _, im in rows)), '#24262c')
        draw = ImageDraw.Draw(image)
        y = 0
        for name, thumb in rows:
            draw.text((10, y + 5), 'chef-sous | ' + name, fill='#fff4d4')
            image.paste(thumb, ((700 - thumb.width) // 2, y + 24))
            y += thumb.height + 24
        target = RUN / 'qa' / ('review-' + mode + '-' + names[0] + '-' + str(page) + '.png')
        target.parent.mkdir(parents=True, exist_ok=True)
        image.save(target)
        print(target)


def export():
    from sprite_gen.frames.extract import apply_palette, build_shared_palette

    manifest = json.loads((RUN / 'manifest.json').read_text(encoding='utf-8'))
    assert list(manifest['animation']['rows']) == STATES
    with Image.open(RUN / 'sprite-sheet-alpha.png') as atlas:
        atlas = atlas.convert('RGBA')
        assert atlas.size == (128, 832)
        assert set(atlas.getchannel('A').get_flattened_data()) == {0, 255}
        before_colors = len({p[:3] for p in atlas.get_flattened_data() if p[3]})
        alpha_bytes = atlas.getchannel('A').tobytes()
        if before_colors > 32:
            atlas = apply_palette(atlas, build_shared_palette([atlas], 32))
        assert atlas.getchannel('A').tobytes() == alpha_bytes
        evidence = {}
        for row, name in enumerate(STATES):
            spec = REQUEST['states'][name]
            actual = manifest['animation']['rows'][name]
            assert actual['row'] == row and actual['frames'] == spec['frames']
            assert actual['loop'] == spec['loop']
            actual['fps'] = spec['fps']
            assert manifest['frame_layout']['rows'][name] == [
                {'x': i * 32, 'y': row * 64, 'w': 32, 'h': 64} for i in range(spec['frames'])]
            boxes = []
            for frame in range(4):
                cell = atlas.crop((frame * 32, row * 64, frame * 32 + 32, row * 64 + 64))
                box = cell.getchannel('A').getbbox()
                assert bool(box) == (frame < spec['frames'])
                if box:
                    assert box[0] >= 1 and box[2] <= 31 and box[3] <= 60, (name, frame, box)
                    assert not any(r > 200 and b > 200 and g < 70 and a for r, g, b, a in cell.get_flattened_data())
                    boxes.append(list(box))
            evidence[name] = boxes
        palette = {p[:3] for p in atlas.get_flattened_data() if p[3]}
        assert len(palette) <= 32
        manifest.update(characterId='chef-sous', game_input='chef-sous.png',
                        sprite_sheet_alpha='chef-sous.png',
                        sprite_sheet_alpha_report='chef-sous.report.json', base_image='base.png',
                        degraded_static_fallback=False)
        manifest['palette_size'] = len(palette)
        (ROOT / 'chef-sous.manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        atlas.save(ROOT / 'chef-sous.png')
        report = json.loads((RUN / 'sprite-sheet-alpha.report.json').read_text(encoding='utf-8'))
        report['roster_export'] = {'source': 'run/sprite-sheet-alpha.png',
                                  'palette_before': before_colors, 'palette_after': len(palette),
                                  'alpha_unchanged': True, 'idle_fps': 0}
        (ROOT / 'chef-sous.report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        contact = Image.new('RGB', (490, 5 * 216 + 32), '#ded4bf')
        draw = ImageDraw.Draw(contact)
        draw.text((12, 9), 'chef-sous | idle + walk | 3x NEAREST', fill='#302923')
        for row, name in enumerate(STATES[:5]):
            y = 32 + row * 216
            draw.text((10, y + 8), name, fill='#302923')
            strip = atlas.crop((0, row * 64, 128, row * 64 + 64)).resize((384, 192), Image.Resampling.NEAREST)
            contact.paste(strip, (102, y + 20), strip)
        contact.save(ROOT / 'chef-sous-contact.png')
        result = {'ok': True, 'sheet': [128, 832], 'cell': [32, 64], 'rows': 13,
                  'frames': sum(s['frames'] for s in REQUEST['states'].values()),
                  'alpha': 'binary', 'opaque_colors': len(palette), 'frame_bounds': evidence}
        (RUN / 'qa' / 'validation.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({k: v for k, v in result.items() if k != 'frame_bounds'}))


def normalize():
    import numpy as np
    from sprite_gen.curate.curation import apply_transform, empty_curation, write_curation_atomic
    from sprite_gen.spec.runio import publish_guard

    manifest = json.loads((RUN / 'frames/frames-manifest.json').read_text(encoding='utf-8'))
    rows = {row['state']: row for row in manifest['rows']}
    curation = empty_curation()
    curation['pixel_unfake'] = True
    for name in STATES:
        frames = [Image.open(RUN / path).convert('RGBA') for path in rows[name]['files']]
        boxes = [frame.getbbox() for frame in frames]
        foot_centers = []
        for frame, box in zip(frames, boxes):
            alpha = np.asarray(frame.getchannel('A'))
            foot_top = box[3] - max(1, round((box[3] - box[1]) * .2))
            _, xs = np.nonzero(alpha[foot_top:box[3]])
            foot_centers.append(float(np.mean(xs + .5)))
        target = 46 if name in ('typing_back', 'typing_side', 'sleep') else 54
        scale = min(target / max(box[3] - box[1] for box in boxes),
                    14.5 / max(max(foot - box[0], box[2] - foot)
                               for box, foot in zip(boxes, foot_centers)), 1.0)
        transforms = {}
        for i, (frame, box, foot_x) in enumerate(zip(frames, boxes, foot_centers)):
            transform = {'scale': scale, 'dx': (16 - foot_x) * scale,
                         'dy': 60 - (32 + (box[3] - 32) * scale)}
            preview = apply_transform(frame, transform, (32, 64), snap_scale=1)
            bounds = preview.getbbox()
            transform['dy'] += 60 - bounds[3]
            preview = apply_transform(frame, transform, (32, 64), snap_scale=1)
            bounds = preview.getbbox()
            assert bounds and bounds[0] >= 1 and bounds[2] <= 31 and bounds[1] >= 4 and bounds[3] == 60, (name, i, bounds)
            transforms[str(i)] = transform
        curation['states'][name] = {'selected': list(range(len(frames))),
                                     'order': list(range(len(frames))), 'transforms': transforms}
        print(name, 'scale', round(scale, 4))
    with publish_guard(RUN):
        write_curation_atomic(RUN, curation)


if __name__ == '__main__':
    if sys.argv[1] == 'export':
        export()
    elif sys.argv[1] == 'normalize':
        normalize()
    else:
        review(sys.argv[1], sys.argv[2].split(',') if len(sys.argv) > 2 else STATES)
