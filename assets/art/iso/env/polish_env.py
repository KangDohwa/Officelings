"""Repaint ENV pixels only; keep the existing S0 manifests byte-for-byte.

Uses the half-open masks/review placement from runs/iso-env/build_env.py.
Run with the installed sprite-gen venv's Python; no generation or pack writes.
"""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent
RAMPS = {
    'wood': '#4C2A23 #804A2F #B96537 #C78251 #ECA662',
    'navy': '#111924 #122031 #325571 #48728F #6492A3',
    'metal': '#191E1E #262D38 #565958 #808985 #89918F',
    'cream': '#605E5C #898D7B #D0B999 #E7D1AC #F2EBD3',
    'teal': '#10282C #235E5C #20746C #3C9F96 #5DB1A5',
    'red': '#3F1F18 #61282B #9F2937 #BF6554 #ECA662',
}
COL = {k: [tuple(int(c[n:n+2], 16) for n in (1, 3, 5))
           for c in v.split()] for k, v in RAMPS.items()}
frozen_paths = [OUT / 'floors/floors.iso.json', OUT / 'walls/walls.iso.json']
frozen = {p: p.read_bytes() for p in frozen_paths}
manifests = [json.loads(frozen[p]) for p in frozen_paths]


def layer(mask, color, alpha=255):
    a = np.zeros((*mask.shape, 4), np.uint8)
    a[mask] = (*color, alpha)
    return a


def paint(a, mask, ramp, stop, alpha=255):
    a[mask] = (*COL[ramp][stop], alpha)


def over(a, b):
    a = a.copy()
    a[b[:, :, 3] > 0] = b[b[:, :, 3] > 0]
    return a


def save(a, name, zoom=1):
    im = a if isinstance(a, Image.Image) else Image.fromarray(a)
    if zoom != 1:
        im = im.resize((im.width*zoom, im.height*zoom), Image.Resampling.NEAREST)
    im.save(OUT / name)
    return im


def put(im, a, x, y):
    im.alpha_composite(Image.fromarray(a), (int(x), int(y)))


def clip(name, row, **extra):
    return dict(name=name, direction='any', row=row, frames=1, fps=1, loop=True, **extra)


def sheet(ident, kind, file, w, h, anchor, rows, **extra):
    return dict(id=ident, kind=kind, file=file, cellW=w, cellH=h,
                anchor=dict(x=anchor[0], y=anchor[1]), scale=1,
                projection='iso2to1', rows=rows, **extra)


fy, fx = np.indices((32, 64), dtype=float)
di = (fx+.5-32)/64 + (fy+.5-16)/32
dj = (fy+.5-16)/32 - (fx+.5-32)/64
DIAMOND = (di >= -.5) & (di < .5) & (dj >= -.5) & (dj < .5)
u, v = (di+.5)*64, (dj+.5)*64
board = np.floor(v/32).astype(int)

# Common board ends continue across every variant. Broad, broken paint clusters
# replace the former long repeated scratches; joints/knots vary inside each cell.
template = layer(DIAMOND, COL['wood'][2])
paint(template, DIAMOND & (board == 1), 'wood', 3)
paint(template, DIAMOND & ((v % 32) < 1.4), 'wood', 1)
paint(template, DIAMOND & ((v % 32) >= 1.4) & ((v % 32) < 2.6), 'wood', 3)

# Identify actual cross-owner raster contacts independently of any color choice.
owner = np.full((96, 192), -1, int)
local = np.full_like(owner, -1)
ids = np.arange(2048).reshape(32, 64)
for i in range(3):
    for j in range(3):
        px, py = 64+32*(i-j), 16*(i+j)
        owner[py:py+32, px:px+64][DIAMOND] = 3*i+j
        local[py:py+32, px:px+64][DIAMOND] = ids[DIAMOND]
parent = np.arange(2048)


def find(n):
    while parent[n] != n:
        parent[n] = parent[parent[n]]
        n = parent[n]
    return n


for oa, ob, la, lb in [(owner[:, :-1], owner[:, 1:], local[:, :-1], local[:, 1:]),
                       (owner[:-1], owner[1:], local[:-1], local[1:])]:
    edge = (oa >= 0) & (ob >= 0) & (oa != ob)
    for a, b in zip(la[edge], lb[edge]):
        parent[find(int(b))] = find(int(a))
groups = {}
for n in ids[DIAMOND]:
    groups.setdefault(find(int(n)), []).append(int(n))
flat = template.reshape(-1, 4)
seam = np.zeros(2048, bool)
for group in groups.values():
    if len(group) > 1:
        colors, counts = np.unique(flat[group], axis=0, return_counts=True)
        flat[group] = colors[counts.argmax()]
        seam[group] = True
seam = seam.reshape(32, 64)
safe = DIAMOND & (u > 5) & (u < 59) & (v > 4) & (v < 60) & ~seam
wood = []
for variant in range(6):
    a = template.copy()
    for b in range(2):
        local_v = v-b*32
        phase = variant*7+b*13
        # Irregular broad darker/lighter strokes stay aligned with +i planks.
        wav = np.sin((u+phase)/17)*.55
        lo = 8+(phase % 6)
        cluster = safe & (board == b) & (local_v > lo+wav) & (local_v < lo+7+wav)
        cluster &= (u > 8+(phase % 11)+np.sin(local_v/2)*2) & (u < 49-(phase % 9)+np.sin(local_v/3)*3)
        paint(a, cluster, 'wood', 2 if b == 1 else 3)
        if variant in (2, 5) and b == 0:
            paint(a, cluster & (u < 28) & (local_v > lo+3), 'wood', 1)
        center = 22+(phase % 3)
        stroke = safe & (board == b) & (np.abs(local_v-center-wav) < .8)
        stroke &= (u > 12+phase % 21) & (u < 35+phase % 13)
        if variant not in (1, 4):
            paint(a, stroke, 'wood', 2 if b == 1 else 1)
        glint = safe & (board == b) & (local_v > 3) & (local_v < 4.5)
        glint &= (u > 12+(phase % 13)) & (u < 25+(phase % 13))
        if variant in (0, 2, 5):
            paint(a, glint, 'wood', 4)
        joint = 10+(variant*17+b*23) % 43
        if (variant+b) % 3 != 0:
            paint(a, safe & (board == b) & (np.abs(u-joint) < .8), 'wood', 1)
            paint(a, safe & (board == b) & (u > joint+1) & (u < joint+2.3), 'wood', 3)
    if variant in (0, 3):
        ku, kv = 18+(variant*7) % 26, 18+(variant*11) % 26
        r = ((u-ku)/4.5)**2 + ((v-kv)/1.6)**2
        paint(a, safe & (r < 1.4) & (r > .65), 'wood', 1)
        paint(a, safe & (r < .35), 'wood', 0)
        paint(a, safe & (r > 1.4) & (r < 2.2) & (v < kv), 'wood', 3)
    # One sparse scuffed patch, never a noisy per-pixel checker texture.
    scuff = safe & (u > 33+variant) & (u < 39+variant) & (v > 52-variant) & (v < 54-variant) & (variant % 2 == 0)
    paint(a, scuff, 'wood', 3)
    a[seam] = template[seam]
    wood.append(a)

dark = layer(DIAMOND, COL['metal'][0])
light = layer(DIAMOND, COL['cream'][3])
paint(dark, DIAMOND & (u > 12) & (u < 29) & (v > 19) & (v < 23), 'metal', 1)
paint(light, DIAMOND & (u > 35) & (u < 49) & (v > 42) & (v < 44), 'cream', 2)
EDGES = ('+i', '+j', '-i', '-j')
distances = [.5-di, .5-dj, .5+di, .5+dj]
CORNERS = [(0, 1), (1, 2), (2, 3), (3, 0)]
floors = [wood[0], dark, light]
border = []
for dist in distances:
    p = layer(DIAMOND & (dist < 3/64), COL['wood'][1])
    paint(p, DIAMOND & (dist >= 1/64) & (dist < 2/64), 'wood', 3)
    floors.append(p)
    q = layer(DIAMOND & (dist < 3/64), COL['wood'][0])
    paint(q, DIAMOND & (dist >= 1/64) & (dist < 2/64), 'wood', 4)
    paint(q, DIAMOND & (dist >= 2/64) & (dist < 3/64), 'wood', 2)
    border.append(q)
for ea, eb in CORNERS:
    mask = DIAMOND & (distances[ea] < 4/64) & (distances[eb] < 4/64)
    p = layer(mask, COL['wood'][3])
    paint(p, mask & ((distances[ea] < 1/64) | (distances[eb] < 1/64)), 'wood', 1)
    floors.append(p)
    q = layer(mask, COL['wood'][2])
    paint(q, mask & ((distances[ea] < 1/64) | (distances[eb] < 1/64)), 'wood', 0)
    paint(q, mask & ((distances[ea] >= 1/64) & (distances[ea] < 2/64) |
                     (distances[eb] >= 1/64) & (distances[eb] < 2/64)), 'wood', 4)
    border.append(q)
save(np.concatenate(floors), 'floors/floors.png')
save(np.concatenate(wood), 'floors/wood-variants.png')
save(np.concatenate([dark, light, *border]), 'floors/checker-border.png')


def variant_index(i, j):
    h = (((i & 0xffffffff)*0x1f123bb5) ^ ((j & 0xffffffff)*0x5f356495) ^ 0x6a09e667) & 0xffffffff
    h = ((h ^ (h >> 16))*0x45d9f3b) & 0xffffffff
    h ^= h >> 16
    return h % 6


def floor_cell(i, j, checker):
    is_checker = checker(i, j)
    a = floors[1+(i+j)%2].copy() if is_checker else wood[variant_index(i, j)].copy()
    neighbors = [(i+1, j), (i, j+1), (i-1, j), (i, j-1)]
    active = [checker(*p) != is_checker for p in neighbors]
    patches = border if is_checker else floors[3:]
    for n in range(4):
        if active[n]:
            a = over(a, patches[n])
    for n, (ea, eb) in enumerate(CORNERS):
        if active[ea] and active[eb]:
            a = over(a, patches[4+n])
    return a


def patch_room(w, h, checker):
    im = Image.new('RGBA', (32*(w+h), 16*(w+h)))
    for i in range(w):
        for j in range(h):
            put(im, floor_cell(i, j, checker), 32*h+32*(i-j)-32, 16*(i+j))
    return im


for name, checker in [('wood-review-6x6', lambda i, j: False),
                      ('checker-review-6x6', lambda i, j: True),
                      ('floor-review-6x6', lambda i, j: 3 <= i < 6 and 1 <= j < 5)]:
    im = patch_room(6, 6, checker)
    save(im, 'floors/'+name+'.png')
    save(im, 'floors/'+name+'-2x.png', 2)
cases = Image.new('RGBA', (800, 448))
d = ImageDraw.Draw(cases)
for case in range(16):
    cc = {p for n, p in enumerate(((2, 1), (1, 2), (0, 1), (1, 0))) if case & (1 << n)}
    cc.update(p for p in ((0, 0), (0, 2), (2, 0), (2, 2))
              if any(abs(p[0]-a)+abs(p[1]-b) == 1 for a, b in cc.copy()))
    xx, yy = case % 4*200, case // 4*112
    cases.alpha_composite(patch_room(3, 3, lambda i, j: (i, j) in cc), (xx+4, yy+12))
    d.text((xx+4, yy), f'{case:04b}', fill=(*COL['cream'][4], 255))
save(cases, 'floors/floor-transitions-16.png')
save(cases, 'floors/floor-transitions-16-2x.png', 2)

wy, wx = np.indices((64, 32), dtype=float)
FACE_ANCHOR = {'left': (32, 48), 'right': (0, 48)}
WALL, wall_clips, decor_cells, decor_rows = {}, {}, [], []


def rect(t, v, l, top, r, bottom):
    return (t >= l) & (t < r) & (v >= top) & (v < bottom)


for face in ('left', 'right'):
    s = 'l' if face == 'left' else 'r'
    lit = face == 'left'
    t = 32-(wx+.5) if lit else wx+.5
    v = wy+.5-t/2
    mask = (v >= 0) & (v < 48)
    panel = layer(mask, COL['navy'][1 if lit else 0])
    # Faint vertical light falloff, broad hand-brushed patches and inset panels.
    paint(panel, mask & (v >= 23), 'navy', 0)
    brush = (rect(t, v, 2, 3, 5, 19) | rect(t, v, 17, 4, 20, 12))
    paint(panel, mask & brush, 'navy', 1)
    paint(panel, mask & (v < 26) & ((t < 1) | ((t >= 16) & (t < 17))), 'navy', 0)
    paint(panel, mask & (v < 18) & ((t >= 1) & (t < 2)), 'navy', 2 if lit else 1)
    for lo, hi in ((2, 15), (17, 30)):
        inset = mask & rect(t, v, lo, 33, hi, 44)
        paint(panel, inset, 'navy', 2 if lit else 1)
        paint(panel, inset & ((t >= hi-2) | (v >= 42)), 'navy', 0)
        paint(panel, inset & ((t < lo+1) | (v < 34)), 'navy', 3 if lit else 2)
        paint(panel, inset & rect(t, v, lo+3, 36, hi-3, 41), 'navy', 1 if lit else 0)
    # Dado has an actual bevel, recessed shadow and wood face; crown/base remain 2px.
    paint(panel, mask & (v >= 28) & (v < 32), 'wood', 1 if lit else 0)
    paint(panel, mask & (v >= 28) & (v < 29), 'wood', 3 if lit else 2)
    paint(panel, mask & (v >= 31) & (v < 33), 'navy', 0)
    top = layer(mask & (v < 2), COL['wood'][3 if lit else 2])
    paint(top, mask & (v < 1), 'wood', 4 if lit else 3)
    bottom = layer(mask & (v >= 46), COL['wood'][1 if lit else 0])
    paint(bottom, mask & (v >= 46) & (v < 47), 'wood', 3 if lit else 2)
    full = over(over(panel, top), bottom)
    WALL[face] = dict(t=t, v=v, mask=mask, full=full)
    wall_clips['plain-'+s] = full

    aperture = mask & rect(t, v, 3, 9, 29, 34)
    frame = full.copy()
    frame[aperture] = 0
    ring = mask & rect(t, v, 1, 7, 31, 37) & ~aperture
    paint(frame, ring, 'wood', 1 if lit else 0)
    paint(frame, ring & ((v < 8) | (t < 2)), 'wood', 4 if lit else 3)
    paint(frame, ring & (v >= 34) & (v < 36), 'wood', 3 if lit else 2)
    paint(frame, ring & (v >= 36), 'wood', 0)
    glass = layer(aperture, COL['navy'][0])
    paint(glass, aperture & (v > 23), 'navy', 1)
    # Distant skyline silhouettes, one small warm horizon glow, sparse stars.
    skyline = aperture & (v >= 30-((np.floor(t/3).astype(int)*7) % 4))
    paint(glass, skyline, 'metal', 0)
    paint(glass, aperture & rect(t, v, 20, 25, 26, 29), 'wood', 1)
    paint(glass, aperture & rect(t, v, 22, 26, 25, 28), 'wood', 3)
    for tx, vy in ((7, 13), (22, 12), (11, 18), (25, 20)):
        paint(glass, aperture & rect(t, v, tx, vy, tx+1.2, vy+1), 'cream', 3)
    for tx in (6, 11, 22, 26):
        paint(glass, aperture & rect(t, v, tx, 31, tx+1, 32), 'wood', 4)
    mullion = aperture & (((t >= 15) & (t < 17)) | ((v >= 21) & (v < 23)))
    paint(glass, mullion, 'wood', 1 if lit else 0)
    paint(glass, mullion & ((t < 16) | (v < 22)), 'wood', 3 if lit else 2)
    wall_clips['window-frame-'+s], wall_clips['window-glass-'+s] = frame, glass

    # The original open-leaf hinge is t=8 (manifest hingeAlongEdge=.25).
    # Widen towards the latch side so the frozen hinge still meets its jamb.
    aperture = mask & rect(t, v, 8, 7, 29, 48)
    frame = full.copy()
    frame[aperture] = 0
    ring = mask & rect(t, v, 6, 5, 31, 48) & ~aperture
    paint(frame, ring, 'wood', 1 if lit else 0)
    paint(frame, ring & ((v < 6) | (t < 7)), 'wood', 4 if lit else 3)
    opening = layer(aperture & (v < 46), COL['navy'][0])
    closed = layer(aperture & (v < 46), COL['wood'][2 if lit else 1])
    for lo, hi in ((10, 24), (29, 43)):
        inset = aperture & rect(t, v, 7, lo, 25, hi)
        paint(closed, inset, 'wood', 0)
        paint(closed, inset & ((v < lo+1) | (t < 8)), 'wood', 3 if lit else 2)
        paint(closed, inset & rect(t, v, 9, lo+2, 23, hi-2), 'wood', 1 if lit else 2)
        paint(closed, inset & rect(t, v, 10, lo+3, 12, hi-3), 'wood', 2 if lit else 1)
        grain = inset & (np.abs(t-(18+np.sin((v-lo)/4))) < .75) & (v > lo+4) & (v < hi-3)
        paint(closed, grain, 'wood', 0)
    paint(closed, aperture & rect(t, v, 23, 25, 25, 30), 'metal', 0)
    paint(closed, aperture & rect(t, v, 22, 26, 25, 27), 'cream', 3)
    paint(closed, aperture & rect(t, v, 23, 27, 24, 29), 'wood', 4)
    threshold = layer(aperture & (v >= 46), COL['wood'][3 if lit else 2])
    paint(threshold, aperture & (v >= 47), 'wood', 1)
    leaf_t = wx+.5-24 if lit else 8-(wx+.5)
    leaf_v = wy+.5-(14+leaf_t/2)
    leaf_mask = (leaf_t >= 0) & (leaf_t < 6) & (leaf_v >= 0) & (leaf_v < 36)
    leaf = layer(leaf_mask, COL['wood'][1 if lit else 2])
    paint(leaf, leaf_mask & (leaf_t < 1), 'wood', 3)
    paint(leaf, leaf_mask & (leaf_t >= 5), 'wood', 0)
    for lo, hi in ((3, 15), (21, 32)):
        paint(leaf, leaf_mask & rect(leaf_t, leaf_v, 2, lo, 4, hi), 'wood', 2 if lit else 1)
    paint(leaf, leaf_mask & rect(leaf_t, leaf_v, 4, 18, 5, 20), 'cream', 3)
    for name, a in [('door-frame', frame), ('door-opening', opening),
                    ('door-leaf-closed', closed), ('door-leaf-open', leaf), ('door-threshold', threshold)]:
        wall_clips[name+'-'+s] = a
    cap = layer(mask & (t >= 30), COL['navy'][0])
    paint(cap, mask & (t >= 30) & (t < 31) & (v >= 2) & (v < 46), 'navy', 2 if lit else 1)
    cap = over(cap, layer(mask & (t >= 30) & (v < 2), COL['wood'][3 if lit else 2]))
    cap = over(cap, layer(mask & (t >= 30) & (v >= 46), COL['wood'][1 if lit else 0]))
    for name, a in [('end-cap', cap), ('top-trim', top), ('base-trim', bottom)]:
        wall_clips[name+'-'+s] = a

    # Separate transparent decor cells use the host wall's exact edge anchor.
    curtain = np.zeros((64, 32, 4), np.uint8)
    curtain_mask = mask & (rect(t, v, 1, 8, 7, 35) | rect(t, v, 25, 8, 31, 35))
    paint(curtain, curtain_mask, 'red', 2 if lit else 1)
    paint(curtain, curtain_mask & (np.floor(t).astype(int) % 3 == 0), 'red', 3 if lit else 2)
    paint(curtain, curtain_mask & ((v >= 28) & (v < 30)), 'wood', 3)
    paint(curtain, mask & rect(t, v, 1, 6, 31, 7), 'wood', 1)
    paint(curtain, mask & rect(t, v, 2, 7, 30, 8), 'wood', 3 if lit else 2)
    picture = layer(mask & rect(t, v, 7, 9, 25, 25), COL['wood'][1])
    paint(picture, mask & rect(t, v, 8, 10, 24, 24), 'wood', 3 if lit else 2)
    paint(picture, mask & rect(t, v, 10, 12, 22, 22), 'cream', 3)
    paint(picture, mask & rect(t, v, 11, 13, 21, 21), 'teal', 0)
    paint(picture, mask & rect(t, v, 12, 16, 16, 21), 'teal', 2)
    paint(picture, mask & rect(t, v, 16, 14, 20, 19), 'teal', 1)
    paint(picture, mask & rect(t, v, 16, 13, 18, 15), 'wood', 4)
    r = ((t-16)/7)**2+((v-16)/7)**2
    clock = layer(mask & (r <= 1), COL['wood'][1])
    paint(clock, mask & (r < .7), 'cream', 3)
    paint(clock, mask & (r < .7) & (t < 16) & (v < 15), 'cream', 4)
    paint(clock, mask & (r < .7) & (((t > 15) & (t < 16.5) & (v > 11) & (v < 17)) |
                                  ((t > 15) & (t < 20) & (v > 16) & (v < 17.5))), 'metal', 0)
    for tx, vy in ((16, 10), (16, 21), (11, 16), (21, 16)):
        paint(clock, mask & rect(t, v, tx, vy, tx+1, vy+1), 'wood', 1)
    lights = np.zeros((64, 32, 4), np.uint8)
    cable_v = 6+2*np.sin(t*np.pi/32)
    paint(lights, mask & (np.abs(v-cable_v) < .65), 'metal', 0)
    for tx in (6, 25):
        paint(lights, mask & rect(t, v, tx, 8, tx+2, 10), 'wood', 1)
        paint(lights, mask & rect(t, v, tx-1, 10, tx+3, 14), 'wood', 3)
        paint(lights, mask & rect(t, v, tx, 10, tx+2, 13), 'cream', 4)
    for name, a, mount in [('curtain', curtain, (16, 6)), ('picture', picture, (16, 9)),
                            ('clock', clock, (16, 9)), ('string-lights', lights, (16, 6))]:
        n = len(decor_cells)
        decor_cells.append(a)
        # Mount contact is the rod, frame top, clock rim or the cable attachment.
        yy, xx = np.where(a[:, :, 3] > 0)
        k = np.argmin(np.abs((32-xx if lit else xx)-mount[0])+np.abs((yy-(32-xx if lit else xx)/2)-mount[1]))
        anchor = dict(zip(('x', 'y'), FACE_ANCHOR[face]))
        decor_rows.append(clip(name+'-'+s, n, face=face, role='attachment', anchor=anchor,
                               edgeAnchor=anchor, mountingAnchor=dict(t=mount[0], v=mount[1]),
                               anchorChecks=[dict(x=int(xx[k]), y=int(yy[k]), radius=0)]))

wall_sheet = manifests[1]['sheets'][0]
wall_atlas = np.zeros((384, 128, 4), np.uint8)
for c in wall_sheet['rows']:
    a = wall_clips[c['name']]
    put_y, put_x = c['row']*64, c.get('column', 0)*32
    wall_atlas[put_y:put_y+64, put_x:put_x+32] = a
save(wall_atlas, 'walls/walls.png')
corner = np.zeros((64, 64, 4), np.uint8)
for face in ('left', 'right'):
    p = WALL[face]
    join = p['mask'] & (p['t'] < 1.5)
    a = p['full'].copy()
    a[~join] = 0
    offset = 0 if face == 'left' else 32
    corner[:, offset:offset+32] = a
save(corner, 'walls/walls-corner.png')
save(np.concatenate(decor_cells), 'walls/wall-decor.png')

sy, sx = np.indices((128, 128), dtype=float)
si = (sx+.5-64)/64+(sy+.5-64)/32
sj = (sy+.5-64)/32-(sx+.5-64)/64
shadows = []
shadow_rows = []
for n, face in enumerate(('left', 'right')):
    normal, travel = (si, sj) if face == 'left' else (sj, si)
    mask = (normal >= 0) & (normal < .18) & (travel >= 0) & (travel < 1)
    a = layer(mask, COL['navy'][0], 48)
    paint(a, mask & (normal < .055), 'navy', 0, 96)
    shadows.append(a)
    yy, xx = np.where(a[:, :, 3] == 96)
    shadow_rows.append(clip('wall-contact-'+face, 0, column=n, face=face,
                            anchorChecks=[dict(x=int(xx[0]), y=int(yy[0]), radius=0)]))
save(np.concatenate(shadows, axis=1), 'walls/wall-contact-shadows.png')

extra = dict(version=1, projection='iso2to1', light=dict(screenDirection='upper-left'), sheets=[
    sheet('iso-env-wood-variants', 'floor', 'floors/wood-variants.png', 64, 32, (32, 16),
          [clip('wood-i-v'+str(n), n, family='wood', axis='i') for n in range(6)],
          patches=[dict(name='wood-variants-6x6', family='wood', axis='i', tiles=[dict(row=n) for n in range(6)])]),
    sheet('iso-env-checker-border', 'floor', 'floors/checker-border.png', 64, 32, (32, 16),
          [clip('checker-dark', 0, family='checker', phase=0),
           clip('checker-light', 1, family='checker', phase=1),
           *[clip('checker-border-'+str(n), n+2, family='checker-trim', transition=True) for n in range(8)]],
          patches=[dict(name='checker-trim-base-6x6', family='checker',
                        tiles=[dict(row=0), dict(row=1)], phaseRule='parity')]),
    sheet('iso-env-wall-decor', 'wall', 'walls/wall-decor.png', 32, 64, (32, 48), decor_rows),
    sheet('iso-env-wall-contact', 'shadow', 'walls/wall-contact-shadows.png', 128, 128, (64, 64), shadow_rows)],
    optional=True, variantSelection='See NOTES-polish.md; engine integration is outside this lane.')
(OUT / 'polish.iso.json').write_text(json.dumps(extra, indent=2)+'\n', encoding='utf-8')


def module(face, kind):
    s = 'l' if face == 'left' else 'r'
    names = {'plain': ['plain'], 'window': ['window-frame', 'window-glass'],
             'open-door': ['door-frame', 'door-opening', 'door-leaf-open', 'door-threshold'],
             'closed-door': ['door-frame', 'door-opening', 'door-leaf-closed', 'door-threshold']}[kind]
    a = np.zeros((64, 32, 4), np.uint8)
    for name in names:
        a = over(a, wall_clips[name+'-'+s])
    return a


def rear_walls(door_state='open', decorated=True):
    im = Image.new('RGBA', (640, 368))
    for face in ('left', 'right'):
        for n in range(10):
            kind = door_state+'-door' if face == 'left' and n == 4 else 'window' if face == 'right' and n == 4 else 'plain'
            ax, ay = FACE_ANCHOR[face]
            gx, gy = (-32*n if face == 'left' else 32*n), -16+16*n
            a = module(face, kind)
            if n == 9:
                a = over(a, wall_clips['end-cap-'+('l' if face == 'left' else 'r')])
            if decorated:
                name = ('curtain' if face == 'right' and n == 4 else 'picture' if n == 6 else
                        'clock' if face == 'right' and n == 2 else 'string-lights' if n in (0, 1) else None)
                if name:
                    c = next(c for c in decor_rows if c['name'] == name+'-'+('l' if face == 'left' else 'r'))
                    a = over(a, decor_cells[c['row']])
            put(im, a, 320+gx-ax, 64+gy-ay)
    put(im, corner, 288, 0)
    return im


wall_review = rear_walls()
save(wall_review, 'walls/wall-review-room.png')
save(wall_review, 'walls/wall-review-room-3x.png', 3)
pantry = lambda i, j: 8 <= i <= 9 and 0 <= j <= 5
floor10 = patch_room(10, 10, pantry)


def room(door_state):
    im = Image.new('RGBA', (640, 368))
    im.alpha_composite(floor10, (0, 48))
    for face, a in zip(('left', 'right'), shadows):
        for n in range(10):
            gx, gy = (-32*n if face == 'left' else 32*n), -16+16*n
            put(im, a, 320+gx-64, 64+gy-64)
    im.alpha_composite(rear_walls(door_state))
    yy, xx = np.indices((368, 640), dtype=float)
    gi = (xx+.5-320)/64+(yy+.5-64)/32
    gj = (yy+.5-64)/32-(xx+.5-320)/64
    cut = (gi >= -.5) & (gi < 9.5) & (gj >= -.5) & (gj < 9.5) & ((gi >= 9.5-4/64) | (gj >= 9.5-4/64))
    a = np.array(im)
    paint(a, cut, 'wood', 1)
    return Image.fromarray(a)


shell = room('open')
save(shell, 'room-shell-review.png')
save(shell, 'room-shell-review-3x.png', 3)
save(room('closed'), 'walls/room-shell-closed-door.png')
board_im = Image.new('RGBA', (640, 252), (*COL['navy'][0], 255))
draw = ImageDraw.Draw(board_im)
for n, m in enumerate(manifests[1]['wallModules']):
    xx, yy = n % 8*80, n // 8*84
    a = corner if m['face'] == 'rear-corner' else np.zeros((64, 32, 4), np.uint8)
    if m['face'] != 'rear-corner':
        for p in m['parts']:
            a = over(a, wall_clips[p['clip']])
    put(board_im, a, xx+8, yy+14)
    draw.text((xx+2, yy+2), m['id'][9:], fill=(*COL['cream'][3], 255))
for n, c in enumerate(decor_rows):
    xx = n*80
    face = c['face']
    a = module(face, 'window' if c['name'].startswith('curtain') else 'plain')
    put(board_im, over(a, decor_cells[n]), xx+8, 182)
    draw.text((xx+2, 170), c['name'], fill=(*COL['cream'][3], 255))
save(board_im, 'walls/wall-modules-review-4x.png', 4)

# Compact eye-review: actual native cells enlarged with nearest-neighbor only.
detail = Image.new('RGBA', (1152, 680), (*COL['navy'][0], 255))
dd = ImageDraw.Draw(detail)
dd.text((12, 8), 'WOOD VARIANTS / common seams, changing joints and wear', fill=(*COL['cream'][3], 255))
for n, a in enumerate(wood):
    enlarged = Image.fromarray(a).resize((256, 128), Image.Resampling.NEAREST)
    detail.alpha_composite(enlarged, (12+n % 3*280, 30+n // 3*148))
dd.text((12, 332), 'WALLS / left lit, right shadow / night, curtain, closed and open door', fill=(*COL['cream'][3], 255))
for n, (face, kind, curtained) in enumerate([
        ('left', 'window', False), ('right', 'window', False),
        ('left', 'window', True), ('right', 'window', True),
        ('left', 'closed-door', False), ('right', 'closed-door', False),
        ('left', 'open-door', False), ('right', 'open-door', False)]):
    a = module(face, kind)
    if curtained:
        a = over(a, decor_cells[0 if face == 'left' else 4])
    detail.alpha_composite(Image.fromarray(a).resize((128, 256), Image.Resampling.NEAREST), (12+n*140, 358))
    dd.text((12+n*140, 620), face[0]+' '+kind+(' curtain' if curtained else ''), fill=(*COL['cream'][3], 255))
save(detail, 'polish-detail-review.png')

# Existing physical checks must still touch their actual layer, not fake markers.
for c in wall_sheet['rows']:
    for check in c['anchorChecks']:
        assert wall_clips[c['name']][check['y'], check['x'], 3] > 0, c['name']
for p, data in frozen.items():
    assert p.read_bytes() == data, 'frozen manifest changed: '+str(p)
assert np.array_equal(np.array(shell.resize((1920, 1104), Image.Resampling.NEAREST))[::3, ::3], np.array(shell))
for a in wood:
    assert np.array_equal(a[seam], template[seam])
assert len({hashlib.sha256(a.tobytes()).hexdigest() for a in wood}) == 6

# Independently assemble all 36 ordered variant pairs on both grid axes, then
# measure cross-owner RGB. This covers combinations absent from the checker patch.
pair_checks = 0
for step in ((32, 16), (-32, 16)):
    for a in wood:
        for b in wood:
            canvas = np.zeros((64, 160, 4), np.uint8)
            owners = np.full((64, 160), -1, int)
            for n, (cell, px, py) in enumerate(((a, 48, 0), (b, 48+step[0], 16))):
                canvas[py:py+32, px:px+64][DIAMOND] = cell[DIAMOND]
                owners[py:py+32, px:px+64][DIAMOND] = n
            for aa, bb, oa, ob in [(canvas[:, :-1], canvas[:, 1:], owners[:, :-1], owners[:, 1:]),
                                    (canvas[:-1], canvas[1:], owners[:-1], owners[1:])]:
                edge = (oa >= 0) & (ob >= 0) & (oa != ob)
                assert np.max(np.abs(aa[:, :, :3].astype(int)-bb[:, :, :3].astype(int))[edge], initial=0) == 0
            pair_checks += 1
yy, xx = np.indices((368, 640), dtype=float)
expected_walls = (yy+.5-.5*np.abs(xx+.5-320) >= 0) & (yy+.5-.5*np.abs(xx+.5-320) < 48)
assert np.all(np.array(wall_review)[:, :, 3][expected_walls] == 255)
closed_walls = np.array(rear_walls('closed', False))
assert not closed_walls[:, :, 3][~expected_walls].any()
assert np.all(closed_walls[:, :, 3][expected_walls] == 255)
ground = np.array(floor10)
yy, xx = np.indices((320, 640), dtype=float)
gi = (xx+.5-320)/64+(yy+.5-16)/32
gj = (yy+.5-16)/32-(xx+.5-320)/64
expected_floor = (gi >= -.5) & (gi < 9.5) & (gj >= -.5) & (gj < 9.5)
assert np.array_equal(ground[:, :, 3] == 255, expected_floor)
notes = [
    'PASS: existing floors.iso.json and walls.iso.json were read only; SHA256:',
    *[p.name+' '+hashlib.sha256(data).hexdigest() for p, data in frozen.items()],
    'PASS: 72 ordered wood-neighbor pairs across both axes; cross-owner RGB difference 0.',
    'PASS: six different wood cells, each 1,024 owned opaque texels; hash choice is deterministic.',
    'PASS: hash-selected 10x10 floor exactly covers the independent ground envelope.',
    'PASS: 20 closed rear segments exactly fill the independently computed 48px envelope.',
    'PASS: open/decorated walls fill the same envelope; open leaves may project in front within their frozen cells.',
    'PASS: all original physical contact checks remain on their own layers.',
    'PASS: 640x368 native room; 1920x1104 review is an exact nearest-neighbor 3x expansion.',
    'Scope: this is geometry/material evidence, not runtime integration or human art approval.',
]
(OUT / 'review-geometry.txt').write_text('\n'.join(notes)+'\n', encoding='utf-8')
print('ENV polish: frozen manifests unchanged; six wood variants / 72 seam pairs; eight checker trims; eight decor cells; two AO cells.')
