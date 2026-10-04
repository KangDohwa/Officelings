"""TV lane only: authored native pixels from axis-aligned model surfaces.

No downloads, runtime edits, external image generators, transforms at runtime,
or writes outside this script's directory. Run with Python -B.
"""
from pathlib import Path
import copy
import hashlib
import json
import math
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[4]
SRC = ROOT / 'assets/art/iso/furniture-v2/lounge'
FACINGS = ('se', 'sw', 'ne', 'nw')
ANCHORS = {'se': (76, 72), 'sw': (52, 72), 'ne': (52, 72), 'nw': (76, 72)}
TV_TOP = 48  # Sole S0 exception; see CHANGES-TV-v3.md. Console remains z16.
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


def rgba(c, a=255):
    return tuple(int(c[n:n+2], 16) for n in (1, 3, 5)) + (a,)


PALETTE = np.array(sorted({rgba(c)[:3] for r in RAMPS.values() for c in r.split()}), dtype=np.int16)


def point(x, y):
    return {'x': int(x), 'y': int(y)}


def write_json(name, obj):
    (OUT / name).write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def save(name, arr):
    Image.fromarray(np.asarray(arr, dtype=np.uint8), 'RGBA').save(OUT / name)


def snap_palette(arr):
    arr = arr.copy()
    used, inv = np.unique(arr[:, :, :3][arr[:, :, 3] > 0], axis=0, return_inverse=True)
    if len(used):
        dist = np.max(np.abs(used[:, None, :].astype(np.int16) - PALETTE[None, :, :]), axis=2)
        arr[:, :, :3][arr[:, :, 3] > 0] = PALETTE[np.argmin(dist, axis=1)][inv]
    arr[arr[:, :, 3] == 0] = 0
    return arr


def rotate(q, facing):
    q = np.asarray(q, dtype=float).copy()
    u, v = q[..., 0].copy(), q[..., 1].copy()
    if facing == 'sw':
        q[..., 0], q[..., 1] = 1-v, u
    elif facing == 'ne':
        q[..., 0], q[..., 1] = v, -u
    elif facing == 'nw':
        q[..., 0], q[..., 1] = -u, 1-v
    return q


def project(q, facing):
    q = rotate(q, facing)
    ax, ay = ANCHORS[facing]
    xy = np.stack((ax + 32*(q[..., 0]-q[..., 1]), ay + 16*(q[..., 0]+q[..., 1])-q[..., 2]), axis=-1)
    return np.floor(xy+.5)


def depth_anchor(u, v, facing):
    w = rotate(np.array([u, v, 0.]), facing)
    return {'i': float(w[0]), 'j': float(w[1])}


class Raster:
    def __init__(self, facing):
        self.facing = facing
        self.arr = np.zeros((128, 128, 4), np.uint8)
        self.depth = np.full((128, 128), -np.inf)
        self.labels = np.full((128, 128), '', dtype=object)
        self.world = np.zeros((128, 128, 3), float)
        self.edges = []
        self.contacts = {}
        self.keys = {}

    def face(self, verts, color, label, key, contact):
        verts = np.array(verts, dtype=float)
        xy = project(verts, self.facing)
        for a, b in zip(xy, np.roll(xy, -1, axis=0)):
            dx, dy = b-a
            assert dx == 0 or abs(dx) == 2*abs(dy), ('skew', self.facing, label, a, b)
            if dx or dy:
                self.edges.append({'part': label if isinstance(label, str) else 'per-cell', 'a': a.tolist(), 'b': b.tolist()})
        for idx in ((0, 1, 2), (0, 2, 3)):
            t = xy[list(idx)]
            den = (t[1, 1]-t[2, 1])*(t[0, 0]-t[2, 0])+(t[2, 0]-t[1, 0])*(t[0, 1]-t[2, 1])
            if abs(den) < 1e-10:
                continue
            x0, y0 = np.maximum(np.floor(t.min(axis=0)).astype(int), 0)
            x1, y1 = np.minimum(np.ceil(t.max(axis=0)).astype(int), 128)
            yy, xx = np.mgrid[y0:y1, x0:x1].astype(float)
            xx += .5
            yy += .5
            a = ((t[1, 1]-t[2, 1])*(xx-t[2, 0])+(t[2, 0]-t[1, 0])*(yy-t[2, 1]))/den
            b = ((t[2, 1]-t[0, 1])*(xx-t[2, 0])+(t[0, 0]-t[2, 0])*(yy-t[2, 1]))/den
            c = 1-a-b
            inside = (a >= -1e-9) & (b >= -1e-9) & (c >= -1e-9)
            uvz = a[..., None]*verts[idx[0]]+b[..., None]*verts[idx[1]]+c[..., None]*verts[idx[2]]
            w = rotate(uvz, self.facing)
            dep = w[:, :, 0]+w[:, :, 1]+w[:, :, 2]/16
            take = inside & (dep >= self.depth[y0:y1, x0:x1]-1e-8)
            iy, ix = np.where(take)
            if not len(ix):
                continue
            ys, xs = iy+y0, ix+x0
            q = uvz[iy, ix]
            self.depth[ys, xs] = dep[iy, ix]
            self.world[ys, xs] = q
            colors = color(q) if callable(color) else np.tile(rgba(color), (len(q), 1))
            self.arr[ys, xs] = colors
            names = label(q) if callable(label) else np.full(len(q), label, object)
            self.labels[ys, xs] = names
            for name in set(names):
                self.keys[name] = key(name) if callable(key) else key
                self.contacts[name] = contact(name) if callable(contact) else contact

    def box(self, bounds, material, role, number=None):
        u0, u1, v0, v1, z0, z1 = bounds
        # All six faces reproject from the model, including rear orientations.
        faces = [
            ([[u0,v0,z0],[u0,v1,z0],[u0,v1,z1],[u0,v0,z1]], (-1,0,0)),
            ([[u1,v0,z0],[u1,v1,z0],[u1,v1,z1],[u1,v0,z1]], (1,0,0)),
            ([[u0,v0,z0],[u1,v0,z0],[u1,v0,z1],[u0,v0,z1]], (0,-1,0)),
            ([[u0,v1,z0],[u1,v1,z0],[u1,v1,z1],[u0,v1,z1]], (0,1,0)),
            ([[u0,v0,z1],[u1,v0,z1],[u1,v1,z1],[u0,v1,z1]], (0,0,1)),
        ]
        for verts, normal in faces:
            n = rotate(np.array(normal, float), self.facing)-rotate(np.zeros(3), self.facing)
            if np.dot(n, [1,1,32]) <= 0:
                continue
            surface = role
            if role == 'console':
                surface = 'rear' if normal[2] or normal[0] < 0 else 'front' if normal[0] else 'side'
            def labels(q, s=surface):
                nums = np.full(len(q), number) if number is not None else (q[:,1] >= .5).astype(int)
                return np.array([f'{s}-{int(k):02d}' for k in nums], object)
            def keys(name):
                k = int(name.split('-')[-1])
                if surface == 'ground':
                    return depth_anchor((u0+u1)/2, (v0+v1)/2, self.facing)
                if surface == 'monitor' and k == 0:
                    return depth_anchor(.09375, .5, self.facing)
                if surface == 'monitor':
                    return depth_anchor(.09375, 0 if k == 1 else 1, self.facing)
                ku = .375 if surface == 'front' else -.25 if surface == 'rear' else (u0+u1)/2
                kv = (v0+v1)/2 if surface == 'side' else (0 if k == 0 else 1)
                return depth_anchor(ku, kv, self.facing)
            def contacts(name):
                k = int(name.split('-')[-1])
                if surface == 'ground':
                    return [(u0+u1)/2, (v0+v1)/2, 0]
                if surface == 'monitor' and k == 0:
                    return [.1875, .5, 16]
                if surface == 'monitor':
                    return [.125, 0 if k == 1 else 1, 18]
                return [u1, 0 if k == 0 else 1, z0]
            def colors(q, norm=n, mat=material, nn=normal):
                if mat == 'wood':
                    base = '#C78251' if norm[2] else '#B96537' if norm[1] > 0 else '#804A2F'
                    cols = np.tile(rgba(base), (len(q),1))
                    # Sparse broad grain on the top, parallel to the model axis.
                    if norm[2]:
                        stripe = np.abs(q[:,0]-.125) < .025
                        cols[stripe] = rgba('#B96537')
                        highlight = q[:,0] < u0+.04
                        cols[highlight] = rgba('#ECA662')
                    elif norm[1] > 0:
                        cols[q[:,2] > z1-.8] = rgba('#C78251')
                    return cols
                if mat == 'metal':
                    base = '#808985' if norm[2] else '#565958' if norm[1] > 0 else '#262D38'
                    cols = np.tile(rgba(base), (len(q),1))
                    if norm[2]:
                        cols[q[:,0] < u0+.05] = rgba('#89918F')
                    return cols
                if mat == 'panel':
                    base = '#262D38' if norm[2] else '#191E1E'
                    cols = np.tile(rgba(base), (len(q),1))
                    if nn[0] < 0:
                        cols[(q[:,2] > 22) & (q[:,2] < 23) & (q[:,1] > .1875) & (q[:,1] < .8125)] = rgba('#262D38')
                    return cols
                base = '#262D38' if norm[2] or norm[1] > 0 else '#191E1E'
                return np.tile(rgba(base), (len(q),1))
            self.face(verts, colors, labels, keys, contacts)


def old_textures():
    atlas = np.array(Image.open(SRC / 'tv-cabinet-screen.png').convert('RGBA'))
    textures = {}
    for n, facing in enumerate(FACINGS):
        mask = np.array(Image.open(SRC / f'tv-cabinet-screen-mask-{facing}.png'))[:,:,3] > 0
        yy, xx = np.where(mask)
        if facing in ('se','sw'):
            cols = range(int(xx.min()), int(xx.max())+1)
            textures[facing] = []
            for frame in range(4):
                cell = atlas[n*128:(n+1)*128,frame*128:(frame+1)*128]
                # Un-shear the old exact 2:1 mask into its native 32x15 texels.
                rect = np.zeros((15,32,4),np.uint8)
                for k,x in enumerate(cols):
                    ys = np.where(mask[:,x])[0]
                    rect[:,k] = cell[ys[0]:ys[0]+15,x]
                textures[facing].append(snap_palette(rect))
        else:
            textures[facing] = []
            for frame in range(4):
                cell = atlas[n*128:(n+1)*128,frame*128:(frame+1)*128]
                colors = cell[mask]
                textures[facing].append(colors[len(colors)//2])
    return textures


def model(facing):
    r = Raster(facing)
    # Four recessed feet make the z=0 contacts explicit.
    k = 0
    for u in (-.1875,.25):
        for v in (-.3125,1.25):
            r.box((u,u+.0625,v,v+.125,0,3),'dark','ground',k)
            k += 1
    # 56px-wide console: one open shelf and a flush, handle-free door.
    r.box((-.25,.375,-.375,1.375,3,5),'wood','console')
    r.box((-.25,-.1875,-.375,1.375,5,14),'wood','console')
    for va,vb in ((-.375,-.3125),(.4375,.5),(1.3125,1.375)):
        r.box((-.25,.375,va,vb,5,14),'wood','console')
    r.box((-.1875,.375,.5,1.3125,5,14),'wood','console')
    r.box((.25,.3125,-.3125,.4375,5,14),'dark','console')
    r.box((-.1875,.25,-.3125,.4375,8,9),'wood','console')
    r.box((.3125,.375,-.3125,.4375,8,9),'wood','console')
    r.box((-.25,.375,-.375,1.375,14,16),'wood','console')
    # Flat metallic base and short upright; the panel starts at z18.
    r.box((-.0625,.3125,.1875,.8125,16,17),'metal','monitor',0)
    r.box((.0625,.1875,.4375,.5625,17,18),'metal','monitor',0)
    # 1/16 tile = two horizontal pixels and one rise pixel; no CRT volume.
    for va,vb,num in ((-.375,.5,1),(.5,1.375,2)):
        r.box((.0625,.125,va,vb,18,TV_TOP),'panel','monitor',num)
    # A short lower rear electronics plate, never a deep box.
    for va,vb,num in ((.0625,.5,1),(.5,.9375,2)):
        r.box((-.0625,.0625,va,vb,18,24),'panel','monitor',num)
    if facing in ('se','sw'):
        # One-pixel vertical rim/chin. Corners snap once, all slopes remain 2:1.
        verts = [[.125,-.34375,19],[.125,1.34375,19],[.125,1.34375,TV_TOP-1],[.125,-.34375,TV_TOP-1]]
        contact = [.125,.5,19]
    else:
        verts = [[-.0625,.46875,20],[ -.0625,.53125,20],[-.0625,.53125,21],[-.0625,.46875,21]]
        contact = [-.0625,.5,20]
    r.face(verts, '#186D7A', 'screen-00', depth_anchor(.125 if facing in ('se','sw') else -.0625,.5,facing),contact)
    return r


def physical_check(r, mask, label):
    yy,xx = np.where(mask)
    target = np.array(r.contacts[label])
    # The check is the closest visible assembly contact surface, never a glyph.
    costs = ((r.world[yy,xx]-target)*[32,32,1])**2
    k = np.argmin(costs.sum(axis=1))
    return [{'x': int(xx[k]), 'y': int(yy[k]), 'radius': 0}]


def clip(name, direction, row, part_id, depth, check, frames=1, state=None, column=0, rank=0):
    c = {'name':name,'direction':direction,'row':row,'column':column,'frames':frames,
         'fps':3 if frames == 4 else 1,'loop':True,'partId':part_id,
         'anchor':point(*ANCHORS[direction]),'depthAnchor':depth,'isoZOrder':rank,
         'anchorChecks':check,'contactKind':'assembly-surface'}
    if state is not None:
        c['state'] = state
    return c


def sheet(ident, file, kind, rows):
    return {'id':ident,'kind':kind,'file':file,'cellW':128,'cellH':128,
            'anchor':point(76,72),'scale':1,'projection':'iso2to1','rows':rows}


def font(size):
    try:
        return ImageFont.truetype('C:/Windows/Fonts/consola.ttf',size)
    except OSError:
        return ImageFont.load_default()


def proof_images(masters, screen_frames, glows):
    bg = '#122031'
    contact = Image.new('RGBA',(1536,470),bg)
    draw = ImageDraw.Draw(contact)
    for n,f in enumerate(FACINGS):
        draw.text((n*384+16,12),f'{f.upper()}  native 128px / 3x',font=font(18),fill='#F2EBD3')
        im = Image.fromarray(masters[f]).resize((384,384),Image.Resampling.NEAREST)
        contact.alpha_composite(im,(n*384,50))
    contact.save(OUT/'tv-cabinet-four-views-3x.png')
    comparison = Image.new('RGBA',(1160,1980),bg)
    d = ImageDraw.Draw(comparison)
    d.text((20,12),'TV V3: thin OLED-style panel / native + exact nearest 3x',font=font(22),fill='#F2EBD3')
    hero = Image.open(ROOT/'assets/art/iso-concept/iso-office-hero.png').convert('RGBA')
    integrated = Image.open('C:/Codex-Workspace/vd-lanes/isowire/artifacts/iso-integration-final/iso-demo-24.0s-2x.png').convert('RGBA')
    comparison.alpha_composite(hero.crop((365,315,550,495)).resize((259,252),Image.Resampling.NEAREST),(20,58))
    comparison.alpha_composite(integrated.crop((75,350,280,545)).resize((287,273),Image.Resampling.NEAREST),(310,58))
    d.text((20,35),'HERO TV / warm wood + teal',font=font(14),fill='#D0B999')
    d.text((310,35),'RECENT INTEGRATED TV / v2',font=font(14),fill='#D0B999')
    d.text((630,62),'Geometry / one S0 exception:',font=font(20),fill='#F2EBD3')
    for n,line in enumerate(('cell 128 x 128','anchor 76/52,72','footprint 1 x 2 / 2 x 1','console z16 / panel z48*','panel depth: 2 x 1 pixels','light fixed upper-left','*sole S0 height exception')):
        d.text((630,95+26*n),line,font=font(17),fill='#89918F')
    for n,f in enumerate(FACINGS):
        y = 348+n*404
        d.text((20,y),f'{f.upper()}      V2 1x     V3 1x                    V2 3x                      V3 3x',font=font(16),fill='#F2EBD3')
        old = Image.open(SRC/f'tv-cabinet-empty-{f}.png').convert('RGBA')
        new = Image.fromarray(masters[f])
        comparison.alpha_composite(old,(20,y+82))
        comparison.alpha_composite(new,(156,y+82))
        comparison.alpha_composite(old.resize((384,384),Image.Resampling.NEAREST),(300,y+20))
        comparison.alpha_composite(new.resize((384,384),Image.Resampling.NEAREST),(700,y+20))
    comparison.save(OUT/'before-after.png')
    states = Image.new('RGBA',(1536,820),bg)
    sd = ImageDraw.Draw(states)
    for n,f in enumerate(('se','sw')):
        for k in range(4):
            sd.text((k*384+16,n*410+8),f'{f.upper()}  legacy phase {k} / 3 fps',font=font(17),fill='#F2EBD3')
            full = masters[f].copy()
            m = screen_frames[f][k][:,:,3]>0
            full[m] = screen_frames[f][k][m]
            states.alpha_composite(Image.fromarray(full).resize((384,384),Image.Resampling.NEAREST),(k*384,n*410+25))
    states.save(OUT/'tv-cabinet-screen-states-3x.png')
    layers = Image.new('RGBA',(1536,470),bg)
    ld = ImageDraw.Draw(layers)
    for n,f in enumerate(FACINGS):
        ld.text((n*384+16,8),f'{f.upper()} GLASS / SEPARATE GLOW',font=font(15),fill='#F2EBD3')
        layers.alpha_composite(Image.fromarray(screen_frames[f][0]).resize((384,384),Image.Resampling.NEAREST),(n*384,35))
        # Bottom 1x samples demonstrate the glow stays separate from static art.
        layers.alpha_composite(Image.fromarray(glows[f][0]),(n*384+128,310))
    layers.save(OUT/'tv-cabinet-overlays-proof.png')


def build():
    old = json.loads((SRC/'tv-cabinet.iso.json').read_text(encoding='utf-8-sig'))
    hashes = {str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in SRC.glob('tv-cabinet*') if p.is_file()}
    textures = old_textures()
    rows, cells, screen_rows, glow_rows = [], [], [], []
    screen_atlas = np.zeros((32*128,4*128,4),np.uint8)
    glow_atlas = np.zeros((4*128,4*128,4),np.uint8)
    masters, all_frames, glows, audits = {}, {}, {}, {}
    variants = copy.deepcopy(old['furniture'][0]['variants'])
    for n,f in enumerate(FACINGS):
        r = model(f)
        screen_mask = r.labels == 'screen-00'
        static = r.arr.copy()
        static[screen_mask] = 0
        frames = []
        for k in range(4):
            frame = np.zeros((128,128,4),np.uint8)
            yy,xx = np.where(screen_mask)
            if f in ('se','sw'):
                q = r.world[yy,xx]
                # Use screen x in both outward front facings to retain play direction.
                u = (q[:,1]+.34375)/1.6875
                u = 1-u
                v = (TV_TOP-1-q[:,2])/(TV_TOP-20)
                tx = np.clip(np.floor(u*32).astype(int),0,31)
                ty = np.clip(np.floor(v*15).astype(int),0,14)
                frame[yy,xx] = textures[f][k][ty,tx]
                # Repaint only the play glyph, preserving the four legacy field
                # phases. A small flat screen UI mark remains readable at 1x.
                cream = np.isin(frame[:,:,0], [208,231,242]) & screen_mask
                frame[cream] = rgba('#186D7A')
                icon = Image.new('RGBA',(128,128))
                idraw = ImageDraw.Draw(icon)
                cx,cy = project([.125,.5,(19+TV_TOP-1)/2],f).astype(int)
                idraw.polygon([(cx-3,cy-4),(cx-3,cy+4),(cx+3,cy)],fill=rgba('#E7D1AC'))
                ia = np.array(icon)
                mark = (ia[:,:,3]>0) & screen_mask
                frame[mark] = ia[mark]
            else:
                frame[yy,xx] = textures[f][k]
            frame[screen_mask,3] = 255
            frames.append(snap_palette(frame))
        all_frames[f] = frames
        master = static.copy()
        master[screen_mask] = frames[0][screen_mask]
        masters[f] = master
        save(f'tv-cabinet-empty-{f}.png',master)
        mask = np.zeros((128,128,4),np.uint8)
        mask[screen_mask] = rgba('#F2EBD3')
        save(f'tv-cabinet-screen-mask-{f}.png',mask)
        variant = variants[n]
        variant['parts'] = []
        sums = np.zeros((128,128),np.uint8)
        recon = np.zeros((128,128,4),np.uint8)
        checks = {}
        for label in sorted(set(r.labels.flatten())-{'','screen-00'}):
            m = r.labels == label
            cell = np.zeros((128,128,4),np.uint8)
            cell[m] = static[m]
            sums[m] += 1
            recon[m] = cell[m]
            role,num = label.split('-')
            part_id = f'tv-cabinet__{role}__{f}__{num}'
            c = clip(label,f,len(cells)//4,part_id,r.keys[label],physical_check(r,m,label),column=len(cells)%4)
            rows.append(c)
            cells.append(cell)
            checks[label] = c['anchorChecks']
            variant['parts'].append({'sheetId':'iso-tv-cabinet-v3-parts','animation':label,'direction':f,
                                     'partId':part_id,'depthAnchor':c['depthAnchor'],'isoZOrder':0})
        sums[screen_mask] += 1
        recon[screen_mask] = frames[0][screen_mask]
        assert np.array_equal(recon,master)
        assert np.all(sums[master[:,:,3]>0] == 1) and np.all(sums[master[:,:,3]==0] == 0)
        screen_check = physical_check(r,screen_mask,'screen-00')
        state_data = [('default',frames),('idle',[frames[0]]),('playing',frames),('static',[frames[0]])]
        state_data += [(f'frame-{k:02d}',[frames[k]]) for k in range(4)]
        for state,fs in state_data:
            row = len(screen_rows)
            c = clip('screen-'+state,f,row,f'tv-cabinet__screen__{f}__00',r.keys['screen-00'],screen_check,
                     frames=len(fs),state=state,rank=1)
            c['contactKind'] = 'glass-panel-bottom-rim' if f in ('se','sw') else 'rear-status-LED-surface'
            screen_rows.append(c)
            for k,im in enumerate(fs):
                screen_atlas[row*128:(row+1)*128,k*128:(k+1)*128] = im
        fg = []
        for k in range(4):
            g = np.zeros((128,128,4),np.uint8)
            if f in ('se','sw'):
                q = r.world
                m = screen_mask & (q[:,:,2] < 20.5)
                yy,xx = np.where(m)
                # Binary alpha; runtime opacity controls subdued screen spill.
                g[yy,xx] = rgba('#338F88')
                g[yy[::2],xx[::2]] = rgba('#5DB1A5' if k in (1,2) else '#3C9F96')
            else:
                g[screen_mask] = rgba('#3C9F96')
            fg.append(g)
            glow_atlas[n*128:(n+1)*128,k*128:(k+1)*128] = g
        glows[f] = fg
        glow_check = physical_check(r,fg[0][:,:,3]>0,'screen-00')
        glow_rows.append(clip('glow-default',f,n,f'tv-cabinet__edge__{f}__00',r.keys['screen-00'],glow_check,
                              frames=4,state='default',rank=2))
        variant['screen'] = {'sheetId':'iso-tv-cabinet-v3-screen','animation':'screen-default','direction':f}
        variant['screenStates'] = {state:'screen-'+state for state,_ in state_data}
        variant['stateAliases'] = copy.deepcopy(variant.get('stateAliases',{}))
        variant['glow'] = {'sheetId':'iso-tv-cabinet-v3-glow','animation':'glow-default','direction':f,
                           'opacity':.18,'isoZOrder':2}
        variant['glowStates'] = {'default':'glow-default','idle':{'animation':'glow-default','frame':0},
                                 'playing':'glow-default','static':{'animation':'glow-default','frame':0}}
        variant['glowStates'].update({f'frame-{k:02d}':{'animation':'glow-default','frame':k} for k in range(4)})
        variant['shadow']['sheetId'] = 'iso-tv-cabinet-v3-shadow'
        variant['emptyReference'] = f'tv-cabinet-empty-{f}.png'
        variant['screenMask'] = f'tv-cabinet-screen-mask-{f}.png'
        variant['tvTop'] = TV_TOP
        audits[f] = {'staticParts':len(variant['parts']),'staticOverlapPixels':int((sums>1).sum()),
                     'reconstructionMismatchPixels':int(np.any(recon!=master,axis=2).sum()),
                     'screenMaskPixels':int(screen_mask.sum()),'screenMaskChangesAcrossFrames':0,
                     'anchorChecks':checks,'allModelEdgesLegal':True,'projectedEdges':r.edges}
    parts = np.zeros((math.ceil(len(cells)/4)*128,512,4),np.uint8)
    for k,cell in enumerate(cells):
        parts[k//4*128:(k//4+1)*128,k%4*128:(k%4+1)*128] = cell
    save('tv-cabinet-parts.png',parts)
    save('tv-cabinet-screen.png',screen_atlas)
    save('tv-cabinet-glow.png',glow_atlas)
    manifest = {'version':1,'projection':'iso2to1','requiredCapabilities':['iso2to1','renderItemsV1'],
                'sheets':[sheet('iso-tv-cabinet-v3-parts','tv-cabinet-parts.png','furniture',rows),
                          sheet('iso-tv-cabinet-v3-screen','tv-cabinet-screen.png','furniture',screen_rows),
                          sheet('iso-tv-cabinet-v3-glow','tv-cabinet-glow.png','furniture',glow_rows)],
                'furniture':[{'id':'iso/tv-cabinet','kind':'tv','variants':variants}],
                'provenance':{'design':'generic modern large OLED-style TV; no logos or trademarks',
                              'method':'native authored axis-aligned model, deterministic pixel-center raster; no generated master required',
                              'screenSource':'v2 default 4 phases, 3fps, reprojected via native unsheared texture and palette snap; play glyph repainted for 1x readability',
                              'light':'upper-left, re-evaluated for each facing',
                              'S0Changes':[{'field':'tvTop','from':38,'to':TV_TOP,'document':'CHANGES-TV-v3.md'}]}}
    write_json('tv-cabinet.iso.json',manifest)
    # Shadows use the real console ground envelope and exact (6,3) cast offset.
    shadow_atlas = np.zeros((512,128,4),np.uint8)
    shadow_rows = []
    for n,f in enumerate(FACINGS):
        im = Image.new('RGBA',(128,128))
        dr = ImageDraw.Draw(im)
        poly = project([[-.25,-.375,0],[.375,-.375,0],[.375,1.375,0],[-.25,1.375,0]],f)
        dr.polygon([tuple(v+[6,3]) for v in poly],fill=rgba('#111924',48))
        dr.polygon([tuple(v) for v in poly],fill=rgba('#111924',96))
        arr = np.array(im)
        arr[arr[:,:,3]==0] = 0
        shadow_atlas[n*128:(n+1)*128] = arr
        shadow_rows.append(clip('shadow',f,n,f'tv-cabinet__shadow__{f}__00',{'i':0,'j':0},
                                [dict(point(*ANCHORS[f]),radius=0)]))
        shadow_rows[-1]['renderPass'] = 'shadows'
    save('tv-cabinet-shadow.png',shadow_atlas)
    write_json('tv-cabinet-shadow.iso.json',{'version':1,'projection':'iso2to1',
                 'sheets':[sheet('iso-tv-cabinet-v3-shadow','tv-cabinet-shadow.png','shadow',shadow_rows)]})
    write_json('geometry-reconstruction-measurements.json',audits)
    write_json('source-tv-v2-sha256.json',hashes)
    proof_images(masters,all_frames,glows)
    for file,expected in hashes.items():
        assert hashlib.sha256((ROOT/file).read_bytes()).hexdigest() == expected, ('source changed',file)
    print('Built 4 native masters, 32 screen clips, glow, shadow, disjoint reconstruction and reference comparisons.')
    print('Write root:',OUT)


def validate_disk():
    """Re-read the delivered files, independently of the model's working arrays."""
    m = json.loads((OUT/'tv-cabinet.iso.json').read_text(encoding='utf-8'))
    old = json.loads((SRC/'tv-cabinet.iso.json').read_text(encoding='utf-8-sig'))
    sheets = {s['id']:s for s in m['sheets']}
    arrays = {s['id']:np.array(Image.open(OUT/s['file'])) for s in m['sheets']}
    clips = {(s['id'],c['name'],c['direction']):c for s in m['sheets'] for c in s['rows']}
    def get(ident,name,direction,phase=0):
        c = clips[(ident,name,direction)]
        x = (c.get('column',0)+phase)*128
        y = c['row']*128
        return arrays[ident][y:y+128,x:x+128]
    measures = {'TVTop':{'v2':38,'v3':48},'modelAttemptsPerFacing':2,'facings':{}}
    for n,v in enumerate(m['furniture'][0]['variants']):
        f = v['orientation']
        for k in ('rotation','orientation','footprintW','footprintH','blockedCells','height','points','layer'):
            assert v[k] == old['furniture'][0]['variants'][n][k], ('frozen metadata changed',f,k)
        assert v['tvTop'] == 48 and v['height'] == 16
        static = np.zeros((128,128,4),np.uint8)
        count = np.zeros((128,128),np.uint8)
        queue = sorted(v['parts'],key=lambda p:(p['depthAnchor']['i']+p['depthAnchor']['j'],p['isoZOrder'],p['partId']))
        for p in queue:
            c = get(p['sheetId'],p['animation'],p['direction'])
            visible = c[:,:,3]>0
            count[visible] += 1
            static[visible] = c[visible]
            match = clips[(p['sheetId'],p['animation'],p['direction'])]
            assert match['partId'] == p['partId'] and match['depthAnchor'] == p['depthAnchor']
            assert match['isoZOrder'] == p['isoZOrder']
        mask = np.array(Image.open(OUT/v['screenMask']))[:,:,3]>0
        assert not np.any(count[mask]) and np.max(count) == 1
        default_hashes = []
        for state,name in v['screenStates'].items():
            c = clips[('iso-tv-cabinet-v3-screen',name,f)]
            for k in range(c['frames']):
                screen = get('iso-tv-cabinet-v3-screen',name,f,k)
                assert np.array_equal(screen[:,:,3]>0,mask)
                rebuilt = static.copy()
                rebuilt[mask] = screen[mask]
                if state == 'default':
                    default_hashes.append(hashlib.sha256(screen.tobytes()).hexdigest())
                    if k == 0:
                        assert np.array_equal(rebuilt,np.array(Image.open(OUT/v['emptyReference'])))
                target = {'idle':0,'static':0,'playing':k}.get(state)
                if state.startswith('frame-'):
                    target = int(state.rsplit('-',1)[1])
                if target is not None:
                    assert np.array_equal(screen,get('iso-tv-cabinet-v3-screen','screen-default',f,target))
        glow_alpha = None
        for k in range(4):
            g = get('iso-tv-cabinet-v3-glow','glow-default',f,k)
            assert not np.any((g[:,:,3]>0) & ~mask)
            if glow_alpha is not None:
                assert np.array_equal(g[:,:,3],glow_alpha)
            glow_alpha = g[:,:,3]
        old_mask = np.array(Image.open(SRC/f'tv-cabinet-screen-mask-{f}.png'))[:,:,3]>0
        b = Image.open(OUT/v['emptyReference']).getbbox()
        measures['facings'][f] = {'staticParts':len(v['parts']),'staticDuplicateTexels':int((count>1).sum()),
                                 'emptyRGBAReconstructionMismatch':0,'screenFramesMaskMismatch':0,
                                 'screenV2MaskTexels':int(old_mask.sum()),'screenV3MaskTexels':int(mask.sum()),
                                 'defaultFrameSHA256':default_hashes,'distinctDefaultFrames':len(set(default_hashes)),
                                 'glowMaskTexels':int((glow_alpha>0).sum()),'emptyBBoxExclusive':list(b),
                                 'sourceAnchor':ANCHORS[f]}
    for arr in arrays.values():
        assert np.isin(arr[:,:,3],[0,255]).all()
        assert not np.any(arr[arr[:,:,3]==0])
        used = np.unique(arr[arr[:,:,3]>0,:3],axis=0)
        assert all(any(np.array_equal(c,p) for p in PALETTE) for c in used)
    sh = json.loads((OUT/'tv-cabinet-shadow.iso.json').read_text())
    sha = np.array(Image.open(OUT/sh['sheets'][0]['file']))
    assert set(np.unique(sha[:,:,3])) == {0,48,96}
    assert not np.any(sha[sha[:,:,3]==0])
    saved = json.loads((OUT/'geometry-reconstruction-measurements.json').read_text())
    for f in FACINGS:
        for e in saved[f]['projectedEdges']:
            dx,dy = np.array(e['b'])-np.array(e['a'])
            assert dx == 0 or abs(dx) == 2*abs(dy)
        measures['facings'][f]['checkedProjectedModelEdges'] = len(saved[f]['projectedEdges'])
    hashes = json.loads((OUT/'source-tv-v2-sha256.json').read_text())
    for p,h in hashes.items():
        assert hashlib.sha256((ROOT/p).read_bytes()).hexdigest() == h
    measures['sourceV2FilesHashVerified'] = len(hashes)
    changes = (OUT/'CHANGES-TV-v3.md').read_text(encoding='utf-8')
    s0 = (ROOT/'docs/iso-slice-s0.md').read_text(encoding='utf-8')
    convention = (ROOT/'assets/art/iso/furniture/furniture-part-convention.md').read_text(encoding='utf-8')
    for line in changes.splitlines():
        if line.startswith('-|'):
            assert line[1:] in (s0 if 'A4 sofa' in line else convention)
    measures['exactS0TextDiffVerified'] = True
    measures['activePanel'] = {'horizontalSpan':54,'verticalPanelHeight':28,'maskTexels':1512,
                                'v2HorizontalSpan':32,'v2VerticalPanelHeight':15,'v2MaskTexels':480,
                                'widthIncreasePercent':68.75,'heightIncreasePercent':86.6667,
                                'rectifiedAspect':54/28,'mainPanelThicknessProjection':[2,1],
                                'lowerRearElectronicsMaxThicknessProjection':[6,3]}
    result = subprocess.run([sys.executable,'-B',str(ROOT/'tools/iso_check.py'),'--tolerance','0',str(OUT)],
                            capture_output=True,text=True,encoding='utf-8',
                            env={**os.environ,'PYTHONIOENCODING':'utf-8','PYTHONDONTWRITEBYTECODE':'1'})
    (OUT/'iso-check-tolerance-0.txt').write_text(result.stdout+result.stderr,encoding='utf-8')
    assert result.returncode == 0, result.stdout+result.stderr
    measures['isoCheck'] = {'tolerance':0,'exitCode':result.returncode,'summary':result.stdout.strip().splitlines()[-1]}
    write_json('validation-measurements.json',measures)
    print(json.dumps(measures,ensure_ascii=False,indent=2))


if __name__ == '__main__':
    if '--verify' in sys.argv:
        validate_disk()
    else:
        build()
