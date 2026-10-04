"""Validate S0 iso lane exports. Dependencies: Pillow and numpy only.

See docs/iso-slice-s0.md for the production manifest additions. This does not
build a runtime pack or prove visual quality, reachability, or renderer parity.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
FACINGS = ('se', 'sw', 'ne', 'nw')
GROUPS = [
    ('idle', FACINGS, 1, True), ('walk', FACINGS, 8, True),
    ('seated-typing-front', FACINGS, 6, True),
    ('seated-thinking', FACINGS, 2, True),
    ('sit-down', FACINGS, 10, False), ('stand-up', FACINGS, 10, False),
    ('pickup', FACINGS[:2], 6, False),
    ('report-handover', FACINGS[:2], 6, False),
    ('coffee', FACINGS[:2], 4, True), ('phone', FACINGS[:2], 3, True),
    ('sleep', FACINGS[2:], 1, True), ('talk', FACINGS[:2], 3, True),
    ('surprised', FACINGS[:2], 6, False),
]
CONTRACT = [(name, facing, fps, loop)
            for name, facings, fps, loop in GROUPS for facing in facings]
BLINK_ROWS = [n for n, (_, facing, _, _) in enumerate(CONTRACT)
              if facing in FACINGS[:2]]
RAMPS = {
    'wood': '#4C2A23 #804A2F #B96537 #C78251 #ECA662',
    'navy': '#111924 #122031 #325571 #48728F #6492A3',
    'teal': '#10282C #235E5C #20746C #3C9F96 #5DB1A5',
    'red': '#3F1F18 #61282B #9F2937 #BF6554 #ECA662',
    'metal': '#191E1E #262D38 #565958 #808985 #89918F',
    'cream': '#605E5C #898D7B #D0B999 #E7D1AC #F2EBD3',
    'glass': '#10282C #186D7A #338F88 #4BAFA3 #5DB1A5',
    'leaves': '#0E1E1C #1D3A39 #264F4B #235E5C #537B78',
}
APPROACHES = {'se': (1, 0), 'sw': (1, 1), 'ne': (0, -1), 'nw': (-1, 1)}
WORKER = {'se': 'nw', 'sw': 'ne', 'ne': 'sw', 'nw': 'se'}
HANDS = {'se': ((27, 44), (29, 44)), 'sw': ((3, 44), (5, 44)),
         'ne': ((27, 32), (29, 32)), 'nw': ((3, 32), (5, 32))}
SIT = [(40, -20, 60), (42, -18, 60), (43, -15, 58), (44, -12, 56)]
PART = re.compile(r'^[a-z0-9]+(?:-[a-z0-9]+)*__(ground|rear|front|monitor|screen|side|edge|shadow)__(se|sw|ne|nw)__[0-9]{2}$')
REQUIRED = ('idle walk typing thinking waiting standing-thinking standing-waiting '
            'seated-idle seated-waiting seated-thinking sit-down stand-up pickup '
            'reporting report-handover coffee phone sleep napping talk chatting '
            'surprised tv water darts wave celebrate').split()


def rgb(value):
    if not re.fullmatch(r'#[0-9A-Fa-f]{6}', value):
        raise ValueError('expected #RRGGBB: ' + str(value))
    return tuple(int(value[n:n + 2], 16) for n in (1, 3, 5))


PALETTE = np.array(sorted({rgb(c) for ramp in RAMPS.values() for c in ramp.split()}), dtype=np.int16)


def diamond():
    y, x = np.indices((32, 64), dtype=float)
    dx, dy = x + .5 - 32, y + .5 - 16
    i, j = dx / 64 + dy / 32, dy / 32 - dx / 64
    return (i >= -.5) & (i < .5) & (j >= -.5) & (j < .5)


def point(value):
    return value['x'], value['y']


def local_file(base, name):
    path = (base / name).resolve()
    if not path.is_relative_to(base.resolve()):
        raise ValueError('path escapes manifest directory: ' + str(name))
    return path


def rgba(path):
    with Image.open(path) as im:
        if im.format != 'PNG' or im.mode != 'RGBA':
            raise ValueError('expected RGBA PNG: ' + str(path))
        return np.array(im)


def cells(arr, sheet, clip):
    w, h = sheet['cellW'], sheet['cellH']
    y = clip['row'] * h
    return [arr[y:y + h, x:x + w]
            for x in range(clip.get('column', 0) * w,
                           (clip.get('column', 0) + clip['frames']) * w, w)]


def present(cell, x, y, radius=1):
    h, w = cell.shape[:2]
    return bool(np.any(cell[max(0, y-radius):min(h, y+radius+1),
                            max(0, x-radius):min(w, x+radius+1), 3] > 0))


class Checks:
    def __init__(self):
        self.results = []

    def run(self, owner, name, fn):
        try:
            detail = fn()
            self.results.append((owner, name, 'PASS', detail or ''))
        except (AssertionError, ValueError, KeyError, TypeError, OSError, IndexError) as exc:
            self.results.append((owner, name, 'FAIL', str(exc) or 'check failed'))

    def skip(self, owner, name, reason):
        self.results.append((owner, name, 'SKIP', reason))

    def report(self):
        print('| Export | Check | Result | Detail |')
        print('| --- | --- | --- | --- |')
        for row in self.results:
            print('| ' + ' | '.join(str(v).replace('|', '/').replace('\n', ' ') for v in row) + ' |')
        failed = sum(row[2] == 'FAIL' for row in self.results)
        print(f'{len(self.results)} checks; {failed} failed')
        return 1 if failed else 0


def geometry(arr, sheet):
    kind = sheet['kind']
    sizes = {'character': [(32, 64)], 'blink': [(32, 64)],
             'floor': [(64, 32)], 'wall': [(32, 64), (64, 64)],
             'furniture': [(128, 128), (192, 128)], 'pet': [(32, 32)],
             'prop': [(32, 32)], 'shadow': [(128, 128)]}
    assert kind in sizes, 'unknown sheet kind'
    w, h = sheet['cellW'], sheet['cellH']
    assert type(w) is int and type(h) is int and (w, h) in sizes[kind], 'wrong cell size'
    if kind == 'furniture' and (w, h) == (192, 128):
        assert all(c['partId'].startswith('rug__') for c in sheet['rows']), '192px width reserved for 2×3 rug'
    assert sheet['projection'] == 'iso2to1' and sheet['scale'] == 1, 'projection/scale'
    assert arr.shape[1] % w == 0 and arr.shape[0] % h == 0, 'partial cell'
    assert arr.shape[0] <= 8192 and arr.shape[1] <= 8192 and arr.shape[0]*arr.shape[1] <= 16777216, 'image limits'
    assert sheet['rows'], 'empty clip list'
    keys = set()
    for clip in sheet['rows']:
        key = (clip['name'], clip['direction'])
        assert key not in keys, 'duplicate clip ' + str(key)
        keys.add(key)
        assert clip['direction'] in (*FACINGS, 'any'), 'direction'
        for field in ('row', 'frames'):
            assert type(clip[field]) is int, 'integer ' + field
        column = clip.get('column', 0)
        assert type(column) is int and column >= 0 and clip['row'] >= 0 and clip['frames'] >= 1, 'clip range'
        assert (column + clip['frames'])*w <= arr.shape[1] and (clip['row']+1)*h <= arr.shape[0], 'clip out of bounds'
        anchor = clip.get('anchor', sheet['anchor'])
        assert all(type(v) is int for v in point(anchor)), 'integer source anchor'
        assert 0 <= anchor['x'] <= w and 0 <= anchor['y'] <= h, 'source anchor outside geometry'
    if kind in ('character', 'blink'):
        assert point(sheet['anchor']) == (16, 60) and point(sheet['footAnchor']) == (16, 60), 'foot anchor'
    if kind == 'floor':
        assert point(sheet['anchor']) == (32, 16), 'tile anchor'
    return f'{arr.shape[1]}×{arr.shape[0]}, {w}×{h} cells'


def transparency(arr, sheet):
    allowed = (0, 48, 96) if sheet['kind'] == 'shadow' else (0, 255)
    assert np.isin(arr[:, :, 3], allowed).all(), 'unapproved alpha / antialiasing'
    assert np.any(arr[:, :, 3] == 0) and np.any(arr[:, :, 3] > 0), 'missing transparent background or empty sheet'
    for clip in sheet['rows']:
        for cell in cells(arr, sheet, clip):
            assert np.any(cell[:, :, 3] > 0), 'empty frame: ' + clip['name']
            if sheet['kind'] in ('character', 'blink'):
                assert not cell[:, 0, 3].any() and not cell[61:, :, 3].any(), 'character guard/padding clipped'
            if sheet['kind'] == 'floor':
                mask = diamond()
                assert not cell[:, :, 3][~mask].any(), 'floor pixels outside diamond'
                if not clip.get('transition', False):
                    assert np.all(cell[:, :, 3][mask] == 255), 'hole in base floor'
    return 'straight alpha and guards'


def palette(arr, sheet, tolerance):
    extra = sheet.get('identityPalette', [])
    assert not extra or sheet['kind'] in ('character', 'blink', 'pet'), 'identityPalette only for characters/pets'
    if sheet['kind'] in ('character', 'blink', 'pet'):
        assert extra, 'declare identityPalette from the identity reference'
    colors = np.vstack((PALETTE, np.array([rgb(c) for c in extra], dtype=np.int16).reshape(-1, 3)))
    used = np.unique(arr[:, :, :3][arr[:, :, 3] > 0], axis=0).astype(np.int16)
    distances = np.min(np.max(np.abs(used[:, None, :] - colors[None, :, :]), axis=2), axis=1)
    bad = used[distances > tolerance]
    assert not len(bad), f'{len(bad)} off-palette RGB colors; first {bad[:3].tolist()}'
    return f'{len(used)} colors; RGB max-channel tolerance {tolerance}'


def anchors(arr, sheet):
    kind = sheet['kind']
    for clip in sheet['rows']:
        frames = cells(arr, sheet, clip)
        if kind == 'character':
            name, facing = clip['name'], clip['direction']
            for phase, cell in enumerate(frames):
                if name in ('idle', 'walk', 'pickup', 'report-handover', 'coffee', 'phone', 'talk', 'surprised'):
                    assert cell[60, 4:29, 3].any(), f'no grounded sole: {name}/{facing}/{phase}'
                if name in ('seated-thinking', 'seated-typing-front', 'sleep'):
                    assert point(clip['seatAnchor']) == (16, 44), 'seated pelvis metadata'
                    assert present(cell, 16, 44), f'no pelvis pixels: {name}/{phase}'
                if name == 'seated-typing-front':
                    assert clip['seatProfile'] == 'front' and WORKER[clip['deskOrientation']] == facing, 'typing profile/orientation'
                    assert [point(v) for v in clip['handAnchors']] == list(HANDS[facing]), 'typing hand coordinates'
                    for x, y in HANDS[facing]:
                        assert present(cell, x, y), f'hand contact absent: {facing}/{phase}'
                if name in ('sit-down', 'stand-up'):
                    schedule = SIT if name == 'sit-down' else SIT[::-1]
                    sy, offset, foot_y = schedule[phase]
                    data = clip['phaseContacts'][phase]
                    assert point(data['seatAnchor']) == (16, sy) and point(data['contactOffset']) == (0, offset) and data['footY'] == foot_y, 'transition contact schedule'
                    assert present(cell, 16, sy) and cell[foot_y, 4:29, 3].any(), 'transition contact pixels'
            if name == 'idle':
                yy, _ = np.where(frames[0][:, :, 3] > 0)
                assert 38 <= yy.max()-yy.min()+1 <= 43, 'standing height outside 38–43'
        elif kind == 'floor':
            if not clip.get('transition', False):
                assert all(present(cell, 32, 16, 0) for cell in frames), 'tile center absent'
        elif kind != 'blink':
            checks = clip.get('anchorChecks', [])
            assert checks, 'declare physical anchorChecks (pivot may be transparent)'
            for check in checks:
                x, y = point(check)
                radius = check.get('radius', 1)
                assert type(x) is int and type(y) is int and type(radius) is int and 0 <= radius <= 1, 'anchor check must use integer texels and radius 0/1'
                assert 0 <= x < sheet['cellW'] and 0 <= y < sheet['cellH'], 'physical check outside cell'
                assert all(present(cell, x, y, radius) for cell in frames), 'physical anchor pixel absent'
    return 'physical contacts checked; transparent pivots allowed'


def row_contract(arr, sheet, stage):
    if sheet['kind'] == 'character':
        count = 8 if stage == 'c1' else 38
        assert arr.shape[:2] == (count*64, 128), f'normal atlas must be 128×{count*64}'
        clips = sorted(sheet['rows'], key=lambda c: c['row'])
        assert len(clips) == count, f'{count} stored clip rows required'
        for n, (clip, expected) in enumerate(zip(clips, CONTRACT[:count])):
            assert clip['row'] == n and clip.get('column', 0) == 0 and clip['frames'] == 4, 'row index / four cells'
            assert (clip['name'], clip['direction'], clip['fps'], clip.get('loop', True)) == expected, f'row {n} contract'
        return f'{count} rows / {count*4} cells ({stage})'
    if sheet['kind'] == 'blink':
        assert arr.shape[:2] == (1536, 128), 'blink atlas must be 128×1536'
        assert len(sheet['rows']) == 24, '24 blink clip rows required'
        for n, clip in enumerate(sorted(sheet['rows'], key=lambda c: c['row'])):
            normal_name, facing, fps, loop = CONTRACT[BLINK_ROWS[n]]
            assert (clip['row'], clip.get('column', 0), clip['frames'], clip['name'], clip['direction'], clip['fps'], clip.get('loop', True)) == (n, 0, 4, 'blink-'+normal_name, facing, fps, loop), 'blink clip order/timing'
        return '24 rows / 96 cells'
    if sheet['kind'] == 'pet':
        assert arr.shape[:2] == (288, 128) and point(sheet['anchor']) == (16, 28), 'cat atlas / foot'
        expected = [(n, f) for n in ('idle', 'walk') for f in FACINGS] + [('sleep', 'any')]
        clips = sorted(sheet['rows'], key=lambda c: c['row'])
        assert len(clips) == 9, '9 cat rows'
        for n, (clip, key) in enumerate(zip(clips, expected)):
            assert (clip['name'], clip['direction']) == key and clip['row'] == n and clip['frames'] == 4 and clip.get('column', 0) == 0, 'cat row order'
        return '9 rows / 36 cells'
    return 'clip bounds checked; asset counts remain task acceptance'


def aliases(sheet):
    clips = {(c['name'], c['direction']): c for c in sheet['rows']}
    mapping = {}
    for alias in sheet['aliases']:
        key = (alias['name'], alias['direction'])
        assert key not in clips and key not in mapping and key[1] in FACINGS, 'duplicate/invalid alias'
        assert alias.get('seatProfile', 'front') == 'front', 'end alias unsupported'
        mapping[key] = alias

    def resolve(key, chain):
        assert key not in chain, 'alias cycle'
        if key in clips:
            return clips[key]
        assert key in mapping, 'missing clip/alias: ' + str(key)
        a = mapping[key]
        assert a.get('frame') is None or (type(a['frame']) is int and 0 <= a['frame'] < 4), 'held phase'
        target = a['target']
        assert target['direction'] == key[1], 'alias must preserve camera facing'
        return resolve((target['name'], target['direction']), chain | {key})
    for name in REQUIRED:
        for facing in FACINGS:
            resolve((name, facing), set())
    for key in mapping:
        resolve(key, set())
    for facing in FACINGS:
        for name in ('seated-idle', 'seated-waiting', 'waiting'):
            a = mapping[(name, facing)]
            assert a['target'] == {'name': 'seated-thinking', 'direction': facing} and a['frame'] == 0, 'waiting/rest must hold thinking phase 0'
    return 'all action/facing aliases resolve without cycles'


def face_lock(base, sheet, arrays):
    blink = sheet['blink']
    assert blink['normalRows'] == BLINK_ROWS and blink['durationMs'] == 100, 'blink map/duration'
    blink_arr, blink_sheet = arrays[blink['sheetId']]
    assert blink_sheet['kind'] == 'blink', 'blink sheet kind'
    assert set(sheet['faceLock']) == {'se', 'sw'}, 'exactly SE/SW face-lock masks'
    masks = {}
    for facing, info in sheet['faceLock'].items():
        mask = rgba(local_file(base, info['eyeMask']))
        assert mask.shape == arrays[sheet['id']][0].shape, 'eye mask atlas size'
        assert np.isin(mask[:, :, 3], (0, 255)).all(), 'eye mask alpha'
        masks[facing] = mask[:, :, 3] > 0
    normal = arrays[sheet['id']][0]
    for row, (_, facing, _, _) in enumerate(CONTRACT):
        for authored, mask in masks.items():
            if authored != facing:
                assert not mask[row*64:(row+1)*64].any(), 'eye mask in wrong/back facing'
    for b, n in enumerate(BLINK_ROWS):
        facing = CONTRACT[n][1]
        for phase in range(4):
            sl = np.s_[n*64:(n+1)*64, phase*32:(phase+1)*32]
            mask = masks[facing][sl]
            assert 0 < mask.sum() <= 32, f'missing/oversized eye mask row {n} phase {phase}'
            y, x = np.where(mask)
            assert x.max()-x.min() < 12 and y.max()-y.min() < 4, 'eye mask must be a small eye-only region'
            src = normal[sl]
            dst = blink_arr[b*64:(b+1)*64, phase*32:(phase+1)*32]
            assert np.all(src[:, :, 3][mask] == 255), 'eye mask outside opaque head'
            assert np.array_equal(src[~mask], dst[~mask]), 'blink changes non-eye RGBA'
            assert np.any(src[mask] != dst[mask]), 'closed-eye copy does not change eyes'
    return 'SE/SW masks and all 96 phase-matched non-eye comparisons'


def parts(sheet):
    for clip in sheet['rows']:
        assert PART.fullmatch(clip['partId']), 'partId must be family__role__facing__NN'
        family, role, facing, _ = clip['partId'].split('__')
        assert clip['direction'] == facing, 'part facing mismatch'
        assert type(clip['isoZOrder']) is int, 'isoZOrder must be integer tie rank'
        depth = clip['depthAnchor']
        assert all(type(depth[k]) in (int, float) and math.isfinite(depth[k]) for k in ('i', 'j')), 'finite ground depth anchor'
        if family == 'chair' and role in ('rear', 'front'):
            assert clip['isoZOrder'] == (-20 if role == 'rear' else 20), 'chair seat tie rank'
        if family == 'desk' and role == 'front':
            assert clip['isoZOrder'] == 10, 'desk-front seat tie rank'
    return 'depth-part names / facing / ground anchors / tie ranks'


def seams(arr, sheet, tolerance):
    patches = sheet['patches']
    assert patches, 'declare 6×6 patches for every base family/axis'
    bases = {(c['row'], c.get('column', 0)+n): c for c in sheet['rows']
             if not c.get('transition', False) for n in range(c['frames'])}
    seen = set()
    for patch in patches:
        sources = []
        source_clips = []
        for ref in patch['tiles']:
            row, column = ref['row'], ref.get('column', 0)
            assert type(row) is int and type(column) is int and row >= 0 and column >= 0, 'patch source index'
            cell = arr[row*32:(row+1)*32, column*64:(column+1)*64]
            assert cell.shape == (32, 64, 4) and np.all(cell[:, :, 3][diamond()] == 255), 'patch source must be opaque base tile'
            assert (row, column) in bases, 'patch does not reference a declared base clip'
            clip = bases[(row, column)]
            assert clip['family'] == patch['family'], 'patch/source family mismatch'
            source_clips.append(clip)
            seen.add((row, column))
            sources.append(cell)
        parity = patch.get('phaseRule') == 'parity'
        assert sources and (not parity or len(sources) == 2), 'checker requires two parity sources'
        assert parity == (patch['family'] == 'checker'), 'only checker uses parity'
        if parity:
            assert [c['phase'] for c in source_clips] == [0, 1], 'checker phase order must be 0,1'
            assert not np.array_equal(sources[0], sources[1]), 'checker phases must differ'
        if patch['family'] == 'wood':
            assert patch['axis'] in ('i', 'j') and all(c['axis'] == patch['axis'] for c in source_clips), 'wood axis mismatch'
        out = np.zeros((192, 384, 4), np.uint8)
        count = np.zeros((192, 384), np.uint8)
        owner = np.full((192, 384), -1, np.int16)
        for i in range(6):
            for j in range(6):
                cell = sources[(i+j) % 2 if parity else (i*7+j*11) % len(sources)]
                x, y = 192 + 32*(i-j)-32, 16 + 16*(i+j)-16
                mask = cell[:, :, 3] > 0
                out[y:y+32, x:x+64][mask] = cell[mask]
                count[y:y+32, x:x+64] += mask
                owner[y:y+32, x:x+64][mask] = i*6+j
        yy, xx = np.indices((192, 384), dtype=float)
        dx, dy = xx+.5-192, yy+.5-16
        ii, jj = dx/64+dy/32, dy/32-dx/64
        expected = (ii >= -.5) & (ii < 5.5) & (jj >= -.5) & (jj < 5.5)
        assert np.all(count[expected] == 1) and not count[~expected].any(), '6×6 seam gaps/overlap'
        if not parity:
            for a, b, oa, ob in [(out[:, :-1], out[:, 1:], owner[:, :-1], owner[:, 1:]),
                                  (out[:-1], out[1:], owner[:-1], owner[1:])]:
                boundary = (oa >= 0) & (ob >= 0) & (oa != ob)
                delta = np.max(np.abs(a[:, :, :3].astype(np.int16)-b[:, :, :3].astype(np.int16)), axis=2)
                assert np.all(delta[boundary] <= tolerance), f'{patch["name"]}: edge RGB discontinuity'
    assert seen == set(bases), 'base tiles omitted from seam patches'
    return f'{len(patches)} 6×6 patches; checker parity contrast intentional'


def validate(path, checks, tolerance, seam_tolerance, stage):
    owner = path.name
    try:
        manifest = json.loads(path.read_text(encoding='utf-8-sig'))
        assert manifest['version'] == 1 and manifest['projection'] == 'iso2to1', 'manifest version/projection'
        assert manifest['sheets'], 'empty manifest'
        arrays = {}
        for sheet in manifest['sheets']:
            assert sheet['id'] not in arrays, 'duplicate sheet id'
            arrays[sheet['id']] = (rgba(local_file(path.parent, sheet['file'])), sheet)
    except (AssertionError, ValueError, KeyError, TypeError, OSError) as exc:
        checks.run(owner, 'manifest/images', lambda: (_ for _ in ()).throw(ValueError(str(exc))))
        return
    for ident, (arr, sheet) in arrays.items():
        label = owner + ':' + ident
        checks.run(label, 'cell sizes', lambda: geometry(arr, sheet))
        checks.run(label, 'transparency', lambda: transparency(arr, sheet))
        checks.run(label, 'anchor pixels', lambda: anchors(arr, sheet))
        checks.run(label, 'palette', lambda: palette(arr, sheet, tolerance))
        checks.run(label, 'row contract', lambda: row_contract(arr, sheet, stage))
        if sheet['kind'] == 'floor':
            checks.run(label, '6×6 seams', lambda: seams(arr, sheet, seam_tolerance))
        else:
            checks.skip(label, '6×6 seams', 'floor sheets only')
        if sheet['kind'] == 'furniture':
            checks.run(label, 'depth-part names', lambda: parts(sheet))
        else:
            checks.skip(label, 'depth-part names', 'furniture sheets only')
        if sheet['kind'] == 'character':
            if stage == 'c1':
                checks.skip(label, 'action aliases', 'C2 owns remaining rows/aliases')
            else:
                checks.run(label, 'action aliases', lambda: aliases(sheet))
            if stage == 'final':
                checks.run(label, 'face-lock / blink', lambda: face_lock(path.parent, sheet, arrays))
            else:
                checks.skip(label, 'face-lock / blink', 'C3 owns blink/masks; final gate still required')
        else:
            checks.skip(label, 'face-lock / blink', 'checked from normal character sheet')


def reference_image():
    im = Image.new('RGBA', (768, 320))
    draw = ImageDraw.Draw(im)
    color = lambda value: (*rgb(value), 255)
    foot, pelvis, hand = color('#F2EBD3'), color('#5DB1A5'), color('#BF6554')
    tile = np.zeros((32, 64, 4), np.uint8)
    tile[diamond()] = color('#B96537')
    def mark(x, y, c):
        draw.line((x-2, y, x+2, y), fill=c)
        draw.line((x, y-2, x, y+2), fill=c)
    def project(i, j, z=0):
        return 32*(i-j), 16*(i+j)-z
    for panel, orientation in enumerate(FACINGS):
        ox, oy = panel*192, 0
        dx, dy = ox+96, 80
        ai, aj = APPROACHES[orientation]
        ax, ay = project(ai, aj)
        ax, ay = dx+ax, dy+ay
        w, h = (1, 2) if orientation in ('se', 'nw') else (2, 1)
        for i, j in [(i, j) for i in range(w) for j in range(h)] + [(ai, aj)]:
            px, py = project(i, j)
            im.alpha_composite(Image.fromarray(tile), (dx+px-32, dy+py-16))
        polygon = [project(i, j, 18) for i, j in [(-.5, -.5), (w-.5, -.5), (w-.5, h-.5), (-.5, h-.5)]]
        draw.polygon([(dx+int(x), dy+int(y)) for x, y in polygon], fill=color('#C78251'))
        draw.line([(dx+int(x), dy+int(y)+4) for x, y in polygon[1:3]], fill=color('#804A2F'), width=3)
        draw.rectangle((ax-10, ay-16, ax+10, ay-8), fill=color('#235E5C'))
        sx, sy = ax-16, ay-56
        draw.rectangle((sx+9, sy+28, sx+23, sy+44), fill=color('#ECA662'))
        draw.rectangle((sx+10, sy+18, sx+22, sy+27), fill=color('#191E1E'))
        draw.line((sx+12, sy+45, sx+11, sy+56), fill=color('#262D38'), width=3)
        draw.line((sx+20, sy+45, sx+21, sy+56), fill=color('#262D38'), width=3)
        facing = WORKER[orientation]
        hx = sx + sum(v[0] for v in HANDS[facing])//2
        hy = sy + HANDS[facing][0][1]
        draw.line((sx+16, sy+32, hx, hy), fill=color('#ECA662'), width=3)
        # CRT ground center A + 3/4 f, base z18, top z36.
        fi, fj = {'se': (1, 0), 'sw': (0, 1), 'ne': (0, -1), 'nw': (-1, 0)}[facing]
        mx, my = project(ai+1.125*fi, aj+1.125*fj, 18)
        mx, my = dx+int(mx), dy+int(my)
        draw.rectangle((mx-6, my-18, mx+6, my), fill=color('#E7D1AC'))
        draw.rectangle((mx-4, my-15, mx+4, my-4), fill=color('#186D7A'))
        mark(ax, ay, foot)
        mark(ax, ay-12, pelvis)
        for x, y in HANDS[facing]:
            mark(sx+x, sy+y, hand)
        draw.text((ox+4, 4), f'Desk {orientation.upper()} / worker {facing.upper()}', fill=foot)
        draw.text((ox+4, 144), 'ground / pelvis / hands', fill=foot)
    # Bottom panels: exact tile, standing cell, wall endpoints, palette swatches.
    im.alpha_composite(Image.fromarray(tile), (64, 224))
    mark(96, 240, foot)
    draw.text((4, 164), 'Tile: anchor (32,16)', fill=foot)
    draw.text((4, 300), '64x32 / texel ownership', fill=foot)
    sx, sy = 272, 180
    draw.rectangle((sx+7, sy+21, sx+25, sy+31), fill=color('#191E1E'))
    draw.rectangle((sx+6, sy+32, sx+26, sy+48), fill=color('#ECA662'))
    draw.rectangle((sx+8, sy+49, sx+13, sy+60), fill=color('#262D38'))
    draw.rectangle((sx+19, sy+49, sx+24, sy+60), fill=color('#262D38'))
    mark(288, 240, foot)
    draw.text((196, 164), 'Standing: cell (272,180)', fill=foot)
    draw.text((196, 300), 'foot (16,60) / height 40', fill=foot)
    for x, step, shade in [(440, -32, '#325571'), (520, 32, '#122031')]:
        draw.polygon([(x, 264), (x+step, 280), (x+step, 232), (x, 216)], fill=color(shade))
        mark(x, 264, pelvis)
        mark(x+step, 280, foot)
    draw.text((388, 164), 'Walls: span32/rise16/z48', fill=foot)
    draw.text((388, 300), 'first endpoints: L/R', fill=foot)
    draw.text((580, 164), 'Frozen ramps: dark -> light', fill=foot)
    for row, (name, ramp) in enumerate(RAMPS.items()):
        y = 184+row*16
        draw.text((580, y), name, fill=foot)
        for col, value in enumerate(ramp.split()):
            x = 628+col*24
            draw.rectangle((x, y, x+19, y+11), fill=color(value))
    return im


def reference_check(path):
    arr = rgba(path)
    assert arr.shape == (320, 768, 4), 'reference size'
    expected = {'#F2EBD3': [(128, 96), (288, 112), (512, 64), (608, 80), (96, 240), (288, 240)],
                '#5DB1A5': [(128, 84), (288, 100), (512, 52), (608, 68), (440, 264), (520, 264)],
                '#BF6554': [(115, 72), (117, 72), (299, 88), (301, 88), (499, 52), (501, 52), (619, 68), (621, 68)]}
    for value, points in expected.items():
        for x, y in points:
            assert tuple(arr[y, x]) == (*rgb(value), 255), f'reference contact {x},{y}'
    # Exclude the guide cross; the underlying diamond keeps exactly 1024 texels.
    assert int(diamond().sum()) == 1024, 'tile raster area'
    return hashlib.sha256(path.read_bytes()).hexdigest()[:16]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', nargs='*', type=Path, help='lane .iso.json files or directories')
    parser.add_argument('--tolerance', type=int, default=3, help='maximum per-channel RGB deviation (0..3)')
    parser.add_argument('--seam-tolerance', type=int, default=8, help='edge RGB deviation (0..8)')
    parser.add_argument('--stage', choices=('c1', 'c2', 'final'), default='final', help='partial character handoff; packaging must use final')
    parser.add_argument('--reference', action='store_true', help='write the frozen contact reference only')
    parser.add_argument('--check-reference', action='store_true', help='check existing reference contacts')
    args = parser.parse_args()
    if not 0 <= args.tolerance <= 3 or not 0 <= args.seam_tolerance <= 8:
        parser.error('tolerance cannot exceed the frozen 3/8 limits')
    ref = ROOT / 'assets/art/iso/ref/iso-contact-v1.png'
    checks = Checks()
    if args.reference:
        ref.parent.mkdir(parents=True, exist_ok=True)
        reference_image().save(ref)
    if args.reference or args.check_reference:
        checks.run(ref.name, 'reference contacts', lambda: reference_check(ref))
    paths = sorted({file.resolve() for path in args.paths
                    for file in (path.rglob('*.iso.json') if path.is_dir() else [path])})
    if not paths and not (args.reference or args.check_reference):
        checks.run('input', 'manifests', lambda: (_ for _ in ()).throw(ValueError('no .iso.json exports found')))
    for path in paths:
        validate(path, checks, args.tolerance, args.seam_tolerance, args.stage)
    return checks.report()


if __name__ == '__main__':
    sys.exit(main())
