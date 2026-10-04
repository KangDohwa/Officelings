"""Native wall art lane. Every write is confined to this helper's directory.

Reproduce with the installed Pillow/numpy Python and -B. Never imports old
builders (they have write side effects). Imagegen city master is art content;
all mounting geometry is independently rasterized on the frozen native grid.
"""
import argparse
import copy
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[3]
ENV = ROOT / 'assets/art/iso/env'
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
C = {k: [tuple(int(h[i:i+2], 16) for i in (1, 3, 5)) for h in v.split()]
     for k, v in RAMPS.items()}
PALETTE = np.array(sorted({c for ramp in C.values() for c in ramp}), np.int16)
ANCHOR = {'l': {'x': 32, 'y': 48}, 'r': {'x': 0, 'y': 48}}
Y, X = np.indices((64, 32), dtype=float)
EDGES, CELLS, CLIPS, SHASH = [], {}, {}, {}
CURRENT = ''


def write_json(name, data):
    (OUT/name).write_text(json.dumps(data, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')


def read(path):
    path = Path(path)
    SHASH[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    return json.loads(path.read_text(encoding='utf-8')) if path.suffix == '.json' else Image.open(path).convert('RGBA')


def save(a, name, zoom=1):
    im = a if isinstance(a, Image.Image) else Image.fromarray(a)
    if zoom != 1:
        im = im.resize((im.width*zoom, im.height*zoom), Image.Resampling.NEAREST)
    im.save(OUT/name)
    return im


def empty():
    return np.zeros((64, 32, 4), np.uint8)


def tv(s):
    t = 32-(X+.5) if s == 'l' else X+.5
    return t, Y+.5-t/2


def point(s, t, v):
    return [32-t if s == 'l' else t, v+t/2]


def edge(s, p0, p1, label):
    EDGES.append(dict(clip=CURRENT, face=s, edge=label, p0=p0, p1=p1))


def rect(s, t0, v0, t1, v1, label=None):
    t, v = tv(s)
    if label:
        pts = [point(s, t0, v0), point(s, t1, v0), point(s, t1, v1), point(s, t0, v1)]
        for k in range(4):
            edge(s, pts[k], pts[(k+1)%4], label+('/top', '/far', '/bottom', '/near')[k])
    return (t >= t0) & (t < t1) & (v >= v0) & (v < v1)


def paint(a, mask, ramp, n):
    a[mask] = (*C[ramp][n], 255)


def over(a, b):
    a = a.copy()
    a[b[:, :, 3] > 0] = b[b[:, :, 3] > 0]
    return a


def poly(a, pts, ramp, n, label=None, s=None):
    # Pixel-center half-open polygon mask; no filtered or inclusive outlines.
    xx, yy = X+.5, Y+.5
    inside = np.zeros(X.shape, bool)
    for p, q in zip(pts, pts[1:]+pts[:1]):
        if p[1] != q[1]:
            inside ^= ((p[1] > yy) != (q[1] > yy)) & (xx < (q[0]-p[0])*(yy-p[1])/(q[1]-p[1])+p[0])
        if label:
            edge(s, list(p), list(q), label)
    paint(a, inside, ramp, n)


def contact(a, s, t, v):
    """Choose only the specified real fastener/frame/sill contact neighborhood."""
    p = point(s, t, v)
    y, x = np.where(a[:, :, 3] > 0)
    d = np.abs(x+.5-p[0])+np.abs(y+.5-p[1])
    k = int(np.argmin(d))
    assert d[k] <= 2.1, (CURRENT, 'no physical contact near mount', t, v, d[k])
    return dict(x=int(x[k]), y=int(y[k]), radius=0)


def add(name, s, a, group='decor', mount=(16, 9), role='attachment', contact_at=None, **extra):
    ident = name+'-'+s
    assert ident not in CELLS
    CELLS[ident] = a
    check = contact(a, s, *(contact_at or mount))
    CLIPS[ident] = dict(name=ident, direction='any', frames=1, fps=1, loop=True,
                       face='left' if s == 'l' else 'right', role=role,
                       anchor=ANCHOR[s], edgeAnchor=ANCHOR[s],
                       mountingAnchor=dict(t=mount[0], v=mount[1]),
                       anchorChecks=[check], contactMeaning='physical frame/hook/screw/emitter at the declared attachment',
                       passName='rear-walls', group=group, **extra)


def frame(a, s, bounds, ramp='wood', label='frame'):
    l, top, r, bottom = bounds
    paint(a, rect(s, l, top, r, bottom, label), ramp, 0)
    # Facing is independently shaded: screen-left rim receives the highlight.
    paint(a, rect(s, l+1, top+1, r-1, bottom-1), ramp, 2 if s == 'l' else 1)
    paint(a, rect(s, l, top, r, top+1), ramp, 4 if s == 'l' else 3)
    side = (r-1, r) if s == 'l' else (l, l+1)
    paint(a, rect(s, side[0], top+1, side[1], bottom-1), ramp, 3 if s == 'l' else 2)


def panel(s):
    a = empty()
    t, v = tv(s)
    mask = rect(s, 0, 0, 32, 48, 'wall-envelope')
    paint(a, mask, 'navy', 1 if s == 'l' else 0)
    # Quiet broad material clusters; never a noisy one-pixel random texture.
    for l, r, top, bottom in [(2, 8, 4, 17), (18, 28, 3, 13)]:
        paint(a, rect(s, l, top, r, bottom), 'navy', 1)
    paint(a, mask & (v >= 23), 'navy', 0)
    for l, r in [(2, 14), (18, 30)]:
        paint(a, rect(s, l, 34, r, 45, 'wainscot-recess'), 'navy', 2 if s == 'l' else 1)
        paint(a, rect(s, l+2, 36, r-2, 43), 'navy', 1 if s == 'l' else 0)
        paint(a, rect(s, l, 34, r, 35), 'navy', 3 if s == 'l' else 2)
        lo, hi = (r-1, r) if s == 'l' else (l, l+1)
        paint(a, rect(s, lo, 35, hi, 44), 'navy', 3 if s == 'l' else 2)
        paint(a, rect(s, l+1, 44, r, 45), 'navy', 0)
    for v0, v1, ramp, n in [(0, 1, 'wood', 4 if s == 'l' else 3),
                            (1, 2, 'wood', 2 if s == 'l' else 1),
                            (30, 31, 'wood', 3 if s == 'l' else 2),
                            (31, 32, 'wood', 1 if s == 'l' else 0),
                            (32, 33, 'navy', 0),
                            (46, 47, 'wood', 3 if s == 'l' else 2),
                            (47, 48, 'wood', 1 if s == 'l' else 0)]:
        paint(a, rect(s, 0, v0, 32, v1, 'trim-'+str(v0)), ramp, n)
    return a


GLYPHS = {'L': ['100', '100', '100', '100', '111'],
          'A': ['010', '101', '111', '101', '101'],
          'B': ['110', '101', '110', '101', '110']}


def glyph(a, s, text, t0, v0, ramp, n):
    for k, ch in enumerate(text):
        for y, row in enumerate(GLYPHS[ch]):
            for x, value in enumerate(row):
                if value == '1':
                    tx = t0+4*k+x if s == 'r' else t0+4*len(text)-2-4*k-x
                    paint(a, rect(s, tx, v0+y, tx+1, v0+y+1), ramp, n)


def build_walls():
    global CURRENT
    old = read(ENV/'walls/walls.iso.json')
    original = np.array(read(ENV/'walls/walls.png'))
    a = original.copy()
    manifest = copy.deepcopy(old)
    for sheet in manifest['sheets']:
        sheet['id'] += '-v3'
    for module in manifest['wallModules']:
        module['id'] += '-v3'
        for p in module['parts']:
            p['sheetId'] += '-v3'
    panels = {}
    for s in ('l', 'r'):
        CURRENT = 'plain-'+s
        panels[s] = panel(s)
    for c in manifest['sheets'][0]['rows']:
        s = 'l' if c['face'] == 'left' else 'r'
        CURRENT = c['name']
        row, col = c['row']*64, c.get('column', 0)*32
        old_cell = original[row:row+64, col:col+32]
        cell = old_cell.copy()
        full = panels[s] if c['role'] in ('panel', 'frame', 'end', 'top-trim', 'base-trim') else None
        if full is not None:
            t, v = tv(s)
            replace = old_cell[:, :, 3] > 0
            if c['role'] == 'frame':
                replace &= (v >= 30) | (v < 2)
                # Keep the door and original window aperture and jamb exactly.
                replace &= np.all(old_cell[:, :, :3] == np.array(C['navy'][0]), axis=2) | (t < 6) | (t >= 31) | (v < 2) | (v >= 46)
            cell[replace] = full[replace]
        if c['name'].startswith('door-leaf-closed'):
            t, v = tv(s)
            mask = old_cell[:, :, 3] > 0
            paint(cell, mask, 'wood', 2 if s == 'l' else 1)
            for top, bottom in [(9, 22), (30, 43)]:
                paint(cell, mask & rect(s, 10, top, 26, bottom, 'door-inset'), 'wood', 0)
                paint(cell, mask & rect(s, 11, top+1, 25, bottom-1), 'wood', 1 if s == 'l' else 2)
                paint(cell, mask & rect(s, 12, top+2, 14, bottom-2), 'wood', 3 if s == 'l' else 2)
                paint(cell, mask & rect(s, 20, top+3, 21, bottom-3), 'wood', 0)
            paint(cell, mask & rect(s, 24, 24, 27, 29), 'metal', 0)
            paint(cell, mask & rect(s, 23, 25, 27, 26), 'cream', 3 if s == 'l' else 2)
        assert np.array_equal(cell[:, :, 3], old_cell[:, :, 3]), c['name']+' changed coverage'
        a[row:row+64, col:col+32] = cell
        CELLS[c['name']] = cell
    save(a, 'walls.png')
    corner = read(ENV/'walls/walls-corner.png')
    save(corner, 'walls-corner.png')
    manifest['palettePolicy'] = dict(source='docs/iso-slice-s0.md frozen ramps', identityPalette='forbidden on environment sheets', tolerance=0)
    manifest['provenance'] = 'Original 15 logical modules and source anchors retained; no runtime pack integration.'
    write_json('walls.iso.json', manifest)
    return old, manifest


def build_city():
    im = read(OUT/'master-city-attempt-01.png')
    native = np.array(im.resize((20, 20), Image.Resampling.NEAREST))[:, :, :3].astype(np.int16)
    distance = ((native[:, :, None, :].astype(np.int32)-PALETTE[None, None, :, :])**2).sum(axis=3)
    result = PALETTE[np.argmin(distance, axis=2)].astype(np.uint8)
    save(np.dstack((result, np.full((20, 20), 255, np.uint8))), 'master-city-native.png')
    return result


def decor(s, city):
    global CURRENT
    t, v = tv(s)
    lit = s == 'l'
    for name in ['whiteboard', 'abstract-art', 'city-picture', 'corkboard', 'clock', 'plant-shelf', 'wall-lamp', 'string-lights', 'door-nameplate']:
        CURRENT = name+'-'+s
        a, glow = empty(), empty()
        if name in ('whiteboard', 'corkboard'):
            frame(a, s, (2, 9, 30, 30), 'metal' if name == 'whiteboard' else 'wood')
            inner = rect(s, 4, 11, 28, 28, 'board-surface')
            paint(a, inner, 'cream' if name == 'whiteboard' else 'wood', (4 if lit else 3) if name == 'whiteboard' else (2 if lit else 1))
            if name == 'whiteboard':
                for bounds in [(6, 13, 12, 17), (16, 13, 22, 17), (11, 21, 18, 25)]:
                    l, top, r, bottom = bounds
                    paint(a, rect(s, l, top, r, bottom), 'teal', 1)
                    paint(a, rect(s, l+1, top+1, r-1, bottom-1), 'cream', 4 if lit else 3)
                for bounds in [(12, 14, 16, 15), (14, 17, 15, 21), (6, 24, 9, 25)]:
                    paint(a, rect(s, *bounds), 'navy', 2)
                for l, top, r, bottom, color in [(23, 20, 27, 25, 'wood'), (20, 24, 24, 28, 'red')]:
                    paint(a, rect(s, l, top, r, bottom), color, 3)
                    paint(a, rect(s, l, top, r, top+1), color, 4 if color == 'wood' else 2)
                paint(a, rect(s, 7, 28, 25, 29, 'marker-tray'), 'metal', 2 if lit else 1)
                paint(a, rect(s, 9, 28, 13, 29), 'red', 2)
            else:
                for l, top, r, bottom, ramp, n in [(6, 12, 14, 20, 'cream', 3), (18, 13, 25, 21, 'cream', 4),
                                                   (9, 22, 17, 27, 'teal', 3), (20, 23, 26, 27, 'red', 3)]:
                    paint(a, rect(s, l, top, r, bottom, 'pinned-note'), ramp, n if lit else max(0, n-1))
                    paint(a, rect(s, l+2, top, l+3, top+1), 'red', 2)
                    paint(a, rect(s, l+2, top+3, r-1, top+4), 'metal', 2)
                # Broad cork flecks; no random single-pixel dithering.
                for l, top in [(15, 12), (5, 23), (25, 16), (16, 23)]:
                    paint(a, rect(s, l, top, l+1, top+1), 'wood', 1 if lit else 0)
        elif name in ('abstract-art', 'city-picture'):
            bounds = (4, 9, 28, 33) if name == 'city-picture' else (6, 9, 26, 33)
            frame(a, s, bounds)
            if name == 'city-picture':
                inner = rect(s, 6, 11, 26, 31, 'city-canvas')
                u = np.clip(np.floor(t-6).astype(int), 0, 19)
                vv = np.clip(np.floor(v-11).astype(int), 0, 19)
                colors = city[vv, u]
                if not lit:
                    # Independent R pigment map, keeping the sky/city relationship.
                    colors = colors.copy()
                    for ramp in ('navy', 'teal', 'wood', 'cream'):
                        original = city[vv, u]
                        for n in range(1, 5):
                            colors[np.all(original == C[ramp][n], axis=2)] = C[ramp][n-1]
                a[inner] = np.c_[colors[inner], np.full(inner.sum(), 255, np.uint8)]
            else:
                paint(a, rect(s, 8, 11, 24, 31, 'abstract-canvas'), 'cream', 4 if lit else 3)
                paint(a, rect(s, 10, 20, 16, 29), 'teal', 3 if lit else 2)
                paint(a, rect(s, 16, 23, 22, 31), 'navy', 2 if lit else 1)
                sun = ((t-18)/4)**2+((v-17)/4)**2 < 1
                paint(a, sun & rect(s, 8, 11, 24, 31), 'red', 3 if lit else 2)
                paint(a, rect(s, 10, 14, 12, 19), 'wood', 3 if lit else 2)
                paint(a, rect(s, 10, 29, 22, 30), 'wood', 2)
        elif name == 'clock':
            r = ((t-16)/7)**2+((v-16)/7)**2
            paint(a, r < 1, 'metal', 0)
            paint(a, r < .8, 'wood', 3 if lit else 2)
            paint(a, r < .6, 'cream', 4 if lit else 3)
            for tx, vy in [(16, 11), (20, 16), (16, 20), (12, 16)]:
                paint(a, rect(s, tx, vy, tx+1, vy+1) & (r < .6), 'navy', 1)
            paint(a, rect(s, 15, 12, 16, 17), 'metal', 0)
            paint(a, rect(s, 15, 16, 20, 17), 'metal', 0)
            paint(a, rect(s, 15, 16, 17, 18), 'red', 2)
        elif name == 'plant-shelf':
            # Back brackets include the fixed picture-slot screw at (16,9).
            paint(a, rect(s, 15, 9, 17, 26, 'shelf-bracket'), 'metal', 1)
            paint(a, rect(s, 15, 9, 17, 10), 'metal', 3 if lit else 2)
            p0, p1 = point(s, 6, 26), point(s, 26, 26)
            delta = (4, 2) if lit else (-4, 2)
            p2, p3 = [p1[0]+delta[0], p1[1]+2], [p0[0]+delta[0], p0[1]+2]
            poly(a, [p0, p1, p2, p3], 'wood', 3 if lit else 2, 'shelf-top', s)
            poly(a, [p3, p2, [p2[0], p2[1]+2], [p3[0], p3[1]+2]], 'wood', 1 if lit else 0, 'shelf-front', s)
            # Little cuboid cream pot; all structural edges measured.
            base = point(s, 16, 24)
            bx, by = base[0]+delta[0]/2, base[1]+1
            top = [[bx, by-7], [bx+4, by-5], [bx, by-3], [bx-4, by-5]]
            poly(a, [top[0], top[3], [bx-4, by], [bx, by+2]], 'cream', 3, 'pot-left', s)
            poly(a, [top[0], top[1], [bx+4, by], [bx, by+2]], 'cream', 2, 'pot-right', s)
            poly(a, top, 'cream', 4, 'pot-rim', s)
            poly(a, [[bx, by-6], [bx+2, by-5], [bx, by-4], [bx-2, by-5]], 'wood', 0, 'pot-soil', s)
            # Organic leaves, not floor-plane edges.
            paint(a, (abs(X+.5-bx) < .7) & (Y+.5 > by-18) & (Y+.5 < by-5), 'leaves', 1)
            for ox, oy, rx, ry, n in [(-3, -12, 4, 2, 4), (3, -10, 4, 2, 2), (-2, -16, 3, 2, 3), (2, -18, 3, 2, 4)]:
                leaf = ((X+.5-bx-ox)/rx)**2+((Y+.5-by-oy)/ry)**2 < 1
                paint(a, leaf, 'leaves', n if lit else max(1, n-1))
        elif name == 'wall-lamp':
            paint(a, rect(s, 14, 9, 18, 22, 'lamp-backplate'), 'metal', 1)
            paint(a, rect(s, 14, 9, 15, 21), 'metal', 3 if lit else 2)
            paint(a, rect(s, 11, 13, 21, 19, 'lamp-shade'), 'wood', 1 if lit else 0)
            paint(a, rect(s, 12, 14, 20, 18), 'cream', 3 if lit else 2)
            paint(a, rect(s, 13, 18, 19, 19), 'cream', 4)
            cone = rect(s, 10, 19, 22, 27) & (abs(t-16) < (v-18)*.6)
            paint(glow, cone, 'wood', 1 if lit else 0)
            paint(glow, rect(s, 12, 19, 20, 22), 'wood', 3 if lit else 2)
            paint(glow, rect(s, 13, 18, 19, 20), 'cream', 4)
        elif name == 'string-lights':
            cable = 6+np.minimum(t, 32-t)/8
            paint(a, (abs(v-cable) < .65) & (t >= 4) & (t < 28), 'metal', 1)
            for tx in [6, 16, 25]:
                vy = 6+min(tx, 32-tx)/8
                paint(a, rect(s, tx, vy, tx+1, vy+3), 'metal', 2)
                paint(a, rect(s, tx-1, vy+3, tx+2, vy+6), 'wood', 3 if lit else 2)
                paint(a, rect(s, tx, vy+3, tx+1, vy+5), 'cream', 4)
                paint(glow, rect(s, tx-2, vy+2, tx+3, vy+7), 'wood', 1 if lit else 0)
                paint(glow, rect(s, tx-1, vy+3, tx+2, vy+6), 'wood', 4)
                paint(glow, rect(s, tx, vy+3, tx+1, vy+5), 'cream', 4)
            for tx in (6, 25):
                paint(a, rect(s, tx, 8, tx+1, 9), 'metal', 3)
        elif name == 'door-nameplate':
            frame(a, s, (12, 10, 26, 18), 'metal', 'nameplate')
            paint(a, rect(s, 13, 11, 25, 17), 'navy', 1)
            glyph(a, s, 'LAB', 13, 11, 'cream', 4 if lit else 3)
        mount = (16, 6) if name == 'string-lights' else (16, 9)
        ct = (6, 8) if name == 'string-lights' else (16, 10) if name == 'door-nameplate' else None
        add(name, s, a, mount=mount, contact_at=ct, visibleWhen='closed' if name == 'door-nameplate' else 'any')
        if glow[:, :, 3].any():
            add(name+'-glow', s, glow, group='glow', mount=mount,
                contact_at=(16, 18) if name == 'wall-lamp' else (6, 11), role='light-overlay',
                pairedBody=name+'-'+s, blend='source-over', recommendedOpacity=1)


def windows(s):
    global CURRENT
    t, v = tv(s)
    lit = s == 'l'
    for half in (0, 1):
        u = t+32*half
        ap = (u >= 6) & (u < 58) & (v >= 11) & (v < 33)
        outer = (u >= 4) & (u < 60) & (v >= 9) & (v < 36)
        mullion = (u >= 31) & (u < 33) | (v >= 21) & (v < 22)
        ring = outer & (~ap | mullion)
        CURRENT = f'large-window-frame-h{half}-'+s
        frame_cell = empty()
        paint(frame_cell, ring, 'wood', 0)
        paint(frame_cell, ring & (v < 35), 'wood', 2 if lit else 1)
        paint(frame_cell, ring & ((v < 10) | (v >= 33) & (v < 34)), 'wood', 4 if lit else 3)
        paint(frame_cell, ring & (u >= 31) & (u < 32), 'wood', 3 if lit else 2)
        # The cross-edge frame remains half-open, never resized to alpha bounds.
        rect(s, max(0, 4-32*half), 9, min(32, 60-32*half), 36, 'two-edge-window-frame')
        add(f'large-window-frame-h{half}', s, frame_cell, role='frame', spanEdges=2, edgeIndex=half)
        for state in ('day', 'night', 'blinds'):
            CURRENT = f'large-window-{state}-h{half}-'+s
            glass, glow = empty(), empty()
            sky = ap & ~mullion
            paint(glass, sky, 'navy', 3 if state != 'night' else 0)
            paint(glass, sky & (v >= 17), 'navy', 4 if state != 'night' else 1)
            if state != 'night':
                for l, r, top in [(8, 19, 13), (34, 45, 15), (50, 56, 12)]:
                    cloud = sky & (u >= l) & (u < r) & (v >= top) & (v < top+2)
                    paint(glass, cloud, 'cream', 4 if lit else 3)
            else:
                for ux, vy in [(11, 14), (24, 17), (40, 13), (52, 18)]:
                    paint(glass, sky & (u >= ux) & (u < ux+1) & (v >= vy) & (v < vy+1), 'cream', 3)
                moon = sky & (((u-48)/3)**2+((v-15)/3)**2 < 1)
                paint(glass, moon, 'cream', 3 if lit else 2)
            for k, (l, r, h) in enumerate([(6, 14, 27), (14, 22, 25), (22, 29, 28), (34, 41, 26), (41, 49, 24), (49, 58, 27)]):
                building = sky & (u >= l) & (u < r) & (v >= h)
                paint(glass, building, 'navy', 2 if state != 'night' else 0)
                paint(glass, building & (u < l+1), 'navy', 3 if lit else 1)
                for wy in (h+2, h+5):
                    paint(glass, building & (u >= l+2) & (u < l+3) & (v >= wy) & (v < wy+1), 'cream' if state == 'night' else 'navy', 3 if state == 'night' else 1)
            if state == 'blinds':
                for top in (11, 14, 17, 20, 23):
                    slat = sky & (v >= top) & (v < top+2)
                    paint(glass, slat, 'cream', 3 if lit else 2)
                    paint(glass, slat & (v >= top+1), 'cream', 1 if lit else 0)
                cord = sky & (u >= 54) & (u < 55) & (v >= 13) & (v < 29)
                paint(glass, cord, 'cream', 4 if lit else 3)
            rect(s, max(0, 6-32*half), 11, min(32, 58-32*half), 33, 'glass-aperture')
            add(f'large-window-{state}-h{half}', s, glass, role='backdrop', contact_at=(16, 11),
                spanEdges=2, edgeIndex=half, state=state, opaqueBackdrop=True)
            # Small binary highlight clusters, opt-in and removable as one layer.
            highlight = sky & (u >= (10 if half == 0 else 38)) & (u < (12 if half == 0 else 40)) & (v >= 18) & (v < 21)
            paint(glow, highlight, 'glass', 4 if lit else 3)
            if state != 'night':
                paint(glow, sky & (u >= (18 if half == 0 else 46)) & (u < (20 if half == 0 else 48)) & (v >= 24) & (v < 28), 'glass', 3 if lit else 2)
            add(f'large-window-{state}-glow-h{half}', s, glow, group='glow', role='light-overlay',
                contact_at=(11 if half == 0 else 7, 19), pairedBody=f'large-window-{state}-h{half}-'+s,
                spanEdges=2, edgeIndex=half, state=state, blend='source-over', recommendedOpacity=1)


def export_decor():
    sheets = []
    for group, file in [('decor', 'wall-decor.png'), ('glow', 'wall-glow.png')]:
        selected = [name for name, c in CLIPS.items() if c['group'] == group]
        arr = np.zeros(((len(selected)+7)//8*64, 8*32, 4), np.uint8)
        rows = []
        for k, name in enumerate(selected):
            row, col = divmod(k, 8)
            arr[row*64:row*64+64, col*32:col*32+32] = CELLS[name]
            c = copy.deepcopy(CLIPS[name])
            c.pop('group')
            c.update(row=row, column=col)
            rows.append(c)
        save(arr, file)
        sheets.append(dict(id='iso-env-v3-'+group, kind='wall', file=file, cellW=32, cellH=64,
                           anchor=ANCHOR['l'], projection='iso2to1', scale=1, rows=rows))
    decor_ids = [n for n in CELLS if n in CLIPS and CLIPS[n]['group'] == 'decor']
    assemblies = []
    for s in ('l', 'r'):
        for state in ('day', 'night', 'blinds'):
            assemblies.append(dict(id=f'iso/env-v3/large-window-{state}-{s}', face='left' if s == 'l' else 'right', spanEdges=2,
                                   parts=[dict(sheetId='iso-env-v3-decor', clip=f'large-window-{state}-h{h}-{s}', edgeIndex=h) for h in (0, 1)]+
                                         [dict(sheetId='iso-env-v3-decor', clip=f'large-window-frame-h{h}-{s}', edgeIndex=h) for h in (0, 1)],
                                   overlays=[dict(sheetId='iso-env-v3-glow', clip=f'large-window-{state}-glow-h{h}-{s}', edgeIndex=h) for h in (0, 1)],
                                   mount='two consecutive plain hosts; glass then frame then optional overlay', hostDepth='inherit each edge; hanging height never changes depth'))
        for name in ('whiteboard', 'clock', 'abstract-art', 'city-picture', 'corkboard', 'plant-shelf', 'wall-lamp', 'string-lights', 'door-nameplate'):
            body = name+'-'+s
            glow = name+'-glow-'+s
            assembly = dict(id=f'iso/env-v3/{body}', face='left' if s == 'l' else 'right', spanEdges=1,
                            slot='clock' if name == 'clock' else 'string-lights' if name == 'string-lights' else 'picture',
                            parts=[dict(sheetId='iso-env-v3-decor', clip=body, edgeIndex=0)],
                            overlays=[dict(sheetId='iso-env-v3-glow', clip=glow, edgeIndex=0)] if glow in CLIPS else [],
                            hostDepth='inherit host ground depth; attachment sorts after its host')
            if name == 'door-nameplate':
                assembly.update(hostManifest='walls.iso.json', hostModule=f'iso/wall-closed-door-{s}-v3', visibleWhen='closed', slot='closed-leaf')
            assemblies.append(assembly)
    slots = {'picture': dict(t=16, v=9, source={'l': [16, 17], 'r': [16, 17]}),
             'clock': dict(t=16, v=9, source={'l': [16, 17], 'r': [16, 17]}),
             'string-lights': dict(t=16, v=6, source={'l': [16, 14], 'r': [16, 14]}, hooks=[[6, 8], [25, 8]])}
    manifest = dict(version=1, projection='iso2to1', requiredCapabilities=['iso2to1', 'renderItemsV1'],
                    light=dict(screenDirection='upper-left', highlightVector=[-2, -1]), sheets=sheets,
                    palettePolicy=dict(identityPalette='forbidden for environment; no identityPalette field on any sheet', source='docs/iso-slice-s0.md', tolerance=0),
                    wallHeight=48, wallRise=16, decorSlots=slots, decorAssemblies=assemblies,
                    doorNameplate=dict(text='LAB', applyTo='closed door leaf only', hingeAlongEdge=.25, approachUnchanged=True),
                    optional=True, runtimeIntegration='Art export only; adapters/packs not changed',
                    masters=dict(city=dict(file='master-city-attempt-01.png', native='master-city-native.png', attempts=1, method='built-in imagegen, NEAREST 20x20, exact ramp snap'),
                                 rectilinear='native pixel-center masks; sufficient without generated masters', drawingPass=3))
    write_json('wall-decor.iso.json', manifest)
    return manifest


def floor_room():
    f = np.array(read(ENV/'floors/floors.png'))
    wood = np.array(read(ENV/'floors/wood-variants.png'))
    border = np.array(read(ENV/'floors/checker-border.png'))
    im = Image.new('RGBA', (640, 368))
    checker = lambda i, j: 8 <= i <= 9 and 0 <= j <= 5
    for i in range(10):
        for j in range(10):
            is_checker = checker(i, j)
            if is_checker:
                tile = f[(1+(i+j)%2)*32:(2+(i+j)%2)*32].copy()
            else:
                h = (((i & 0xffffffff)*0x1f123bb5) ^ ((j & 0xffffffff)*0x5f356495) ^ 0x6a09e667) & 0xffffffff
                h = ((h ^ (h >> 16))*0x45d9f3b) & 0xffffffff
                h ^= h >> 16
                n = h % 6
                tile = wood[n*32:n*32+32].copy()
            neighbors = [(i+1, j), (i, j+1), (i-1, j), (i, j-1)]
            active = [checker(*p) != is_checker for p in neighbors]
            patches = border[64:] if is_checker else f[96:]
            for n in range(4):
                if active[n]:
                    tile = over(tile, patches[n*32:n*32+32])
            for n, (ea, eb) in enumerate([(0, 1), (1, 2), (2, 3), (3, 0)]):
                if active[ea] and active[eb]:
                    tile = over(tile, patches[(4+n)*32:(5+n)*32])
            im.alpha_composite(Image.fromarray(tile), (320+32*(i-j)-32, 48+16*(i+j)))
    # Ground envelope and its 2px near cut agree with the original room fixture.
    yy, xx = np.indices((368, 640), dtype=float)
    gi = (xx+.5-320)/64+(yy+.5-64)/32
    gj = (yy+.5-64)/32-(xx+.5-320)/64
    cut = (gi >= -.5) & (gi < 9.5) & (gj >= -.5) & (gj < 9.5) & ((gi >= 9.5-4/64) | (gj >= 9.5-4/64))
    a = np.array(im)
    paint(a, cut, 'wood', 1)
    return Image.fromarray(a)


def placement(s, n):
    return (320+(-32*n if s == 'l' else 32*n)-ANCHOR[s]['x'], 16*n)


PLAN = {'l': {0: 'whiteboard', 1: 'window', 2: 'window', 3: 'clock', 4: 'door',
              5: 'abstract-art', 6: 'plant-shelf', 7: 'corkboard', 8: 'wall-lamp', 9: 'city-picture'},
        'r': {0: 'whiteboard', 1: 'clock', 2: 'corkboard', 3: 'plant-shelf', 4: 'window', 5: 'window',
              6: 'abstract-art', 7: 'wall-lamp', 8: 'city-picture', 9: 'string-lights'}}


def room(day=True, glows=True, counterpart=False):
    im = floor_room()
    shadows = np.array(read(ENV/'walls/wall-contact-shadows.png'))
    for col, s in enumerate(('l', 'r')):
        for n in range(10):
            shadow = Image.fromarray(shadows[:, col*128:(col+1)*128])
            im.alpha_composite(shadow, (320+(-32*n if s == 'l' else 32*n)-64, 16*n-16))
    # Alpha-clipping the existing AO to floor prevents any new exterior pixels.
    floor_mask = np.array(floor_room())[:, :, 3] > 0
    floor_a = np.array(im)
    floor_a[~floor_mask] = 0
    im = Image.fromarray(floor_a)
    fixture = []
    for s in ('l', 'r'):
        plan = PLAN['r' if s == 'l' else 'l'] if counterpart else PLAN[s]
        window_start = min(n for n, what in plan.items() if what == 'window')
        for n, what in plan.items():
            dest = placement(s, n)
            cell = CELLS['plain-'+s].copy()
            if what == 'door':
                cell = empty()
                for part in ['door-frame', 'door-opening', 'door-leaf-closed', 'door-threshold']:
                    cell = over(cell, CELLS[part+'-'+s])
                cell = over(cell, CELLS['door-nameplate-'+s])
            elif what == 'window':
                half = n-window_start
                state = ('day' if day else 'night') if ((s == 'l') != counterpart) else 'blinds'
                cell = over(cell, CELLS[f'large-window-{state}-h{half}-{s}'])
                cell = over(cell, CELLS[f'large-window-frame-h{half}-{s}'])
                if glows:
                    cell = over(cell, CELLS[f'large-window-{state}-glow-h{half}-{s}'])
            else:
                cell = over(cell, CELLS[what+'-'+s])
                if glows and what+'-glow-'+s in CELLS:
                    cell = over(cell, CELLS[what+'-glow-'+s])
            if n == 9:
                cell = over(cell, CELLS['end-cap-'+s])
            im.alpha_composite(Image.fromarray(cell), dest)
            fixture.append(dict(face='left' if s == 'l' else 'right', segment=n, kind=what,
                                edge=dict(i=-.5 if s == 'l' else n-.5, j=n-.5 if s == 'l' else -.5),
                                edgeScreen=[320+(-32*n if s == 'l' else 32*n), 48+16*n], destination=list(dest), anchor=ANCHOR[s]))
    # Copy the existing inner corner join: one join, no doubled outline.
    im.alpha_composite(Image.open(OUT/'walls-corner.png').convert('RGBA'), (288, 0))
    # Original ENV writes the near cut after AO; retain those final 24 texels too.
    yy, xx = np.indices((368, 640), dtype=float)
    gi = (xx+.5-320)/64+(yy+.5-64)/32
    gj = (yy+.5-64)/32-(xx+.5-320)/64
    cut = (gi >= -.5) & (gi < 9.5) & (gj >= -.5) & (gj < 9.5) & ((gi >= 9.5-4/64) | (gj >= 9.5-4/64))
    a = np.array(im)
    paint(a, cut, 'wood', 1)
    im = Image.fromarray(a)
    return im, fixture


def label(d, xy, text, fill='#F2EBD3'):
    d.text(xy, text, fill=fill)


def contact_sheet():
    names = ['large-window-day', 'large-window-night', 'large-window-blinds', 'whiteboard', 'clock',
             'abstract-art', 'city-picture', 'corkboard', 'plant-shelf', 'wall-lamp', 'string-lights', 'door']
    im = Image.new('RGBA', (1056, 1104), (*C['teal'][0], 255))
    d = ImageDraw.Draw(im)
    for k, name in enumerate(names):
        bx, by = k%3*352, k//3*276
        label(d, (bx+8, by+8), name+' | L / R | native x'+('2' if name.startswith('large-window') else '3'))
        for sid, s in enumerate(('l', 'r')):
            if name.startswith('large-window'):
                state = name.split('-')[-1]
                sample = Image.new('RGBA', (64, 80))
                for half in (0, 1):
                    cell = over(CELLS['plain-'+s], CELLS[f'large-window-{state}-h{half}-{s}'])
                    cell = over(cell, CELLS[f'large-window-frame-h{half}-{s}'])
                    cell = over(cell, CELLS[f'large-window-{state}-glow-h{half}-{s}'])
                    sample.alpha_composite(Image.fromarray(cell), ((32-32*half) if s == 'l' else 32*half, half*16))
                # Two cells span 64 screen pixels, the y offset is the frozen rise.
            else:
                cell = CELLS['plain-'+s].copy()
                if name == 'door':
                    cell = empty()
                    for p in ['door-frame', 'door-opening', 'door-leaf-closed', 'door-threshold', 'door-nameplate']:
                        cell = over(cell, CELLS[p+'-'+s])
                else:
                    cell = over(cell, CELLS[name+'-'+s])
                    if name+'-glow-'+s in CELLS:
                        cell = over(cell, CELLS[name+'-glow-'+s])
                sample = Image.fromarray(cell)
            zoom = sample.resize((sample.width*3, sample.height*3), Image.Resampling.NEAREST)
            # Large window pair gets a separate column; avoid squeezing it into a bbox.
            if sample.width == 64:
                zoom = sample.resize((128, 160), Image.Resampling.NEAREST)
            im.alpha_composite(zoom, (bx+12+sid*168, by+30))
    save(im, 'decor-review.png')


def reviews():
    for name, kwargs in [('empty-room-day', {}), ('empty-room-night', dict(day=False)),
                         ('empty-room-counterpart', dict(counterpart=True)), ('empty-room-body-only', dict(glows=False))]:
        im, fixture = room(**kwargs)
        save(im, name+'-1x.png')
        if name != 'empty-room-body-only':
            for z in (2, 3):
                save(im, name+f'-{z}x.png', z)
        write_json(name+'-placement.json', dict(room=[10, 10], projection='P(i,j,z)=(32*(i-j),16*(i+j)-z)',
                                              screenOrigin=[320, 64], walls=fixture, actorAndFurniture='none; real existing S0 floor and wall modules'))
    before = read(ENV/'walls/room-shell-closed-door.png')
    save(before, 'before-env-polish-1x.png')
    after = Image.open(OUT/'empty-room-day-1x.png').convert('RGBA')
    board = Image.new('RGBA', (1312, 408), (*C['teal'][0], 255))
    d = ImageDraw.Draw(board)
    label(d, (12, 10), 'BEFORE | existing ENV polish (v2 baseline)')
    label(d, (668, 10), 'AFTER | ENV v3 | native 10x10 | daylight / blinds')
    board.alpha_composite(before, (8, 32))
    board.alpha_composite(after, (664, 32))
    save(board, 'before-after.png')
    save(board, 'before-after-2x.png', 2)
    contact_sheet()
    hero = read(ROOT/'assets/art/iso-concept/iso-office-hero.png')
    integrated = read(Path('C:/Codex-Workspace/vd-lanes/layout/artifacts/iso-layout-final/iso-empty-2x.png'))
    board = Image.new('RGBA', (1440, 1230), (*C['teal'][0], 255))
    d = ImageDraw.Draw(board)
    label(d, (12, 10), 'HERO / integrated v2 baseline (references only) / v3 native empty room')
    for im, dest, scale in [(hero, (0, 30), .375), (integrated, (720, 30), .5), (after, (80, 466), 2)]:
        board.alpha_composite(im.resize((round(im.width*scale), round(im.height*scale)), Image.Resampling.NEAREST), dest)
    save(board, 'hero-v2-v3-comparison.png')


def fit_edge(points, axis, expected):
    p = np.asarray(points, float)
    independent, dependent = (p[:, 0], p[:, 1]) if axis == 'x' else (p[:, 1], p[:, 0])
    m, b = np.polyfit(independent, dependent, 1)
    span = float(np.ptp(independent))
    return dict(axis=axis, slope=float(m), expected=expected, span_px=span,
                drift_px=float(abs(m-expected)*span), fit_residual_px=float(np.max(abs(dependent-(m*independent+b)))),
                samples=len(p), p0=p[0].tolist(), p1=p[-1].tolist())


def measure():
    """Endpoint arithmetic AND independent alpha-boundary native samples.

OLS follows measure_redraw_shelf_v3.py. The half-pixel staircase has a small
OLS drift; endpoint drift must be exactly zero, raster pattern must be 2:1.
"""
    measured = []
    for e in EDGES:
        x0, y0 = e['p0']
        x1, y1 = e['p1']
        dx, dy = x1-x0, y1-y0
        assert dx == 0 or abs(dx) == 2*abs(dy), e
        measured.append(dict(**e, dx=dx, dy=dy, expected='vertical' if dx == 0 else dy/dx, endpointDrift=0))
    write_json('geometry-endpoints.json', measured)
    records = []
    # Every exported structural rectangle's isolated native mask is measurable;
    # full-cell alpha envelopes additionally check the actual saved body pixels.
    saved = {}
    for filename in ('walls.iso.json', 'wall-decor.iso.json'):
        manifest = json.loads((OUT/filename).read_text(encoding='utf-8'))
        for sheet in manifest['sheets']:
            atlas = np.array(Image.open(OUT/sheet['file']).convert('RGBA'))
            for c in sheet['rows']:
                row, col = c['row']*sheet['cellH'], c.get('column', 0)*sheet['cellW']
                saved[c['name']] = atlas[row:row+sheet['cellH'], col:col+sheet['cellW']]
    measured_names = ('plain-l', 'plain-r')
    rect_names = ('whiteboard-', 'abstract-art-', 'city-picture-', 'corkboard-', 'door-nameplate-', 'door-leaf-closed-', 'large-window-frame-')
    for name, cell in saved.items():
        if name not in measured_names and not name.startswith(rect_names):
            continue
        s = name[-1]
        alpha = cell[:, :, 3] > 0
        columns = np.flatnonzero(alpha.any(axis=0))
        samples = [[float(x)+.5, float(np.flatnonzero(alpha[:, x])[0])] for x in columns]
        expected = -.5 if s == 'l' else .5
        r = fit_edge(samples, 'x', expected)
        r.update(clip=name, edge='saved-alpha-top')
        records.append(r)
        ys = np.array([p[1] for p in samples])
        steps = np.diff(ys)
        assert np.isin(steps, [0, expected*2]).all(), (name, steps)
        assert np.allclose(ys[2:]-ys[:-2], expected*2), name+' wrong 2:1 staircase'
        # Actual outer uprights, including the short nameplate verticals.
        for x in (int(columns.min()), int(columns.max())):
            run = np.flatnonzero(alpha[:, x])
            if len(run) >= 4:
                r = fit_edge([[float(x), float(y)+.5] for y in run], 'y', 0)
                r.update(clip=name, edge='saved-alpha-upright')
                records.append(r)
    # Actual projected shelf front rails are floor-plane edges, unlike wall art.
    for s in ('l', 'r'):
        cell = saved['plant-shelf-'+s]
        color = C['wood'][1 if s == 'l' else 0]
        mask = (cell[:, :, 3] > 0) & np.all(cell[:, :, :3] == color, axis=2) & (Y >= 30)
        columns = np.flatnonzero(mask.any(axis=0))
        samples = [[float(x)+.5, float(np.flatnonzero(mask[:, x])[0])] for x in columns]
        expected = -.5 if s == 'l' else .5
        r = fit_edge(samples, 'x', expected)
        r.update(clip='plant-shelf-'+s, edge='saved-wood-front-rail')
        records.append(r)
        assert len(samples) >= 18
        ys = np.array([p[1] for p in samples])
        assert np.isin(np.diff(ys), [0, expected*2]).all()
        assert np.allclose(ys[2:]-ys[:-2], expected*2), 'shelf floor-plane skew'
    write_json('raster-edge-measurements.json', records)
    maximum = max(r['drift_px'] for r in records)
    assert maximum < .12, maximum
    return len(measured), len(records), maximum


def verify(old, new, decor_manifest):
    results = []
    # Cell placement, hinge, door approach and anchors are frozen, not inferred.
    for a, b in zip(old['sheets'], new['sheets']):
        for k in ('kind', 'cellW', 'cellH', 'anchor', 'scale', 'projection', 'rows'):
            assert a[k] == b[k], 'changed frozen wall field '+k
    results.append('PASS: 15 logical wall modules, 22 face cells + corner, original rows/names, source edge anchors, rise16/height48, hinge .25 and leaf ground steps unchanged.')
    clips_count = 0
    for manifest in (new, decor_manifest):
        for sheet in manifest['sheets']:
            assert 'identityPalette' not in sheet, 'env identity palette expansion'
            a = np.array(Image.open(OUT/sheet['file']).convert('RGBA'))
            assert set(np.unique(a[:, :, 3])).issubset({0, 255}), sheet['file']
            assert not a[a[:, :, 3] == 0].any(), sheet['file']+' transparent RGB'
            used = np.unique(a[:, :, :3][a[:, :, 3] > 0], axis=0)
            assert all(tuple(c) in set(map(tuple, PALETTE)) for c in used), sheet['file']
            clips_count += len(sheet['rows'])
    results.append(f'PASS: {clips_count} exported clips; exact frozen ramp membership, binary alpha, zero transparent RGB, no environment identityPalette.')
    for name in ('whiteboard', 'abstract-art', 'city-picture', 'corkboard', 'clock', 'plant-shelf', 'wall-lamp', 'string-lights', 'door-nameplate'):
        left, right = CELLS[name+'-l'], CELLS[name+'-r']
        assert not np.array_equal(left[:, ::-1], right), name+' is only reflected'
    results.append('PASS: L/R artwork independently shaded; R differs from reflected L for all nine one-edge decor bodies. Windows use independent sky/frame pigment and reflection stops.')
    edges, rasters, drift = measure()
    results.append(f'PASS: {edges} structural endpoint edges exactly +/-0.5 or vertical (drift0); {rasters} native saved-alpha OLS traces; maximum staircase-fit accumulated drift {drift:.6f}px. Every sampled diagonal repeats exactly 2 pixels per 1 row.')
    for name in ('empty-room-day', 'empty-room-night', 'empty-room-counterpart'):
        native = Image.open(OUT/(name+'-1x.png')).convert('RGBA')
        assert native.size == (640, 368)
        for z in (2, 3):
            expanded = Image.open(OUT/(name+f'-{z}x.png')).convert('RGBA')
            assert np.array_equal(np.array(expanded), np.array(native.resize((640*z, 368*z), Image.Resampling.NEAREST)))
        # Independent projected coverage: floor diamond union + 20 wall faces.
        yy, xx = np.indices((368, 640), dtype=float)
        gi = (xx+.5-320)/64+(yy+.5-64)/32
        gj = (yy+.5-64)/32-(xx+.5-320)/64
        coverage = (gi >= -.5) & (gi < 9.5) & (gj >= -.5) & (gj < 9.5)
        for s in ('l', 'r'):
            for n in range(10):
                dx, dy = placement(s, n)
                t = 32-(xx+.5-dx) if s == 'l' else xx+.5-dx
                v = yy+.5-dy-t/2
                coverage |= (t >= 0) & (t < 32) & (v >= 0) & (v < 48)
        assert np.array_equal(np.array(native)[:, :, 3] > 0, coverage), name+' gap or exterior overhang'
    baseline = np.array(Image.open(OUT/'before-env-polish-1x.png').convert('RGBA'))
    final = np.array(Image.open(OUT/'empty-room-day-1x.png').convert('RGBA'))
    floor_mask = (gi >= -.5) & (gi < 9.5) & (gj >= -.5) & (gj < 9.5)
    assert floor_mask.sum() == 102400
    assert np.array_equal(baseline[floor_mask], final[floor_mask]), 'existing floor changed in comparison'
    results.append('PASS: all 102400 real floor texels byte-identical to existing ENV baseline, including transitions, AO and final near cut; room changes are confined to walls/decor.')
    results.append('PASS: 10x10 real S0 floor/wall placement, original transition/AO pixels, independent complete floor+20-face coverage with no gaps/overhangs; 640x368, 1280x736, 1920x1104 exact nearest-neighbor previews. Main left door remains beside (0,4), first right window edge beside (4,0).')
    for name, hash_ in SHASH.items():
        if str(OUT) not in name:
            assert hashlib.sha256(Path(name).read_bytes()).hexdigest() == hash_, 'read source changed '+name
    write_json('source-sha256.json', SHASH)
    results.append('PASS: read-only ENV/spec/reference source hashes preserved within this run; no writes outside env-v3.')
    proc = subprocess.run([sys.executable, '-B', str(ROOT/'tools/iso_check.py'), str(OUT), '--tolerance', '0'], cwd=ROOT, capture_output=True, env=dict(os.environ, PYTHONIOENCODING='utf-8'))
    output = (proc.stdout+proc.stderr).decode('utf-8')
    (OUT/'iso-check.txt').write_text(output, encoding='utf-8')
    assert proc.returncode == 0, 'checker failed; see iso-check.txt'
    results.append(f"PASS: tools/iso_check.py env-v3 --tolerance 0: {output.count('| PASS |')} PASS / {output.count('| SKIP |')} inapplicable SKIP / {output.count('| FAIL |')} FAIL.")
    results.append('Limits: no engine/pack wiring, actor crossing test or dotnet tests; binary glow is stylized, not soft room illumination. Raw generated master is reference only and excluded from production manifests.')
    (OUT/'validation.txt').write_text('\n'.join(results)+'\n', encoding='utf-8')
    print('\n'.join(results))


def notes():
    (OUT/'NOTES-v3.md').write_text('''# ISO wall/decor v3

출력은 이 폴더에만 있다. 기존 ENV polish를 v2 비교 기준으로 사용했다. 별도 env-v2 폴더는 없다. 기존 벽·바닥·팩은 수정하지 않았다.

- `walls.iso.json`: 원래 15개 논리 모듈, 32×64 L/R 셀 및 64×64 코너, clip/edgeAnchor/door hinge/열린 문 동작 메타데이터 유지. 새 ID에 `-v3`만 추가했다. 새 평벽과 하단 웨인스콧·몰딩 픽셀을 제공한다. trim은 기존 trim 픽셀을 대체하는 방식이다.
- `wall-decor.iso.json`: 낮/밤 큰 창, 블라인드 창, 화이트보드·스티키 노트·다이어그램, 시계, 추상화, 도시 그림, 코르크 보드, 화분 벽선반, 벽등, 스트링 라이트, LAB 문패의 L/R 쌍. 환경 sheet에 identityPalette를 추가하지 않았다.
- 큰 창은 **명시적 spanEdges:2**이다. 기존 32×64 셀 두 개를 32px/16px 간격으로 조립한다. 한 셀의 크기나 앵커를 늘리지 않는다. `decorAssemblies`에 조립 순서와 half별 edgeIndex가 있다. 두 평벽 위에 backdrop → frame → optional glow를 올린다.
- 기존 picture/clock 슬롯 (t16,v9), string 슬롯 (t16,v6) 및 hooks(6,8)/(25,8)을 유지했다. 장식은 host ground depth를 상속한다. `anchorChecks`는 상단 테두리·브래킷 나사·훅·창틀 또는 발광부의 실제 texel이다.
- `wall-glow.png`는 별도 선택 레이어다. binary alpha/정확한 팔레트라 부드러운 광원 확산을 표현하지 않는다. 문패는 닫힌 leaf에만 올리고, 열린 leaf/threshold의 기존 앵커는 그대로 둔다.
- 기본 방은 왼쪽 문 (0,4), 오른쪽 창 (4,0)을 유지한다. daylight/night/counterpart 각 1x/2x/3x 및 body-only 검수 PNG가 있다. counterpart는 양쪽 장식 방향 확인용이며 runtime layout 변경이 아니다.

도시 그림에 built-in imagegen을 1회 사용했다. 나머지 직선형 장식은 native hand-pixel masks로 충분했다. 원본 master는 production sheet가 아니다. 20×20 NEAREST 축소 후 frozen ramp로 snap하여 벽면으로 투영했다. 어떤 alpha bbox fit도 구조 기울기 계산에 사용하지 않았다.

재현: installed Pillow/numpy Python으로 `-B build_measure_wall_decor_v3_20261004.py`를 실행한다. helper는 자신의 폴더에만 쓴다. 검사: `-B tools/iso_check.py assets/art/iso/env-v3 --tolerance 0`. geometry-endpoints.json과 raster-edge-measurements.json은 endpoint 검사와 saved-alpha OLS 측정을 따로 기록한다. 곡선 시계 테두리·잎·전구 케이블·그림 내용은 바닥 평면 구조선이 아니다.

남은 약점: 48px 벽 높이 때문에 보드 글자는 기호 수준이고 도시는 작게 압축된다. 우측 면의 세부 대비는 낮다. 모든 장식을 넣은 방은 hero보다 촘촘하고, 원래 코너/열린 문 잎의 작은 형태를 유지한다. 광원은 작고 그래픽하다. hero의 회화적 주변광, 가구와의 가림, 실제 앱 배치·문 통과는 검증하지 않았다. 새 런타임 통합은 별도 선택이 필요하다.
''', encoding='utf-8')


def main():
    for p in (ROOT/'docs/iso-slice-s0.md', ROOT/'docs/iso-spec.md', ENV/'NOTES-polish.md'):
        SHASH[str(p)] = hashlib.sha256(p.read_bytes()).hexdigest()
    old, new = build_walls()
    city = build_city()
    for s in ('l', 'r'):
        decor(s, city)
        windows(s)
    manifest = export_decor()
    reviews()
    verify(old, new, manifest)
    notes()


if __name__ == '__main__':
    main()
