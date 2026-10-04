"""Native shelf redraw and reproducible, read-only source skew audit.

All output is confined to this script's directory. Source assets are read only.
"""
from pathlib import Path
import argparse, csv, hashlib, json, math
import os, subprocess, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[4]
ISO = ROOT / 'assets/art/iso'
FACINGS = ('se', 'sw', 'ne', 'nw')
PYTHON = 'C:/Users/myori/.agents/document-toolchain/runtime/python/Scripts/python.exe'
FONT = ImageFont.truetype('C:/Windows/Fonts/consola.ttf', 15)
SMALL = ImageFont.truetype('C:/Windows/Fonts/consola.ttf', 12)
RAMPS = {
    'wood':'4C2A23 804A2F B96537 C78251 ECA662',
    'navy':'111924 122031 325571 48728F 6492A3',
    'teal':'10282C 235E5C 20746C 3C9F96 5DB1A5',
    'red':'3F1F18 61282B 9F2937 BF6554 ECA662',
    'metal':'191E1E 262D38 565958 808985 89918F',
    'cream':'605E5C 898D7B D0B999 E7D1AC F2EBD3',
    'glass':'10282C 186D7A 338F88 4BAFA3 5DB1A5',
    'leaves':'0E1E1C 1D3A39 264F4B 235E5C 537B78'}
COLORS = {k:[tuple(bytes.fromhex(c)) for c in v.split()] for k,v in RAMPS.items()}
SOURCE_HASHES = {}

def track(path):
    path=Path(path)
    SOURCE_HASHES[str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) else str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    return path

def read_manifest(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def crop_clip(path, sheet, clip, phase=0):
    w, h = sheet['cellW'], sheet['cellH']
    x = (clip.get('column', 0) + phase) * w
    y = clip['row'] * h
    return Image.open(path.parent / sheet['file']).convert('RGBA').crop((x,y,x+w,y+h))

def label(draw, xy, text, font=FONT, fill='#F2EBD3'):
    draw.text(xy, text, font=font, fill=fill)

def inspect():
    OUT.mkdir(parents=True, exist_ok=True)
    groups = {'workstations':['desk','chair'], 'lounge':['sofa','rug','tv-cabinet','side-table'],
              'utility':['plant','shelf','coffee-machine','fridge','water-cooler','floor-lamp']}
    for group, families in groups.items():
        board = Image.new('RGB', (1640, len(families)*298), '#111924')
        draw = ImageDraw.Draw(board)
        for r, family in enumerate(families):
            for c, facing in enumerate(FACINGS):
                im = Image.open(ISO/'furniture-v2'/group/f'{family}-empty-{facing}.png').convert('RGBA')
                board.paste(im.resize((im.width*2, im.height*2),Image.Resampling.NEAREST), (c*410+10,r*298+30), im.resize((im.width*2, im.height*2),Image.Resampling.NEAREST))
                label(draw,(c*410+10,r*298+8),f'{family} {facing.upper()} / 2x')
        board.save(OUT/f'source-{group}.png')
    hero = Image.open(ROOT/'assets/art/iso-concept/iso-office-hero.png').convert('RGB')
    integrated = Image.open('C:/Codex-Workspace/vd-lanes/isowire/artifacts/iso-integration-final/iso-demo-24.0s-2x.png').convert('RGB')
    board = Image.new('RGB',(1920,1360),'#111924')
    for im, y, name in [(hero,30,'HERO / whole reference'), (integrated,700,'RECENT INTEGRATED / supplied screenshot')]:
        im.thumbnail((1900,620))
        board.paste(im,(10,y))
        label(ImageDraw.Draw(board),(10,y-24),name)
    board.save(OUT/'source-references.png')
    print('Source boards written to lane output.')

def polygon_mask(points, size=(128,128)):
    """Half-open pixel-center polygon raster, no antialiasing or edge snapping."""
    yy,xx=np.indices((size[1],size[0]),dtype=float)
    xx+=.5; yy+=.5
    inside=np.zeros(xx.shape,bool)
    prev=points[-1]
    for p in points:
        x0,y0=prev; x1,y1=p
        if y0 != y1:
            inside ^= ((y0>yy)!=(y1>yy)) & (xx<(x1-x0)*(yy-y0)/(y1-y0)+x0)
        prev=p
    return inside

def paint(a,mask,color):
    a[mask]=(*color,255)

def shelf_model(facing):
    """Integer edge vertices, same S0 origin/footprint/height for all facings."""
    flip=facing in ('sw','ne')
    ground=[(30,91),(86,63),(98,69),(42,97)]
    if flip: ground=[(128-x,y) for x,y in ground]
    top=[(x,y-44) for x,y in ground]
    # Long visible face occupies screen right in SE/NW and screen left in SW/NE.
    if flip:
        x0,x1,b0,b1=30,86,69,97
        side=[(86,97),(98,91),(98,47),(86,53)]
    else:
        x0,x1,b0,b1=42,98,97,69
        side=[(30,91),(42,97),(42,53),(30,47)]
    slope=(b1-b0)/(x1-x0)
    yy,xx=np.indices((128,128),dtype=float); xx+=.5; yy+=.5
    u=xx-x0; z=b0+slope*u-yy
    long=(u>=0)&(u<56)&(z>=0)&(z<44)
    narrow=polygon_mask(side); roof=polygon_mask(top)
    a=np.zeros((128,128,4),np.uint8)
    W=COLORS['wood']; bright=flip
    paint(a,long,W[3 if bright else 2])
    paint(a,narrow,W[2 if flip else 3])
    paint(a,roof,W[4])
    # Broad board clusters rather than the v2 scattered, repeating grain.
    edge=long&((u<1)|(u>=55)|(z<1)|(z>=43))
    paint(a,edge,W[1])
    paint(a,long&(z>=42)&(z<43),W[4 if bright else 3])
    # Repaint the narrow plane in screen-space lighting; no reflected RGB.
    sx=xx-(86 if flip else 30)
    sz=(97-.5*sx if flip else 91+.5*sx)-yy
    paint(a,narrow&((sx<1)|(sx>=11)|(sz<1)),W[1])
    paint(a,narrow&(sx>=2)&(sx<3)&(sz>5)&(sz<38),W[3 if flip else 4])
    for q,z0,h in [(5,8,9),(8,26,7)]:
        paint(a,narrow&(sx>=q)&(sx<q+1)&(sz>=z0)&(sz<z0+h),W[1 if flip else 2])
    # Top bevels and two short grain clusters run only along the iso axes.
    ax=48 if flip else 80
    pi=(xx-ax)/64+(yy-72+44)/32
    pj=(yy-72+44)/32-(xx-ax)/64
    depth= pj if flip else pi
    length=pi if flip else pj
    paint(a,roof&(depth>.125),W[3])
    paint(a,roof&(depth<-.125),W[4])
    paint(a,roof&(depth>-.03)&(depth<.0)&(length>.15)&(length<.44),W[3])
    paint(a,roof&(depth>.04)&(depth<.075)&(length>.94)&(length<1.16),W[3])
    if facing in ('se','sw'):
        # Three shelves; real upright book volumes with varied widths/heights.
        for r,(base,ceiling) in enumerate([(3,15),(17,29),(31,41)]):
            opening=long&(u>=3)&(u<53)&(z>=base)&(z<ceiling)
            paint(a,opening,W[0])
            paint(a,opening&(z>=ceiling-1),W[1])
            paint(a,long&(u>=2)&(u<54)&(z>=base-1)&(z<base),W[4 if bright else 3])
            # Object-space coordinates preserve book sequence across front views.
            bu=56-u if flip else u
            specs=[
                [(4,4,9,'navy'),(8,2,10,'teal'),(12,4,8,'cream'),
                 (16,2,9,'red'),(20,4,10,'teal'),(26,4,8,'navy'),
                 (32,2,9,'red'),(36,4,10,'cream'),(40,2,8,'teal'),
                 (44,4,9,'navy'),(50,2,10,'red')],
                [(4,2,10,'cream'),(8,4,8,'red'),(12,2,9,'navy'),
                 (16,4,10,'teal'),(22,2,8,'cream'),(26,4,10,'navy'),
                 (32,4,8,'teal'),(38,2,9,'cream'),(42,4,10,'red'),
                 (48,4,8,'navy')],
                [(4,4,8,'teal'),(10,2,8,'cream'),(14,4,9,'navy'),
                 (20,4,8,'red'),(26,2,9,'cream'),(32,4,8,'navy'),
                 (38,4,9,'teal'),(44,2,8,'red'),(48,4,8,'cream')]
            ][r]
            for n,(start,width,height,material) in enumerate(specs):
                height=min(height+((r+n)%3-1),ceiling-base-1)
                book=opening&(bu>=start)&(bu<start+width)&(z<base+height)
                ramp=COLORS[material]
                paint(a,book,ramp[2 if not bright else 3])
                lit_edge=(u-(56-start-width if flip else start))<1
                paint(a,book&lit_edge,ramp[3 if not bright else 4])
                paint(a,book&(z>=base+height-1),ramp[4])
                paint(a,book&(z>=base+2)&(z<base+3),COLORS['cream'][2])
                if width==4 and n%3!=0:
                    paint(a,book&(bu>=start+1)&(bu<start+3)&(z>=base+height-4)&(z<base+height-3),ramp[1])
        # Thin frame divider: kept vertical, no decorative tilted books.
        paint(a,long&(u>=28)&(u<30)&(z>=3)&(z<41),W[2 if bright else 1])
        paint(a,long&(u>=28)&(u<29)&(z>=3)&(z<41),W[4 if bright else 3])
    else:
        # Actual rear: continuous boards, restrained joins and vertical grain.
        for q in [18,38]:
            paint(a,long&(u>=q)&(u<q+1)&(z>=2)&(z<42),W[1])
            paint(a,long&(u>=q+1)&(u<q+2)&(z>=3)&(z<41),W[3 if bright else 2])
        for q,z0,h in [(8,8,8),(12,27,8),(25,19,12),(43,6,10),(47,29,7)]:
            paint(a,long&(u>=q)&(u<q+1)&(z>=z0)&(z<z0+h),W[2 if bright else 1])
        paint(a,long&(z>=2)&(z<3),W[4 if bright else 3])
    a[a[:,:,3]==0]=0
    # Existing strip roles/depth are retained; split by longitudinal ground cell.
    cut=(56-u if not flip else u)<28
    roofcut=length<.5
    masks=[roof&roofcut,long&cut,roof&~roofcut,long&~cut,narrow]
    # Pixel-center polygon masks touch without overlap. Enforce the contract.
    counts=sum(m.astype(np.uint8) for m in masks)
    assert not (counts>1).any(), 'part surfaces overlap'
    assert np.array_equal(counts>0,a[:,:,3]>0), 'part union differs'
    edges=[]
    for name,vertices in [('top',top),('base',ground)]:
        for n in range(4):
            edges.append(dict(edge=f'{name}-{n}',p0=vertices[n],p1=vertices[(n+1)%4]))
    for n in range(4):
        edges.append(dict(edge=f'upright-{n}',p0=top[n],p1=ground[n]))
    return a,masks,ground,edges

def build_shelf():
    source=track(ISO/'furniture-v2/utility/shelf.iso.json')
    manifest=read_manifest(source)
    track(ISO/'furniture-v2/utility/runs/a5-v2/build.py')
    body=manifest['sheets'][0]; shadow=manifest['sheets'][1]
    atlas=np.zeros((2560,128,4),np.uint8)
    shadows=np.zeros((512,128,4),np.uint8)
    geometry=[]
    for n,facing in enumerate(FACINGS):
        a,masks,ground,edges=shelf_model(facing)
        Image.fromarray(a).save(OUT/f'shelf-empty-{facing}.png')
        clips=[c for c in body['rows'] if c['direction']==facing]
        for clip,mask in zip(clips,masks):
            part=np.zeros_like(a); part[mask]=a[mask]
            atlas[clip['row']*128:(clip['row']+1)*128]=part
            yy,xx=np.where(mask)
            y=int(yy.max()); xs=xx[yy==y]; x=int(xs[len(xs)//2])
            clip['anchorChecks']=[dict(x=x,y=y,radius=0,meaning='lowest texel of this physical top/frame/end surface')]
        contact=polygon_mask(ground)
        cast=polygon_mask([(x+6,y+3) for x,y in ground])
        sh=np.zeros_like(a); sh[cast]=(*COLORS['navy'][0],48); sh[contact]=(*COLORS['navy'][0],96)
        shadows[n*128:(n+1)*128]=sh
        yy,xx=np.where(contact); y=int(yy.max()); xs=xx[yy==y]
        shadow['rows'][n]['anchorChecks']=[dict(x=int(xs[len(xs)//2]),y=y,radius=0,meaning='ground footprint contact')]
        for e in edges:
            x0,y0=e['p0']; x1,y1=e['p1']; dx=x1-x0; dy=y1-y0
            assert dx==0 or abs(dx)==2*abs(dy)
            geometry.append(dict(facing=facing,**e,dx=dx,dy=dy,slope='vertical' if dx==0 else dy/dx,drift_px=0))
    manifest['provenance']={
        'provider':'native deterministic hand-pixel redraw; no new image generation required',
        'source':'assets/art/iso/furniture-v2/utility/shelf.iso.json',
        'geometry':'44px vertical rise; all structural endpoints integer; every floor-plane edge exactly 2:1',
        'modelBoundsBefore':[-.2,-.35,.2,1.35],
        'modelBoundsAfter':[-.1875,-.375,.1875,1.375],
        'derivation':'four baked pixel cells, object book sequence retained, screen-left faces separately repainted',
        'runtimeAcceptance':'private lane export; not integrated into pack'}
    Image.fromarray(atlas).save(OUT/'shelf-parts.png')
    Image.fromarray(shadows).save(OUT/'shelf-shadow.png')
    (OUT/'shelf.iso.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    (OUT/'shelf-geometry.json').write_text(json.dumps(geometry,indent=2)+'\n',encoding='utf-8')
    print('Shelf: four native cells, 20 disjoint parts, four shadow cells, 48 exact model edges.')

def shelf_review():
    board=Image.new('RGB',(1400,850),'#111924'); draw=ImageDraw.Draw(board)
    for col,facing in enumerate(FACINGS):
        for row,version in enumerate(('v2','v3')):
            path=(ISO/'furniture-v2/utility' if version=='v2' else OUT)/f'shelf-empty-{facing}.png'
            im=Image.open(path).convert('RGBA'); x=col*350+15; y=row*418+45
            label(draw,(x,y-30),f'{facing.upper()} {version} / 1x + 3x')
            board.paste(im,(x,y),im)
            big=im.resize((384,384),Image.Resampling.NEAREST).crop((50,35,325,330))
            board.paste(big,(x,y+105),big)
    board.save(OUT/'before-after-1x3x.png')
    # Comparison with hero and recent integrated render, no source edits.
    board=Image.new('RGB',(1600,800),'#111924'); draw=ImageDraw.Draw(board)
    hero=Image.open(ROOT/'assets/art/iso-concept/iso-office-hero.png').convert('RGB')
    hero=hero.crop((540,230,705,490)).resize((330,520),Image.Resampling.NEAREST)
    integrated=Image.open('C:/Codex-Workspace/vd-lanes/isowire/artifacts/iso-integration-final/iso-demo-24.0s-2x.png').convert('RGB')
    # Whole screenshot retained for context; nearest scaled only.
    integrated.thumbnail((950,660),Image.Resampling.NEAREST)
    board.paste(hero,(20,50)); board.paste(integrated,(620,70))
    label(draw,(20,20),'HERO / source crop'); label(draw,(620,20),'INTEGRATED / supplied original')
    for r,v in enumerate(('v2','v3')):
        im=Image.open((ISO/'furniture-v2/utility' if v=='v2' else OUT)/'shelf-empty-se.png').convert('RGBA')
        board.paste(im,(390,40+r*360),im)
        big=im.resize((384,384),Image.Resampling.NEAREST).crop((65,45,320,315))
        board.paste(big,(355,130+r*360),big)
        label(draw,(390,20+r*360),v+' / 1x + 3x')
    board.save(OUT/'hero-integrated-comparison.png')

def wall_alignment_review():
    """Compose the actual frozen wall modules at edge anchors, plus both shelves."""
    wallpath=ISO/'env/walls/walls.iso.json'; data=read_manifest(track(wallpath))
    s=data['sheets'][0]
    board=Image.new('RGB',(1440,800),'#111924'); draw=ImageDraw.Draw(board)
    for col,facing in enumerate(FACINGS):
        side='r' if facing in ('sw','ne') else 'l'
        clip=next(c for c in s['rows'] if c['name']=='plain-'+side)
        wall=crop_clip(wallpath,s,clip)
        for r,version in enumerate(('v2','v3')):
            scene=Image.new('RGBA',(180,128),(17,25,36,255))
            d=ImageDraw.Draw(scene)
            # Source object origin D=(104,75)/(72,75); wall lies behind shelf.
            ox,oy=(104,75) if side=='l' else (72,75)
            wallanchor=clip.get('edgeAnchor',clip['anchor'])
            for k in range(3):
                wx=ox+(-32*k if side=='l' else 32*k)
                wy=oy+16*k
                scene.alpha_composite(wall,(wx-wallanchor['x'],wy-wallanchor['y']))
            im=Image.open((ISO/'furniture-v2/utility' if version=='v2' else OUT)/f'shelf-empty-{facing}.png').convert('RGBA')
            ax=80 if side=='l' else 48
            scene.alpha_composite(im,(ox-ax,oy-72))
            # Reference lines ±0.5 offset above and below object; alignment only.
            p0,p1=((18,50),(146,-14)) if side=='l' else ((25,-9),(153,55))
            d=ImageDraw.Draw(scene); d.line((p0,p1),fill='#5DB1A5',width=1)
            x=col*360; y=35+r*390
            label(draw,(x+10,y-25),f'{facing.upper()} {version} / actual walls / 2x')
            board.paste(scene.convert('RGB').resize((360,256),Image.Resampling.NEAREST),(x,y))
            board.paste(scene.convert('RGB'),(x+90,y+260))
    board.save(OUT/'wall-alignment-before-after.png')

def fit_edge(points, axis='x', expected=None):
    """OLS through every native boundary sample, including staircase pixels.

    Diagonal drift is accumulated y error relative to dy/dx=+/-0.5.
    Upright drift is accumulated x error relative to dx/dy=0.
    Pixel staircases have +/-0.25px fit residual but nearly zero accumulated drift.
    """
    p=np.asarray(points,float)
    independent=p[:,0 if axis=='x' else 1]
    dependent=p[:,1 if axis=='x' else 0]
    m,b=np.polyfit(independent,dependent,1)
    if expected is None: expected=(.5 if m>=0 else -.5) if axis=='x' else 0.
    span=float(independent[-1]-independent[0])
    residual=float(np.max(np.abs(dependent-(m*independent+b))))
    return dict(axis=axis,slope=float(m),expected=float(expected),span_px=span,
                drift_px=float(abs(m-expected)*span),fit_residual_px=residual,
                p0=p[0].tolist(),p1=p[-1].tolist(),samples=len(p))

def simplify_trace(points,epsilon=1.05):
    p=np.asarray(points,float)
    if len(p)<3: return [0,len(p)-1]
    a=p[0]; b=p[-1]; v=b-a
    # Residual in measured dependent axis, rather than perpendicular distance.
    pred=a[1]+(p[:,0]-a[0])*(v[1]/v[0])
    delta=np.abs(p[:,1]-pred)
    k=int(np.argmax(delta))
    if delta[k]<=epsilon: return [0,len(p)-1]
    left=simplify_trace(p[:k+1],epsilon)
    right=simplify_trace(p[k:],epsilon)
    return left[:-1]+[k+t for t in right]

def measure_mask(mask,minimum=8,vertical_only=False):
    records=[]
    for name,vertical,extreme in [('top',False,'min'),('bottom',False,'max'),
                                  ('left',True,'min'),('right',True,'max')]:
        if vertical_only and not vertical: continue
        scan=mask.T if vertical else mask
        vals=[]
        for t in range(scan.shape[1]):
            ink=np.where(scan[:,t])[0]
            if len(ink): vals.append((t,int(ink[0] if extreme=='min' else ink[-1])))
        if len(vals)<minimum: continue
        # Separate discontinuous masks, e.g. isolated table legs.
        groups=[]; start=0
        for n in range(1,len(vals)):
            if vals[n][0]-vals[n-1][0]>1:
                groups.append(vals[start:n]); start=n
        groups.append(vals[start:])
        for group in groups:
            if len(group)<minimum: continue
            cuts=simplify_trace(group)
            for lo,hi in zip(cuts,cuts[1:]):
                segment=group[lo:hi+1]
                if len(segment)<minimum: continue
                points=[(y,x) if vertical else (x,y) for x,y in segment]
                rec=fit_edge(points,'y' if vertical else 'x')
                # x(y) steep boundaries are diagonal, already measured in y(x).
                if vertical and abs(rec['slope'])>.35: continue
                # Endpoint near a bend has +/-0.5 staircase bias: keep all samples.
                rec['edge']=name+'-'+str(lo)
                records.append(rec)
    return records

def color_mask(im,ramps):
    a=np.array(im)
    colors={c for ramp in ramps for c in COLORS[ramp]}
    m=np.zeros(a.shape[:2],bool)
    for color in colors: m|=(a[:,:,:3]==color).all(axis=2)
    return m&(a[:,:,3]>0)

def furniture_cells(path):
    data=read_manifest(track(path))
    sheets={s['id']:s for s in data['sheets']}
    lookup={(s['id'],c['name'],c['direction']):(s,c) for s in data['sheets'] for c in s['rows']}
    for s in data['sheets']: track(path.parent/s['file'])
    for facing in FACINGS:
        variants=[v for v in data['furniture'][0]['variants'] if v.get('orientation',v.get('id'))==facing or v.get('id')==facing]
        variant=next((v for v in variants if v.get('state') in ('default','idle','on')),variants[0])
        s=sheets[variant['parts'][0]['sheetId']]
        im=Image.new('RGBA',(s['cellW'],s['cellH']))
        for part in variant['parts']:
            sheet,clip=lookup[(part['sheetId'],part['animation'],part['direction'])]
            im.alpha_composite(crop_clip(path,sheet,clip))
        yield facing,im

def shelf_outer_measure(im,facing):
    a=np.array(im)[:,:,3]>0
    ranges={
        'se':[('top-long',30,85,'min'),('top-short',87,98,'min'),('base-short',30,44,'max'),('base-long',46,98,'max')],
        'sw':[('top-short',30,42,'min'),('top-long',45,97,'min'),('base-long',31,84,'max'),('base-short',87,97,'max')],
        'ne':[('top-short',30,42,'min'),('top-long',45,97,'min'),('base-long',31,82,'max'),('base-short',86,97,'max')],
        'nw':[('top-long',32,82,'min'),('top-short',85,97,'min'),('base-short',35,42,'max'),('base-long',45,97,'max')]}
    records=[]
    for name,lo,hi,extreme in ranges[facing]:
        pts=[]
        for x in range(lo,hi+1):
            ink=np.where(a[:,x])[0]
            if len(ink): pts.append((x,int(ink[0] if extreme=='min' else ink[-1])))
        if len(pts)>2: records.append(dict(edge=name,**fit_edge(pts)))
    for name,x in [('upright-left',int(np.where(a)[1].min())),('upright-right',int(np.where(a)[1].max()))]:
        ys=np.where(a[:,x])[0]
        lo,hi=int(ys.min()+3),int(ys.max()-3)
        pts=[(int(np.where(a[y])[0][0 if name.endswith('left') else -1]),y) for y in range(lo,hi+1)]
        if len(pts)>3: records.append(dict(edge=name,**fit_edge(pts,'y',0)))
    return records

def audit():
    images={}; rows=[]; coverage=[]
    for path in [ROOT/'docs/iso-slice-s0.md',ISO/'furniture/furniture-part-convention.md',
                 ISO/'furniture-v2/utility/runs/a5-v2/build.py',
                 ISO/'furniture-v2/utility/runs/a5-v2/shelf-master.png',
                 ISO/'furniture-v2/utility/runs/a5-v2/shelf-rear-master.png',
                 ROOT/'assets/art/iso-concept/iso-office-hero.png',
                 Path('C:/Codex-Workspace/vd-lanes/isowire/artifacts/iso-integration-final/iso-demo-24.0s-2x.png')]: track(path)
    families={'workstations':['desk','chair'],'lounge':['sofa','rug','tv-cabinet','side-table'],
              'utility':['plant','shelf','coffee-machine','fridge','water-cooler','floor-lamp']}
    def add(key,im,masks,minimum=8,note=''):
        images[key]=im
        measurements=[]
        for surface,mask,vertical_only in masks:
            for rec in measure_mask(mask,minimum,vertical_only):
                measurements.append(dict(item=key,surface=surface,**rec))
        # Same geometric boundary can occur in the alpha and material masks.
        unique={}
        for rec in measurements:
            sig=(rec['axis'],tuple(rec['p0']),tuple(rec['p1']))
            if sig not in unique: unique[sig]=rec
        measurements=list(unique.values()); rows.extend(measurements)
        coverage.append(dict(item=key,edges=len(measurements),status='MEASURED' if measurements else 'INAPPLICABLE',note=note))
    for group,names in families.items():
        for name in names:
            path=ISO/'furniture-v2'/group/f'{name}.iso.json'
            for facing,im in furniture_cells(path):
                key=f'{group}/{name}/{facing}'
                alpha=np.array(im)[:,:,3]>0
                if name=='shelf':
                    images[key]=im
                    measurements=[dict(item=key,surface='structure',**r) for r in shelf_outer_measure(im,facing)]
                    rows.extend(measurements); coverage.append(dict(item=key,edges=len(measurements),status='MEASURED',note='Explicit full top/base edges and two upright traces.'))
                    continue
                masks=[]; note=''
                if name in ('desk','side-table','tv-cabinet'):
                    masks=[('wood tabletop/cabinet',color_mask(im,['wood']),False)]
                    if name=='desk': masks.append(('monitor housing',color_mask(im,['cream','metal','glass']),False))
                    if name=='tv-cabinet': masks.append(('TV housing/screen',color_mask(im,['navy','metal','glass']),False))
                elif name=='chair':
                    masks=[('cushion/back',color_mask(im,['teal']),False)]
                    note='Caster spokes excluded; cushion/back rectilinear boundaries measured.'
                elif name=='sofa':
                    masks=[('sofa frame/cloth',color_mask(im,['red']),False)]
                    note='Cream cushion/phone excluded from projected structural edges.'
                elif name=='plant':
                    note='Leaves are organic; pot is round/tapered. No rectilinear floor-plane edges to judge.'
                elif name=='floor-lamp':
                    masks=[('straight stem',color_mask(im,['wood']),True)]
                    note='Conical shade and elliptical base excluded; upright stem measured.'
                elif name=='water-cooler':
                    masks=[('chassis',color_mask(im,['cream','metal']),False)]
                    note='Round water bottle excluded; chassis measured.'
                elif name in ('coffee-machine','fridge'):
                    masks=[('chassis',alpha,False)]
                else: masks=[('outer rectangle',alpha,False)]
                add(key,im,masks,note=note)
    for path in [ISO/'env/floors/floors.iso.json',ISO/'env/walls/walls.iso.json',ISO/'env/polish.iso.json']:
        data=read_manifest(track(path))
        for sheet in data['sheets']:
            if sheet['kind']=='shadow': continue
            track(path.parent/sheet['file'])
            for clip in sheet['rows']:
                for phase in range(clip['frames']):
                    im=crop_clip(path,sheet,clip,phase)
                    facing=clip.get('face',clip.get('axis',clip['direction']))
                    key=f"env/{sheet['id']}/{clip['name']}/{facing}/f{phase}"
                    note='Partial floor/decor overlay geometry measured separately.' if clip.get('transition') else ''
                    if clip['name'].startswith('clock'): note='Circular clock outline: curve exception, numeric diagnostics only.'
                    if clip['name'].startswith('string-lights'): note='Sagging cable and hanging lamps: intended curve/fixture shape, numeric diagnostics only.'
                    add(key,im,[('module boundary',np.array(im)[:,:,3]>0,False)],minimum=6,note=note)
    path=ISO/'props/carried-props.iso.json'; data=read_manifest(track(path))
    for sheet in data['sheets']:
        track(path.parent/sheet['file'])
        for clip in sheet['rows']:
            im=crop_clip(path,sheet,clip)
            key=f"props/{clip['name']}/{clip['direction']}"
            if 'cup' in clip['name']:
                add(key,im,[],note='Round/tapered cup, curved rim and handle: no rectilinear projected structural edge.')
            else:
                add(key,im,[('phone/paper outer plane',np.array(im)[:,:,3]>0,False)],minimum=4,
                    note='Carried plane can tilt with the frozen grip; measured deviation is a pose exception, not automatically furniture skew.')
    path=ISO/'pets/cat.iso.json'; data=read_manifest(track(path))
    for sheet in data['sheets']:
        if sheet['kind']!='pet': continue
        track(path.parent/sheet['file'])
        for clip in sheet['rows']:
            for phase in range(clip['frames']):
                key=f"pets/{clip['name']}/{clip['direction']}/f{phase}"
                add(key,crop_clip(path,sheet,clip,phase),[],note='Organic body/legs/tail. Projection is foot-anchor based, with no rectilinear floor-plane edge.')
    # V3 measured by the same native raster rule, separate from source audit.
    v3=[]
    for facing in FACINGS:
        im=Image.open(OUT/f'shelf-empty-{facing}.png').convert('RGBA')
        for rec in measure_mask(np.array(im)[:,:,3]>0,8): v3.append(dict(item=f'v3/shelf/{facing}',surface='structure',**rec))
    (OUT/'v3-raster-edge-measurements.json').write_text(json.dumps(v3,indent=2)+'\n',encoding='utf-8')
    (OUT/'skew-edge-measurements.json').write_text(json.dumps(rows,indent=2)+'\n',encoding='utf-8')
    (OUT/'skew-coverage.json').write_text(json.dumps(coverage,indent=2)+'\n',encoding='utf-8')
    columns=['item','surface','edge','axis','slope','expected','span_px','drift_px','fit_residual_px','samples','p0','p1']
    with (OUT/'skew-edge-measurements.csv').open('w',newline='',encoding='utf-8-sig') as f:
        writer=csv.DictWriter(f,fieldnames=columns); writer.writeheader(); writer.writerows(rows)
    flagged={}
    for rec in rows:
        if rec['drift_px']>1.0+1e-9:
            flagged.setdefault(rec['item'],[]).append(rec)
    def exception(key):
        return key.startswith('props/') or '/clock-' in key or '/string-lights-' in key
    structural=sorted((k for k in flagged if not exception(k)),key=lambda k:max(r['drift_px'] for r in flagged[k]),reverse=True)
    exceptions=sorted((k for k in flagged if exception(k)),key=lambda k:max(r['drift_px'] for r in flagged[k]),reverse=True)
    ranked=structural+exceptions
    pages=[]
    for page in range(math.ceil(len(ranked)/8)):
        keys=ranked[page*8:(page+1)*8]
        board=Image.new('RGB',(1600,math.ceil(len(keys)/2)*340),'#111924'); draw=ImageDraw.Draw(board)
        for n,key in enumerate(keys):
            x=(n%2)*800; y=(n//2)*340
            im=images[key]; scale=3 if im.width>=64 else 5
            visible=im.getbbox(); box=(max(0,visible[0]-3),max(0,visible[1]-3),min(im.width,visible[2]+3),min(im.height,visible[3]+3))
            cropped=im.crop(box); big=cropped.resize((cropped.width*scale,cropped.height*scale),Image.Resampling.NEAREST)
            board.paste(big,(x+12,y+75),big)
            worst=max(flagged[key],key=lambda r:r['drift_px'])
            label(draw,(x+12,y+6),f'#{page*8+n+1} '+key,SMALL)
            label(draw,(x+12,y+28),f'max accumulated drift {worst["drift_px"]:.3f}px | {len(flagged[key])} edges >1px',SMALL)
            for j,rec in enumerate(flagged[key]):
                p0=np.array(rec['p0']); p1=np.array(rec['p1']); off=np.array(box[:2])
                a=(p0-off)*scale+[x+12,y+75]; b=(p1-off)*scale+[x+12,y+75]
                draw.line((*a,*b),fill='#FF5D6C',width=2)
                # Dashed correct parallel line through the first measured sample.
                v=(p1-p0).copy()
                if rec['axis']=='x': v[1]=rec['expected']*v[0]
                else: v[0]=0
                for t in np.arange(0,1,.12):
                    aa=(p0+v*t-off)*scale+[x+12,y+75]
                    bb=(p0+v*min(t+.065,1)-off)*scale+[x+12,y+75]
                    draw.line((*aa,*bb),fill='#5DB1A5',width=1)
                textx=x+max(380,big.width+28)
                label(draw,(textx,y+80+j*28),f'{rec["edge"]}: {rec["slope"]:+.4f}, {rec["drift_px"]:.2f}px',SMALL)
        name=f'skew-annotated-{page+1:02d}.png'; board.save(OUT/name); pages.append(name)
    report=[ '# SKEW AUDIT — v2 전체 자산', '',
        '원본은 읽기만 했다. 원시 측정값·좌표는 `skew-edge-measurements.csv/json`, 전체 검사/제외 목록은 `skew-coverage.json`에 있다.', '',
        '측정: 원본 manifest의 부품을 합성한 native 1× 셀을 사용한다. 구조 소재 마스크의 top/bottom y(x), left/right x(y)를 추출하고, 꺾임을 1.05px로 분할한다. 최소 연속 표본은 가구 8px, 환경 6px, 작은 소품 4px이다. 전체 구간 최소제곱 기울기 m을 계산한다. 대각선 누적오차=|m−sign(m)·0.5|·Δx, 수직 누적오차=|dx/dy|·Δy. 누적오차 **>1px**만 아래 표에 올린다. 계단형 래스터의 작은 잔차를 실제 기울기와 구분한다.', '',
        '책장은 top/base의 전체 긴 변·짧은 변과 양쪽 수직선을 지정해 별도로 측정했다. 벽·바닥은 선언된 모든 clip/frame과 polish 모듈을 검사했다. 원형/유기체/의도적인 들고 있는 자세는 별도 적용 범위를 표시한다.', '',
        f'검사 셀/프레임 {len(coverage)}개, 측정 선분 {len(rows)}개. >1px 구조 윤곽 후보 {len(structural)}개 방향과 곡면/그립 자세 예외 {len(exceptions)}개를 구분했다. 아래 번호와 주석 PNG 번호가 같다. 빨강=실제 선, 청록 점선=정확한 평행 기준.', '',
        '| 순위 | 자산 / 방향 / frame | 최악 선분 | 기울기 (기준) | 길이 px | 누적오차 px | 해석 |',
        '|---:|---|---|---:|---:|---:|---|']
    for n,key in enumerate(ranked):
        if n==len(structural):
            report+=['','## 곡면·그립 자세 예외 — 구조 결함 수에 포함하지 않음','',
                '| 주석 번호 | 자산 / 방향 / frame | 관찰 선분 | 기울기 (기준) | 길이 px | 수치 차이 px | 해석 |',
                '|---:|---|---|---:|---:|---:|---|']
        r=max(flagged[key],key=lambda q:q['drift_px'])
        interpretation='그립 자세 예외' if key.startswith('props/') else ('의도된 곡면/처진 케이블' if exception(key) else '구조 윤곽 재검토 대상')
        report.append(f'| {n+1} | {key} | {r["surface"]} / {r["edge"]} | {r["slope"]:+.5f} ({r["expected"]:+.1f}) | {r["span_px"]:.0f} | {r["drift_px"]:.3f} | {interpretation} |')
    report+=['','## 주석 이미지','']+[f'- [{p}]({p})' for p in pages]
    report+=['','## 방향별 전체 측정 요약','',
        '| 자산 / 방향 | 측정 선분 | 최대 누적오차 px | 판정 / 적용 범위 |','|---|---:|---:|---|']
    for cell in coverage:
        key=cell['item']; selected=[r for r in rows if r['item']==key]
        maximum=max((r['drift_px'] for r in selected),default=None)
        result=('곡면/자세 예외' if exception(key) else ('>1px 후보' if maximum is not None and maximum>1+1e-9 else ('측정 구간 ≤1px' if selected else '직선 기준 적용 불가')))
        report.append(f'| {key} | {len(selected)} | {maximum:.3f}'+' | '+result+' |' if maximum is not None else f'| {key} | 0 | — | {result} |')
    report+=['','## 적용 범위와 한계','',
        '- 식물 잎·원형 화분, 램프의 원뿔 갓/타원형 바닥, 컵의 원형 림/손잡이, 고양이 36프레임은 직선 2:1 기준이 적용되지 않는다. 제외 기록을 누락 없이 coverage에 남겼다.',
        '- 램프는 수직 stem, 정수기는 병을 제외한 chassis, 소파는 구조 frame/cloth, 의자는 cushion/back, 책상과 TV장은 가구와 화면 housing을 각각 측정했다.',
        '- 그림의 소재 경계가 부품을 가리는 곳은 짧은 선분만 남는다. 최소 길이 미만의 1~3px 디테일과 완전히 가려진 구조선은 이 감사에서 측정할 수 없다.',
        '- 앵커의 평행 이동은 선의 기울기를 바꾸지 않는다. 본 감사는 기울기 감사이며 런타임 좌표·벽 부착 오프셋·사람과의 가림을 승인하지 않는다.',
        '- 측정 선분은 보이는 래스터 경계다. 곡면·베벨·소재 무늬의 의도 여부를 자동으로 판별할 수 없으므로 수치 위반을 모두 제공하고, 기능 결함이라고 단정하지 않는다. 다른 lane 자산은 수정하지 않았다.', '']
    (OUT/'SKEW-AUDIT.md').write_text('\n'.join(report),encoding='utf-8')
    (OUT/'source-sha256.json').write_text(json.dumps(SOURCE_HASHES,indent=2)+'\n',encoding='utf-8')
    print(f'Audit: {len(coverage)} cells/frames, {len(rows)} measured edges, {len(structural)} structural candidates + {len(exceptions)} curve/pose exceptions; {len(pages)} annotated sheets.')
    for facing in FACINGS:
        r=[r for r in rows if r['item']==f'utility/shelf/{facing}']
        print(facing,[(v['edge'],round(v['slope'],5),round(v['drift_px'],3)) for v in r])
    print('V3 raster max drift',max(r['drift_px'] for r in v3))

def verify_and_notes():
    old=read_manifest(ISO/'furniture-v2/utility/shelf.iso.json')
    new=read_manifest(OUT/'shelf.iso.json')
    results=[]
    for field in ['version','projection','facings','light','furniture','requiredCapabilities']:
        assert new[field]==old[field],field+' frozen metadata changed'
    for a,b in zip(old['sheets'],new['sheets']):
        assert {k:v for k,v in a.items() if k!='rows'}=={k:v for k,v in b.items() if k!='rows'},'sheet metadata changed'
        for ac,bc in zip(a['rows'],b['rows']):
            assert {k:v for k,v in ac.items() if k!='anchorChecks'}=={k:v for k,v in bc.items() if k!='anchorChecks'},'clip anchor/depth/name changed'
    results.append('PASS: S0 source anchors, cell sizes, version/projection, facings, light, footprints, height44, blockedCells, parts/depth/isoZOrder unchanged against v2.')
    atlas=np.array(Image.open(OUT/'shelf-parts.png').convert('RGBA'))
    allowed=set(c for ramp in COLORS.values() for c in ramp)
    for facing in FACINGS:
        empty=np.array(Image.open(OUT/f'shelf-empty-{facing}.png').convert('RGBA'))
        counts=np.zeros((128,128),int); reconstructed=np.zeros_like(empty)
        for clip in new['sheets'][0]['rows']:
            if clip['direction']!=facing: continue
            part=atlas[clip['row']*128:(clip['row']+1)*128]
            m=part[:,:,3]>0; counts+=m
            reconstructed[m]=part[m]
        assert counts.max()==1 and np.array_equal(reconstructed,empty),'disjoint reconstruction '+facing
        assert set(np.unique(empty[:,:,3])).issubset({0,255}),'body alpha'
        assert not empty[empty[:,:,3]==0].any(),'transparent RGB'
        assert all(tuple(c) in allowed for c in np.unique(empty[:,:,:3][empty[:,:,3]>0],axis=0)),'ramp colors'
    results.append('PASS: four empty cells exactly reconstructed from five non-overlapping parts each; binary alpha, zero transparent RGB, exact frozen ramp colors.')
    geom=json.loads((OUT/'shelf-geometry.json').read_text())
    assert len(geom)==48
    for e in geom:
        x0,y0=e['p0']; x1,y1=e['p1']
        assert x0==x1 or abs(x1-x0)==2*abs(y1-y0),'model edge skew'
        if e['edge'].startswith('upright'): assert abs(y1-y0)==44,'height'
    raster=json.loads((OUT/'v3-raster-edge-measurements.json').read_text())
    maximum=max(r['drift_px'] for r in raster)
    assert maximum<1,'raster skew'
    results.append(f'PASS: 48 structural endpoint edges exactly slope +/-0.5 or vertical; all uprights44px; independently sampled raster max accumulated drift {maximum:.6f}px.')
    hashes=json.loads((OUT/'source-sha256.json').read_text())
    changed=[]
    for name,expected in hashes.items():
        path=Path(name); path=path if path.is_absolute() else ROOT/path
        if hashlib.sha256(path.read_bytes()).hexdigest()!=expected: changed.append(name)
    assert not changed,'read source changed during work: '+str(changed)
    results.append(f'PASS: {len(hashes)} read-only source hashes unchanged since audit snapshot (source art, manifests, frozen specs and external reference).')
    # Required validator is read-only; -B prevents __pycache__ outside our lane.
    env=dict(os.environ,PYTHONIOENCODING='utf-8')
    proc=subprocess.run([sys.executable,'-B',str(ROOT/'tools/iso_check.py'),str(OUT),'--tolerance','0'],
                        cwd=ROOT,env=env,capture_output=True,text=True,encoding='utf-8')
    (OUT/'iso-check.txt').write_text(proc.stdout+proc.stderr,encoding='utf-8')
    assert proc.returncode==0,'iso_check failed; see iso-check.txt'
    passed=proc.stdout.count('| PASS |'); skipped=proc.stdout.count('| SKIP |'); failed=proc.stdout.count('| FAIL |')
    results.append(f'PASS: tools/iso_check.py output_dir --tolerance 0: PASS {passed} / SKIP {skipped} / FAIL {failed}. SKIP: floor seams, character face-lock/blink, shadow furniture depth names are inapplicable.')
    results.append('No pack integration, app build, full dotnet test, git mutations, or changes to any other lane performed.')
    (OUT/'validation.txt').write_text('\n'.join(results)+'\n',encoding='utf-8')
    changes='''# CHANGES — bookshelf v3

**Frozen S0 changes: NONE. Exact `docs/iso-slice-s0.md` text diff is empty.**
Projection P(i,j,z)=(32(i−j),16(i+j)−z), snap floor(v+0.5), scale1, 128×128 cells, origins SE/NW(80,72) and SW/NE(48,72), 1×2 / 2×1 footprints, height44, existing ground depthAnchor/isoZOrder/partId, light(−2,−1) and shadow(+6,+3; alpha48/96) all remain unchanged. Desk A/C/H and source seated pelvis(16,44) are untouched.

```diff
--- docs/iso-slice-s0.md (before)
+++ docs/iso-slice-s0.md (after)
# No changed lines. This file was not written.
```

Only the private shelf artwork geometry was adjusted to integer 2:1 endpoints. The former model bounds in v2's private builder were not a frozen S0 constant. Their small native-grid correction is:

```diff
- shelf bounds (i0,j0,i1,j1,z0,z1) = (-0.2,-0.35,0.2,1.35,0,44)
+ shelf bounds (i0,j0,i1,j1,z0,z1) = (-0.1875,-0.375,0.1875,1.375,0,44)
```

This moves each unsnapped ideal vertex by at most 1.2px horizontally / 0.6px vertically and keeps the same occupied cells and source anchors. It corrects the malformed visible v2 silhouette rather than changing a room wall, floor grid or integration origin. Manifest differences are confined to actual physical anchorChecks and provenance; all variant metadata, part names, ordering and depth fields are retained exactly.

Four shelf views were hand-pixeled on the native grid. Books vary by row in height/width/color/order. SE/SW show the front; NE/NW show the actual back. Screen-left planes are repainted lighter in each cell, with no runtime reflection. Two native drawing passes, zero new image-generation calls: the deterministic redraw was sufficient for this rectilinear 44px asset. Original v2 and all other lanes remain the fallback unchanged.

Output files are only under `assets/art/iso/furniture-v3/utility-shelf/`. Runtime adapters/packs were not modified.
'''
    (OUT/'CHANGES-SHELF-v3.md').write_text(changes,encoding='utf-8')
    source=json.loads((OUT/'skew-edge-measurements.json').read_text())
    lines=['# 책장 기울기 측정·원인·검수','',
        '**주원인: 생성 원본을 목표 bbox에 가로/세로 비균등 축소한 v2 cleanup.** 원점 이동이나 한 번의 픽셀 반올림으로 설명되는 오차가 아니다.','',
        '`assets/art/iso/furniture-v2/utility/runs/a5-v2/build.py`의 clean_master는 alpha bbox를 가이드 bbox에 NEAREST로 resize한다. 원본 앞면 bbox는 (280,162,977,1117)=697×955, 가이드 bbox는 (30,19,99,98)=69×79다. 따라서 sx=69/697=0.099137931, sy=79/955=0.082722513, sy/sx=0.835617가 된다. 원본 긴 윗변의 OLS 기울기 −0.491878은 이 affine fit 뒤 −0.410432가 되며, 실제 SE 측정 −0.411893과 일치한다. 원본 master는 2:1에 가까웠으므로 master 자체보다 이 bbox fit이 주원인이다.','',
        'SE는 앞면 fitted master를 직접 사용하고 NE는 후면 fitted master를 사용한다. SW/NW는 canonical 3D plane sampling으로 재투영되어 긴 변이 대체로 ±0.5로 돌아간다. 반사 시 조명 보정은 이미 있었으며 주된 기울기 원인은 아니다. 기존 앵커와 모델 배치 계약은 보존했다. 어떤 고정 원점/벽 오프셋 이동도 선의 기울기를 바꿀 수 없으므로 앵커 수정으로 고치지 않았다.','',
        '| 방향 | 변 | 실제 기울기 | 기준 | 길이 px | 누적오차 px |','|---|---|---:|---:|---:|---:|']
    for facing in FACINGS:
        for r in source:
            if r['item']==f'utility/shelf/{facing}':
                lines.append(f'| {facing.upper()} | {r["edge"]} | {r["slope"]:+.6f} | {r["expected"]:+.1f} | {r["span_px"]:.0f} | {r["drift_px"]:.3f} |')
    lines+=['','v3의 SE/NW 지면 꼭짓점은 (30,91),(86,63),(98,69),(42,97), 상단은 각 y−44다. SW/NE는 x′=128−x 위치에서 새로 그렸다. 긴 변 Δx56/Δy28, 짧은 변 Δx12/Δy6, 수직 Δx0/Δy44. 네 방향 48개 구조선의 endpoint drift는 정확히 0이다. pixel-center 래스터 회귀에서는 표본 끝의 계단 위상 때문에 최대 0.026316px 차이가 남는다.','',
        '| 비교 | 파일 |','|---|---|',
        '| v2/v3 네 방향 native1× 및 정확한3× 확대 | [before-after-1x3x.png](before-after-1x3x.png) |',
        '| 실제 frozen wall 모듈과 평행 기준선 | [wall-alignment-before-after.png](wall-alignment-before-after.png) |',
        '| hero 및 제공된 최근 통합 render | [hero-integrated-comparison.png](hero-integrated-comparison.png) |','',
        '시각 검수: v2의 SE/NE 상단/밑판 선이 벽과 벌어지는 현상이 줄었고, v3의 프레임·밑판·책 상단은 서로 평행하다. SW/NE의 넓은 왼쪽 면, SE/NW의 좁은 왼쪽 면이 각각 밝다. 책 배열은 각 층마다 다른 폭/높이/색 조합을 쓴다. 새 master는 두 번 그렸고, 참조/비교/감사 보드 열람은 총 13개 이내다.','',
        '남은 약점: v3의 넓은 목재 면은 hero보다 평평하고 깨끗하며, 책 제목/표지 표현은 몇 픽셀의 띠에 제한된다. strict 2:1 때문에 둥근 모서리·기울어진 책의 자연스러움은 줄였다. 통합 이미지 비교는 원본 screenshot을 옆에 둔 것이며, 새 자산을 실제 앱에 설치한 render가 아니다. 런타임 벽 부착 간격·보행/배우 가림·pack 소비는 이번 lane에서 검증하지 않았다.','',
        '전체 감사는 [SKEW-AUDIT.md](SKEW-AUDIT.md), 정확한 표본 좌표/길이는 CSV/JSON, 구조 endpoint는 shelf-geometry.json, 재현 명령/검증은 아래와 validation.txt에 있다. 다른 가구는 수정하지 않았다.','',
        '```powershell',
        f"& '{PYTHON}' -B assets/art/iso/furniture-v3/utility-shelf/measure_redraw_shelf_v3.py --audit --verify",
        f"& '{PYTHON}' -B tools/iso_check.py assets/art/iso/furniture-v3/utility-shelf --tolerance 0",
        '```','',
        '재그리기까지 재현하려면 첫 명령에 --build를 추가한다. helper의 모든 쓰기 대상은 자신의 출력 폴더다. --inspect는 참조 보드를 다시 만든다.']
    (OUT/'MEASUREMENTS-SHELF-v3.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print('\n'.join(results))

if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--inspect',action='store_true')
    parser.add_argument('--build',action='store_true')
    parser.add_argument('--audit',action='store_true')
    parser.add_argument('--verify',action='store_true')
    args=parser.parse_args()
    if args.inspect: inspect()
    if args.build:
        build_shelf(); shelf_review(); wall_alignment_review()
    if args.audit: audit()
    if args.verify: verify_and_notes()
