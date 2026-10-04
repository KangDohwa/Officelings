"""Chair v3 only. Reads v2/snapshots; all writes are adjacent to this helper. Run -B."""
from pathlib import Path
import copy, hashlib, importlib.util, json, math, subprocess, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[4]
V2 = ROOT / 'assets/art/iso/furniture-v2/workstations'
ACTORS = V2 / 'rev2-source/actor-inputs'
FACINGS = ('se', 'sw', 'ne', 'nw')
TEAL = ('10282C', '235E5C', '20746C', '3C9F96', '5DB1A5')
METAL = ('191E1E', '262D38', '565958', '808985', '89918F')
DARK = (TEAL[0],TEAL[0],TEAL[1],TEAL[1],TEAL[2])
EDGES, COMPONENTS, SOURCES = [], {}, {}

def out(name):
    p = (OUT / name).resolve()
    assert p.is_relative_to(OUT)
    return p

def source(p):
    p = Path(p)
    SOURCES[str(p)] = hashlib.sha256(p.read_bytes()).hexdigest()
    return p

def doc(p): return json.loads(source(p).read_text(encoding='utf-8'))
def imread(p): return Image.open(source(p)).convert('RGBA')
def rgba(c): return (*bytes.fromhex(c), 255)
def blank(): return Image.new('RGBA', (128,128))
def alpha(im): return np.array(im)[:,:,3] > 0
def rot(i,j,f): return {'se':(i,j),'sw':(-j,i),'ne':(j,-i),'nw':(-i,-j)}[f]

def proj(p,f):
    i,j = rot(p[0]/16,p[1]/16,f)
    return (64+32*(i-j),80+16*(i+j)-p[2])

def points(pp,f): return [proj(p,f) for p in pp]

def record(pp,f,role):
    pts=points(pp,f)
    snapped=[(math.floor(x+.5),math.floor(y+.5)) for x,y in pts]
    for a,b in zip(snapped,snapped[1:]+snapped[:1]):
        dx,dy=b[0]-a[0],b[1]-a[1]
        assert dx==0 or abs(dx)==2*abs(dy),(f,role,'snapped',a,b)
    for a,b in zip(pts,pts[1:]+pts[:1]):
        dx,dy=b[0]-a[0],b[1]-a[1]
        if dx==dy==0: continue
        assert dx==0 or abs(dx)==2*abs(dy),(f,role,a,b)
        EDGES.append(dict(facing=f,component=role,a=a,b=b,dx=dx,dy=dy))
    return snapped

def polygon(im,pp,f,color,role,outline=None):
    ImageDraw.Draw(im).polygon(record(pp,f,role),fill=rgba(color),outline=rgba(outline) if outline else None)

def line(im,pp,f,color,role,width=1):
    pts=points(pp,f)
    for a,b in zip(pts,pts[1:]):
        dx,dy=b[0]-a[0],b[1]-a[1]
        assert dx==0 or abs(dx)==2*abs(dy),(f,role,a,b)
        EDGES.append(dict(facing=f,component=role,a=a,b=b,dx=dx,dy=dy))
    ImageDraw.Draw(im).line([(math.floor(x+.5),math.floor(y+.5)) for x,y in pts],fill=rgba(color),width=width)

def prism(poly,z0,z1,f,ramp,role,outline=True):
    """Orthogonal model contours; every projected edge is exactly 2:1 or vertical."""
    im=blank()
    transformed=[rot(i,j,f) for i,j in poly]
    faces=[]
    for n,(a,b) in enumerate(zip(poly,poly[1:]+poly[:1])):
        ar,br=transformed[n],transformed[(n+1)%len(poly)]
        di,dj=br[0]-ar[0],br[1]-ar[1]
        if dj>0 or di<0:
            pp=[(*a,z0),(*b,z0),(*b,z1),(*a,z1)]
            shade=1 if dj>0 else 2
            faces.append((sum(ar)+sum(br),pp,shade))
    for _,pp,shade in sorted(faces,key=lambda x:x[0]):
        polygon(im,pp,f,ramp[shade],role+':side',ramp[0] if outline else None)
    polygon(im,[(*p,z1) for p in poly],f,ramp[3],role+':top',ramp[0] if outline else None)
    for n,(a,b) in enumerate(zip(poly,poly[1:]+poly[:1])):
        ar,br=transformed[n],transformed[(n+1)%len(poly)]
        if br[1]<ar[1] or br[0]>ar[0]:
            line(im,[(*a,z1),(*b,z1)],f,ramp[4],role+':light')
    return im

def box(i0,j0,i1,j1,z0,z1,f,ramp,role,outline=True):
    return prism([(i0,j0),(i1,j0),(i1,j1),(i0,j1)],z0,z1,f,ramp,role,outline)

def chair(f):
    rear,front=blank(),blank()
    base=blank()
    # Five spokes: four tile axes plus a fifth screen-vertical axis. No skewed rays.
    fifth={'se':(4,4),'sw':(4,-4),'ne':(-4,4),'nw':(-4,-4)}[f]
    spokes=[(6,0),(0,6),(-6,0),(0,-6),fifth]
    for n,(i,j) in sorted(enumerate(spokes),key=lambda t:sum(rot(*t[1],f))):
        line(base,[(0,0,2),(i,j,2)],f,TEAL[0],'base-spoke-'+str(n),3)
        line(base,[(0,0,3),(i,j,3)],f,TEAL[1],'base-rim-'+str(n),1)
        base.alpha_composite(box(i-1,j-1,i+1,j+1,0,2,f,DARK,'caster-'+str(n)))
    # Short gas-lift, adjustment lever, and under-seat dish.
    base.alpha_composite(box(-1,-1,1,1,2,8,f,DARK,'gas-lift'))
    base.alpha_composite(box(-2,-2,2,2,6,8,f,DARK,'seat-mechanism'))
    line(base,[(0,2,7),(2,2,7),(2,3,7)],f,TEAL[0],'adjustment-lever',1)
    rear.alpha_composite(base)

    # Backrest is a thin vertical plane with stepped rounded corners. No headrest.
    back=blank()
    outline=[(-4,-3,21),(-4,3,21),(-4,3,20),(-4,4,20),(-4,4,12),
             (-4,3,12),(-4,3,10),(-4,-3,10),(-4,-3,12),(-4,-4,12),
             (-4,-4,20),(-4,-3,20)]
    polygon(back,outline,f,TEAL[1],'mesh-frame',TEAL[0])
    # One-pixel inset and sparse woven mesh, entirely on the frozen teal ramp.
    mask=Image.fromarray(alpha(back).astype(np.uint8)*255).filter(ImageFilter.MinFilter(3))
    aa=np.array(back); inner=np.array(mask)>0
    yy,xx=np.indices(inner.shape)
    aa[inner]=rgba(TEAL[2])
    aa[inner & ((xx+2*yy)%4==0)]=rgba(TEAL[1])
    aa[inner & ((xx+2*yy)%4==2)]=rgba(TEAL[3])
    back=Image.fromarray(aa)
    line(back,[(-4,-3,21),(-4,3,21)],f,TEAL[3],'upper-frame-highlight')
    # Lumbar band turns forward at its ends; orthogonal model steps define the curve.
    line(back,[(-4,-3,15),(-4,-3,14),(-3,-3,14),(-3,-2,14),
               (-3,2,14),(-3,3,14),(-4,3,14),(-4,3,15)],
         f,TEAL[0],'lumbar-curve',2)
    line(back,[(-3,-2,15),(-3,2,15)],f,TEAL[2],'lumbar-top-light')
    # Slim bent support beneath the mesh.
    line(back,[(-4,0,10),(-3,0,10),(-3,0,8),(-1,0,8)],f,TEAL[0],'back-support',2)
    if f in ('ne','nw'):front.alpha_composite(back)
    else:rear.alpha_composite(back)

    # Plush 5px cushion. Stair-stepped corner contours have no illegal diagonal bevels.
    contour=[(-2,-4),(2,-4),(2,-3),(3,-3),(3,-2),(4,-2),(4,2),
             (3,2),(3,3),(2,3),(2,4),(-2,4),(-2,3),(-3,3),
             (-3,2),(-4,2),(-4,-2),(-3,-2),(-3,-3),(-2,-3)]
    cushion=prism(contour,7,11,f,TEAL,'cushion-waterfall')
    inset=[(-2,-3),(2,-3),(2,-2),(3,-2),(3,2),(2,2),(2,3),
           (-2,3),(-2,2),(-3,2),(-3,-2),(-2,-2)]
    cushion.alpha_composite(prism(inset,11,12,f,TEAL,'cushion-soft-crown',outline=False))
    # Broad sewn bevel/piping follows true front edges, rather than a flat square face.
    for a,b in zip(contour,contour[1:]+contour[:1]):
        ar,br=rot(*a,f),rot(*b,f)
        if br[1]>ar[1] or br[0]<ar[0]:
            line(cushion,[(*a,10),(*b,10)],f,TEAL[2],'waterfall-bevel')
            line(cushion,[(*a,9),(*b,9)],f,TEAL[1],'cushion-seam')
    # Cushion contact remains exactly (64,68), z12. Actor is above the whole pad/lip.
    a=np.array(cushion);a[68,64]=rgba(TEAL[3]);cushion=Image.fromarray(a)
    # In rear views the seat is physically in front of the bottom back-frame joint.
    # Resolve that empty-object joint before assigning the two disjoint actor layers.
    fa=np.array(front);fa[alpha(cushion)]=0;front=Image.fromarray(fa)
    rear.alpha_composite(cushion)

    # Low adjustable armrests z13; outer frame never enters the typing hand contacts.
    arms=blank()
    for j in (-5,5):
        arm=blank()
        line(arm,[(-2,j,8),(-2,j,10),(-1,j,10),(-1,j,12)],f,TEAL[0],'curved-arm-support',2)
        arm.alpha_composite(box(-2,j-0.5,2,j+0.5,12,13,f,DARK,'arm-pad'))
        # Width .5 does not yield integer vertices; projected straight slope stays 2:1.
        # Native sampling uses the same floor(value+.5) convention at rasterization.
        arms.alpha_composite(arm)
        if sum(rot(0,j,f))>0:front.alpha_composite(arm)
        else:rear.alpha_composite(arm)
    # Component ownership in the empty chair is exclusive, including opaque joints.
    ra,fa=np.array(rear),np.array(front)
    # Seat-envelope joints also belong behind the occupant; keep their actual arm ink.
    joints=alpha(cushion)&(fa[:,:,3]>0)
    ra[joints]=fa[joints];fa[joints]=0
    ra[fa[:,:,3]>0]=0
    for a in (ra,fa):a[a[:,:,3]==0]=0
    COMPONENTS[f]={'base':base,'back':back,'cushion':cushion,'arms':arms}
    return Image.fromarray(ra),Image.fromarray(fa)

def cell(s,atlas,c,phase=0):
    x=(c.get('column',0)+phase)*s['cellW'];y=c['row']*s['cellH']
    return atlas.crop((x,y,x+s['cellW'],y+s['cellH']))

def build():
    d=doc(V2/'chair.iso.json');s=d['sheets'][0]
    atlas=Image.new('RGBA',imread(V2/s['file']).size)
    geometry=[]
    for f in FACINGS:
        rear,front=chair(f)
        full=rear.copy();full.alpha_composite(front)
        for name,image in (('rear-00',rear),('front-00',front)):
            c=next(c for c in s['rows'] if c['name']==name and c['direction']==f)
            # Physical contact: cushion z12 or backrest/arm attachment, not a debug point.
            target=(64,68) if name=='rear-00' else proj((-4,0,12),f)
            yy,xx=np.where(alpha(image));n=np.argmin((xx-target[0])**2+(yy-target[1])**2)
            c['anchorChecks']=[dict(x=int(xx[n]),y=int(yy[n]),radius=0)]
            c['contactKind']='cushion-top-z12' if name=='rear-00' else 'backrest-or-arm-junction'
            atlas.paste(image,(c.get('column',0)*128,c['row']*128))
        full.save(out('chair-empty-'+f+'.png'))
        geometry.append(dict(facing=f,bounds=full.getbbox(),cushionThickness=5,cushionTop=12,
                             backTop=21,seatPixel=[64,68],casterCount=5))
    atlas.save(out(s['file']))
    d['provenance']={
        'designReference':'Generic Korean ergonomic chair; built-in generated master-chair-v3-attempt1.png, no logo.',
        'derivation':'Generated appearance master followed by native-grid geometric reauthoring. Exact frozen teal ramp, binary alpha, upper-left highlights in every orientation. No runtime mirrors.',
        'chairV3':'Rounded stepped contour, 5px cushion, whole cushion/lip behind actor; z21 mesh back with lumbar curve; z13 low arms, gas lift, five caster contacts. Original part IDs/cells/anchors/ground keys preserved.',
        'limitations':'Low S0 back and arm height restrict ergonomic silhouette. Static snapshot compositing only; no runtime pack edits.'}
    out('chair.iso.json').write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    sd=doc(V2/'chair-shadow.iso.json');ss=sd['sheets'][0]
    sa=Image.new('RGBA',(128,512))
    for f in FACINGS:
        sh=blank();draw=ImageDraw.Draw(sh)
        # Compact ground envelope and exact (6,3) cast translation, alpha 48/96.
        ground=[proj((i,j,0),f) for i,j in [(-5,-4),(5,-4),(5,4),(-5,4)]]
        draw.polygon([(int(x+6),int(y+3)) for x,y in ground],fill=(*bytes.fromhex('111924'),48))
        fifth={'se':(4,4),'sw':(4,-4),'ne':(-4,4),'nw':(-4,-4)}[f]
        for i,j in [(6,0),(0,6),(-6,0),(0,-6),fifth]:
            pp=[proj((i+a,j+b,0),f) for a,b in [(-1,-1),(1,-1),(1,1),(-1,1)]]
            draw.polygon([(int(x),int(y)) for x,y in pp],fill=(*bytes.fromhex('111924'),96))
        c=next(c for c in ss['rows'] if c['direction']==f)
        cx,cy=proj((6,0,0),f)
        c['anchorChecks']=[dict(x=int(cx),y=int(cy),radius=0)]
        sa.paste(sh,(0,c['row']*128))
    sa.save(out('chair-shadow.png'))
    out('chair-shadow.iso.json').write_text(json.dumps(sd,indent=2)+'\n',encoding='utf-8')
    out('geometry-chair-v3.json').write_text(json.dumps(dict(summary=geometry,edges=EDGES),indent=2)+'\n',encoding='utf-8')
    return d,geometry

def review_module():
    spec=importlib.util.spec_from_file_location('chair_v2_review',V2/'measure-A3-rev2.py')
    m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
    def mapped(p):
        if p.is_relative_to(m.ART/'characters'):
            p=ACTORS/p.relative_to(m.ART/'characters')
            if p.name.endswith('.iso.json'):p=p.with_name(p.name.replace('.iso.json','.json'))
        return source(p)
    m.source=mapped
    return m

def overlay(m,items,sprite,pos,key,masks,size=(192,144)):
    labels=Image.new('L',size);scene=Image.new('RGBA',size,'#E7D1AC')
    covers={k:np.zeros((64,32),bool) for k in ('chair','desk')}
    layers=items+[dict(key=key,id='actor',family='actor',image=sprite,pos=pos)]
    for item in sorted(layers,key=lambda t:t['key']):
        scene.alpha_composite(item['image'],item['pos'])
        labels.paste(1 if item['family']=='actor' else 2,item['pos'],item['image'].getchannel('A'))
        if item['family']!='actor' and item['key']>key:
            covers[item['family']] |= m.local_mask(alpha(item['image']),item,pos)
    owned=np.array(labels.crop((*pos,pos[0]+32,pos[1]+64)))==1
    assert np.array_equal(owned&alpha(sprite),~(covers['chair']|covers['desk'])&alpha(sprite))
    result={}
    for name,mask in masks.items():
        total=int(mask.sum());visible=int((mask&owned).sum())
        result[name]=dict(visible=visible,total=total,fraction=visible/total if total else None,
                         chairHidden=int((mask&covers['chair']).sum()),deskHidden=int((mask&covers['desk']).sum()))
    return result,scene

def savepair(name,image):
    image.save(out(name+'-1x.png'))
    image.resize((image.width*3,image.height*3),Image.Resampling.NEAREST).save(out(name+'-3x.png'))

def validate_and_review(d,geometry):
    m=review_module();m.FURN=V2;desk,ds=m.furniture('desk');oldchair,oldcs=m.furniture('chair')
    m.FURN=OUT;newchair,newcs=m.furniture('chair')
    # Metadata swap compatibility: identical geometry, part IDs, atlas positions and depth.
    old=doc(V2/'chair.iso.json')
    assert d['furniture']==old['furniture']
    for a,b in zip(old['sheets'][0]['rows'],d['sheets'][0]['rows']):
        for k in a:
            if k not in ('anchorChecks','contactKind'):assert a[k]==b[k],(a['partId'],k)
    emptyboard=Image.new('RGBA',(768,380),'#E7D1AC');draw=ImageDraw.Draw(emptyboard)
    for di,f in enumerate(FACINGS):
        for si,root in enumerate((V2,OUT)):
            image=imread(root/('chair-empty-'+f+'.png'))
            x=di*192;y=si*185
            emptyboard.alpha_composite(image.crop((32,40,96,100)),(x+110,y+20))
            emptyboard.alpha_composite(image.crop((32,40,96,100)).resize((192,180),Image.Resampling.NEAREST),(x,y+5))
            draw.text((x+3,y+3),f+' '+('v2' if si==0 else 'v3')+' 1x/3x',fill='#191E1E')
        s=d['sheets'][0];a=imread(OUT/s['file']);cells=[cell(s,a,c) for c in s['rows'] if c['direction']==f]
        assert not np.any(alpha(cells[0])&alpha(cells[1]))
        full=cells[0].copy();full.alpha_composite(cells[1])
        assert full.tobytes()==imread(OUT/('chair-empty-'+f+'.png')).tobytes()
    emptyboard.save(out('empty-before-after-chair-v3.png'))
    records=[];panels={};queues=[];walkers=[];phasepanels={}
    for ci,(folder,(name,run,skin)) in enumerate(m.PILOTS.items()):
        ad=m.load_json(m.ART/'characters'/folder/(name+'.iso.json'))
        s=next(s for s in ad['sheets'] if s['kind']=='character');a=m.load_image(m.ART/'characters'/folder/s['file'])
        for di,orientation in enumerate(FACINGS):
            v=m.variant(desk,orientation);profile=v['seatProfiles'][0];f=profile['isoFacing']
            sg=profile['seatGround'];sd=sum(sg.values());origin=(96,88);g=m.project(sg['i'],sg['j']);co=(origin[0]+g[0],origin[1]+g[1])
            akey=(sd,0,'actor',folder)
            variants={stage:(m.variant(ch,f),cs) for stage,ch,cs in [('before',oldchair,oldcs),('after',newchair,newcs)]}
            head=m.canonical_head(folder,run,f)
            clips=[c for c in s['rows'] if c['direction']==f and (c.get('seatAnchor') or c.get('phaseContacts'))]
            for c in clips:
                for phase in range(c['frames']):
                    sprite=m.cell(s,a,c,phase)
                    if c.get('phaseContacts'):
                        pc=c['phaseContacts'][phase];pivot=m.point(pc['seatAnchor']);offset=m.point(pc['contactOffset'])
                        pos=(co[0]+offset[0]-pivot[0],co[1]+offset[1]-pivot[1])
                        assert pos[1]+pc['footY']==co[1]
                    else:
                        pivot=m.point(c['seatAnchor']);offset=m.point(profile['contactOffset'])
                        assert pivot==(16,44)
                        pos=(origin[0]+offset[0]-pivot[0],origin[1]+offset[1]-pivot[1])
                    off=m.point(s['headOffsetsByClip'][c['name']+'/'+f][phase]);hm=alpha(m.shift(head,*off))
                    assert not np.any(hm&~alpha(sprite))
                    masks={'head':hm}
                    if c['name']=='seated-typing-front':
                        masks,_=m.regions(sprite,m.shift(head,*off),c,skin,folder,f)
                        assert [(pos[0]+h['x'],pos[1]+h['y']) for h in c['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in profile['handContacts']]
                    yy,xx=np.indices((64,32))
                    masks['pelvisThighs']=alpha(sprite)&~hm&(yy>=pivot[1]-1)&(yy<=pivot[1]+6)
                    # Non-typing clips have no authored hand contacts. Track all exposed
                    # upper-body skin, including forearms, without claiming a hand-only ROI.
                    if c['name']!='seated-typing-front':
                        exposed=np.zeros((64,32),bool);spr=np.array(sprite)
                        for color in skin:exposed|=np.all(spr[:,:,:3]==tuple(bytes.fromhex(color)),axis=2)
                        masks['upperBodySkin']=exposed&alpha(sprite)&~hm&(yy<=pivot[1]+2)
                    pair={}
                    for stage,(cv,cs) in variants.items():
                        items=m.parts(v,ds,origin,0,sd,'desk')+m.parts(cv,cs,co,sd,sd,'chair')
                        counts,scene=overlay(m,items,sprite,pos,akey,masks)
                        pair[stage]=counts
                        records.append(dict(stage=stage,character=folder,desk=orientation,worker=f,action=c['name'],phase=phase,destination=pos,**counts))
                        if stage=='after':
                            assert counts['head']['fraction'] >= (.85 if f in ('ne','nw') else .90),(folder,f,c['name'],phase,counts['head'])
                            if 'hands' in counts:assert counts['hands']['fraction']==1,(folder,f,phase,counts['hands'])
                            # Direct cushion cover check: the complete pad is physically behind the actor.
                            cushion_local=m.local_mask(alpha(COMPONENTS[f]['cushion']),dict(pos=(co[0]-64,co[1]-80)),pos)
                            chair_front=next(i for i in items if i['id']==f'chair__front__{f}__00')
                            front_local=m.local_mask(alpha(chair_front['image']),chair_front,pos)
                            cushion_hidden=int((masks['pelvisThighs']&cushion_local&front_local).sum())
                            records[-1]['cushionOverPelvisThighs']=cushion_hidden
                            assert cushion_hidden==0,(folder,f,c['name'],phase,'cushion over thighs',cushion_hidden)
                            phasepanels[(folder,orientation,c['name'],phase)]=scene
                        if phase==0 and c['name']=='seated-typing-front':
                            panels[(stage,folder,orientation)]=scene
                            queues.append(dict(stage=stage,character=folder,desk=orientation,actorKey=akey,items=[dict(partId=i['id'],key=i['key'],destination=i['pos']) for i in sorted(items,key=lambda x:x['key'])]))
                    assert pair['after']['head']['visible']>=pair['before']['head']['visible'],(folder,f,c['name'],phase,'head regression')
                    if 'upperBodySkin' in pair['after']:
                        assert pair['after']['upperBodySkin']['visible']>=pair['before']['upperBodySkin']['visible'],(folder,f,c['name'],phase,'skin regression')
            # Walkers retain ordinary ground depth, never substitute seat depth for them.
            cv,cs=variants['after']
            for stage,(wcv,wcs) in variants.items():
                ordinary=[]
                for p in wcv['parts']:
                    ss,aa=wcs[p['sheetId']];cc=next(x for x in ss['rows'] if x['partId']==p['partId'])
                    ordinary.append(dict(key=(sum(p['depthAnchor'].values()),p['isoZOrder'],'chair',p['partId']),id=p['partId'],family='chair',image=m.cell(ss,aa,cc),pos=(96-64,88-80)))
                for pi,(wi,wj) in enumerate([(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(1,1),(-1,1),(1,-1)]):
                    wp=m.project(wi,wj);pos=(96+wp[0]-16,88+wp[1]-60)
                    for wf in FACINGS:
                        wc=next(c for c in s['rows'] if c['name']=='walk' and c['direction']==wf);wh=m.canonical_head(folder,run,wf)
                        for phase in range(wc['frames']):
                            sprite=m.cell(s,a,wc,phase);off=m.point(s['headOffsetsByClip']['walk/'+wf][phase]);hm=alpha(m.shift(wh,*off));key=(wi+wj,0,'walker',folder)
                            count,scene=overlay(m,ordinary,sprite,pos,key,{'head':hm})
                            if key>max(i['key'] for i in ordinary):assert count['head']['fraction']==1
                            walkers.append(dict(stage=stage,character=folder,chair=f,worker=wf,phase=phase,ground=[wi,wj],head=count['head'],ordinaryDepth=True))
                            if stage=='after' and pi==3 and wf==f and phase==0:panels[('walker',folder,orientation)]=scene
    board=Image.new('RGBA',(1536,432),'#E7D1AC')
    for ci,folder in enumerate(m.PILOTS):
        for di,orientation in enumerate(FACINGS):
            for si,stage in enumerate(('before','after')):
                scene=panels[(stage,folder,orientation)].copy();ImageDraw.Draw(scene).text((2,2),folder+' D:'+orientation+' '+stage,fill='#191E1E');board.alpha_composite(scene,((di*2+si)*192,ci*144))
    savepair('contact-before-after-chair-v3',board)
    walkboard=Image.new('RGBA',(768,432),'#E7D1AC')
    for ci,folder in enumerate(m.PILOTS):
        for di,orientation in enumerate(FACINGS):walkboard.alpha_composite(panels[('walker',folder,orientation)],(di*192,ci*144))
    savepair('walkers-chair-v3',walkboard)
    # All lowering phases, compact native atlas plus explicit 3x terminal/initial views.
    transition=Image.new('RGBA',(768,1728),'#E7D1AC')
    for ci,folder in enumerate(m.PILOTS):
        for di,orientation in enumerate(FACINGS):
            for phase in range(4):
                scene=phasepanels[(folder,orientation,'sit-down',phase)].copy()
                ImageDraw.Draw(scene).text((2,2),folder+' '+orientation+' lower p'+str(phase),fill='#191E1E')
                transition.alpha_composite(scene,(di*192,(ci*4+phase)*144))
    transition.save(out('transitions-chair-v3-1x.png'))
    tz=Image.new('RGBA',(2304,2592),'#E7D1AC')
    for ci,folder in enumerate(m.PILOTS):
        for di,orientation in enumerate(FACINGS):
            for pi,phase in enumerate((0,3)):
                scene=phasepanels[(folder,orientation,'sit-down',phase)].copy()
                ImageDraw.Draw(scene).text((2,2),folder+' '+orientation+' lower p'+str(phase),fill='#191E1E')
                tz.alpha_composite(scene.resize((576,432),Image.Resampling.NEAREST),(di*576,(ci*2+pi)*432))
    tz.save(out('transitions-chair-v3-3x.png'))
    # Review every stored seated phase, including thinking/sleep and raising.
    phases=Image.new('RGBA',(1536,3888),'#E7D1AC')
    for k,((folder,o,action,phase),scene) in enumerate(phasepanels.items()):
        image=scene.copy();ImageDraw.Draw(image).text((2,2),folder+' '+o+' '+action+' p'+str(phase),fill='#191E1E')
        phases.alpha_composite(image,((k%8)*192,(k//8)*144))
    phases.save(out('all-seated-phases-chair-v3-1x.png'))
    # Reference comparison at genuine source 1x and nearest-neighbor 3x.
    hero=imread(ROOT/'assets/art/iso-concept/iso-office-hero.png')
    integrated=imread(Path('C:/Codex-Workspace/vd-lanes/isowire/artifacts/iso-integration-final/iso-demo-24.0s-2x.png'))
    # Integrated reference was exported 2x; nearest sample its original logical pixels.
    integrated1=integrated.resize((integrated.width//2,integrated.height//2),Image.Resampling.NEAREST)
    refs=Image.new('RGBA',(1024,650),'#E7D1AC');draw=ImageDraw.Draw(refs)
    for ci,(label,image) in enumerate([
        ('v2',imread(V2/'chair-empty-ne.png').crop((32,40,96,104))),
        ('v3',imread(OUT/'chair-empty-ne.png').crop((32,40,96,104))),
        ('hero original',hero.crop((1230,597,1300,671))),
        ('integrated 2x -> native',integrated1.crop((270,145,340,219))) ]):
        x=ci*256;draw.text((x+3,3),label+' / 1x and 3x',fill='#191E1E');refs.alpha_composite(image,(x+110,24));refs.alpha_composite(image.resize((image.width*3,image.height*3),Image.Resampling.NEAREST),(x+5,100))
    for di,o in enumerate(FACINGS):
        scene=panels[('after','dev-hoodie-v2',o)].crop((40,30,160,130))
        refs.alpha_composite(scene,(di*256+126,351));refs.alpha_composite(scene.resize((360,300),Image.Resampling.NEAREST).crop((36,0,292,270)),(di*256,380));draw.text((di*256+3,339),'dev / desk '+o,fill='#191E1E')
    refs.save(out('quality-v2-v3-hero-integrated-chair-v3.png'))
    checks=dict(emptyReconstruction=True,disjointParts=True,swapCompatibleMetadata=True,
                frozenGeometry=True,exactProjectedEdges=len(EDGES),seatedAfterFrames=sum(r['stage']=='after' for r in records),
                allSeatedHeadGates=True,headNoRegression=True,typingHands100=True,cushionDoesNotCoverPelvisThighs=True,
                nonTypingUpperBodySkinNoRegression=True,
                walkerCases=len(walkers),frontWalkerHeads100=True,snapshotsReadOnly=True)
    payload=dict(checks=checks,geometry=geometry,records=records,walkerRecords=walkers,queues=queues,
                 inputSHA256=SOURCES,scope='Fixed existing actor snapshots, v2 desk, all stored seated/transition clips and phases. Pixel ownership/depth compositing, no runtime pack or WPF change.')
    out('measurements-chair-v3.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(checks,indent=2))
    for f in FACINGS:
        rr=[r for r in records if r['stage']=='after' and r['worker']==f]
        print(f,'head min',min(r['head']['fraction'] for r in rr),'typing hand min',min(r['hands']['fraction'] for r in rr if 'hands' in r))
    # Verify immutable inputs still match the exact inputs used for the review.
    assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in SOURCES.items())
    return payload

def main():
    if '--finalize-only' in sys.argv:
        finalize(json.loads(out('measurements-chair-v3.json').read_text(encoding='utf-8')))
        return
    d,g=build()
    if '--build-only' not in sys.argv:validate_and_review(d,g)

def finalize(payload):
    import os
    env=dict(os.environ,PYTHONIOENCODING='utf-8',PYTHONDONTWRITEBYTECODE='1')
    command=[sys.executable,'-B',str(ROOT/'tools/iso_check.py'),'--tolerance','0',str(OUT)]
    result=subprocess.run(command,cwd=ROOT,capture_output=True,text=True,encoding='utf-8',env=env)
    out('validation-chair-v3.txt').write_text('Command: tools/iso_check.py --tolerance 0 assets/art/iso/furniture-v3/chair\n\n'+result.stdout+result.stderr+'\n'+json.dumps(payload['checks'],ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    assert result.returncode==0,result.stdout+result.stderr
    rr=[r for r in payload['records'] if r['stage']=='after']
    lines=['# Chair v3 검증 기록','',
           'v2 책상과 rev2-source/actor-inputs의 고정된 세 캐릭터를 합성했다. 최종 출력 파트와 실제 깊이 키를 정렬한 뒤 전체 화면의 픽셀 소유권을 계산했고, 인물 셀에서 별도로 계산한 가림 마스크와 일치하는지 확인했다. 캐릭터·책상 픽셀과 분모는 전후 동일하다.','',
           '| 인물 방향 | 저장된 착석·전환 프레임 | 머리 최저 가시성 | 의자에 가린 머리 최대 | 타이핑 손 | 방석이 골반·허벅지를 덮은 픽셀 |',
           '| --- | ---: | ---: | ---: | ---: | ---: |']
    for f in FACINGS:
        rows=[r for r in rr if r['worker']==f]
        lines.append(f"| {f.upper()} | {len(rows)} | {100*min(r['head']['fraction'] for r in rows):.2f}% | {max(r['head']['chairHidden'] for r in rows)} | 100% | 0 |")
    lines += ['',
              '타이핑·생각·앉기·일어나기·잠자기: 최종 216프레임, v2와 합쳐 432프레임. 타이핑 손은 rev2와 같은 피부색·접점 반경 2 ROI로 48프레임을 검사했다. 다른 착석 클립에는 손 접점이 없어 노출된 상체 피부 전체를 별도 ROI로 검사했고 가시성 감소가 없었다. 이를 손만의 100% 판정으로 바꾸어 보고하지 않는다.','',
              'NE/NW 머리 최저치는 기존 v2 책상의 가림에서 발생했다. 의자는 모든 216프레임에서 머리를 0px 가렸다. 팔걸이/프레임은 골반·허벅지 ROI를 NE 최대 1px, NW 최대 2px 덮으며, 방석 덮임은 전 방향 0px다.','',
              '보행은 의자 주위 8개 위치 × 4방향 × 4위상 × 3인물 × 4의자 방향 × 전후 = 3,072건이다. 의자와 보행자는 각자의 일반 지면 깊이를 사용했다. 의자보다 앞인 보행자 머리는 100% 보이고, 모든 사례에서 독립 가림 계산과 화면 소유권이 일치했다. 전환의 발바닥은 지면에 고정된다.','',
              '셀 128×128, 시트 512×256, 앵커 (64,80), 방석 꼭대기 z12/최대 두께 5px, 등판 z21, 팔걸이 z13. 투영된 실제/스냅된 구조 외곽 1,200개는 모두 2:1 또는 수직이다. 시트는 Teal 5색만 사용하며 알파는 0/255다. 그림자는 Navy #111924, 알파 0/48/96, cast offset (6,3), 실제 바퀴 접촉점을 사용한다.','',
              '체커: 16항목, 0실패. floor seam, 비가구 shadow의 part 이름, character face-lock/blink만 해당 없음으로 SKIP했다. 파트 마스크는 서로 겹치지 않으며 네 empty PNG를 RGBA까지 정확히 복원한다. 기존 파트 ID, 시트 ID, 위치, 이름, footprint, 변형, 앵커, 깊이 키와 seatGroupId를 보존했다.','',
              '픽셀 단위 결과·깊이 큐·입력 SHA-256은 measurements-chair-v3.json, 구조 외곽은 geometry-chair-v3.json, 체커 전문은 validation-chair-v3.txt에 있다. v2와 읽은 입력 파일의 SHA-256은 검증 전후 동일했다.','',
              '## 타이핑 프레임','',
              '| 인물 | 인물/책상 방향 | 위상 | 머리 | 손 | 골반·허벅지 의자 가림 |',
              '| --- | --- | ---: | ---: | ---: | ---: |']
    for r in rr:
        if r['action']=='seated-typing-front':
            lines.append(f"| {r['character']} | {r['worker'].upper()}/{r['desk'].upper()} | {r['phase']} | {r['head']['visible']}/{r['head']['total']} | {r['hands']['visible']}/{r['hands']['total']} | {r['pelvisThighs']['chairHidden']}px |")
    out('measurements-chair-v3.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    changes='''# Chair v3 인계

## S0 변경 없음

**정확한 S0 텍스트 diff는 빈 diff다. docs/iso-slice-s0.md를 수정하지 않았다.**

`P(i,j,z)=(32(i-j),16(i+j)-z)`, 128×128 셀, (64,80) 의자 앵커, 1×1 footprint, 방석 12, 등판 21, 착석 골반 (16,44), A/C/H, 방향/파트 이름/일반 지면 깊이/seatGroupId를 유지했다. `furniture-part-convention.md` 표의 예전 `back28`보다 현재 S0와 v2 manifest의 `chair back top 21`을 우선했다. 원본 문서나 다른 레인은 수정하지 않았다.

## 디자인과 겹침 수정

넓고 네모난 v2 방석을 계단식 둥근 윤곽과 5px 두께, 한 픽셀 솟은 중앙 면, waterfall 앞쪽 경사·재봉선으로 다시 그렸다. 배색은 동결 Teal 5색뿐이다. 낮은 메쉬 등판에 얇은 프레임과 요추 곡선을 넣고, 어두운 조절형 팔걸이·가스 리프트·5개 바퀴 접점을 그렸다. 헤드레스트와 로고는 없다.

방석 윗면과 앞쪽 립 전체는 인물 뒤쪽 rear 파트다. NE/NW 등판은 기존 규약대로 front이며, 방석과 겹치는 실제 접합부는 rear에 넣었다. 이 분할은 물체 부품의 접합면을 따른다. 인물별 투명 구멍이나 임의의 소스 y 띠를 사용하지 않았다. 파트의 -20/20 깊이 순서를 바꾸지 않았다.

`chair-parts.png`, `chair.iso.json`, 네 `chair-empty-*.png`, `chair-shadow.png`, `chair-shadow.iso.json`의 파일명과 파트 ID를 그대로 쓴다. atlas 배치와 variant는 v2와 같고, 물리 접점 `anchorChecks`와 제작 provenance만 갱신했다. pack이나 런타임 어댑터는 변경하지 않았다.

## 비교 자료

- `empty-before-after-chair-v3.png`: 네 방향 빈 의자, 1x/3x.
- `contact-before-after-chair-v3-1x.png`, `-3x.png`: v2 책상에서 세 파일럿의 전후 착석.
- `quality-v2-v3-hero-integrated-chair-v3.png`: v2/v3, hero 원본, 통합 렌더와 1x/3x 비교. 통합 렌더는 원래 2x이므로 nearest로 native 1x에 맞췄다.
- `all-seated-phases-chair-v3-1x.png`: 저장된 모든 착석·전환 216프레임.
- `transitions-chair-v3-1x.png`, `-3x.png`: 낮추기 4위상/시작·끝 확대.
- `walkers-chair-v3-1x.png`, `-3x.png`: 독립 지면 깊이로 정렬한 보행 겹침.

## 남은 약점

S0의 등판 21·방석 12를 유지해 뒤쪽에서 등판이 낮고 짧게 보인다. 1x에서는 세밀한 메쉬·곡면이 계단형 윤곽으로 읽히며 제품 사진처럼 매끄럽지 않다. 5개 별형 다리는 2:1/수직 구조 규칙에 맞춰 단순화했고, 작은 바퀴는 실제 이중 캐스터의 분리감이 약하다. NE/NW의 일부 기존 책상 가림과 팔걸이/프레임의 하체 1~2px 가림은 남는다. 방석에 의한 골반·허벅지 가림과 의자에 의한 머리 가림은 0px다.

이 검증은 고정된 actor snapshots와 v2 책상의 정적 합성이다. 최신 live 캐릭터 입력, WPF/Core 실제 렌더 순서·instance tie·연속 재생·DPI는 검증하지 않았다. 기본 팔레트와 접점의 자동 검증이 미술 품질이나 runtime 통합 통과를 대신하지 않는다.

## 마스터와 재현

내장 image generation을 1회 사용했다. `master-chair-v3-attempt1.png`는 형태/재질 참고용이며 최종 시트와 별도다. 이후 native geometry로 다시 투영해 두 시안을 검토하고 채택했다. 최종 프롬프트는 `generation-prompt-chair-v3.txt`에 보존했다. 생성 이미지는 다른 레인의 자산을 편집하거나 잘라 쓰지 않았다.

유일한 새 helper `chair-v3-build-review.py`는 모든 쓰기를 자기 폴더로 제한한다. 읽은 v2와 actor 입력의 SHA-256을 검사한다. 기존 Python/Pillow/numpy를 사용했으며 설치나 dotnet 테스트를 하지 않았다.

```powershell
& 'C:\\Users\\myori\\.agents\\document-toolchain\\runtime\\python\\Scripts\\python.exe' -B 'assets/art/iso/furniture-v3/chair/chair-v3-build-review.py'
& 'C:\\Users\\myori\\.agents\\document-toolchain\\runtime\\python\\Scripts\\python.exe' -B 'assets/art/iso/furniture-v3/chair/chair-v3-build-review.py' --finalize-only
```
'''
    out('CHANGES-chair-v3.md').write_text(changes,encoding='utf-8')
    print(result.stdout)
    print('Wrote CHANGES-chair-v3.md, measurements-chair-v3.md, validation-chair-v3.txt')

if __name__=='__main__':main()
