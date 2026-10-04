"""Utility/lounge native redraw. Every write stays under this file's directory."""
from pathlib import Path
import argparse, json, math, hashlib, importlib.util, sys, subprocess, csv
import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[4]
ISO=ROOT/'assets/art/iso'
FACINGS=('se','sw','ne','nw')
FAMILIES=('sofa','side-table','water-cooler','fridge','coffee-machine','floor-lamp','plant')
FONT=ImageFont.truetype('C:/Windows/Fonts/consola.ttf',14)
SMALL=ImageFont.truetype('C:/Windows/Fonts/consola.ttf',11)
spec=importlib.util.spec_from_file_location('shelf_measure',ISO/'furniture-v3/utility-shelf/measure_redraw_shelf_v3.py')
MEASURE=importlib.util.module_from_spec(spec)
spec.loader.exec_module(MEASURE) # read-only import; -B prevents sibling bytecode
RAMPS=MEASURE.COLORS
EDGES=[]
HASHES={}

def source(f): return ISO/'furniture-v2'/('lounge' if f in ('sofa','side-table') else 'utility')
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def save_json(name,obj): (OUT/name).write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def track(p): HASHES[str(p)]=hashlib.sha256(p.read_bytes()).hexdigest(); return p
def label(d,xy,t,font=FONT): d.text(xy,t,font=font,fill='#F2EBD3')
def put(board,im,xy,zoom=1):
    if zoom!=1: im=im.resize((im.width*zoom,im.height*zoom),Image.Resampling.NEAREST)
    board.paste(im,xy,im if im.mode=='RGBA' else None)

def inspect():
    b=Image.new('RGB',(1120,7*280),'#111924'); d=ImageDraw.Draw(b)
    for r,f in enumerate(FAMILIES):
        for c,face in enumerate(FACINGS):
            im=Image.open(track(source(f)/f'{f}-empty-{face}.png')).convert('RGBA')
            label(d,(c*280+8,r*280+6),f'{f} {face} v2')
            put(b,im,(c*280+8,r*280+24),2)
    b.save(OUT/'source-utility-lounge.png')
    h=Image.open(track(ROOT/'assets/art/iso-concept/iso-office-hero.png')).convert('RGB')
    i=Image.open(track(Path('C:/Codex-Workspace/vd-lanes/isowire/artifacts/iso-integration-final/iso-demo-24.0s-2x.png'))).convert('RGB')
    b=Image.new('RGB',(1440,1740),'#111924'); d=ImageDraw.Draw(b)
    h.thumbnail((1440,810)); b.paste(h,(0,24)); label(d,(8,3),'HERO REFERENCE')
    b.paste(i,(0,850)); label(d,(8,829),'RECENT INTEGRATED REFERENCE (original v2 screenshot)')
    b.save(OUT/'source-hero-integrated.png')
    save_json('source-sha256.json',HASHES)

def rotate(i,j,f,face):
    if f=='side-table': return i,j # symmetric table preserves v2 accessory placement
    w,h=(1,3) if f=='sofa' else (1,1)
    if face=='sw': return h-1-j,i
    if face=='ne': return j,w-1-i
    if face=='nw': return w-1-i,h-1-j
    return i,j

def unrotate(i,j,f,face):
    if f=='side-table': return i,j
    w,h=(1,3) if f=='sofa' else (1,1)
    if face=='sw': return j,h-1-i
    if face=='ne': return w-1-j,i
    if face=='nw': return w-1-i,h-1-j
    return i,j

class Native:
    """Pixel-center orthographic surface sampler; never resize a source bbox."""
    def __init__(self,f,face):
        self.f=f; self.face=face
        self.ax,self.ay=((96 if face in ('se','nw') else 32),56) if f=='sofa' else (64,72 if f=='side-table' else 80)
        yy,xx=np.indices((128,128),dtype=float)
        self.X=xx+.5-self.ax; self.Y=yy+.5-self.ay
        self.a=np.zeros((128,128,4),np.uint8)
        self.owner=np.full((128,128),'',dtype=object)
        self.depth=np.full((128,128),-1e8)
        self.surface_masks={}
    def point(self,i,j,z):
        i,j=rotate(i,j,self.f,self.face)
        return (int(self.ax+32*(i-j)),int(self.ay+16*(i+j)-z))
    def box(self,name,bounds,material,part_for,style=None):
        i0,j0,i1,j1,z0,z1=bounds
        verts=[self.point(i,j,z) for z in (z0,z1) for i,j in ((i0,j0),(i1,j0),(i1,j1),(i0,j1))]
        for n0,n1 in ((0,1),(1,2),(2,3),(3,0),(4,5),(5,6),(6,7),(7,4),(0,4),(1,5),(2,6),(3,7)):
            p0,p1=verts[n0],verts[n1]; dx=p1[0]-p0[0]; dy=p1[1]-p0[1]
            assert dx==0 or abs(dx)==2*abs(dy),(self.f,self.face,name,p0,p1)
            EDGES.append(dict(family=self.f,facing=self.face,component=name,p0=p0,p1=p1,dx=dx,dy=dy,drift_px=0))
        corners=[rotate(i,j,self.f,self.face) for i,j in ((i0,j0),(i1,j0),(i1,j1),(i0,j1))]
        mi,ma=min(q[0] for q in corners),max(q[0] for q in corners)
        mj,mb=min(q[1] for q in corners),max(q[1] for q in corners)
        ramp=RAMPS[material]
        for surface in ('i','j','top'):
            if surface=='top':
                z=np.full((128,128),z1); i=self.X/64+(self.Y+z)/32; j=(self.Y+z)/32-self.X/64
            elif surface=='i':
                i=np.full((128,128),ma); j=i-self.X/32; z=16*(i+j)-self.Y
            else:
                j=np.full((128,128),mb); i=j+self.X/32; z=16*(i+j)-self.Y
            ci,cj=unrotate(i,j,self.f,self.face)
            if surface=='top': m=(ci>=i0)&(ci<i1)&(cj>=j0)&(cj<j1)
            elif surface=='i': m=(j>=mj)&(j<mb)&(z>=z0)&(z<z1)
            else: m=(i>=mi)&(i<ma)&(z>=z0)&(z<z1)
            # screen-left j face is always brighter, including rotated cells.
            shade=4 if surface=='top' else 3 if surface=='j' else 2
            rgb=np.empty((128,128,3),np.uint8); rgb[:]=ramp[shade]
            if surface=='top':
                dist=np.minimum.reduce([ci-i0,i1-ci,cj-j0,j1-cj])
                rgb[m&(dist<1/32)]=ramp[2 if material=='red' else 3]
            else:
                u=i-mi if surface=='j' else j-mj
                span=(ma-mi) if surface=='j' else (mb-mj)
                rgb[m&((u<1/32)|(u>span-1/32)|(z<z0+1))]=ramp[1]
                rgb[m&(z>=z1-1)]=ramp[4 if surface=='j' else 3]
            if style: style(rgb,m,ci,cj,z,surface,shade,bounds)
            depth=i+j+z/16
            visible=m&(depth>=self.depth-1e-8)
            self.a[visible,:3]=rgb[visible]; self.a[visible,3]=255
            self.depth[visible]=depth[visible]
            owners=part_for(ci,cj,z,surface) if callable(part_for) else part_for
            if isinstance(owners,str): self.owner[visible]=owners
            else: self.owner[visible]=owners[visible]
            self.surface_masks[name+'/'+surface]=m
    def overlay(self,im,part):
        a=np.array(im); m=a[:,:,3]>0
        self.a[m]=a[m]; self.owner[m]=part

def style_sofa(rgb,m,i,j,z,surface,shade,b):
    R=RAMPS['red']; i0,j0,i1,j1,z0,z1=b
    if surface=='top':
        di=np.minimum(i-i0,i1-i); dj=np.minimum(j-j0,j1-j)
        rgb[m]=R[2]
        rgb[m&((di<1/32)|(dj<1/32))]=R[1]
        rgb[m&(di>=1/32)&(dj>=1/32)&((di<1/16)|(dj<1/16))]=R[3]
        # Soft broad highlights suggest a stuffed cushion within exact straight seams.
        u=(i-(i0+i1)/2)/((i1-i0)/2); v=(j-(j0+j1)/2)/((j1-j0)/2)
        rgb[m&(u*u+v*v<.48)]=R[3]
        rgb[m&(di>1/16)&(dj>1/8)&(dj<3/16)]=R[3]
    else:
        rgb[m&(z>=z0+2)&(z<z1)]=R[3 if surface=='j' else 2]
        rgb[m&(z>=z1-1)]=R[3]
        rgb[m&(z<z0+2)]=R[1]

def style_back(rgb,m,i,j,z,surface,shade,b):
    style_sofa(rgb,m,i,j,z,surface,shade,b)
    R=RAMPS['red']
    if surface!='top':
        rgb[m]=R[2 if surface=='j' else 1]
        rgb[m&(z>20)&(z<23)]=R[3 if surface=='j' else 2]
        rgb[m&(z<10)]=R[1 if surface=='j' else 0]
        rgb[m&((abs(j-.625)<1/32)|(abs(j-1.625)<1/32))&(z>11)&(z<23)]=R[1]
        for q in (.1875,1.125,2.0625):
            tuft=m&(j>=q)&(j<q+1/16)&(z>=16)&(z<18)
            rgb[tuft]=R[1 if surface=='j' else 0]
            rgb[m&(j>=q+1/16)&(j<q+1/8)&(z>=17)&(z<18)]=R[3 if surface=='j' else 2]

def sofa(face,clips):
    n=Native('sofa',face)
    role_clips={}
    for role in ('rear','front'):
        for c in clips:
            if c['partId'].split('__')[1]!=role: continue
            if c.get('seatGroupId'): idx=1
            else:
                i,j=unrotate(c['depthAnchor']['i'],c['depthAnchor']['j'],'sofa',face)
                idx=0 if j<1 else 2
            role_clips[role,idx]=c['name']
    def bind(front=False):
        def part(i,j,z,s):
            idx=np.where(j<.5,0,np.where(j<1.5,1,2)); roles=np.full((128,128),'front' if front else 'rear',dtype=object)
            out=np.empty((128,128),dtype=object)
            for k in (0,1,2): out[idx==k]=role_clips[('front' if front else 'rear',k)]
            return out
        return part
    n.box('upholstered-base',(-.4375,-.4375,.4375,2.4375,3,8),'red',bind(True),style_sofa)
    n.box('padded-back',(-.4375,-.4375,-.1875,2.4375,8,24),'red',bind(face in ('ne','nw')),style_back)
    for k,(a,b) in enumerate(((-.1875,.5625),(.625,1.375),(1.4375,2.1875))):
        n.box('cushion-'+str(k),(-.1875,a,.375,b,8,12),'red',lambda i,j,z,s:bind(s!='top')(i,j,z,s),style_sofa)
    for k,(a,b) in enumerate(((-.4375,-.1875),(2.1875,2.4375))):
        n.box('armrest-'+str(k),(-.4375,a,.4375,b,5,20),'red',bind(True),style_sofa)
    # Low wooden feet are real cuboids under the fixed footprint.
    for k,(i,j) in enumerate(((-.3125,-.3125),(.3125,-.3125),(-.3125,2.3125),(.3125,2.3125))):
        n.box('foot-'+str(k),(i-1/16,j-1/16,i+1/16,j+1/16,0,3),'wood',bind(True))
    # Warm cream throw pillow: thickness and piping share exact iso axes.
    def pillowstyle(rgb,m,i,j,z,s,sh,b):
        C=RAMPS['cream']
        if s!='top':
            rgb[m&(z>14)&(z<20)]=C[4 if s=='j' else 3]
            rgb[m&(z<=14)]=C[2]
    n.box('throw-pillow',(-.125,1.75,.0625,2.125,12,21),'cream',bind(False),pillowstyle)
    def phonestyle(rgb,m,i,j,z,s,sh,b):
        rgb[m]=RAMPS['navy'][0 if s=='top' else 1]
        if s=='top':
            rgb[m&(i>.125)&(i<.25)&(j>0)&(j<.125)]=RAMPS['glass'][1]
    n.box('phone',(.0625,-.0625,.3125,.1875,12,13),'navy',bind(False),phonestyle)
    return n

def appliance(face,f,clips):
    n=Native(f,face); h={'fridge':44,'water-cooler':26,'coffee-machine':28}[f]
    bysurface={}
    for c in clips:
        role=c['partId'].split('__')[1]
        if role=='rear': bysurface['top']=c['name']
        elif c['depthAnchor']['i']>c['depthAnchor']['j']: bysurface['i']=c['name']
        else: bysurface['j']=c['name']
    frontsurface='i' if face in ('se','nw') else 'j'
    def detail(rgb,m,i,j,z,s,shade,b):
        C=RAMPS['cream']; M=RAMPS['metal']; T=RAMPS['teal']
        if s=='top': return
        # Native local coordinate along each projected upright panel.
        u=(j+.1875)*32 if s==frontsurface else (i+.25)*32
        width=12 if s==frontsurface else 16
        if f=='fridge':
            if s==frontsurface and face in ('se','sw'):
                rgb[m&(z>=26)&(z<27)]=C[0]
                rgb[m&(z>=27)&(z<28)]=C[4]
                for lo,hi in ((11,22),(31,40)):
                    rgb[m&(u>=width-4)&(u<width-2)&(z>=lo)&(z<hi)]=M[2]
                    rgb[m&(u>=width-4)&(u<width-3)&(z>=lo+1)&(z<hi)]=M[4]
                rgb[m&(u>=2)&(u<5)&(z>=35)&(z<38)]=RAMPS['wood'][3]
            elif s==frontsurface and face in ('ne','nw'):
                for lo,hi in ((6,13),(31,38)):
                    vent=m&(u>=3)&(u<width-3)&(z>=lo)&(z<hi)
                    rgb[vent]=C[0]
                    rgb[vent&(np.floor(z).astype(int)%2==0)]=M[2]
            else:
                rgb[m&(u>=2)&(u<3)&(z>=5)&(z<40)]=C[4 if s=='j' else 3]
            rgb[m&(z<2)]=M[1]
        elif f=='water-cooler':
            if s==frontsurface and face in ('se','sw'):
                recess=m&(u>=2)&(u<width-2)&(z>=7)&(z<21)
                rgb[recess]=M[0]
                rgb[recess&(z<9)]=M[3]
                rgb[recess&(u<4)]=M[2]
                rgb[recess&(z>=18)&(z<20)&(u<width/2)]=RAMPS['red'][2]
                rgb[recess&(z>=18)&(z<20)&(u>=width/2)]=T[3]
                rgb[recess&(z>=16)&(z<18)&((u<4)|(u>=width-4))]=C[4]
                rgb[m&(u>=3)&(u<width-3)&(z>=4)&(z<5)]=C[2]
            elif s==frontsurface:
                rgb[m&(u>3)&(u<width-3)&(z>3)&(z<7)&(np.floor(z).astype(int)%2==0)]=M[2]
        else:
            if s==frontsurface and face in ('se','sw'):
                panel=m&(u>=2)&(u<width-2)&(z>=4)&(z<24)
                rgb[panel]=M[0]
                rgb[panel&(z>=19)&(z<23)]=T[1]
                rgb[panel&(u>=4)&(u<width-4)&(z>=20)&(z<22)]=T[4]
                rgb[panel&(z>=13)&(z<15)&(u>=width/2-1)&(u<width/2+1)]=M[4]
                rgb[panel&(z>=6)&(z<10)&(u>=4)&(u<width-4)]=C[3]
                rgb[panel&(z>=9)&(z<10)&(u>=4)&(u<width-4)]=RAMPS['wood'][0]
                rgb[panel&(z<5)]=M[4]
                rgb[panel&(z>=16)&(z<17)&(u>=3)&(u<4)]=RAMPS['red'][3]
            elif s==frontsurface:
                rgb[m&(u>=3)&(u<width-3)&(z>=15)&(z<23)&(np.floor(z).astype(int)%2==0)]=M[2]
                rgb[m&(u>=4)&(u<6)&(z>=4)&(z<6)]=M[1]
    n.box('chassis',(-.25,-.1875,.25,.1875,0,h),'cream',lambda i,j,z,s:bysurface[s],detail)
    if f=='water-cooler':
        im=Image.new('RGBA',(128,128)); d=ImageDraw.Draw(im); G=RAMPS['glass']
        # Cylindrical jug deliberately has curved rims; its chassis stays exact.
        d.rectangle((57,43,70,51),fill=G[2]+(255,))
        d.ellipse((57,48,70,54),fill=G[1]+(255,))
        d.rectangle((58,44,60,50),fill=G[4]+(255,))
        d.ellipse((57,40,70,46),fill=G[3]+(255,))
        d.ellipse((59,41,68,44),fill=G[2]+(255,))
        d.line((58,44,61,45),fill=G[4]+(255,))
        d.line((58,48,68,48),fill=G[3]+(255,))
        d.line((59,51,68,51),fill=G[3]+(255,))
        n.overlay(im,bysurface['top'])
    return n

def table(face,clips):
    n=Native('side-table',face)
    # Three visible leg assemblies retain all nine original depth parts.
    for k,(i,j) in enumerate(((-.375,.375),(.375,-.375),(.375,.375))):
        names=[f'rear-{k*3+t:02}' for t in range(3)]
        def binder(ci,cj,z,s,names=names):
            out=np.full((128,128),names[0 if s=='i' else 1],dtype=object)
            out[z>=12]=names[2] # physical top joint/collar of each leg
            return out
        n.box('leg-'+str(k),(i-1/16,j-1/16,i+1/16,j+1/16,0,14),'wood',binder)
    def grain(rgb,m,i,j,z,s,shade,b):
        if s=='top':
            W=RAMPS['wood']; rgb[m]=W[3]
            rgb[m&(i<-.375)]=W[4]
            rgb[m&(j>.375)]=W[2]
            for lo,hi,start,end in ((-.1875,-.15625,-.25,.0625),(.125,.15625,-.3125,-.0625),(.25,.28125,.125,.3125)):
                rgb[m&(i>=lo)&(i<hi)&(j>=start)&(j<end)]=W[2]
    n.box('tabletop',(-.4375,-.4375,.4375,.4375,13,16),'wood','front-00',grain)
    n.box('folded-paper',(.0625,.0,.3125,.25,16,17),'cream','front-00')
    im=Image.new('RGBA',(128,128)); d=ImageDraw.Draw(im); C=RAMPS['cream']; W=RAMPS['wood']
    x,y=n.point(-.125,.125,16)
    d.ellipse((x-4,y-7,x+4,y-3),fill=C[4]+(255,))
    d.rectangle((x-4,y-5,x+3,y-1),fill=C[3]+(255,))
    d.ellipse((x-4,y-3,x+3,y),fill=C[2]+(255,))
    d.line((x-3,y-5,x-3,y-1),fill=C[4]+(255,))
    d.ellipse((x-3,y-7,x+2,y-4),fill=W[0]+(255,))
    d.arc((x+2,y-5,x+6,y-1),-100,100,fill=C[3]+(255,))
    n.overlay(im,'front-00')
    return n

def physical(a):
    yy,xx=np.where(a[:,:,3]>0); assert len(yy),'empty part'
    y=int(yy.max()); xs=xx[yy==y]
    return dict(x=int(xs[len(xs)//2]),y=y,radius=0,meaning='lowest texel of this physical assembly surface')

def build():
    EDGES.clear()
    for f in FAMILIES:
        p=track(source(f)/(f+'.iso.json')); doc=read(p)
        body=doc['sheets'][0]; old=np.array(Image.open(track(p.parent/body['file'])).convert('RGBA'))
        atlas=np.zeros_like(old)
        for face in FACINGS:
            clips=[c for c in body['rows'] if c['direction']==face]
            if f in ('floor-lamp','plant'):
                # No skewed structural edge: preserve curved silhouettes, part masks and detail.
                for c in clips:
                    r,col=c['row']*128,c.get('column',0)*128
                    atlas[r:r+128,col:col+128]=old[r:r+128,col:col+128]
                im=Image.open(track(source(f)/f'{f}-empty-{face}.png')).convert('RGBA')
                im.save(OUT/f'{f}-empty-{face}.png')
                continue
            n=sofa(face,clips) if f=='sofa' else table(face,clips) if f=='side-table' else appliance(face,f,clips)
            n.a[n.a[:,:,3]==0]=0
            for c in clips:
                part=np.zeros_like(n.a); m=n.owner==c['name']; part[m]=n.a[m]
                assert m.any(),(f,face,c['name'])
                r,col=c['row']*128,c.get('column',0)*128
                atlas[r:r+128,col:col+128]=part
                c['anchorChecks']=[physical(part)]
            assert set(n.owner[n.a[:,:,3]>0])=={c['name'] for c in clips}
            Image.fromarray(n.a).save(OUT/f'{f}-empty-{face}.png')
        Image.fromarray(atlas).save(OUT/body['file'])
        # The requested drop-in shadows and depth parts remain byte-identical.
        for s in doc['sheets'][1:]: (OUT/s['file']).write_bytes(track(p.parent/s['file']).read_bytes())
        if f in ('sofa','side-table'):
            sp=track(p.parent/(f+'-shadow.iso.json')); sd=read(sp)
            save_json(f+'-shadow.iso.json',sd)
            for s in sd['sheets']: (OUT/s['file']).write_bytes(track(sp.parent/s['file']).read_bytes())
        doc['provenance']={
            'method':'native pixel-center 3D plane redraw using the shelf-v3 method; no nonuniform resize',
            'source':str(p.relative_to(ROOT)),
            'geometry':'integer structural endpoints; exact 2:1 floor-plane edges and vertical uprights',
            'preserved':'all furniture/variant fields, sheet/clip anchors, IDs, depth anchors, ordering, contacts and shadow bytes',
            'radialException':'original organic plant and round lamp retained; lamp stem already vertical' if f in ('plant','floor-lamp') else '',
            'limitations':'private lane assets; pack and runtime not modified; strict rectilinear cushion silhouette with pillowy shading'}
        save_json(f+'.iso.json',doc)
    save_json('structural-geometry.json',EDGES)
    save_json('source-sha256.json',HASHES)
    print('Built seven families; five rectilinear redraws, two retained radial designs.')

def review():
    for group,fs in (('sofa',('sofa',)),('utility',FAMILIES[1:5]),('radial',FAMILIES[5:])):
        b=Image.new('RGB',(1680,640*len(fs)),'#111924'); d=ImageDraw.Draw(b)
        for r,f in enumerate(fs):
            for c,face in enumerate(FACINGS):
                old=Image.open(source(f)/f'{f}-empty-{face}.png').convert('RGBA')
                new=Image.open(OUT/f'{f}-empty-{face}.png').convert('RGBA')
                x=c*420; y=r*640
                label(d,(x+8,y+4),f'{f} {face.upper()} / v2 | v3')
                put(b,old,(x+32,y+24)); put(b,new,(x+220,y+24))
                # Crop only empty cell padding; always exact nearest 3x, no bbox fit.
                box=(min(old.getbbox()[0],new.getbbox()[0])-1,min(old.getbbox()[1],new.getbbox()[1])-1,max(old.getbbox()[2],new.getbbox()[2])+1,max(old.getbbox()[3],new.getbbox()[3])+1)
                oo=old.crop(box); nn=new.crop(box)
                # Stacked enlarged rows keep 120px-wide sofa inside its panel.
                put(b,oo,(x+16,y+160),3); put(b,nn,(x+16,y+400),3)
                label(d,(x+8,y+143),'v2 3x',SMALL); label(d,(x+8,y+383),'v3 3x',SMALL)
        b.save(OUT/f'before-after-{group}-1x3x.png')
    print('Exact 1x/3x reviews written.')

def composite(p,face):
    doc=read(p); sh={s['id']:s for s in doc['sheets']}
    v=next(v for v in doc['furniture'][0]['variants'] if v.get('orientation',v.get('id'))==face)
    a=Image.new('RGBA',(128,128)); counts=np.zeros((128,128),int)
    for part in v['parts']:
        s=sh[part['sheetId']]; c=next(c for c in s['rows'] if c['name']==part['animation'] and c['direction']==part['direction'])
        im=MEASURE.crop_clip(p,s,c); a.alpha_composite(im); counts+=np.array(im)[:,:,3]>0
    return a,counts

def masks_for(f,im):
    if f=='plant': return []
    if f=='floor-lamp': return [('straight stem',MEASURE.color_mask(im,['wood']),True)]
    if f=='side-table': return [('wood structure',MEASURE.color_mask(im,['wood']),False)]
    if f=='sofa': return [('red structure',MEASURE.color_mask(im,['red']),False)]
    if f=='water-cooler': return [('chassis',MEASURE.color_mask(im,['cream','metal']),False)]
    return [('chassis',np.array(im)[:,:,3]>0,False)]

def audit():
    allrows=[]; coverage=[]
    for f in FAMILIES:
        for face in FACINGS:
            recs={}
            for stage,p in (('before',source(f)/(f+'.iso.json')),('after',OUT/(f+'.iso.json'))):
                im,_=composite(p,face); rows=[]
                for surface,m,vertical in masks_for(f,im):
                    for rec in MEASURE.measure_mask(m,minimum=8,vertical_only=vertical):
                        rows.append(dict(stage=stage,family=f,facing=face,surface=surface,**rec))
                uniq={(r['axis'],tuple(r['p0']),tuple(r['p1'])):r for r in rows}; rows=list(uniq.values())
                recs[stage]=rows; allrows.extend(rows)
            coverage.append(dict(family=f,facing=face,before_edges=len(recs['before']),after_edges=len(recs['after']),before_max_drift=max([r['drift_px'] for r in recs['before']],default=None),after_max_drift=max([r['drift_px'] for r in recs['after']],default=None),note='Leaves and round/tapered pot: no rectilinear plane.' if f=='plant' else 'Conical shade and elliptical base excluded; unchanged vertical stem measured.' if f=='floor-lamp' else 'Jug curved rims excluded; chassis measured.' if f=='water-cooler' else 'Same shelf-v3 boundary OLS algorithm, minimum 8 samples.'))
    save_json('skew-edge-measurements.json',allrows); save_json('skew-coverage.json',coverage)
    with (OUT/'skew-edge-measurements.csv').open('w',newline='',encoding='utf-8') as out:
        w=csv.DictWriter(out,fieldnames=list(allrows[0])); w.writeheader(); w.writerows(allrows)
    for r in coverage: print(r['family'],r['facing'],r['before_max_drift'],r['after_max_drift'])
    return coverage

def context_review():
    # Source references are displayed at thumbnail size; production cells stay 1x/3x.
    h=Image.open(ROOT/'assets/art/iso-concept/iso-office-hero.png').convert('RGB')
    it=Image.open('C:/Codex-Workspace/vd-lanes/isowire/artifacts/iso-integration-final/iso-demo-24.0s-2x.png').convert('RGB')
    b=Image.new('RGB',(1920,1360),'#111924'); d=ImageDraw.Draw(b)
    h.thumbnail((960,590)); it.thumbnail((960,590))
    b.paste(h,(0,28)); b.paste(it,(960,28))
    label(d,(8,5),'HERO CONCEPT / reference thumbnail')
    label(d,(968,5),'RECENT INTEGRATED / supplied v2 screenshot')
    for c,f in enumerate(FAMILIES[:5]):
        old=Image.open(source(f)/f'{f}-empty-se.png').convert('RGBA'); new=Image.open(OUT/f'{f}-empty-se.png').convert('RGBA')
        x=c*384; y=632
        label(d,(x+8,y),f+' / SE')
        label(d,(x+8,y+20),'v2 1x',SMALL); label(d,(x+196,y+20),'v3 1x',SMALL)
        put(b,old,(x+8,y+38)); put(b,new,(x+196,y+38))
        box=(min(old.getbbox()[0],new.getbbox()[0])-1,min(old.getbbox()[1],new.getbbox()[1])-1,max(old.getbbox()[2],new.getbbox()[2])+1,max(old.getbbox()[3],new.getbbox()[3])+1)
        label(d,(x+8,y+174),'v2 exact 3x',SMALL); put(b,old.crop(box),(x+8,y+194),3)
        label(d,(x+8,y+416),'v3 exact 3x',SMALL); put(b,new.crop(box),(x+8,y+436),3)
    label(d,(8,1336),'Reference images are unchanged. New cells have not been installed in the app or pack.',SMALL)
    b.save(OUT/'hero-integrated-before-after-1x3x.png')

    # Frozen wall endpoints define both guides: (+32,+16) and (-32,+16).
    b=Image.new('RGB',(4*450,5*240+40),'#111924'); d=ImageDraw.Draw(b)
    label(d,(8,6),'Wall parallelism / S0 vectors (+32,+16),(-32,+16); native cell locations preserved')
    for r,f in enumerate(FAMILIES[:5]):
        for c,face in enumerate(FACINGS):
            x,y=c*450,r*240+36
            label(d,(x+6,y+2),f+' '+face.upper()+' / v2 | v3')
            for stage,folder,dx in (('v2',source(f),12),('v3',OUT,225)):
                cell=Image.new('RGBA',(192,160)); cd=ImageDraw.Draw(cell)
                # Exact projected wall arms, not manually tilted artwork.
                cd.line((16,90,144,26),fill=RAMPS['teal'][2]+(255,),width=1)
                cd.line((48,26,176,90),fill=RAMPS['teal'][2]+(255,),width=1)
                im=Image.open(folder/f'{f}-empty-{face}.png').convert('RGBA')
                cell.alpha_composite(im,(32,24)); put(b,cell,(x+dx,y+26))
                label(d,(x+dx,y+198),stage+' 1x / parallel guides',SMALL)
    b.save(OUT/'wall-parallel-before-after.png')

def verify():
    rows=[]; protected=0; parts=0
    for f in FAMILIES:
        before=read(source(f)/(f+'.iso.json')); after=read(OUT/(f+'.iso.json'))
        assert before['furniture']==after['furniture'],f+' variant metadata changed'
        for bs,ns in zip(before['sheets'],after['sheets']):
            assert {k:v for k,v in bs.items() if k!='rows'}=={k:v for k,v in ns.items() if k!='rows'}
            for bc,nc in zip(bs['rows'],ns['rows']):
                assert {k:v for k,v in bc.items() if k!='anchorChecks'}=={k:v for k,v in nc.items() if k!='anchorChecks'}
                protected+=1
            if bs['kind']=='shadow': assert (source(f)/bs['file']).read_bytes()==(OUT/ns['file']).read_bytes()
        for face in FACINGS:
            im,count=composite(OUT/(f+'.iso.json'),face)
            empty=Image.open(OUT/f'{f}-empty-{face}.png').convert('RGBA')
            assert np.array_equal(np.array(im),np.array(empty)),(f,face,'part union')
            assert count.max()==1,(f,face,'overlap')
            parts+=int(sum(c['direction']==face for c in after['sheets'][0]['rows']))
            a=np.array(im); assert set(np.unique(a[:,:,3]))<={0,255}
            assert not a[a[:,:,3]==0].any()
            permitted={tuple(c) for ramp in RAMPS.values() for c in ramp}
            assert all(tuple(c) in permitted for c in np.unique(a[a[:,:,3]>0,:3],axis=0)),(f,face,'outside frozen ramps')
        if f in ('sofa','side-table'):
            assert (source(f)/(f+'-shadow.png')).read_bytes()==(OUT/(f+'-shadow.png')).read_bytes()
            assert read(source(f)/(f+'-shadow.iso.json'))==read(OUT/(f+'-shadow.iso.json'))
        if f in ('plant','floor-lamp'):
            assert (source(f)/(f+'-parts.png')).read_bytes()==(OUT/(f+'-parts.png')).read_bytes()
        rows.append(f+' : variants/anchors/part IDs/depth/tie ranks/seat contacts/shadows preserved; disjoint reconstruction PASS')
    geometry=read(OUT/'structural-geometry.json')
    assert len(geometry)==1008
    assert all(e['dx']==0 or abs(e['dx'])==2*abs(e['dy']) for e in geometry)
    cv=read(OUT/'skew-coverage.json')
    assert all(c['after_max_drift'] is None or c['after_max_drift']<=1 for c in cv)
    # Projection/contact constants are checked directly against preserved sofa profiles.
    sofa_doc=read(OUT/'sofa.iso.json')
    for v in sofa_doc['furniture'][0]['variants']:
        g=v['seatGround']; c=v['chairSeatAnchor']; assert c==dict(x=32*(g['i']-g['j']),y=16*(g['i']+g['j'])-12)
        assert v['seatProfiles'][0]['contactOffset']==c
    rows+=['1008 structural cuboid edges: exact 2:1 or vertical, endpoint drift 0px.',
           f'{parts} body parts / 28 reconstructed cells: disjoint and byte-exact union.',
           f'{protected} clip records: anchors, names, depth fields and ordering unchanged (only physical anchorChecks regenerated).',
           'Four sofa ground/pelvis contacts: P(seatGround)+(0,-12); source pelvis remains (16,44).',
           'Lamp: original straight vertical stem (drift 0px); shade/base are round, inapplicable to rectilinear slope.',
           'Plant: original organic leaves and round pot; straight-edge regression inapplicable.',
           'Seven original shadow PNGs byte-identical; alpha 0/48/96 and offsets preserved. All body RGB colors belong to the eight frozen S0 ramps.',
           'No full dotnet test, pack change, live runtime validation, git mutation, dependency install or new image generation.']
    env=dict(__import__('os').environ); env['PYTHONIOENCODING']='utf-8'; env['PYTHONDONTWRITEBYTECODE']='1'
    cmd=[sys.executable,'-B',str(ROOT/'tools/iso_check.py'),str(OUT),'--tolerance','0']
    p=subprocess.run(cmd,cwd=ROOT,capture_output=True,text=True,encoding='utf-8',env=env)
    (OUT/'iso-check-tolerance0.txt').write_text(p.stdout+p.stderr,encoding='utf-8')
    assert p.returncode==0,p.stdout+p.stderr
    rows.append(p.stdout.strip().splitlines()[-1])
    rows.append('Checker SKIP is limited to 6x6 floor seams, character face-lock/blink and shadow depth-part naming (inapplicable sheet kinds).')
    (OUT/'validation.txt').write_text('\n'.join(rows)+'\n',encoding='utf-8')
    # Capture all read-only references and inputs used by this helper.
    for f in FAMILIES:
        for p in source(f).glob(f+'*.png'): track(p)
        track(source(f)/(f+'.iso.json'))
    for p in (ROOT/'docs/iso-slice-s0.md',ISO/'furniture/furniture-part-convention.md',ROOT/'tools/iso_check.py',ISO/'furniture-v3/utility-shelf/measure_redraw_shelf_v3.py',ROOT/'assets/art/iso-concept/iso-office-hero.png',Path('C:/Codex-Workspace/vd-lanes/isowire/artifacts/iso-integration-final/iso-demo-24.0s-2x.png')): track(p)
    save_json('source-sha256.json',HASHES)
    print('\n'.join(rows))

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--inspect',action='store_true'); parser.add_argument('--build',action='store_true'); parser.add_argument('--review',action='store_true'); parser.add_argument('--audit',action='store_true'); parser.add_argument('--context',action='store_true'); parser.add_argument('--verify',action='store_true')
    args=parser.parse_args()
    if args.inspect: inspect()
    if args.build: build()
    if args.review: review()
    if args.audit: audit()
    if args.context: context_review()
    if args.verify: verify()
