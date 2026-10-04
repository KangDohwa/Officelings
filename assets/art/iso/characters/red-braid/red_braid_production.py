"""Lane-local drawing normalization, motion derivation and v3 ownership review.
Run with Python -B. All writes are restricted to this file's directory.
"""
from pathlib import Path
from collections import Counter
import copy, hashlib, importlib.util, json, math, sys
import numpy as np
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True
OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[4]
F = ['se', 'sw', 'ne', 'nw']
CID = 'red-braid'
INK = '#321414'
BG = '#E7D1AC'
def rgb(c): return tuple(bytes.fromhex(c.lstrip('#')))
def rgba(c): return (*rgb(c),255)
def read(p): return json.loads(p.read_text(encoding='utf-8'))
def write(name,doc): (OUT/name).write_text(json.dumps(doc,indent=2)+'\n',encoding='utf-8')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def empty(): return Image.new('RGBA',(32,64))
def shift(im,x=0,y=0):
    out=Image.new('RGBA',im.size); out.alpha_composite(im,(x,y)); return out
def region(im,box):
    out=empty();out.paste(im.crop(box),box[:2]);return out
def opaque(im): return np.array(im)[:,:,3]>0
def clean(im):
    a=np.array(im);a[:,0]=0;a[61:]=0;a[a[:,:,3]==0]=0
    # Remove isolated one/two-texel cutout residue; keep connected outlines.
    m=a[:,:,3]>0;seen=np.zeros_like(m)
    for y,x in zip(*np.where(m)):
        if seen[y,x]:continue
        stack=[(y,x)];seen[y,x]=True;group=[]
        while stack:
            ay,ax=stack.pop();group.append((ay,ax))
            for dy,dx in [(-1,-1),(-1,0),(-1,1),(0,-1),(0,1),(1,-1),(1,0),(1,1)]:
                by,bx=ay+dy,ax+dx
                if 0<=by<64 and 0<=bx<32 and m[by,bx] and not seen[by,bx]:seen[by,bx]=True;stack.append((by,bx))
        if len(group)<=2:
            for ay,ax in group:a[ay,ax]=0
    return Image.fromarray(a)
def cell(im,r,p=0): return im.crop((p*32,r*64,(p+1)*32,(r+1)*64))
def pair(im,name):
    im.save(OUT/(name+'.png'))
    im.resize((im.width*3,im.height*3),Image.Resampling.NEAREST).save(OUT/(name+'-3x.png'))

def slots(path,cols,rows):
    im=Image.open(path).convert('RGBA');results=[];boxes=[]
    # Find transparent row gaps inside each column instead of slicing a shoe/head
    # at the nominal sheet midpoint. These masters have no drawn grid.
    column_ranges=[]
    for c in range(cols):
        a=np.array(im)[:,c*im.width//cols:(c+1)*im.width//cols,3]
        ys=np.where((a>=160).sum(1)>8)[0]
        gaps=np.where(np.diff(ys)>3)[0]
        runs=np.split(ys,gaps+1)
        runs=sorted(runs,key=len,reverse=True)[:rows]
        column_ranges.append(sorted((int(run[0]),int(run[-1])+1) for run in runs))
    for r in range(rows):
        for c in range(cols):
            ya,yb=column_ranges[c][r]
            origin=(c*im.width//cols,ya)
            s=im.crop((*origin,(c+1)*im.width//cols,yb))
            a=np.array(s);a[a[:,:,3]<160]=0;a[a[:,:,3]>=160,3]=255
            s=Image.fromarray(a);box=s.getbbox();assert box
            results.append(s.crop(box));boxes.append([origin[0]+box[0],origin[1]+box[1],origin[0]+box[2],origin[1]+box[3]])
    return results,boxes

def normalize():
    locked=Image.open(ROOT/'assets/art/roster/red-braid/locked/red-braid.png').convert('RGBA')
    arr=np.array(locked);colors=np.unique(arr[:,:,:3][arr[:,:,3]>0],axis=0)
    base=np.array(Image.open(ROOT/'assets/art/roster/red-braid/base.png').convert('RGB'))
    patch=base[320:740,240:780,:].astype(np.int32)
    g=(patch[:,:,1]>patch[:,:,0]+15)&(patch[:,:,1]>patch[:,:,2]+5)&(patch[:,:,1]>65)&(patch[:,:,1]<160)
    green=Counter(tuple(map(int,c)) for c in patch[g]).most_common(2)
    assert green, 'Original green eyes not found'
    colors=np.unique(np.vstack([colors,[c for c,n in green]]),axis=0)
    palette=colors.astype(np.int32)
    write('identity-palette.json',['#'+bytes(c).hex().upper() for c in colors.astype(np.uint8)])
    write('palette-source.json',{'locked':'assets/art/roster/red-braid/locked/red-braid.png','greenEyeReference':'assets/art/roster/red-braid/base.png','baseCrop':[240,320,780,740],'greenSamples':[{'rgb':list(c),'count':n} for c,n in green]})
    proof=[];board=Image.new('RGBA',(1170,780),BG);d=ImageDraw.Draw(board)
    for kind,cols,rows,height,sole in [('idle',2,2,40,60),('walk',4,2,40,60),('seat',2,2,36,56)]:
        src,boxes=slots(OUT/f'master-{kind}-a1.png',cols,rows)
        common=min(height/max(im.height for im in src),(28 if kind=='walk' else 29)/max(im.width for im in src))
        for k,im in enumerate(src):
            factor=40/im.height if kind=='idle' else common
            w,h=round(im.width*factor),round(im.height*factor)
            out=empty();out.paste(im.resize((w,h),Image.Resampling.NEAREST),((32-w)//2,sole+1-h))
            a=np.array(out);mask=a[:,:,3]>0
            source=a[:,:,:3][mask].astype(np.int32)
            distance=((source[:,None,:]-palette[None,:,:])**2).sum(2)
            a[:,:,:3][mask]=palette[distance.argmin(1)]
            out=clean(Image.fromarray(a));f=F[k] if kind!='walk' else ('se' if k<4 else 'ne')
            name=f'{kind}-{f}'+(f'-{k%4}' if kind=='walk' else '')
            out.save(OUT/(name+'-native.png'))
            proof.append({'name':name,'sourceBox':boxes[k],'scale':factor,'nativeSize':[w,h],'box':out.getbbox()})
            x=(k%8)*143;y={'idle':0,'walk':260,'seat':520}[kind]
            d.text((x+4,y+2),name,fill=INK);board.alpha_composite(out,(x+2,y+38))
            board.alpha_composite(out.resize((96,192),Image.Resampling.NEAREST),(x+42,y+20))
    board.save(OUT/'normalization-review.png');write('normalization-proof.json',proof)
    print(json.dumps(proof));print('Green samples:',green)

SKIN = '#FBC68D'
SHADE = '#F39E62'
CREAM = '#F6DEB8'
DENIM = '#354F79'
DARKDENIM = '#283959'
BOOT = '#681F15'
HANDS = {'se':[(27,44),(29,44)],'sw':[(3,44),(5,44)],'ne':[(27,32),(29,32)],'nw':[(3,32),(5,32)]}
GRIP = {'se':(24,44),'sw':(8,44),'ne':(24,40),'nw':(8,40)}
EYES = {'se':[(14,32,17,36),(19,32,22,35)],'sw':[(9,32,12,35),(15,33,18,36)]}

def palm(im,point,phase=0):
    x,y=point;d=ImageDraw.Draw(im)
    d.rectangle((x-1,y-1,x+1,y+1),fill=rgba(INK))
    d.rectangle((x-1,y-1,x,y),fill=rgba(SKIN))
    d.point((x+1,y),fill=rgba(SHADE));d.point((x,y+1),fill=rgba(SKIN))
    d.point((x-1,y-1 if phase%2==0 else y),fill=rgba(CREAM))
    d.point((x,y),fill=rgba(SKIN))

def arm(im,start,elbow,hand,near=True,phase=0):
    d=ImageDraw.Draw(im)
    d.line([start,elbow,hand],fill=rgba(INK),width=4)
    d.line([start,elbow],fill=rgba(CREAM if near else SHADE),width=2)
    d.point((start[0],start[1]+1),fill=rgba('#B83D19'))
    d.line([elbow,hand],fill=rgba(SKIN if near else SHADE),width=2)
    d.point((elbow[0]-1,elbow[1]),fill=rgba(SKIN))
    palm(im,hand,phase)

def leg(im,hip,knee,foot,near):
    d=ImageDraw.Draw(im);color=DENIM if near else DARKDENIM
    d.line([hip,knee,(foot[0],foot[1]-2)],fill=rgba(INK),width=5)
    d.line([hip,knee,(foot[0],foot[1]-2)],fill=rgba(color),width=3)
    d.point((knee[0]-1,knee[1]),fill=rgba('#415B85'))
    d.polygon([(foot[0]-2,foot[1]-2),(foot[0]+1,foot[1]-2),(foot[0]+3,foot[1]-1),(foot[0]+2,foot[1]),(foot[0]-2,foot[1])],fill=rgba(INK))
    d.rectangle((foot[0]-1,foot[1]-2,foot[0]+1,foot[1]-1),fill=rgba(BOOT))
    d.point((foot[0]-1,foot[1]-2),fill=rgba('#B83D19'))

def reflect_body(im):
    im=shift(im.transpose(Image.Transpose.FLIP_LEFT_RIGHT),1)
    a=np.array(im);old=a.copy();yy,xx=np.indices(a.shape[:2])
    for lighter,darker in [('#415B85','#354F79'),('#FBC68D','#F39E62')]:
        m=(old[:,:,:3]==rgb(lighter)).all(2)&(xx>18)&(old[:,:,3]>0)
        a[m,:3]=rgb(darker)
        m=(old[:,:,:3]==rgb(darker)).all(2)&(xx<13)&(old[:,:,3]>0)
        a[m,:3]=rgb(lighter)
    return Image.fromarray(a)

def prepare_rigs():
    heads={};braids={};idles={};seats={}
    greens=read(OUT/'palette-source.json')['greenSamples']
    green=tuple(greens[0]['rgb'])+(255,)
    for f in F:
        raw=Image.open(OUT/f'idle-{f}-native.png').convert('RGBA')
        head=region(raw,(0,0,32,40))
        if f in EYES:
            d=ImageDraw.Draw(head)
            for n,(x0,y0,x1,y1) in enumerate(EYES[f]):
                # Eye-only native repairs: large two-row iris, one ivory catchlight.
                top=y0+1 if f=='se' and n==0 else y0
                d.rectangle((x0,top,x1-1,y1-1),fill=rgba(SKIN))
                d.line([(x0,top),(x1-1,top)],fill=rgba(INK))
                d.point((x0+1,top),fill=rgba(CREAM))
                d.point((x0+1,top+1),fill=rgba(INK))
                d.point((x1-1,top+1),fill=green)
                d.point((x0+1,y1-1),fill=green)
            # Two distinct freckles below the eyes, outside the blink mask.
            d.point((17 if f=='se' else 12,36),fill=rgba('#A93D2C'))
            d.point((20 if f=='se' else 16,36),fill=rgba('#A93D2C'))
        head.save(OUT/f'head-{f}.png');heads[f]=head
        a=np.array(raw);yy,xx=np.indices(a.shape[:2]);r,g,b=a[:,:,0].astype(int),a[:,:,1].astype(int),a[:,:,2].astype(int)
        hair=(r>90)&(r>g*1.35)&(b<90)
        ribbon=(a[:,:,:3]==rgb(CREAM)).all(2)
        side=(xx<=14) if f in ('se','sw') else (xx>=18)
        keep=(yy>=40)&(yy<=48)&side&(hair|ribbon)&(a[:,:,3]>0)
        a[~keep]=0;braid=Image.fromarray(a);braid.save(OUT/f'braid-{f}.png');braids[f]=braid
        body=region(raw,(0,40,32,61));body.alpha_composite(head)
        # Keep the generated neutral pose, reconnect a practical standing grip.
        h=GRIP[f]
        arm(body,(21,40) if h[0]>16 else (11,40),(23,42) if h[0]>16 else (9,42),h,True)
        body.alpha_composite(head);body.alpha_composite(braid)
        idles[f]=clean(body)
        source=Image.open(OUT/f'seat-{f}-native.png').convert('RGBA')
        base=region(source,(0,45,32,57))
        base.alpha_composite(region(raw,(11,40,22,46)))
        d=ImageDraw.Draw(base);d.rectangle((13,43,20,47),fill=rgba(DARKDENIM));d.line([(13,44),(19,44)],fill=rgba(DENIM),width=2)
        base.alpha_composite(head);base.alpha_composite(braid)
        seats[f]=base
    return heads,braids,idles,seats

def frame(f,name,p,rigs):
    heads,braids,idles,seats=rigs;head=heads[f];braid=braids[f];hy=0
    if name=='idle':return idles[f].copy(),0,None
    if name=='walk':
        basef='se' if f in ('se','sw') else 'ne'
        source=Image.open(OUT/f'walk-{basef}-{p}-native.png').convert('RGBA')
        bob=[0,1,0,-1][p];hy=1 if p==1 else 0
        body=shift(region(source,(11,40,22,51)),y=bob)
        # Four non-rigid native joint repairs; the generated overalls supply the cloth clusters.
        farfeet=[(10,58),(14,57),(23,60),(16,56)]
        nearfeet=[(24,60),(20,60),(12,57),(18,60)]
        farknees=[(12,53),(14,54),(20,54),(17,51)]
        nearknees=[(21,54),(19,54),(17,51),(19,54)]
        leg(body,(14,48+bob),farknees[p],farfeet[p],False)
        leg(body,(19,47+bob),nearknees[p],nearfeet[p],True)
        left=[(13,46),(12,46),(7,45),(9,44)][p]
        right=[(20,48),(22,47),(26,45),(25,43)][p]
        arm(body,(11,40+bob),(10,43+bob),left,False,p)
        arm(body,(21,40+bob),(23,43+bob),right,True,p)
        if f in ('sw','nw'):body=reflect_body(body)
        body.alpha_composite(shift(head,y=hy));body.alpha_composite(shift(braid,y=hy))
        return clean(body),hy,None
    if name in ('sit-down','stand-up'):
        q=p if name=='sit-down' else 3-p
        if q==0:return idles[f].copy(),0,None
        if q==3:return frame(f,'seated-thinking',0,rigs)
        # Preserve shoe world-ground through frozen source footY 60/58.
        body=seats[f].copy() if q==2 else idles[f].copy()
        feet=region(body,(0,48,32,57 if q==2 else 61))
        if q==2:feet=shift(feet,y=2)
        body=region(body,(0,0,32,48));body.alpha_composite(feet)
        d=ImageDraw.Draw(body);d.rectangle((14,41,19,48),fill=rgba(DARKDENIM))
        if q==2:
            knee=(19,52) if f in ('se','ne') else (13,52)
            d.line([(16,46),knee],fill=rgba(INK),width=5)
            d.line([(16,46),knee],fill=rgba(DARKDENIM),width=3)
            d.point((15,48),fill=rgba(DENIM))
        body.alpha_composite(head);body.alpha_composite(braid)
        return clean(body),0,None
    seated=name in ('seated-typing-front','seated-thinking','sleep')
    if seated:
        body=seats[f].copy();a=np.array(body)
        yy,xx=np.indices(a.shape[:2]);a[(yy>=40)&(yy<45)&((xx<11)|(xx>21))]=0
        body=Image.fromarray(a)
        if name=='seated-typing-front':
            left,right=HANDS[f]
            left=(left[0],left[1]+[0,-1,0,0][p]);right=(right[0],right[1]+[0,0,-1,1][p])
            if f in ('se','ne'):
                arm(body,(11,40),(19,43) if f=='se' else (21,38),left,False,p)
                arm(body,(21,40),(25,43) if f=='se' else (25,36),right,True,p)
            else:
                arm(body,(21,40),(13,43) if f=='sw' else (11,38),right,False,p)
                arm(body,(11,40),(7,43) if f=='sw' else (7,36),left,True,p)
        else:
            left=(13,46);right=[(22,45),(23,42),(24,40),(22,45)][p]
            if f in ('sw','nw'):left,right=(32-right[0],right[1]),(32-left[0],left[1])
            arm(body,(11,40),(11,44),left,False,p);arm(body,(21,40),(23,43),right,True,p)
        if name=='sleep':
            legs=region(body,(0,47,32,57));a=np.array(body);a[47:]=0;body=Image.fromarray(a)
            body.alpha_composite(shift(legs,x=1 if f=='ne' else -1,y=-1))
            ImageDraw.Draw(body).rectangle((14,45,19,48),fill=rgba(DARKDENIM))
        body.alpha_composite(head);body.alpha_composite(braid)
        return clean(body),0,None
    # Stored front-facing action motion, separate runtime props and bubbles.
    body=region(idles[f],(11,40,22,61))
    body.alpha_composite(region(idles[f],(0,49,32,61)))
    grip=[GRIP[f]]*4;left=(10,46);right=GRIP[f]
    if name=='pickup':grip=[(24,44),(26,47),(26,50),(24,44)]
    if name=='report-handover':grip=[(24,44),(27,43),(28,42),(27,43)]
    if name=='coffee':grip=[(24,44),(26,41),(26,37),(24,44)]
    if name=='phone':
        # Anatomical RIGHT is screen-left in both independently drawn front heads.
        grip=[(8,44),(7,40),(6,36),(6,36)]
        left=grip[p];right=GRIP[f]
    elif name in ('pickup','report-handover','coffee'):
        if f=='sw':grip=[(32-x,y) for x,y in grip]
        right=grip[p]
    elif name=='talk':right=[(24,44),(26,42),(27,39),(24,44)][p]
    elif name=='surprised':
        left=[(10,46),(7,42),(6,40),(8,43)][p];right=[(24,44),(27,40),(27,38),(25,42)][p]
    if f=='sw' and name not in ('pickup','report-handover','coffee','phone'):
        left,right=(32-right[0],right[1]),(32-left[0],left[1])
    arm(body,(11,40),(9,43),left,False,p);arm(body,(21,40),(23,42),right,True,p)
    body.alpha_composite(head);body.alpha_composite(braid)
    return clean(body),0,grip[p] if name in ('pickup','report-handover','coffee','phone') else None

def build():
    rigs=prepare_rigs()
    template=read(ROOT/'assets/art/iso/characters/dev-hoodie-v2/dev-hoodie.iso.json')
    def renamed(v):
        if isinstance(v,str):return v.replace('dev-hoodie-v2',CID).replace('dev-hoodie',CID)
        if isinstance(v,list):return [renamed(x) for x in v]
        if isinstance(v,dict):return {k:renamed(x) for k,x in v.items()}
        return v
    doc=renamed(template);normal,blink=doc['sheets'];normal['file']=CID+'-iso.png'
    for s in doc['sheets']:s['identityPalette']=read(OUT/'identity-palette.json')
    normal['headOffsetsByClip']={};atlas=Image.new('RGBA',(128,2432))
    for clip in normal['rows']:
        name,f,r=clip['name'],clip['direction'],clip['row']
        for key in ['mirrorOf','sourceMaster','handRepair','motionRepair','handAnchorsByPhase']:clip.pop(key,None)
        clip['sourceMaster']='master-'+('walk' if name=='walk' else 'seat' if name in ('seated-typing-front','seated-thinking','sleep') else 'idle')+'-a1.png'
        if name=='walk' and f in ('sw','nw'):
            clip['bodyDerivation']={'facing':'se' if f=='sw' else 'ne','reflection':'x=32-x','head':'independently drawn','braid':'anatomical RIGHT separately restored','lighting':'upper-left native correction'}
        offsets=[];grips=[]
        for p in range(4):
            im,hy,grip=frame(f,name,p,rigs);atlas.paste(im,(p*32,r*64));offsets.append({'x':0,'y':hy})
            if grip:grips.append([{'x':grip[0],'y':grip[1]}])
        normal['headOffsetsByClip'][name+'/'+f]=offsets
        if grips:clip['handAnchorsByPhase']=grips
    atlas.save(OUT/(CID+'-iso.png'))
    doc['production']={'method':'three serial built-in image generations: four independent idle facings, eight walking keyposes, four independent seated facings; fixed native normalization and extensive joint/contact repairs','stage':'final','standingReferenceHeight':40,'sourceIdentity':'assets/art/roster/red-braid/locked/red-braid.png','eyeColorSource':'assets/art/roster/red-braid/base.png','headLayers':['head-'+f+'.png' for f in F],'headOffsets':'walk 0,+1,0,0; head stabilizes gaze on up/contact; torso bob 0,+1,0,-1; all other actions 0; no phase fitting or recentering','braid':'anatomical RIGHT; independent facing layers; no head or braid mirror','seatProfiles':['front'],'rejectedSeatProfiles':['end'],'light':'upper-left','walkPhases':['contact','down','passing','up'],'visualAcceptance':'static review only; runtime playback and independent human acceptance not verified'}
    write(CID+'.iso.json',doc);write(CID+'-iso.manifest.json',doc)
    print('Published 38 rows / 152 normal cells. Re-derive blink next.')

def project(i,j):return math.floor(32*(i-j)+.5),math.floor(16*(i+j)+.5)
def point(p):return p['x'],p['y']
def snapshot_furniture():
    target=OUT/'v3-inputs';target.mkdir(exist_ok=True);hashes={}
    if (OUT/'v3-input-hashes.json').exists():return target
    for family,folder in [('desk','workstations'),('chair','chair')]:
        root=ROOT/'assets/art/iso/furniture-v3'/folder
        doc=read(root/(family+'.iso.json'))
        files=[root/(family+'.iso.json')]+[root/s['file'] for s in doc['sheets']]
        for path in files:
            hashes[path.relative_to(ROOT).as_posix()]=sha(path)
            dest=target/(family+'.json' if path.suffix=='.json' else path.name)
            dest.write_bytes(path.read_bytes())
    write('v3-input-hashes.json',hashes)
    return target
def family(root,name):
    d=read(root/(name+'.json'));return d['furniture'][0],{s['id']:(s,Image.open(root/s['file']).convert('RGBA')) for s in d['sheets']}
def variant(d,f):return next(v for v in d['variants'] if v['orientation']==f and v['state']=='default')
def parts(v,sheets,origin,ground_depth,seat_depth,owner):
    # Same seat-group and ordinary depth queue as v3 verify-desk-rig-v3-lane.py.
    items=[]
    for part in v['parts']:
        s,a=sheets[part['sheetId']]
        c=next(c for c in s['rows'] if c['name']==part['animation'] and c['direction']==part['direction'])
        assert all(part[k]==c[k] for k in ('partId','depthAnchor','isoZOrder'))
        depth=seat_depth if part.get('seatGroupId') else ground_depth+sum(part['depthAnchor'].values())
        anchor=point(c.get('anchor',s['anchor']));w,h=s['cellW'],s['cellH'];x,y=c.get('column',0)*w,c['row']*h
        im=a.crop((x,y,x+w,y+h))
        items.append({'key':(depth,part['isoZOrder'],owner,part['partId']),'id':part['partId'],'image':im,'pos':(origin[0]-anchor[0],origin[1]-anchor[1])})
    if v.get('screen'):
        desc=v['screen'];s,a=sheets[desc['sheetId']]
        c=next(c for c in s['rows'] if c['name']==desc['animation'] and c['direction']==desc['direction'])
        depth=ground_depth+sum(c['depthAnchor'].values());anchor=point(c.get('anchor',s['anchor']))
        w,h=s['cellW'],s['cellH'];x,y=c.get('column',0)*w,c['row']*h
        items.append({'key':(depth,c['isoZOrder'],owner,c['partId']),'id':c['partId'],'image':a.crop((x,y,x+w,y+h)),'pos':(origin[0]-anchor[0],origin[1]-anchor[1])})
    return items
def local_mask(mask,item,actor_pos):
    im=Image.fromarray(mask.astype(np.uint8)*255);out=Image.new('L',(32,64))
    out.paste(im,(item['pos'][0]-actor_pos[0],item['pos'][1]-actor_pos[1]));return np.array(out)>0

def seated_review():
    root=snapshot_furniture();desk,ds=family(root,'desk');chair,cs=family(root,'chair')
    doc=read(OUT/(CID+'.iso.json'));s=doc['sheets'][0];a=Image.open(OUT/s['file']).convert('RGBA')
    records=[];queues=[];board=Image.new('RGBA',(1536,1030),BG);draw=ImageDraw.Draw(board)
    allphases=Image.new('RGBA',(768,616),BG);ad=ImageDraw.Draw(allphases)
    yy,xx=np.indices((64,32));skinc=[rgb(c) for c in ['#FBC68D','#F39E62','#FAC086','#F6B47A','#FCC287','#F6BE86','#F4BE8F','#F6B47A','#F6DEB8']]
    for di,df in enumerate(F):
        v=variant(desk,df);profile=v['seatProfiles'][0];f=profile['isoFacing'];sg=profile['seatGround'];sd=sum(sg.values());origin=(160,128)
        gp=project(sg['i'],sg['j']);chair_origin=(origin[0]+gp[0],origin[1]+gp[1]);cv=variant(chair,f)
        items=parts(v,ds,origin,0,sd,'desk')+parts(cv,cs,chair_origin,sd,sd,'chair')
        clip=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==f)
        co=point(profile['contactOffset']);pos=(origin[0]+co[0]-16,origin[1]+co[1]-44);key=(sd,0,'actor',CID)
        expected=[(origin[0]+h['x'],origin[1]+h['y']) for h in profile['handContacts']]
        actual=[(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]
        assert expected==actual,'C/H drift'
        head=Image.open(OUT/f'head-{f}.png').convert('RGBA')
        queue=sorted(items+[{'id':'actor','key':key,'pos':pos}],key=lambda i:i['key'])
        queues.append({'desk':df,'worker':f,'items':[{'id':i['id'],'key':i['key'],'destination':i['pos']} for i in queue]})
        for p in range(4):
            sprite=cell(a,clip['row'],p);off=point(s['headOffsetsByClip']['seated-typing-front/'+f][p]);hm=opaque(shift(head,*off));sa=np.array(sprite);op=opaque(sprite)
            assert np.all(op[hm]);skin=np.zeros((64,32),bool)
            for c in skinc:skin|=(sa[:,:,:3]==c).all(2)&op
            hands=np.zeros_like(op);dist=[]
            for h in clip['handAnchors']:
                radius=np.maximum(abs(xx-h['x']),abs(yy-h['y']));hands|=(radius<=2)&skin&~hm
                dist.append(int(radius[skin&~hm].min()))
            torso=op&~hm&~hands&(xx>=11)&(xx<=22)&(yy<44)
            regions={'head':hm,'torso':torso,'hands':hands}
            scene=Image.new('RGBA',(320,256),BG);labels=Image.new('L',scene.size);cover=np.zeros((64,32),bool);occluders={}
            layers=sorted(items+[{'id':'actor','key':key,'pos':pos,'image':sprite}],key=lambda i:i['key'])
            for i in layers:
                scene.alpha_composite(i['image'],i['pos']);labels.paste(1 if i['id']=='actor' else 2,i['pos'],i['image'].getchannel('A'))
                if i['id']!='actor' and i['key']>key:
                    mask=local_mask(opaque(i['image']),i,pos);cover|=mask;occluders[i['id']]=mask
            owned=np.array(labels.crop((*pos,pos[0]+32,pos[1]+64)))==1
            assert np.array_equal(owned&op,~cover&op)
            result={}
            for name,mask in regions.items():
                total=int(mask.sum());visible=int((mask&owned).sum());assert total>0
                assert visible==int((mask&~cover).sum())
                result[name]={'visible':visible,'total':total,'fraction':visible/total,'coveringParts':{name:int((pm&mask).sum()) for name,pm in occluders.items() if (pm&mask).any()}}
            records.append({'desk':df,'worker':f,'phase':p,'actorDestination':pos,'handDistances':dist,'contactsErrorPx':0,**result})
            crop=scene.crop((64,32,256,176));allphases.alpha_composite(crop,(di*192,40+p*144))
            ad.text((di*192+3,40+p*144+3),f'{df.upper()} / {f.upper()} phase {p}',fill=INK)
            if p==0:
                bx=(di%2)*768;by=(di//2)*505
                draw.text((bx+4,by+3),f'Desk {df.upper()} / worker {f.upper()} / native1x and nearest3x',fill=INK)
                board.alpha_composite(crop,(bx,by+30));board.alpha_composite(crop.resize((576,432),Image.Resampling.NEAREST),(bx+192,by+30))
                text='  '.join(f'{n} {r["visible"]}/{r["total"]}={r["fraction"]*100:.1f}%' for n,r in result.items())
                draw.text((bx+4,by+475),text,fill=INK)
    board.save(OUT/'iso-seated-review.png');allphases.save(OUT/'iso-seated-all-phases-1x.png')
    write('seated-depth-queues.json',queues)
    sourcehashes=read(OUT/'v3-input-hashes.json')
    unchanged=all(sha(ROOT/p)==h for p,h in sourcehashes.items())
    summary={f:{n:{'minimumPct':round(min(r[n]['fraction'] for r in records if r['worker']==f)*100,3),'maximumPct':round(max(r[n]['fraction'] for r in records if r['worker']==f)*100,3),'counts':[[r[n]['visible'],r[n]['total']] for r in records if r['worker']==f]} for n in ['head','torso','hands']} for f in F}
    proof={'method':'v3 verify-desk-rig-v3-lane.py parts queue; independent alpha cover and serialized final ownership agree','roi':{'head':'Exact independent canonical head layer alpha at headOffsetsByClip; y<40, including outline and face; braid extension is separately preserved below head. No denominator shrink against furniture.','torso':'Opaque actor excluding canonical head and hands, source x11..22 inclusive and y<44; identical central torso proxy to measure-A3-rev2.py.','hands':'Opaque skin/ivory hand pixels outside head within Chebyshev radius2 of either frozen contact; same contact ROI approach as measure-A3-rev2.py.'},'inputHashes':sourcehashes,'originalInputsUnchangedAtEnd':unchanged,'records':records,'summary':summary,'headGates':{f:summary[f]['head']['minimumPct']>=(85 if f in ('ne','nw') else 90) for f in F},'handsFullyVisible':all(r['hands']['fraction']==1 for r in records)}
    write('seated-visibility.json',proof);print(json.dumps({'minimumVisibilityPct':{f:{n:summary[f][n]['minimumPct'] for n in ['head','torso','hands']} for f in F},'headGates':proof['headGates'],'handsFullyVisible':proof['handsFullyVisible'],'inputsUnchanged':unchanged}))

def reviews():
    doc=read(OUT/(CID+'.iso.json'));s=doc['sheets'][0];a=Image.open(OUT/s['file']).convert('RGBA');b=Image.open(OUT/doc['sheets'][1]['file']).convert('RGBA');br={r:k for k,r in enumerate(s['blink']['normalRows'])}
    # Every normal and blink phase is shown at 1x and exact nearest3x.
    pages=[]
    for page,start in enumerate(range(0,38,10),1):
        end=min(start+10,38);board=Image.new('RGBA',(1240,40+(end-start)*200),BG);d=ImageDraw.Draw(board)
        d.text((5,5),'NORMAL: 1x / 3x | BLINK: 1x / 3x | every stored phase; back views have no blink',fill=INK)
        for row in range(start,end):
            y=40+(row-start)*200;c=s['rows'][row];d.text((4,y+3),f'{row:02d} {c["name"]}\n{c["direction"]}',fill=INK)
            strip=a.crop((0,row*64,128,(row+1)*64));board.alpha_composite(strip,(170,y));board.alpha_composite(strip.resize((384,192),Image.Resampling.NEAREST),(300,y))
            if row in br:
                strip=b.crop((0,br[row]*64,128,(br[row]+1)*64));board.alpha_composite(strip,(720,y));board.alpha_composite(strip.resize((384,192),Image.Resampling.NEAREST),(850,y))
        board.save(OUT/f'rows-review-page-{page}.png');pages.append(board)
    full=Image.new('RGBA',(2480,max(p.height for p in pages)*2),BG)
    for i,p in enumerate(pages):full.alpha_composite(p,((i%2)*1240,(i//2)*2040))
    full.save(OUT/'iso-rows-review.png')
    contact=Image.new('RGBA',(1300,940),BG);d=ImageDraw.Draw(contact)
    d.text((6,4),'RED-BRAID / IDLE + CONTACT, DOWN, PASSING, UP / native1x + nearest3x / pilots + hero',fill=INK)
    for k,f in enumerate(F):
        y=35+k*220;d.text((4,y+5),f.upper(),fill=INK)
        poses=[cell(a,k)]+[cell(a,4+k,p) for p in range(4)]
        for p,im in enumerate(poses):
            x=35+p*145;contact.alpha_composite(im,(x,y));contact.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+34,y))
        for j,(folder,name) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
            im=cell(Image.open(ROOT/f'assets/art/iso/characters/{folder}/{name}-iso.png'),k)
            contact.alpha_composite(im,(800+j*140,y));contact.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(835+j*140,y));d.text((800+j*140,y+198),name,fill=INK)
    contact.save(OUT/'iso-contact.png')
    comparison=Image.new('RGBA',(1440,950),BG);d=ImageDraw.Draw(comparison);flat=Image.open(ROOT/'assets/art/roster/red-braid/locked/red-braid.png').convert('RGBA');hero=Image.open(ROOT/'assets/art/iso-concept/iso-office-hero.png').convert('RGBA')
    d.text((5,5),'LOCKED FLAT / ISO40px / SEATED / CLOSED EYES / THREE PILOTS / HERO crop: original + nearest3x',fill=INK)
    for k,f in enumerate(F):
        y=35+k*222
        original=flat.crop(([2,1,3,3][k]*32,0,[2,1,3,3][k]*32+32,64))
        images=[original,cell(a,k),cell(a,8+k),cell(b,k) if k<2 else cell(a,8+k)]
        for j,im in enumerate(images):
            x=10+j*155;comparison.alpha_composite(im,(x,y));comparison.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+34,y))
        for j,(folder,name) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
            im=cell(Image.open(ROOT/f'assets/art/iso/characters/{folder}/{name}-iso.png'),k);x=660+j*140
            comparison.alpha_composite(im,(x,y));comparison.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+34,y))
        crop=hero.crop((685,350,840,510)).resize((39,40),Image.Resampling.NEAREST)
        comparison.alpha_composite(crop,(1100,y+45));comparison.alpha_composite(crop.resize((117,120),Image.Resampling.NEAREST),(1150,y+35))
        d.text((10,y+198),f.upper()+'   flat    /   idle    /   seated    /   blink (back: no eyes)',fill=INK)
    comparison.save(OUT/'iso-vs-flat.png')
    export_walk_gifs(a)
    print('All reviews + four walk GIFs exported.')

def export_walk_gifs(a):
    gifproof={}
    arr=np.array(a);palette=np.unique(arr[:,:,:3][arr[:,:,3]>0],axis=0)
    assert len(palette)<255
    table=[0,0,0]+palette.ravel().tolist()+[0]*(768-3-3*len(palette))
    for k,f in enumerate(F):
        frames=[cell(a,4+k,p).resize((96,192),Image.Resampling.NEAREST) for p in range(4)]
        indexed=[]
        for frame in frames:
            src=np.array(frame);indices=np.zeros(src.shape[:2],np.uint8)
            for i,color in enumerate(palette,1):indices[(src[:,:,:3]==color).all(2)&(src[:,:,3]>0)]=i
            im=Image.fromarray(indices,'P');im.putpalette(table);im.info['transparency']=0;indexed.append(im)
        indexed[0].save(OUT/f'walk-{f}-3x.gif',save_all=True,append_images=indexed[1:],duration=[120,130,120,130],loop=0,disposal=2,transparency=0,optimize=False)
        strip=Image.new('RGBA',(384,192))
        for p,im in enumerate(frames):strip.paste(im,(96*p,0))
        strip.save(OUT/f'walk-{f}-3x.png')
        gifproof[f]={'frames':4,'size':[96,192],'durationsMs':[120,130,120,130],'loopMs':500,'fpsEquivalent':8,'actualPlaybackVerified':False}
    write('gif-proof.json',gifproof)

def gifs():
    export_walk_gifs(Image.open(OUT/(CID+'-iso.png')).convert('RGBA'))
    print('Four walk GIFs rebuilt with explicit shared palette and reserved transparency index0.')

def verify():
    import subprocess,os
    doc=read(OUT/(CID+'.iso.json'));s=doc['sheets'][0];a=Image.open(OUT/s['file']).convert('RGBA')
    assert (OUT/(CID+'.iso.json')).read_bytes()==(OUT/(CID+'-iso.manifest.json')).read_bytes()
    heads={f:Image.open(OUT/f'head-{f}.png').convert('RGBA') for f in F}
    headchecks=0;connectedchecks=0;grips=[];distinct={};loops={};standing={}
    skincolors=[rgb(c) for c in [SKIN,SHADE,CREAM,'#F6B47A','#FAC086','#F6BE86','#FCC287']]
    yy,xx=np.indices((64,32))
    for c in s['rows']:
        f,r,name=c['direction'],c['row'],c['name']
        cells=[]
        for p in range(4):
            im=cell(a,r,p);arr=np.array(im);cells.append(arr)
            off=point(s['headOffsetsByClip'][name+'/'+f][p]);assert all(abs(v)<=1 for v in off)
            h=shift(heads[f],*off);mask=opaque(h)
            assert np.array_equal(arr[mask],np.array(h)[mask]),('head changed',r,p)
            headchecks+=1
            m=arr[:,:,3]>0;seed=tuple(np.argwhere(m)[0]);seen=np.zeros_like(m);seen[seed]=True;stack=[seed]
            while stack:
                ay,ax=stack.pop()
                for dy,dx in [(-1,-1),(-1,0),(-1,1),(0,-1),(0,1),(1,-1),(1,0),(1,1)]:
                    by,bx=ay+dy,ax+dx
                    if 0<=by<64 and 0<=bx<32 and m[by,bx] and not seen[by,bx]:seen[by,bx]=True;stack.append((by,bx))
            assert np.array_equal(m,seen),('disconnected body',r,p,int((m&~seen).sum()))
            connectedchecks+=1
            anchors=([{'x':x,'y':y} for x,y in HANDS[f]] if name=='seated-typing-front' else c.get('handAnchorsByPhase',[[]]*4)[p])
            if name=='idle':anchors=[{'x':GRIP[f][0],'y':GRIP[f][1]}]
            skin=np.zeros_like(m)
            for col in skincolors:skin|=(arr[:,:,:3]==col).all(2)&m&~mask
            for hand in anchors:
                radius=np.maximum(abs(xx-hand['x']),abs(yy-hand['y']));distance=int(radius[skin].min());assert distance<=1,('skin grip absent',r,p,hand,distance)
                grips.append({'row':r,'phase':p,'hand':hand,'skinDistance':distance})
        if name in ('walk','seated-typing-front'):
            count=len({im.tobytes() for im in cells});assert count==4;distinct[name+'/'+f]=count
        if name=='walk':
            changes=[int(np.any(cells[p]!=cells[(p+1)%4],axis=2).sum()) for p in range(4)]
            loops[f]={'adjacentChangesIncluding3to0':changes,'wrapAtMostInternalMaximum':changes[-1]<=max(changes[:-1]),'humanPlaybackAcceptance':False}
        if name=='idle':
            y=np.where(cells[0][:,:,3]>0)[0];standing[f]=int(y.max()-y.min()+1);assert 38<=standing[f]<=42
    transitions={}
    for k,f in enumerate(F):
        assert all(cell(a,16+k,p).tobytes()==cell(a,20+k,3-p).tobytes() for p in range(4))
        assert cell(a,16+k,0).tobytes()==cell(a,k).tobytes()
        assert cell(a,16+k,3).tobytes()==cell(a,12+k).tobytes()
        contacts=s['rows'][16+k]['phaseContacts']
        grounded=all(pc['contactOffset']['y']-pc['seatAnchor']['y']+pc['footY']==0 and opaque(cell(a,16+k,p))[pc['footY'],4:29].any() for p,pc in enumerate(contacts))
        assert grounded
        transitions[f]={'reverseExact':True,'idleAndNeutralSeatEndpointsExact':True,'allPhaseSolesWorldGrounded':bool(grounded)}
    gifchecks={}
    for k,f in enumerate(F):
        im=Image.open(OUT/f'walk-{f}-3x.gif');durations=[];exact=True
        assert im.n_frames==4 and im.size==(96,192)
        for p in range(4):
            im.seek(p);durations.append(im.info['duration'])
            target=np.array(cell(a,4+k,p).resize((96,192),Image.Resampling.NEAREST));actual=np.array(im.convert('RGBA'))
            mask=target[:,:,3]>0
            exact&=bool(np.array_equal(actual[mask],target[mask]) and np.array_equal(actual[:,:,3],target[:,:,3]))
        assert exact,(f,'GIF RGBA differs');assert durations==[120,130,120,130]
        gifchecks[f]={'opaquePixelsAndAlphaExact':exact,'frames':4,'durationsMs':durations,'actualPlaybackVerified':False}
    vis=read(OUT/'seated-visibility.json');assert all(vis['headGates'].values()) and vis['handsFullyVisible']
    assert all(sha(ROOT/p)==h for p,h in vis['inputHashes'].items())
    proof={'canonicalHeadRGBAComparisons':headchecks,'connectedBodyCells':connectedchecks,'standingHeights':standing,'distinctMotionPhases':distinct,'grips':grips,'transitions':transitions,'walkLoopPixelEvidence':loops,'gifSerializedFrames':gifchecks,'headAndBraidFacingDrawingsIndependent':True,'limits':['4-phase timing and pixel-change metrics do not prove natural playback.','Runtime motion speed, WPF draw order, sofa and prop compositing not verified.']}
    write('derivation-proof.json',proof)
    result=subprocess.run([sys.executable,'-B',str(ROOT/'tools/iso_check.py'),str(OUT),'--stage','final','--tolerance','0'],capture_output=True,text=True,encoding='utf-8',env={**os.environ,'PYTHONIOENCODING':'utf-8'})
    (OUT/'validation.txt').write_text(result.stdout+result.stderr,encoding='utf-8')
    report={'stage':'final','tolerance':0,'exitCode':result.returncode,'PASS':result.stdout.count('| PASS |'),'SKIP':result.stdout.count('| SKIP |'),'FAIL':result.stdout.count('| FAIL |')}
    write('validation-summary.json',report)
    print(json.dumps({'checker':report,'canonicalHeads':headchecks,'connectedCells':connectedchecks,'grips':len(grips),'walkLoopEvidence':loops}))
    assert result.returncode==0,result.stdout+result.stderr

def final_review():
    contact=Image.open(OUT/'iso-contact.png').convert('RGBA')
    board=Image.new('RGBA',(1300,1280),BG);board.alpha_composite(contact);d=ImageDraw.Draw(board)
    doc=read(OUT/(CID+'.iso.json'));a=Image.open(OUT/doc['sheets'][0]['file']).convert('RGBA')
    d.text((6,951),'FINAL LOWERING PHASE2: bent hip bridge / 1x and 3x | HERO actual crops / original and nearest3x',fill=INK)
    for k,f in enumerate(F):
        im=cell(a,16+k,2);x=10+k*150;y=980
        board.alpha_composite(im,(x,y));board.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+34,y));d.text((x,y+200),f+' sit phase2',fill=INK)
    hero=Image.open(ROOT/'assets/art/iso-concept/iso-office-hero.png').convert('RGBA')
    for x,box in [(630,(685,350,840,510)),(940,(1190,500,1360,680))]:
        native=hero.crop(box);board.alpha_composite(native,(x,985))
        small=native.resize((round(native.width*40/native.height),40),Image.Resampling.NEAREST)
        board.alpha_composite(small,(x+170,995));board.alpha_composite(small.resize((small.width*3,120),Image.Resampling.NEAREST),(x+170,1050))
    board.save(OUT/'final-review.png')
    print('Final motion, repaired lowering, three pilots and hero comparison ready.')

def notes():
    from datetime import datetime,timezone
    start=datetime.fromisoformat('2026-10-04T03:57:29+00:00');end=datetime.now(timezone.utc)
    seconds=(end-start).total_seconds();hours=seconds/3600
    timing={'startUtc':start.isoformat(),'endUtc':end.isoformat(),'measuredLaneSeconds':seconds,'measuredLaneHours':hours,'activeLanes':1,'includes':['reference reading','three serial generation waits','native repairs','visual review','validation','notes'],'excludes':['initial reading before first clock observation','later final chat'],'generationToolWallSeconds':[32.3,38.0,29.7],'generationWaitAlreadyIncluded':True,'masterAttempts':{'idle':1,'walk':1,'seat':1},'viewedImages':14,'throughputNotIndependentlyAccepted':True}
    write('lane-timing.json',timing)
    text=f'''# red-braid ISO 제작 기록

요청한 파일을 이 폴더에 내보냈다. 최종 checker는 **12 PASS / 5 SKIP / 0 FAIL**, exit 0이다. 시각 검수는 정지 화면 기준이며, 실제 재생의 자연스러움과 런타임 동작까지 합격했다고 주장하지 않는다.

`red-braid-iso.png`는 128×2432, 38행·152셀이다. `red-braid-iso-blink.png`는 128×1536, 24행·96셀. `red-braid-eyes-se.png`와 `red-braid-eyes-sw.png`, 내용이 같은 `red-braid.iso.json`·`red-braid-iso.manifest.json`, 요청한 네 `iso-*.png` 검수판과 네 방향 `walk-*-3x.gif`도 포함했다. `final-review.png`와 행별 검수판 네 장은 최종 픽셀을 보여준다.

내장 이미지 생성은 **순차 3회**, 마스터마다 **1회**였다. idle 마스터에 네 방향을 각각 그렸고, walk 마스터에는 SE/NE 키 포즈 8개, seat 마스터에는 네 방향 착석을 그렸다. 원본 PNG와 실제 프롬프트는 `master-*-a1.png`·`generation-*-prompt.txt`에 있다. 별도 API나 CLI 생성은 사용하지 않았다. 내장 도구가 자동으로 만든 원본 캐시는 CODEX_HOME/generated_images에 있으며, 선택본은 이 폴더에 복사했다.

마스터를 네이티브 격자로 한 번 축소한 뒤 원본 팔레트, 이진 알파, 윤곽과 접점을 정리했다. 앞머리·얼굴·상체와 의상 군집은 생성 그림에서 가져왔고, 눈 하이라이트·주근깨·손·무릎·발과 전환 연결부는 픽셀로 보정했다. 동작 248셀을 모두 따로 생성한 결과는 아니다. 팔레트는 locked 원본 32색과 base 얼굴에서 실제 채취한 초록 두 색을 허용하며, 최종 normal에는 31색이 쓰였다.

머리는 네 방향 독립 레이어다. 외땋은 머리와 리본은 해부학적 오른쪽을 지켰다. SW/NW 보행에서 몸만 x′=32−x로 파생하고 조명을 보정한 뒤, 해당 방향 머리·땋은 머리를 복원했다. 전화는 두 전면 뷰 모두 해부학적 오른손에 들도록 따로 그렸다. idle 높이 40px, 머리 영역 19px 이상, 발 (16,60), 골반 (16,44), S0 손 접점과 전환 일정은 고정했다. 프레임별 피팅이나 재중심화는 없다.

보행은 역스윙, 교대 지지발, 무릎 굽힘과 네 위상을 넣었다. 몸통 bob은 0/+1/0/−1px, 머리는 0/+1/0/0px로 마지막 up→contact에서 시선을 안정시켰다. 네 방향 모두 4개 서로 다른 프레임이고, 3→0 픽셀 변화량은 SE244/SW254/NE241/NW253으로 내부 구간 최대보다 작다. 이 수치는 단독 seam을 확인하는 근거이며 생체역학이나 재생 품질의 합격 기준은 아니다.

최종 모션을 정한 뒤 `blink_resync.py`로 normal을 복사하고 SE/SW 눈 마스크 안의 픽셀만 닫힌 눈으로 바꿨다. NE/NW 눈 마스크는 없다. 96셀 모두 눈 밖 RGBA가 normal과 정확히 같고, canonical head 152셀도 선언된 오프셋에서 바이트가 일치한다. 모션을 바꾸면 이 스크립트를 다시 실행해야 한다.

v3 책상·의자 입력을 `v3-inputs/`에 고정하고, `verify-desk-rig-v3-lane.py`와 같은 part/seat-depth queue로 16개 typing 위상을 합성했다. alpha 덮임과 마지막 픽셀 소유권을 각각 계산해 일치를 확인했다. 입력 원본 해시는 측정 종료 때도 같았다. C/H 오차 0px, 32개 typing 손 피부 접점은 1px 이내다.

| 작업자 방향 | 머리 visible/total | 머리 가시율 | 몸통 가시율 범위 | 손 ROI 가시율 |
| --- | --- | --- | --- | --- |
| SE | 282/282 | 100% | 100% | 100% |
| SW | 291/291 | 100% | 100% | 100% |
| NE | 260/278 | 93.53% | 16.07–17.54% | 100% |
| NW | 245/276 | 88.77% | 4.08% | 100% |

머리 분모는 `head-<방향>.png`의 전체 불투명 영역이며 가구에 맞춰 줄이지 않았다. 몸통은 불투명 캐릭터에서 머리·손을 제외한 source x11..22, y<44 영역. 손은 고정 접점의 Chebyshev 반경 2 안에 있는 피부·손 하이라이트 픽셀이다. `seated-visibility.json`에 각 위상의 visible/total, occluder, ROI 정의와 가구 해시를 기록했다. 머리 기준은 전면 90%, 후면 85%를 모두 충족한다. 뒤쪽 몸통과 땋은 머리 아래쪽은 여전히 등받이에 많이 가린다.

검사 명령은 `python -B tools/iso_check.py assets/art/iso/characters/red-braid --stage final --tolerance 0`. 실제 결과는 `validation.txt`. SKIP 5개는 바닥 이음·가구 part 검사와 blink 시트의 중복 face-lock 검사로, 캐릭터에 적용되지 않는다. 추가로 152개 연결 실루엣, 80개 피부 손 접점, 정확한 sit/stand 역순과 끝점, GIF 네 프레임의 불투명 RGB·알파 및 120/130/120/130ms 직렬화 일치를 검증했다. 최초 GIF의 투명 색인 오류는 공통 팔레트로 고쳤다. `derivation-proof.json`, `blink-resync-proof.json`, `validation-summary.json`이 근거다.

14개 이미지를 열어 원본 얼굴·hero 크롭·세 파일럿 옆에서 1×/3×를 비교했다. 38개 normal 행과 24개 blink 행의 모든 위상을 행별 검수판으로 보았고, 이후 수정한 보행·전환은 final-review로 다시 확인했다. 네 방향 착석 16개 합성도 보았다.

남은 약점: 네 프레임 보행은 중간 자세가 생략돼 딱딱할 수 있다. passing은 반대 발 contact를 함께 압축한 포즈이며 두 발의 완전한 8단계 gait를 표현하지 못한다. 손과 전환의 무릎 군집은 단순하고, 주근깨·리본은 작은 픽셀로 줄어든다. sleep은 약한 착석 기대기 수준이다. hero 같은 여유로운 볼륨과 동작까지 맞췄다고 보지 않는다.

검증하지 못한 것: GIF 플레이어의 실제 시간 재생, 이동 속도와 보폭·발 미끄러짐, WPF/runtime/DPI/깜빡임 전환·깊이 순서, sofa sleep과 실제 소품 합성, 독립적인 사람의 정체성·모션 승인. 직접 파일 쓰기는 이 red-braid 폴더에만 했다. 공유 문서·코드·테스트·가구·pack·다른 lane을 수정하지 않았고 git 쓰기나 dotnet 전체 테스트도 실행하지 않았다.

실측 시간: **{hours:.4f} lane-hours / {seconds/60:.2f}분**. 시작 {start.isoformat()} → 종료 {end.isoformat()}. 한 세션의 조사·생성 대기·보정·검수·검사·기록 시간을 포함한다. 생성 도구 호출 wall time 32.3+38.0+29.7초는 이미 포함돼 이중 합산하지 않았다. 첫 시계 관측 전의 초기 읽기와 이 기록 뒤 채팅 보고는 제외했다. 이 수치를 승인된 제작 처리량으로 일반화하지 않는다.

재구성은 같은 Python 환경에서 `-B red_braid_production.py normalize`, `build`, `-B blink_resync.py`, `seated_review`, `reviews`, `verify`, `final_review` 순서다. Pillow와 NumPy만 사용하며 모든 쓰기는 이 폴더에 한정된다.
'''
    (OUT/'NOTES-C1-C2-C3.md').write_text(text,encoding='utf-8')
    core=[OUT/s['file'] for s in read(OUT/(CID+'.iso.json'))['sheets']]
    core += [OUT/(CID+'.iso.json'),OUT/(CID+'-iso.manifest.json')]+[OUT/f'{CID}-eyes-{f}.png' for f in ('se','sw')]+[OUT/f'head-{f}.png' for f in F]+[OUT/f'walk-{f}-3x.gif' for f in F]
    write('artifact-hashes.json',{p.name:sha(p) for p in core})
    print(json.dumps({'laneHours':round(hours,4),'minutes':round(seconds/60,2),'notes':'NOTES-C1-C2-C3.md','coreArtifactCount':len(core),'viewedImages':14}))

if __name__=='__main__':
    globals()[sys.argv[1]]()
