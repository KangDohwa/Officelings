"""Desk rig v3: only this directory is writable. Run Python -B; Pillow/numpy.
Generated master -> frozen ramps -> authored rational 2:1 planes. No chair export.
"""
from pathlib import Path
import copy, hashlib, importlib.util, json, math, shutil, sys
import numpy as np
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True
OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[4]
OLD = ROOT / 'assets/art/iso/furniture-v2/workstations'
FACINGS = ('se', 'sw', 'ne', 'nw')
spec = importlib.util.spec_from_file_location('desk_geometry_v3', ROOT/'runs/furniture-v2/geometry.py')
g = importlib.util.module_from_spec(spec); spec.loader.exec_module(g)
CI = -.21875
TOP = 40
EDGES = []

def read(p): return json.loads(p.read_text(encoding='utf-8'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def alpha(a): return a[:, :, 3] > 0
def get(atlas, c, phase=0):
    x=(c.get('column',0)+phase)*128; y=c['row']*128
    return np.array(atlas.crop((x,y,x+128,y+128)))
def put(atlas,c,a,phase=0):
    atlas.paste(Image.fromarray(a),((c.get('column',0)+phase)*128,c['row']*128))
def project(p,f):
    i,j=g.rotate(p[0],p[1],f,1,2); ax,ay=g.source_anchor('desk',f)
    return ax+32*(i-j), ay+16*(i+j)-p[2]
def poly(points,f,label):
    pts=[project(p,f) for p in points]
    for a,b in zip(pts,pts[1:]+pts[:1]):
        dx,dy=b[0]-a[0],b[1]-a[1]
        assert abs(dx)<1e-8 or abs(abs(dx)-2*abs(dy))<1e-8,(label,f,a,b)
        EDGES.append(dict(object=label,facing=f,a=a,b=b,dx=dx,dy=dy))
    return g.polygon_mask(pts)
def color(name,n): return (*g.RAMPS[name][n],255)
def paint(a,mask,name,n): a[mask]=color(name,n)
def check(a,target):
    y,x=np.where(alpha(a)); assert len(x),'empty part'
    k=np.argmin((x-target[0])**2+(y-target[1])**2)
    return [dict(x=int(x[k]),y=int(y[k]),radius=0)]
def shifted(a,dx,dy):
    im=Image.new('RGBA',(128,128)); im.alpha_composite(Image.fromarray(a),(dx,dy)); return np.array(im)
def nearest(masks):
    yy,xx=np.indices((128,128)); dist=[]
    for mask in masks:
        y,x=np.where(mask); d=np.full(mask.shape,np.inf)
        for a,b in zip(x,y): d=np.minimum(d,(xx-a)**2+(yy-b)**2)
        dist.append(d)
    return np.argmin(dist,axis=0)
def ground(f,i,j):
    ri,rj=g.rotate(i,j,f,1,2); return dict(i=ri,j=rj)
def box_model(f,bounds):
    m=g.Model('desk',f,1,2)
    for b in bounds: m.box(b,'metal','monitor')
    m.render_boxes(); return m

def keyboard(f):
    a=np.zeros((128,128,4),np.uint8)
    # User-right mouse is always negative canonical j, reprojected for each facing.
    case=[(.4375,0,19),(.6875,0,19),(.6875,.5,19),(.4375,.5,19)]
    mask=poly(case,f,'keyboard-top'); paint(a,mask,'metal',1)
    bottom=poly([(i,j,18) for i,j,z in case],f,'keyboard-bottom')
    paint(a,bottom & ~mask,'metal',0)
    for a0,b0 in zip(case,case[1:]+case[:1]):
        face=poly([a0,b0,(b0[0],b0[1],18),(a0[0],a0[1],18)],f,'keyboard-case-side')
        paint(a,face & ~mask,'metal',2 if f in ('se','ne') else 1)
    keym=np.zeros((128,128),bool)
    for row in range(4):
        for col in range(7):
            i=.453125+row*.0546875; j=.015625+col*.0703125
            cap=poly([(i,j,19),(i+.03125,j,19),(i+.03125,j+.046875,19),(i,j+.046875,19)],f,'keycap') & mask
            paint(a,cap,'cream',2 if row==0 else 3); keym |= cap
            # One upper-left native highlight per cap, with a dark separating gap.
            y,x=np.where(cap)
            if len(x):
                k=np.argmin(x+2*y); a[y[k],x[k]]=color('cream',4)
    bar=poly([(.640625,.125,19),(.6640625,.125,19),(.6640625,.34375,19),(.640625,.34375,19)],f,'spacebar') & mask
    paint(a,bar,'cream',2)
    mousecase=[(.5625,-.1875,19),(.6875,-.1875,19),(.6875,0,19),(.5625,0,19)]
    mm=poly(mousecase,f,'mouse-top'); paint(a,mm,'metal',3)
    mb=poly([(i,j,18) for i,j,z in mousecase],f,'mouse-base'); paint(a,mb & ~mm,'metal',0)
    for a0,b0 in zip(mousecase,mousecase[1:]+mousecase[:1]):
        side=poly([a0,b0,(b0[0],b0[1],18),(a0[0],a0[1],18)],f,'mouse-side')
        paint(a,side & ~mm,'metal',1)
    # Button divider/wheel follows the mouse's own ground axis.
    line=Image.new('RGBA',(128,128)); d=ImageDraw.Draw(line)
    p0,p1=project((.5625,-.09375,19),f),project((.609375,-.09375,19),f)
    d.line([p0,p1],fill=color('metal',1)); la=np.array(line); active=alpha(la)&mm; a[active]=la[active]
    wx,wy=project((.609375,-.09375,19),f); wx,wy=math.floor(wx+.5),math.floor(wy+.5)
    if mm[wy,wx]: a[wy,wx]=color('cream',4)
    # The frozen contact row touches the case rim of keyboard and mouse.
    hc=g.s0.APPROACHES[f]; v=(hc[0],hc[1]); center=project((.625,0,18),f)
    keyboard_contact=[]; mouse_contact=[]
    for x in (math.floor(center[0]+.5)-1,math.floor(center[0]+.5)+1):
        y=math.floor(center[1]+.5)
        if not a[y,x,3]: a[y,x]=color('metal',2)
        # Inverse-project this exact contact; j>=0 belongs to the keyboard lip.
        ax,ay=g.source_anchor('desk',f); ri=(x-ax)/64+(y-ay+18)/32; rj=(y-ay+18)/32-(x-ax)/64
        canonical_j={'se':rj,'sw':1-ri,'ne':ri,'nw':1-rj}[f]
        (keyboard_contact if canonical_j>=0 else mouse_contact).append([x,y])
    return a, dict(keycapPixels=int(keym.sum()),keyboardContacts=keyboard_contact,mouseContacts=mouse_contact,
                   keyboardBounds=[.4375,0,.6875,.5,18,19],mouseBounds=[.5625,-.1875,.6875,0,18,19],rightSide='canonical negative j')

def monitor(f):
    base=(-.375,-.1875,-.0625,.1875,18,19)
    neck=(-.25,-.0625,-.1875,.0625,19,26)
    panel=(-.25,-.5,-.1875,.5,22,TOP)
    # All box edges use rational native coordinates; save projected geometry proof.
    for name,b in [('monitor-base',base),('monitor-neck',neck),('monitor-panel',panel)]:
        i0,j0,i1,j1,z0,z1=b; cs=[(i0,j0),(i1,j0),(i1,j1),(i0,j1)]
        poly([(*p,z1) for p in cs],f,name)
        for a,b0 in zip(cs,cs[1:]+cs[:1]): poly([(*a,z0),(*b0,z0),(*b0,z1),(*a,z1)],f,name)
    m=box_model(f,[base,neck,panel]); bm=box_model(f,[base]); nm=box_model(f,[neck])
    a=m.pixels.copy(); active=alpha(a)
    bmask=alpha(bm.pixels)&active&(bm.depthmap>=m.depthmap-1e-8)
    nmask=alpha(nm.pixels)&active&(nm.depthmap>=m.depthmap-1e-8)&~bmask
    pmask=active & ~(bmask|nmask)
    # Dark anti-glare frame; narrow upper-left highlights retain fixed lighting.
    a[pmask]=color('metal',1)
    if f in ('se','sw'):
        fronti=-.1875
        screen=poly([(fronti,-.46875,23),(fronti,.46875,23),(fronti,.46875,39),(fronti,-.46875,39)],f,'screen-panel') & pmask
        edge=poly([(-.25,-.5,40),(-.1875,-.5,40),(-.1875,.5,40),(-.25,.5,40)],f,'monitor-upper-rim')&pmask
        paint(a,edge,'metal',4)
        # Thin panel side is not a CRT body; identify physical 2px shell part.
        side=poly([(-.25,-.5,22),(-.1875,-.5,22),(-.1875,-.5,40),(-.25,-.5,40)],f,'monitor-thin-side')&pmask
        if f=='sw': side=poly([(-.25,.5,22),(-.1875,.5,22),(-.1875,.5,40),(-.25,.5,40)],f,'monitor-thin-side')&pmask
        paint(a,side,'metal',2)
        ledpoint=project((fronti,-.4375,22),f)
        sx,sy=math.floor(ledpoint[0]+.5),math.floor(ledpoint[1]+.5)
        led=np.zeros_like(screen); led[sy,sx]=True
        led &= pmask & ~screen
        if not led.any():
            y,x=np.where(pmask & ~screen); k=np.argmin((x-sx)**2+(y-sy)**2); led[y[k],x[k]]=True
        screenmask=screen|led
        a[screenmask]=0
        rim=(side|edge)&~screenmask
        rest=pmask&~screenmask&~rim
        owners=[bmask,nmask,rest,rim]
        depths=[ground(f,CI,0),ground(f,CI,0),ground(f,fronti,0),ground(f,CI,.5 if f=='sw' else -.5)]
        # Display uv uses horizontal extent 32px and vertical 18px: native 16:9.
        panel_i=fronti
    else:
        screen=np.zeros_like(pmask)
        edge=poly([(-.25,-.5,40),(-.1875,-.5,40),(-.1875,.5,40),(-.25,.5,40)],f,'monitor-upper-rim') & pmask
        paint(a,edge,'metal',4)
        # Subtle shallow VESA plate, never an added case or tower.
        plate=poly([(-.25,-.125,27),(-.25,.125,27),(-.25,.125,30),(-.25,-.125,30)],f,'monitor-VESA-plate')&pmask
        paint(a,plate,'metal',2)
        ledpoint=project((-.25,-.4375,23),f); sx,sy=math.floor(ledpoint[0]+.5),math.floor(ledpoint[1]+.5)
        y,x=np.where(pmask); k=np.argmin((x-sx)**2+(y-sy)**2)
        led=np.zeros_like(pmask); led[y[k],x[k]]=True; screenmask=led
        a[led]=0
        owners=[bmask,nmask,pmask&~led]
        depths=[ground(f,CI,0),ground(f,CI,0),ground(f,-.25,0)]; panel_i=-.25
    assert all(o.any() for o in owners),(f,'empty monitor geometry')
    return a,owners,depths,screen,led,panel_i

def screen_frame(f,state,phase,screen,led,panel_i):
    a=np.zeros((128,128,4),np.uint8); a[screen]=color('glass',0)
    y,x=np.where(screen)
    if len(x):
        # Unproject into the vertical display plane, then map the generated master.
        ax,ay=g.source_anchor('desk',f)
        if f=='se': j=panel_i-(x+.5-ax)/32
        else: j=1-panel_i-(x+.5-ax)/32
        ri,rj=g.rotate(panel_i,0,f,1,2)
        z=ay+16*(ri+rj)-(y+.5)+(16*j if f=='se' else -16*j)
        u=np.clip((.46875-j)/.9375,0,.999999); vv=np.clip((39-z)/16,0,.999999)
        if state=='working':
            master=np.array(Image.open(OUT/'master-desk-rig-v3.png').convert('RGBA'))
            # Recorded master front glass quad: top-left, top-right, bottom-left.
            mx=543+u*(827-543); my=384+u*(252-384)+vv*(594-384)
            sample=master[np.clip(my.astype(int),0,master.shape[0]-1),np.clip(mx.astype(int),0,master.shape[1]-1),:3]
            pal=np.array(g.RAMPS['glass']+g.RAMPS['cream'][2:5],np.int16)
            # Integer RGB distance using int32; no extra colors or alpha retained.
            idx=((sample[:,None].astype(np.int32)-pal[None].astype(np.int32))**2).sum(axis=2).argmin(axis=1)
            a[y,x,:3]=pal[idx].astype(np.uint8); a[y,x,3]=255
            # Crisp dark editor header, sidebar divider, and 3fps cursor cycle.
            header=vv<.16; a[y[header],x[header]]=color('glass',1)
            split=(u>.25)&(u<.30); a[y[split],x[split]]=color('glass',2)
            cursor=(u>.34+phase*.065)&(u<.41+phase*.065)&(vv>.80)&(vv<.88)
            a[y[cursor],x[cursor]]=color('glass',4)
        elif state=='screensaver':
            field=(u>.10)&(u<.90)&(vv>.18)&(vv<.88); a[y[field],x[field]]=color('glass',1)
            emblem=(abs(u-.5)+abs(vv-.52)<.14); a[y[emblem],x[emblem]]=color('glass',4)
        else:
            # Off is a dark anti-glare panel, not a bright lit default.
            sheen=(u<.15)&(vv<.48); a[y[sheen],x[sheen]]=color('navy',1)
    a[led]=color('metal',2) if state=='off' else color('glass',4 if state=='screensaver' else 2+phase%3)
    return a

def build():
    doc=read(OLD/'desk.iso.json'); s,ss=doc['sheets']
    atlas=Image.open(OLD/s['file']).convert('RGBA'); output=Image.new('RGBA',atlas.size)
    oldscreens=Image.open(OLD/ss['file']).convert('RGBA'); screens=Image.new('RGBA',oldscreens.size)
    evidence=[]
    for f in FACINGS:
        clips=[c for c in s['rows'] if c['direction']==f]
        nonmonitor=[c for c in clips if '__monitor__' not in c['partId']]
        monitorclips=[c for c in clips if '__monitor__' in c['partId']]
        images={c['partId']:get(atlas,c) for c in clips}
        # Keep desk structure/master grain; refill every previously hidden top texel.
        original_nonmonitor=np.zeros((128,128,4),np.uint8)
        for c in nonmonitor:
            a=images[c['partId']]; original_nonmonitor[alpha(a)]=a[alpha(a)]
        save=g.monitor; g.monitor=lambda m:None; bare=g.make_model('desk',f); g.monitor=save
        top=poly([(-.43,-.43,18),(.625,-.43,18),(.625,1.22,18),(-.43,1.22,18)],f,'desk-top-retained')
        wood=np.zeros_like(original_nonmonitor); paint(wood,top,'wood',3)
        yy,xx=np.indices(top.shape); ax,ay=g.source_anchor('desk',f)
        ri=(xx+.5-ax)/64+(yy+.5-ay+18)/32; rj=(yy+.5-ay+18)/32-(xx+.5-ax)/64
        # Grain follows the actual plane and is sparse enough for keys/props to read.
        cj={'se':rj,'sw':1-ri,'ne':ri,'nw':1-rj}[f]
        grain=(abs(((cj+.15)%.3125)-.15625)<.018)&top
        paint(wood,grain,'wood',2)
        woodcolors=np.array(g.RAMPS['wood'])
        retain=top & np.any(np.all(original_nonmonitor[:,:,:3,None]==woodcolors.T[None,None],axis=2),axis=2)
        wood[retain]=original_nonmonitor[retain]
        # Assign restored top by physical ground-cell labels, preserving existing IDs.
        owner=nearest([alpha(images[c['partId']]) for c in nonmonitor])
        for li,meta in enumerate(bare.meta[1:],1):
            region=top&(bare.labels==li)
            if not region.any():continue
            cand=[c for c in nonmonitor if c.get('seatGroupId') and c['isoZOrder']==-20] if meta.get('seatGroupId') else [c for c in nonmonitor if c['depthAnchor']==meta['depthAnchor'] and c['partId'].split('__')[1]==meta['role']]
            if not cand: cand=[c for c in nonmonitor if not c.get('seatGroupId') and c['name'] not in ('rear-15','rear-16')]
            target=min(cand,key=lambda c:abs(c['depthAnchor']['i']-meta['depthAnchor']['i'])+abs(c['depthAnchor']['j']-meta['depthAnchor']['j']))
            owner[region]=nonmonitor.index(target)
        # Preserve the original visible physical per-cell strips. Geometry only
        # supplies ownership for newly exposed tabletop and removed accessories.
        for ni,c in enumerate(nonmonitor):
            if c['name'] not in ('rear-15','rear-16'):
                owner[retain & alpha(images[c['partId']])]=ni
        # Extract old mugs before cleaning top; no second copies embedded in body.
        mugclip=next(c for c in nonmonitor if c['name']=='rear-15'); mug=images[mugclip['partId']].copy()
        oldmg=mugclip['depthAnchor']; oldx,oldy=g.P(oldmg['i'],oldmg['j'],18)
        newmg=ground(f,.5625,.8125); newx,newy=g.P(newmg['i'],newmg['j'],18)
        mug=shifted(mug,round(newx-oldx),round(newy-oldy))
        for c in nonmonitor:
            a=images[c['partId']]; a[top]=0
            if c['name'] in ('rear-15','rear-16'): a[:]=0
            region=top & (owner==nonmonitor.index(c)); a[region]=wood[region]
        surface=next(c for c in nonmonitor if c.get('seatGroupId') and c['isoZOrder']==-20)
        ka,kproof=keyboard(f); km=alpha(ka)
        for c in nonmonitor: images[c['partId']][km]=0
        images[surface['partId']][km]=ka[km]
        surface['contactKind']='horizontal-desktop-keyboard-and-right-mouse'
        # Consistent legal mug/paper positions in all four authored directions.
        paperclip=next(c for c in nonmonitor if c['name']=='rear-16')
        pa=np.zeros_like(ka); pm=poly([(-.0625,.9375,18),(.1875,.9375,18),(.1875,1.1875,18),(-.0625,1.1875,18)],f,'paper')
        paint(pa,pm,'cream',3)
        line=Image.new('RGBA',(128,128)); d=ImageDraw.Draw(line)
        for j in (1.0,1.0625): d.line([project((-.015625,j,18),f),project((.125,j,18),f)],fill=color('cream',1))
        la=np.array(line); select=alpha(la)&pm; pa[select]=la[select]
        for c,prop,gd in [(mugclip,mug,newmg),(paperclip,pa,ground(f,.0625,1.0625))]:
            mask=alpha(prop)
            for bc in nonmonitor: images[bc['partId']][mask]=0
            images[c['partId']][mask]=prop[mask]; c['depthAnchor']=gd
        ma,mowners,mdepths,screen,led,pi=monitor(f); cover=alpha(ma)|screen|led
        # Distinct 3D footprints can overlap in a rear projected silhouette.
        # Remove the hidden input texels; the pilot checks determine acceptance.
        kproof['hiddenByPanelPixels']=int((cover & km).sum())
        kproof['mugPixelsHiddenByPanel']=int((cover & alpha(mug)).sum())
        assert not np.any(cover & pm),(f,'panel overlaps paper')
        for c in nonmonitor: images[c['partId']][cover]=0
        for c,mask,depth in zip(monitorclips,mowners,mdepths):
            a=np.zeros_like(ma); a[mask]=ma[mask]; images[c['partId']]=a
            c['depthAnchor']=depth
            c['contactKind']='monitor-flat-base' if c==monitorclips[0] else ('monitor-neck-join' if c==monitorclips[1] else 'monitor-panel-shell-join')
        for c in clips:
            a=images[c['partId']]; a[~alpha(a)]=0
            assert alpha(a).any(),(f,c['partId'],'empty physical part')
            # Contact checks track physical material, using prior contact where it remains.
            target=tuple(c['anchorChecks'][0][k] for k in ('x','y'))
            if c in monitorclips:
                cy,cx=np.where(alpha(a)); target=(int(np.median(cx)),int(np.max(cy)))
            if c==surface: target=project((.625,0,18),f)
            if c==mugclip: target=project((.5625,.8125,18),f)
            if c==paperclip: target=project((.0625,1.0625,18),f)
            c['anchorChecks']=check(a,target); put(output,c,a)
        mask=np.zeros_like(ma); mask[screen|led]=color('cream',4)
        Image.fromarray(mask).save(OUT/f'desk-screen-mask-{f}.png')
        for c in ss['rows']:
            if c['direction']!=f:continue
            c['depthAnchor']=ground(f,pi,0); c['contactKind']='screen-glass-and-status-led' if screen.any() else 'rear-status-led'
            for phase in range(c['frames']): put(screens,c,screen_frame(f,c['state'],phase,screen,led,pi),phase)
            c['anchorChecks']=check(mask,project((pi,0,23),f))
        for v in doc['furniture'][0]['variants']:
            if v['orientation']!=f:continue
            for p in v['parts']:
                c=next(c for c in clips if c['partId']==p['partId']); p['depthAnchor']=copy.deepcopy(c['depthAnchor'])
            v['monitorTop']=TOP
            v['monitorGroundCenter']=ground(f,CI,0)
            v['monitorFootprint']={'panel':{'canonicalBounds':[-.25,-.5,-.1875,.5],'thicknessPx':2,'widthPx':32,'heightPx':18,'nominalAspectRatio':'16:9','groundCorners':[ground(f,i,j) for i,j in [(-.25,-.5),(-.1875,-.5),(-.1875,.5),(-.25,.5)]]},'base':{'canonicalBounds':[-.375,-.1875,-.0625,.1875],'groundCorners':[ground(f,i,j) for i,j in [(-.375,-.1875),(-.0625,-.1875),(-.0625,.1875),(-.375,.1875)]]}}
            v['inputDevices']={'keyboard':dict(depthAnchor=ground(f,.5625,.25),partId=surface['partId'],canonicalBounds=kproof['keyboardBounds']),'mouse':dict(depthAnchor=ground(f,.625,-.09375),partId=surface['partId'],canonicalBounds=kproof['mouseBounds'],userSide='right')}
        evidence.append(dict(orientation=f,center=ground(f,CI,0),oldCenter=ground(f,-.125,0),panelBounds=[-.25,-.5,-.1875,.5,22,TOP],baseBounds=[-.375,-.1875,-.0625,.1875,18,19],screenPixels=int(screen.sum()),ledPixels=int(led.sum()),oldScreenPixels=int(alpha(np.array(Image.open(OLD/f'desk-screen-mask-{f}.png'))).sum()),topRestoredPixels=int((top & ~alpha(original_nonmonitor)).sum()),keyboard=kproof,mugGround=newmg,paperGround=ground(f,.0625,1.0625),monitorPartIds=[c['partId'] for c in monitorclips]))
    output.save(OUT/s['file']); screens.save(OUT/ss['file'])
    doc['provenance']['DESKrigV3']='One built-in generated master, exact rational projected 27-inch thin-panel rig; native 16:9 shell 32x18, z22..40, 2px panel depth, 1px base. Screen derived from recorded generated-master quad and snapped to S0 ramps. Four authored projections, fixed light. Frozen desk/seat/hand/source anchors and all v2 part IDs retained; no chair export.'
    doc['provenance']['limitations']='Pilot/overlap evidence uses frozen A3 rev2 character snapshots and read-only v2 chair. Static queue checks do not certify WPF runtime, DPI, navigation or live character lane changes.'
    (OUT/'desk.iso.json').write_text(json.dumps(doc,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for f in FACINGS:
        full=Image.new('RGBA',(128,128))
        for c in s['rows']:
            if c['direction']==f: full.alpha_composite(Image.fromarray(get(output,c)))
        c=next(c for c in ss['rows'] if c['direction']==f and c['state']=='off')
        full.alpha_composite(Image.fromarray(get(screens,c))); full.save(OUT/f'desk-empty-{f}.png')
    for name in ('desk-shadow.png','desk-shadow.iso.json'): shutil.copyfile(OLD/name,OUT/name)
    (OUT/'geometry-proof-desk-rig-v3.json').write_text(json.dumps(dict(edges=EDGES,objects=evidence,masterSHA256=sha(OUT/'master-desk-rig-v3.png'),masterFrontGlassQuad=[[543,384],[827,252],[543,594]],allEdges2to1OrVertical=True,generatedMasterAttempts=1,nativeGeometryAttempts=1),indent=2)+'\n',encoding='utf-8')
    print(json.dumps(evidence,indent=2))

if __name__=='__main__': build()
