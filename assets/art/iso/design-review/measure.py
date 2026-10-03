"""Read-only source review; every output stays beside this script. Python -B + Pillow/numpy."""
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[3]
ART = ROOT / 'assets/art/iso'
FURN = ART / 'furniture-v2/workstations'
FACINGS = ('se', 'sw', 'ne', 'nw')
PILOTS = {
    'dev-hoodie-v2': ('dev-hoodie', 'runs/v2', ('6D3713', '8B4D15', 'AC5F2D', 'BA672C', 'C97E32')),
    'punk-girl': ('punk-girl', 'runs', ('E36854', 'F19B5B', 'FBC186', 'F7C4B7')),
    'professor': ('professor', 'runs/v1', ('744B31', 'C49F7C', 'EFA763', 'F3B06B', 'F6AD62')),
}
SOURCES = {}
SCENARIOS = ('baseline', 'head-up1', 'chair-down4', 'chair-down8',
             'chair-rear', 'desk-seat-rear', 'desk-seat-rear+chair-down4',
             'desk-seat-rear+chair-down8', 'head-over-chair',
             'head-up4', 'head-up8', 'desk-surface-rear',
             'desk-surface-rear+chair-down4', 'desk-surface-rear+chair-down5',
             'desk-surface-rear+chair-down6', 'desk-surface-rear+chair-down7',
             'desk-surface-rear+chair-down8')


def source(path):
    SOURCES[str(path.relative_to(ROOT)).replace('\\', '/')] = hashlib.sha256(path.read_bytes()).hexdigest()
    return path


def load_json(path):
    return json.loads(source(path).read_text(encoding='utf-8'))


def load_image(path):
    return Image.open(source(path)).convert('RGBA')


def point(p):
    return p['x'], p['y']


def project(i, j):
    return math.floor(32 * (i - j) + .5), math.floor(16 * (i + j) + .5)


def shift(image, dx=0, dy=0):
    out = Image.new('RGBA', image.size)
    out.alpha_composite(image, (dx, dy))
    return out


def mirrored(image):
    return shift(image.transpose(Image.Transpose.FLIP_LEFT_RIGHT), dx=1)


def cell(sheet, atlas, clip, phase=0):
    w, h = sheet['cellW'], sheet['cellH']
    x, y = (clip.get('column', 0) + phase) * w, clip['row'] * h
    return atlas.crop((x, y, x + w, y + h))


def furniture(name):
    doc = load_json(FURN / (name + '.iso.json'))
    sheets = {s['id']: (s, load_image(FURN / s['file'])) for s in doc['sheets']}
    return doc['furniture'][0], sheets


def variant(family, facing):
    return next(v for v in family['variants'] if v['orientation'] == facing and v['state'] == 'default')


def parts(var, sheets, origin, ground_depth, seat_depth, owner):
    items = []
    for p in var['parts']:
        s, atlas = sheets[p['sheetId']]
        c = next(c for c in s['rows'] if c['name'] == p['animation'] and c['direction'] == p['direction'])
        assert all(p[k] == c[k] for k in ('partId', 'depthAnchor', 'isoZOrder')), p['partId']
        depth = seat_depth if p.get('seatGroupId') else ground_depth + sum(p['depthAnchor'].values())
        anchor = point(c.get('anchor', s['anchor']))
        items.append({'key': (depth, p['isoZOrder'], owner, p['partId']),
                      'id': p['partId'], 'image': cell(s, atlas, c),
                      'pos': (origin[0] - anchor[0], origin[1] - anchor[1]),
                      'seat': bool(p.get('seatGroupId')), 'family': owner})
    return items


def canonical_head(folder, run, facing):
    """Alpha boundaries from each pilot's builder, never inferred from furniture overlap."""
    base = ART / 'characters' / folder / run
    if folder == 'professor':
        return load_image(base / ('head-' + facing + '.png'))
    f = facing if folder == 'punk-girl' else ('se' if facing in ('se', 'sw') else 'ne')
    im = load_image(base / (f + '-idle-native.png'))
    end = 38 if folder == 'punk-girl' else (41 if f == 'se' else 39)
    out = Image.new('RGBA', (32, 64))
    out.paste(im.crop((0, 0, 32, end)), (0, 0))
    if folder == 'dev-hoodie-v2' and f == 'se':
        # build.py's one-time eye/headphone/neck repairs can extend canonical alpha.
        d = ImageDraw.Draw(out)
        for x, y in ((15, 33), (20, 32)):
            d.rectangle((x, y, x + 1, y + 2), fill='white')
        d.line([(18, 36), (19, 36)], fill='white')
        d.rectangle((13, 38, 15, 40), fill='white')
        d.rectangle((21, 37, 23, 39), fill='white')
        d.line([(16, 39), (19, 40), (21, 38)], fill='white')
    return mirrored(out) if folder != 'punk-girl' and facing in ('sw', 'nw') else out


def regions(sprite, head, clip, skin_hex):
    a = np.array(sprite)
    opaque = a[:, :, 3] > 0
    hm = np.array(head)[:, :, 3] > 0
    assert not np.any(hm & ~opaque), 'canonical head must exist in serialized actor'
    yy, xx = np.indices(opaque.shape)
    skin = np.zeros_like(opaque)
    for color in skin_hex:
        skin |= np.all(a[:, :, :3] == tuple(bytes.fromhex(color)), axis=2)
    hands = np.zeros_like(opaque)
    distances = []
    for h in clip['handAnchors']:
        radius = np.maximum(abs(xx - h['x']), abs(yy - h['y']))
        hands |= (radius <= 2) & skin & opaque & ~hm
        distances.append(int(radius[skin & opaque & ~hm].min()))
    assert max(distances) <= 1, distances
    torso = opaque & ~hm & ~hands & (11 <= xx) & (xx <= 22) & (yy < clip['seatAnchor']['y'])
    return {'head': hm, 'torso': torso, 'hands': hands}, distances


def local_alpha(item, actor_pos):
    local = Image.new('RGBA', (32, 64))
    local.alpha_composite(item['image'], (item['pos'][0] - actor_pos[0], item['pos'][1] - actor_pos[1]))
    return np.array(local)[:, :, 3] > 0


def split_surface(item):
    """Fixed source geometry proxy, chosen on the empty desk, never on actor masks."""
    a = np.array(item['image']); yy, xx = np.indices(a.shape[:2])
    if '__se__' in item['id']:
        top = yy < np.minimum(.5 * xx + 53, 111 - .5 * xx)
        keyboard = (80 <= xx) & (xx <= 103) & (55 <= yy) & (yy <= 70)
    else:
        top = yy < np.minimum(.5 * xx + 46, 127 - .5 * xx)
        keyboard = (48 <= xx) & (xx <= 82) & (73 <= yy) & (yy <= 85)
    select = (top | keyboard) & (a[:, :, 3] > 0)
    surface = a.copy(); surface[~select] = 0
    fascia = a.copy(); fascia[select] = 0
    assert not np.any((surface[:, :, 3] > 0) & (fascia[:, :, 3] > 0))
    assert np.array_equal(surface + fascia, a), 'split must reconstruct original bytes'
    return Image.fromarray(surface), Image.fromarray(fascia)


def simulate(items, sprite, masks, actor_pos, actor_key, scenario, back):
    changed = [dict(i) for i in items]
    if back:
        if 'desk-surface-rear' in scenario:
            for i in list(changed):
                if i['family'] == 'desk' and i['seat']:
                    surface, fascia = split_surface(i)
                    i['image'] = fascia
                    changed.append(dict(i, image=surface, id=i['id'] + ':surface',
                                        key=(actor_key[0], -20, 'desk', i['id'] + ':surface')))
        for i in changed:
            chair_front = i['id'].startswith('chair__front__')
            if chair_front and 'chair-down' in scenario:
                n = int(scenario.split('chair-down')[1])
                # Geometric surrogate: shorten upper backrest, preserve all y>=68 seat/base pixels.
                a = np.array(i['image']); upper = a.copy(); upper[68:] = 0
                a[:68] = 0
                moved = np.array(shift(Image.fromarray(upper), dy=n)); moved[68:] = 0
                im = Image.fromarray(a); im.alpha_composite(Image.fromarray(moved)); i['image'] = im
            if (chair_front and scenario == 'chair-rear') or (
                i['family'] == 'desk' and i['seat'] and 'desk-seat-rear' in scenario
            ):
                i['key'] = (actor_key[0], -20, i['key'][2], i['key'][3])
        if scenario.startswith('head-up'):
            n = int(scenario[7:])
            a = np.array(sprite); head = a.copy(); head[~masks['head']] = 0
            a[masks['head']] = 0
            sprite = Image.fromarray(a); sprite.alpha_composite(shift(Image.fromarray(head), dy=-n))
            moved_mask = np.zeros_like(masks['head']); moved_mask[:-n] = masks['head'][n:]
            masks = dict(masks, head=moved_mask)
    covered = np.zeros((64, 32), bool)
    owner = np.full((64, 32), '', dtype=object)
    overlaps = []
    for i in sorted(changed, key=lambda i: i['key']):
        if i['key'] <= actor_key:
            continue
        alpha = local_alpha(i, actor_pos)
        if back and scenario == 'head-over-chair' and i['family'] == 'chair':
            alpha = alpha & ~masks['head']  # diagnostic layer bypass; desk occlusion remains
        owner[alpha] = i['id']
        covered |= alpha
        if np.any(alpha & masks['head']):
            overlaps.append({'partId': i['id'], 'headOverlap': int((alpha & masks['head']).sum())})
    counts = {name: {'visible': int((mask & ~covered).sum()), 'total': int(mask.sum())}
              for name, mask in masks.items()}
    counts['headTopmostOccluders'] = {str(p): int(((owner == p) & masks['head']).sum())
                                    for p in np.unique(owner[masks['head'] & covered])}
    counts['headOverlaps'] = overlaps  # not additive: independently overlapping parts
    chair_cover = np.zeros((64, 32), bool)
    for i in changed:
        if i['key'] > actor_key and i['family'] == 'chair':
            chair_cover |= local_alpha(i, actor_pos)
    counts['chairOnlyHeadVisible'] = int((masks['head'] & ~chair_cover).sum())
    scene = Image.new('RGBA', (192, 144), '#E7D1AC')
    for i in sorted(changed + [{'key': actor_key, 'image': sprite, 'pos': actor_pos}], key=lambda i: i['key']):
        scene.alpha_composite(i['image'], i['pos'])
    if back and scenario == 'head-over-chair':
        # Redraw only head pixels covered by chair but not by a later desk part.
        lift = np.array(sprite); chair_mask = np.zeros((64, 32), bool); desk_mask = chair_mask.copy()
        for i in changed:
            if i['key'] > actor_key:
                if i['family'] == 'chair': chair_mask |= local_alpha(i, actor_pos)
                else: desk_mask |= local_alpha(i, actor_pos)
        lift[~(masks['head'] & chair_mask & ~desk_mask)] = 0
        scene.alpha_composite(Image.fromarray(lift), actor_pos)
    return counts, scene


def save_pair(name, image):
    image.save(OUT / (name + '-1x.png'))
    image.resize((image.width * 3, image.height * 3), Image.Resampling.NEAREST).save(OUT / (name + '-3x.png'))


def ratio(m):
    return f"{m['visible']}/{m['total']} ({100*m['visible']/m['total']:.1f}%)" if m['total'] else '0/0 (N/A)'


def main():
    desk, ds = furniture('desk'); chair, cs = furniture('chair')
    records, queues, panels = [], [], {}
    contact = Image.new('RGBA', (768, 1728), '#E7D1AC')
    diagnostics = Image.new('RGBA', (768, 432), '#E7D1AC')
    for ci, (folder, (name, run, skin)) in enumerate(PILOTS.items()):
        base = ART / 'characters' / folder
        doc = load_json(base / (name + '.iso.json'))
        sheet = next(s for s in doc['sheets'] if s['kind'] == 'character')
        atlas = load_image(base / sheet['file'])
        for di, orientation in enumerate(FACINGS):
            v = variant(desk, orientation); profile = next(p for p in v['seatProfiles'] if p['id'] == 'front')
            facing = profile['isoFacing']; ground = profile['seatGround']; sd = ground['i'] + ground['j']
            origin = (96, 88); g = project(ground['i'], ground['j'])
            chair_origin = (origin[0] + g[0], origin[1] + g[1])
            assert point(v['linkedChair']['chairOffset']) == g
            cv = variant(chair, v['linkedChair']['orientation'])
            clip = next(c for c in sheet['rows'] if c['name'] == 'seated-typing-front' and c['direction'] == facing)
            assert clip['deskOrientation'] == orientation
            c = point(profile['contactOffset']); sa = point(clip['seatAnchor'])
            assert c == (g[0], g[1] - 12) and sa == (16, 44)
            pos = (origin[0] + c[0] - sa[0], origin[1] + c[1] - sa[1])
            assert [(pos[0] + h['x'], pos[1] + h['y']) for h in clip['handAnchors']] == [
                (origin[0] + h['x'], origin[1] + h['y']) for h in profile['handContacts']]
            akey = (sd, 0, 'actor', folder)
            items = parts(v, ds, origin, 0, sd, 'desk') + parts(cv, cs, chair_origin, sd, sd, 'chair')
            queues.append({'character': folder, 'desk': orientation, 'worker': facing,
                           'actorDestination': pos, 'actorKey': akey,
                           'items': [{'partId': i['id'], 'key': i['key'], 'destination': i['pos']} for i in sorted(items, key=lambda i: i['key'])]})
            head = canonical_head(folder, run, facing)
            for phase in range(4):
                sprite = cell(sheet, atlas, clip, phase)
                offset = point(sheet['headOffsetsByClip'][clip['name'] + '/' + facing][phase])
                masks, distances = regions(sprite, shift(head, *offset), clip, skin)
                for scenario in SCENARIOS:
                    counts, scene = simulate(items, sprite, masks, pos, akey, scenario, facing in ('ne', 'nw'))
                    records.append({'character': folder, 'desk': orientation, 'worker': facing,
                                    'phase': phase, 'scenario': scenario, 'handDistances': distances, **counts})
                    if phase == 0:
                        panels[(folder, orientation, scenario)] = scene.copy()
                    if scenario == 'baseline':
                        # Independently rasterize layer ownership in Pillow at full scene size.
                        labels = Image.new('L', (192, 144))
                        for i in sorted(items + [{'key': akey, 'image': sprite, 'pos': pos}], key=lambda i: i['key']):
                            labels.paste(1 if i['key'] == akey else 2, i['pos'] + (i['pos'][0] + i['image'].width, i['pos'][1] + i['image'].height), i['image'].getchannel('A'))
                        owned = np.array(labels.crop((pos[0], pos[1], pos[0] + 32, pos[1] + 64))) == 1
                        assert int((owned & masks['head']).sum()) == counts['head']['visible']
                        ImageDraw.Draw(scene).text((3, 3), f'{folder} {orientation}/{facing} p{phase}', fill='#191E1E')
                        contact.alpha_composite(scene, (di * 192, (ci * 4 + phase) * 144))
                if phase == 0:
                    # Fixed region masks, independent of visibility: head/torso/hands.
                    mask_image = np.zeros((64, 32, 4), np.uint8)
                    for region, color in [('torso', (80, 150, 255, 255)), ('hands', (255, 220, 80, 255)), ('head', (255, 90, 150, 255))]:
                        mask_image[masks[region]] = color
                    panel = Image.new('RGBA', (192, 144), '#E7D1AC')
                    panel.alpha_composite(sprite, (25, 45)); panel.alpha_composite(Image.fromarray(mask_image), (100, 45))
                    ImageDraw.Draw(panel).text((3, 3), f'{folder} {facing}', fill='#191E1E')
                    diagnostics.alpha_composite(panel, (di * 192, ci * 144))
    save_pair('contact', contact); save_pair('regions', diagnostics)
    compare = Image.new('RGBA', (192 * len(SCENARIOS), 144 * 6), '#E7D1AC')
    for ci, folder in enumerate(PILOTS):
        for di, orientation in enumerate(('se', 'sw')):
            for si, scenario in enumerate(SCENARIOS):
                panel = panels[(folder, orientation, scenario)].copy()
                ImageDraw.Draw(panel).text((3, 3), f'{folder} {orientation} / {scenario}', fill='#191E1E')
                compare.alpha_composite(panel, (si * 192, (ci * 2 + di) * 144))
    save_pair('proposals', compare)
    selected = ('baseline', 'desk-surface-rear', 'desk-surface-rear+chair-down4', 'desk-surface-rear+chair-down7')
    board = Image.new('RGBA', (768, 864), '#E7D1AC')
    for ci, folder in enumerate(PILOTS):
        for di, orientation in enumerate(('se', 'sw')):
            for si, scenario in enumerate(selected):
                panel = panels[(folder, orientation, scenario)].copy()
                d = ImageDraw.Draw(panel)
                d.text((3, 3), f'{folder} {orientation}', fill='#191E1E')
                d.text((3, 15), scenario, fill='#191E1E')
                board.alpha_composite(panel, (si * 192, (ci * 2 + di) * 144))
    save_pair('recommended', board)
    for ci, folder in enumerate(PILOTS):
        save_pair('recommended-' + folder, board.crop((0, ci * 288, 768, (ci + 1) * 288)))
    # Source-part board exposes misclassified monitor pixels without using a target mask.
    board = Image.new('RGBA', (640, 256), '#E7D1AC')
    for row, orientation in enumerate(('se', 'sw')):
        v = variant(desk, orientation)
        selected = [p for p in parts(v, ds, (76 if orientation == 'se' else 52, 72), 0, 0, 'desk') if p['seat']]
        for col, (label, group) in enumerate((('full desk', parts(v, ds, (76 if orientation == 'se' else 52, 72), 0, 0, 'desk')),
                                            ('seat front part', selected),
                                            ('monitor parts', [p for p in parts(v, ds, (76 if orientation == 'se' else 52, 72), 0, 0, 'desk') if '__monitor__' in p['id']]),
                                            ('chair front', [p for p in parts(variant(chair, 'nw' if row == 0 else 'ne'), cs, (64, 80), 0, 0, 'chair') if '__front__' in p['id']]))):
            im = Image.new('RGBA', (160, 128), '#E7D1AC')
            for p in sorted(group, key=lambda p: p['key']): im.alpha_composite(p['image'], p['pos'])
            ImageDraw.Draw(im).text((1, 1), orientation + ' ' + label, fill='#191E1E')
            board.alpha_composite(im, (col * 160, row * 128))
    save_pair('source-parts', board)
    hero = load_image(ROOT / 'assets/art/iso-concept/iso-office-hero.png')
    boxes = {'dev': (680, 345, 840, 505), 'professor': (830, 345, 990, 505), 'punk': (995, 345, 1155, 505)}
    crop_board = Image.new('RGBA', (480, 180), '#E7D1AC')
    for j, (name, box) in enumerate(boxes.items()):
        crop_board.alpha_composite(hero.crop(box), (j * 160, 20))
        ImageDraw.Draw(crop_board).text((j * 160 + 3, 3), name, fill='#191E1E')
    save_pair('hero-crops', crop_board)
    for p in [ROOT / 'docs/iso-slice-s0.md', ART / 'furniture/furniture-part-convention.md']:
        source(p)
    for folder, (name, run, _) in PILOTS.items():
        source(ART / 'characters' / folder / run / 'build.py')
    # Two independent earlier art-lane measurements reproduce exactly with fresh source compositing.
    for folder in ('punk-girl', 'professor'):
        proof = load_json(ART / 'characters' / folder / PILOTS[folder][1] / 'contact-proof.json')
        for old in proof['contacts']:
            r = next(r for r in records if r['character'] == folder and r['desk'] == old['desk'] and r['phase'] == 0 and r['scenario'] == 'baseline')
            old = old.get('phases', [old])[0]
            assert (r['head']['visible'], r['head']['total']) == (old['visibleHeadTexels'], old['headTexels'])
    payload = {'definition': 'canonical generated head alpha + published clip offset; torso ROI opaque non-head/non-hand 11<=x<=22 and y<44; hands exact skin within Chebyshev radius2 of anchors',
               'heroCrops': boxes, 'sourceHashes': SOURCES, 'queues': queues, 'records': records}
    (OUT / 'measurements.json').write_text(json.dumps(payload, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    lines = ['# 착석 가림 실측', '', 'desk는 가구 방향, worker는 캐릭터 방향. 4 typing phase의 픽셀 수를 합산(단일 프레임 면적의 4배).', '',
             '| 캐릭터 | desk / worker | 머리 visible/total | 몸통 ROI visible/total | 손 ROI visible/total |', '|---|---|---:|---:|---:|']
    for folder in PILOTS:
        for orientation in FACINGS:
            rows = [r for r in records if r['character'] == folder and r['desk'] == orientation and r['scenario'] == 'baseline']
            values = [{k: sum(r[name][k] for r in rows) for k in ('visible', 'total')} for name in ('head', 'torso', 'hands')]
            lines.append(f"| {folder} | {orientation.upper()} / {rows[0]['worker'].upper()} | " + ' | '.join(map(ratio, values)) + ' |')
    lines += ['', '## 시뮬레이션: 후면 머리 노출률 (phase 0)', '',
              '| 시나리오 | dev NW | dev NE | punk NW | punk NE | prof NW | prof NE |', '|---|' + '---:|' * 6]
    for scenario in SCENARIOS:
        rows = [r for r in records if r['scenario'] == scenario and r['desk'] in ('se', 'sw') and r['phase'] == 0]
        lines.append(f'| {scenario} | ' + ' | '.join(ratio(r['head']) for r in rows) + ' |')
    lines += ['', '## 후면 기준 topmost 머리 가림 기여 (phase 0, 서로 겹치지 않는 수)', '', '| 캐릭터 / worker | 의자만 있을 때 머리 visible/total | 최종 가림 part: 픽셀 |', '|---|---:|---|']
    for r in records:
        if r['scenario'] == 'baseline' and r['phase'] == 0 and r['worker'] in ('ne', 'nw'):
            lines.append(f"| {r['character']} / {r['worker'].upper()} | {r['chairOnlyHeadVisible']}/{r['head']['total']} | " + ', '.join(f'{p}: {n}' for p, n in r['headTopmostOccluders'].items()) + ' |')
    (OUT / 'measurements.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    assert len(records) == 3 * 4 * 4 * len(SCENARIOS)
    assert project(1, 0) == (32, 16) and project(1, 1) == (0, 32)
    # Independent source preservation and visibility accounting checks.
    for p, digest in SOURCES.items():
        assert hashlib.sha256((ROOT / p).read_bytes()).hexdigest() == digest, p
    for r in records:
        for name in ('head', 'torso', 'hands'):
            assert 0 <= r[name]['visible'] <= r[name]['total']
        assert sum(r['headTopmostOccluders'].values()) == r['head']['total'] - r['head']['visible']
    print('\n'.join(lines))
    print(f'OK: {len(records)} scenario/phase records; source hashes unchanged; output only {OUT}')


if __name__ == '__main__':
    main()
