"""glasses-bun lane only. Run with Python -B from the workspace root."""
from pathlib import Path
import argparse, copy, datetime, hashlib, importlib.util, json, math, subprocess, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[4]
RUN=OUT/'runs'
RUN.mkdir(exist_ok=True)
FLAT=ROOT/'assets/art/roster/glasses-bun/locked/glasses-bun.png'
F=('se','sw','ne','nw')
BG='#E7D1AC'
def save_json(path,data): path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def empty(): return Image.new('RGBA',(32,64))
def shift(im,x=0,y=0):
    out=Image.new('RGBA',im.size);out.alpha_composite(im,(x,y));return out
def region(im,box):
    out=empty();out.paste(im.crop(box),box[:2]);return out
def prepare():
    save_json(RUN/'timing.json',{'startUTC':'2026-10-04T02:17:24+00:00','initialReadingBeforeClockExcluded':True})
    flat=Image.open(FLAT).convert('RGBA')
    colors=np.unique(np.array(flat)[:,:,:3][np.array(flat)[:,:,3]>0],axis=0)
    save_json(RUN/'identity-palette.json',['#'+bytes(c).hex().upper() for c in colors])
    board=Image.new('RGBA',(1200,700),BG);d=ImageDraw.Draw(board)
    d.text((10,5),'LOCKED glasses-bun identity: twin brown buns, round glasses, cream cardigan, terracotta long skirt',fill='#191E1E')
    strip=flat.crop((0,0,128,64));board.alpha_composite(strip,(10,28));board.alpha_composite(strip.resize((512,256),Image.Resampling.NEAREST),(10,102))
    base=Image.open(ROOT/'assets/art/roster/glasses-bun/base.png').convert('RGBA');base.thumbnail((260,320));board.alpha_composite(base,(540,30))
    hero=Image.open(ROOT/'assets/art/iso-concept/iso-office-hero.png').convert('RGBA')
    for k,(title,p) in enumerate([('hero dev','assets/art/iso/characters/dev-hoodie-v2/runs/v2/hero-dev-crop.png'),('hero professor','assets/art/iso/characters/professor/runs/v1/hero-professor-crop.png')]):
        im=Image.open(ROOT/p).convert('RGBA');im.thumbnail((175,180));board.alpha_composite(im,(820+k*180,30));d.text((820+k*180,220),title,fill='#191E1E')
    for k,(folder,cid) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
        atlas=Image.open(ROOT/f'assets/art/iso/characters/{folder}/{cid}-iso.png').convert('RGBA')
        row=atlas.crop((0,256,128,320));x=10+k*396
        d.text((x,390),cid+' finished walk SE 1x / 3x',fill='#191E1E');board.alpha_composite(row,(x,410));board.alpha_composite(row.resize((384,192),Image.Resampling.NEAREST),(x,480))
    board.save(RUN/'reference-board.png')
    strip.resize((768,384),Image.Resampling.NEAREST).save(RUN/'identity-reference.png')
    print('prepared',len(colors),'identity colors')

def components(mask):
    # Run-length connected-component extraction; no fixed image-slot cutting.
    parent=[];boxes=[];counts=[];prev=[]
    def find(a):
        while parent[a]!=a:parent[a]=parent[parent[a]];a=parent[a]
        return a
    for y,row in enumerate(mask):
        edges=np.diff(np.r_[False,row,False].astype(np.int8));starts=np.where(edges==1)[0];ends=np.where(edges==-1)[0]
        current=[]
        for x0,x1 in zip(starts,ends):
            n=len(parent);parent.append(n);boxes.append([int(x0),y,int(x1),y+1]);counts.append(int(x1-x0));current.append((x0,x1,n))
            for a,b,j in prev:
                if a<=x1 and b>=x0:
                    j=find(j);nroot=find(n)
                    if j!=nroot:parent[j]=nroot
        prev=current
    groups={}
    for n,box in enumerate(boxes):
        r=find(n)
        if r not in groups:groups[r]=[box.copy(),0]
        b=groups[r][0];b[0]=min(b[0],box[0]);b[1]=min(b[1],box[1]);b[2]=max(b[2],box[2]);b[3]=max(b[3],box[3]);groups[r][1]+=counts[n]
    return sorted(groups.values(),key=lambda p:p[1],reverse=True)

def palette_data():
    identity=json.loads((RUN/'identity-palette.json').read_text())
    actual=np.array([list(bytes.fromhex(c[1:])) for c in identity],np.int32)
    targets=['23120D','432719','65402A','8D5835','B47B4E','7A3C23','A55531','C47546','DF9566','976647','CE9261','F0B47D','FFD29A','FFF0C8','F4D8AE','C5A382','91785E','F2EBD3']
    chosen=[]
    for t in targets:
        c=actual[((actual-np.array(list(bytes.fromhex(t))))**2).sum(axis=1).argmin()].tolist()
        if c not in chosen:chosen.append(c)
    chosen.append(list(bytes.fromhex('F2EBD3')))
    return np.unique(np.array(chosen,np.int32),axis=0),identity

def snap(im):
    a=np.array(im);alpha=a[:,:,3]>=150;pal,_=palette_data();c=a[:,:,:3][alpha].astype(np.int32)
    a[:,:,:3][alpha]=pal[((c[:,None,:]-pal[None,:,:])**2).sum(axis=2).argmin(axis=1)]
    a[~alpha]=0;a[alpha,3]=255
    return Image.fromarray(a)

def cut_master(name,count,green):
    im=Image.open(RUN/(name+'.png')).convert('RGBA');a=np.array(im)
    alpha=a[:,:,3]>=150
    if green:
        r,g,b=a[:,:,0].astype(int),a[:,:,1].astype(int),a[:,:,2].astype(int)
        alpha&=~((g>100)&(g>r*1.3)&(g>b*1.3))
    a[~alpha]=0;a[alpha,3]=255;im=Image.fromarray(a)
    blobs=components(alpha)[:count];assert len(blobs)==count
    blobs.sort(key=lambda p:(p[0][1]//(im.height//2),p[0][0]))
    save_json(RUN/(name+'-extraction.json'),{'sourceSHA256':sha(RUN/(name+'.png')),'components':[{'box':b,'opaqueTexels':n} for b,n in blobs]})
    return im,[b for b,n in blobs]

def normalize():
    proof=[];board=Image.new('RGBA',(1200,750),BG);d=ImageDraw.Draw(board)
    for name,kind,n,green in [('idle-master-a1','idle',4,False),('seat-master-a1','seat',4,True),('walk-master-a2','walk',8,True)]:
        raw,boxes=cut_master(name,n,green)
        if kind=='walk':
            # One scale and a fixed source-column grid, common row baseline for the entire gait.
            factor=42/max(b[3]-b[1] for b in boxes);scale=factor
            for k,b in enumerate(boxes):
                row,col=divmod(k,4);f='se' if row==0 else 'ne'
                baseline=max(bb[3]-1 for bb in boxes[row*4:(row+1)*4]);cx=raw.width*(col+.5)/4
                # Sample the fixed transform rather than fit/recenter an animation phase.
                arr=np.array(raw.filter(ImageFilter.BoxBlur(3)));yy,xx=np.indices((64,32));sx=np.round((xx-16)/scale+cx).astype(int);sy=np.round((yy-60)/scale+baseline).astype(int)
                valid=(sx>=b[0])&(sx<b[2])&(sy>=b[1])&(sy<b[3]);out=np.zeros((64,32,4),np.uint8);out[valid]=arr[sy[valid],sx[valid]]
                native=snap(Image.fromarray(out));native.save(RUN/f'{f}-walk-{col}-native.png')
                proof.append({'kind':kind,'facing':f,'phase':col,'scaleOnce':scale,'fixedSourceAnchor':[cx,baseline],'sourceBox':b,'nativeBox':native.getbbox()})
        else:
            for k,b in enumerate(boxes):
                f=F[k];h=42 if kind=='idle' else 38;w=b[2]-b[0];hh=b[3]-b[1];scale=min(h/hh,28/w)
                native=raw.crop(b).resize((round(w*scale),round(hh*scale)),Image.Resampling.BOX)
                out=empty();out.alpha_composite(native,((32-native.width)//2,61-native.height if kind=='idle' else 57-native.height));out=snap(out)
                out.save(RUN/f'{f}-{kind}-native.png');proof.append({'kind':kind,'facing':f,'scaleOnce':scale,'sourceBox':b,'nativeBox':out.getbbox()})
    for k,p in enumerate(proof):
        x=(k%8)*150;y=(k//8)*360;label=p['facing']+' '+p['kind']+str(p.get('phase',''))
        im=Image.open(RUN/(p['facing']+'-'+p['kind']+(('-'+str(p['phase'])) if 'phase' in p else '')+'-native.png')).convert('RGBA')
        d.text((x+2,y+4),label+' 1x /3x',fill='#191E1E');board.alpha_composite(im,(x+3,y+28));board.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+42,y+28))
    board.save(RUN/'masters-native-review.png');save_json(RUN/'normalization.json',proof)
    print(json.dumps(proof,indent=2))

def check_module():
    spec=importlib.util.spec_from_file_location('iso_check_readonly',ROOT/'tools/iso_check.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
def col(s):return tuple(bytes.fromhex(s.lstrip('#')))+(255,)
INK=col('26130C');HAIR=col('5D3F27');HL=col('975A37');SKIN=col('F7CD94');SKINSH=col('F4BC7B');CREAM=col('FCEAC7');CREAMSH=col('DBAA72');SKIRT=col('C76E45');SKIRTSH=col('AF5130');WHITE=col('F2EBD3')
SKINHEX=['F7CD94','F4BC7B','E78F61']
def line(im,points,color,width=1):ImageDraw.Draw(im).line(points,fill=color,width=width)
def rect(im,box,color):ImageDraw.Draw(im).rectangle(box,fill=color)
def clean(im):
    a=np.array(im);a[:,0]=0;a[61:]=0;a[a[:,:,3]==0]=0;return Image.fromarray(a)
def mirrored_body(im):
    im=shift(im.transpose(Image.Transpose.FLIP_LEFT_RIGHT),1);a=np.array(im);old=a.copy()
    # Source upper-left light stays on screen-left after anchor-correct mirroring.
    for ramp in [('26130C','40291A','5D3F27','784228','975A37'),('784228','AF5130','C76E45','E78F61'),('DBAA72','FBE5BE','FCEAC7')]:
        cs=[col(s) for s in ramp]
        for i,c in enumerate(cs[1:],1):
            mask=(old==c).all(axis=2);ys,xs=np.where(mask)
            for y,x in zip(ys,xs):a[y,x]=cs[min(len(cs)-1,i+1) if x<16 else max(1,i-1)]
    return Image.fromarray(a)
def arm(im,start,elbow,wrist,hand,p=0):
    line(im,[start,elbow,wrist],INK,4);line(im,[(start[0]-1,start[1]),(elbow[0]-1,elbow[1]),wrist],CREAMSH,3)
    line(im,[(start[0]-1,start[1]),(elbow[0]-1,elbow[1]),(wrist[0]-1,wrist[1])],CREAM,1)
    line(im,[wrist,hand],SKINSH,2);x,y=hand
    rect(im,(x-1,y-1,x,y),SKIN);im.putpixel((x-1,y-1),WHITE if p==3 else SKIN)
    im.putpixel((x+1,y-(p%2)),SKINSH)
def strip_arms(im,seated=False):
    a=np.array(im);a[40:49,:11]=0;a[40:49,23:]=0;return Image.fromarray(a)
def repair_connections(im,head):
    # Remove isolated single antialias remnants. Connect real detached shoes to hem.
    a=np.array(im);hm=np.array(head)[:,:,3]>0
    for box,n in components(a[:,:,3]>0):
        if n<=2:
            x0,y0,x1,y1=box
            if not hm[y0:y1,x0:x1].any():a[y0:y1,x0:x1]=0
    im=Image.fromarray(a)
    comps=components(a[:,:,3]>0)
    if len(comps)>1:
        largest=comps[0][0]
        for box,n in comps[1:]:
            if n>2:
                x=(box[0]+box[2]-1)//2;y=box[1]
                opaque=np.array(im)[:,:,3]>0;ys,xs=np.where(opaque & (np.indices(opaque.shape)[0]<y))
                if len(xs):
                    k=((xs-x)**2+(ys-y)**2).argmin();line(im,[(int(xs[k]),int(ys[k])),(x,y)],SKINSH if y>53 else CREAMSH,1)
    im.alpha_composite(head);return clean(im)

def build():
    check=check_module();heads={};idle={};seat={};eye={}
    eye_starts={'se':[(16,34),(21,33)],'sw':[(9,33),(14,34)]}
    for f in F:
        base=Image.open(RUN/f'{f}-idle-native.png').convert('RGBA');head=region(base,(0,0,32,40))
        if f in eye_starts:
            # Repaint only native diagonal eye/glasses clusters; generated hairline remains.
            eye[f]=[]
            for x,y in eye_starts[f]:
                dr=ImageDraw.Draw(head);dr.ellipse((x-2,y-1,x+2,y+3),fill=SKIN,outline=INK)
                for yy in range(y,y+3):
                    for xx in range(x,x+2):head.putpixel((xx,yy),INK);eye[f].append((xx,yy))
                head.putpixel((x,y),WHITE);head.putpixel((x+1,y+2),HAIR)
            x0,y0=eye_starts[f][0];x1,y1=eye_starts[f][1];line(head,[(x0+2,y0+1),(x1-2,y1+1)],INK)
            head.putpixel((18 if f=='se' else 13,38),SKIRTSH)
        head.save(RUN/f'head-{f}.png');heads[f]=head
        base=strip_arms(base);arm(base,(12,41),(11,44),(11,47),(11,48));arm(base,(21,41),(23,42),(24,43),(24,44))
        if f in ('sw','nw'):
            # Idle prop grip is frozen at the opposite screen side.
            arm(base,(12,41),(10,42),(8,43),(8,44 if f=='sw' else 40))
        if f=='ne':arm(base,(21,40),(23,40),(24,40),(24,40))
        base.alpha_composite(head);idle[f]=repair_connections(base,head)
        si=Image.open(RUN/f'{f}-seat-native.png').convert('RGBA');a=np.array(si);a[:40]=0;si=Image.fromarray(a)
        # One canonical head across all rigs, source offset exactly zero.
        rect(si,(14,40,19,44),CREAM);rect(si,(14,44,19,46),SKIRT);si.alpha_composite(head);seat[f]=repair_connections(si,head)
    save_json(RUN/'eye-pixels.json',eye)
    joints={};grips={'se':(24,44),'sw':(8,44),'ne':(24,40),'nw':(8,40)}
    prop={'pickup':[(24,44),(27,49),(27,46),(24,44)],'report-handover':[(24,44),(27,44),(28,43),(24,44)],'coffee':[(24,44),(25,40),(25,37),(24,44)],'phone':[(24,44),(26,40),(26,36),(26,36)]}
    def prop_hand(f,action,p):
        x,y=prop[action][p]
        if f=='sw' and action!='phone':x=32-x
        return x,y
    def make(f,action,p):
        head=heads[f];im=idle[f].copy()
        if action=='walk':
            bf='se' if f in ('se','sw') else 'ne';im=Image.open(RUN/f'{bf}-walk-{p}-native.png').convert('RGBA');a=np.array(im);a[:40]=0;im=Image.fromarray(a)
            if f in ('sw','nw'):im=mirrored_body(im)
            # Preserve drawing-derived draped skirt and shoes; repair arm counter-swing.
            im=strip_arms(im);bob=[0,1,0,-1][p]
            upper=region(im,(0,40,32,49));a=np.array(im);a[40:49]=0;im=Image.fromarray(a);im.alpha_composite(shift(upper,y=bob))
            # Lead foot and opposite hand reverse through the loop.
            l=[(15,47),(14,46),(10,44),(11,44)][p];r=[(21,43),(23,45),(25,47),(25,46)][p]
            if f in ('sw','nw'):l,r=(32-r[0],r[1]),(32-l[0],l[1])
            arm(im,(12,41+bob),(11,43+bob),(l[0],l[1]-1),l,p);arm(im,(21,41+bob),(23,43+bob),(r[0],r[1]-1),r,p+1)
            # Fixed head bob is an explicit <=1px translation, shared with blink.
            head=shift(head,y=bob)
            a=np.array(im);a[61:]=0
            # Contact/down support the near right foot; passing/up support left.
            # Only the chosen drawing-derived lower limb is grounded. The swing foot stays lifted.
            supportRight=p<2
            if f in ('sw','nw'):supportRight=not supportRight
            x0,x1=(17,31) if supportRight else (1,17)
            part=Image.fromarray(a).crop((x0,54,x1,61));box=part.getbbox()
            if box:
                fy=54+box[3]-1;dy=60-fy
                if dy:
                    a[54:61,x0:x1]=0;im=Image.fromarray(a);im.alpha_composite(part,(x0,54+dy));a=np.array(im)
            im=Image.fromarray(a);joints[f+'/'+str(p)]={'bodyBob':bob,'headOffset':bob,'leftHand':l,'rightHand':r,'supportFootScreenSide':'right' if supportRight else 'left','counterSwing':'left arm forward at right-foot contact/down, right arm forward at left-foot passing/up; opposite facing body is anchor-correct mirrored'}
        elif action in ('seated-typing-front','seated-thinking','sleep'):
            im=seat[f].copy();a=np.array(im);a[40:47,:11]=0;a[40:47,23:]=0;im=Image.fromarray(a)
            if action=='seated-typing-front':
                hs=check.HANDS[f];toward=1 if f in ('se','ne') else -1;rear=f in ('ne','nw')
                e=[(20 if toward==1 else 12,36 if rear else 42),(24 if toward==1 else 8,37 if rear else 41)]
                for k,st in enumerate([(12,41),(21,41)]):
                    x,y=hs[k];w=(x-toward,y-1);hand=(x,y-[0,1,0,0][(p+k)%4]);arm(im,st,e[k],w,hand,p+k)
                im.alpha_composite(head)
                for x,y in hs:im.putpixel((x,y),SKIN)
                # Different fingertip press and cuff texel, while H always has skin.
                x,y=hs[p%2];im.putpixel((x,y-1),SKINSH if p<2 else SKIN)
            else:
                l=(12,46);r=[(22,45),(24,42),(25,40),(22,45)][p] if action=='seated-thinking' else (22,45)
                arm(im,(12,41),(11,44),(12,45),l,p);arm(im,(21,41),(23,43),(r[0],r[1]+1),r,p)
                if action=='sleep':
                    lower=region(im,(0,47,32,57));a=np.array(im);a[47:]=0;im=Image.fromarray(a);im.alpha_composite(shift(lower,x=[0,1,1,0][p] if f=='ne' else -[0,1,1,0][p]))
                im.alpha_composite(head)
        elif action in ('sit-down','stand-up'):
            q=3-p if action=='stand-up' else p
            if q==0:return idle[f].copy(),0
            if q==3:return make(f,'seated-thinking',0)
            im=seat[f].copy();lower=region(im,(0,44,32,57));a=np.array(im);a[44:]=0;im=Image.fromarray(a)
            fy=check.SIT[q][2];im.alpha_composite(shift(lower,y=fy-56));rect(im,(14,40,19,48),CREAM);im.alpha_composite(head)
        elif action!='idle':
            im=strip_arms(im);l=(8,44) if f=='sw' else (11,48);r=(24,48) if f=='sw' else grips[f]
            if action in prop:
                h=prop_hand(f,action,p)
                if h[0]<16:l=h
                else:r=h
            elif action=='talk':
                if f=='se':r=[(24,44),(27,42),(26,40),(24,44)][p]
                else:l=[(8,44),(5,42),(6,40),(8,44)][p]
            elif action=='surprised':l=[(11,48),(8,42),(7,40),(10,43)][p];r=[(24,44),(26,41),(27,39),(24,42)][p]
            arm(im,(12,41),(11,43),(l[0],l[1]-1),l,p);arm(im,(21,41),(23,43),(r[0],r[1]-1),r,p)
        im.alpha_composite(head);im=repair_connections(im,head)
        return im,([0,1,0,-1][p] if action=='walk' else 0)
    atlas=Image.new('RGBA',(128,2432));clips=[];offsets={}
    for row,(action,f,fps,loop) in enumerate(check.CONTRACT):
        clip={'name':action,'direction':f,'row':row,'column':0,'frames':4,'fps':fps,'loop':loop,'sourceMaster':'runs/'+('walk-master-a2.png' if action=='walk' else 'seat-master-a1.png' if action.startswith('seated') or action=='sleep' else 'idle-master-a1.png')}
        if action.startswith('seated') or action=='sleep':clip['seatAnchor']={'x':16,'y':44}
        if action=='seated-typing-front':clip.update({'seatProfile':'front','deskOrientation':next(d for d,w in check.WORKER.items() if w==f),'handAnchors':[{'x':x,'y':y} for x,y in check.HANDS[f]]})
        if action in ('sit-down','stand-up'):
            schedule=check.SIT[::-1] if action=='stand-up' else check.SIT
            clip['phaseContacts']=[{'seatAnchor':{'x':16,'y':sy},'contactOffset':{'x':0,'y':o},'footY':fy} for sy,o,fy in schedule]
        if action in prop:clip['handAnchorsByPhase']=[[dict(zip(('x','y'),prop_hand(f,action,p)))] for p in range(4)]
        oo=[]
        for p in range(4):
            im,hy=make(f,action,p);atlas.paste(im,(32*p,64*row));oo.append({'x':0,'y':hy})
        offsets[action+'/'+f]=oo;clips.append(clip)
    atlas.save(OUT/'glasses-bun-iso.png')
    src=json.loads((ROOT/'assets/art/iso/characters/dev-hoodie-v2/dev-hoodie.iso.json').read_text(encoding='utf-8'))
    _,identity=palette_data();common={'cellW':32,'cellH':64,'anchor':{'x':16,'y':60},'footAnchor':{'x':16,'y':60},'scale':1,'projection':'iso2to1','identityPalette':identity}
    normal={**common,'id':'glasses-bun-normal','kind':'character','file':'glasses-bun-iso.png','rows':clips,'aliases':copy.deepcopy(src['sheets'][0]['aliases']),'blink':{'sheetId':'glasses-bun-blink','normalRows':check.BLINK_ROWS,'durationMs':100},'faceLock':{f:{'eyeMask':f'glasses-bun-eyes-{f}.png'} for f in ('se','sw')},'headOffsetsByClip':offsets}
    bclips=[{'name':'blink-'+check.CONTRACT[r][0],'direction':check.CONTRACT[r][1],'row':b,'column':0,'frames':4,'fps':check.CONTRACT[r][2],'loop':check.CONTRACT[r][3]} for b,r in enumerate(check.BLINK_ROWS)]
    doc={'version':1,'projection':'iso2to1','requiredCapabilities':['iso2to1','renderItemsV1'],'sheets':[normal,{**common,'id':'glasses-bun-blink','kind':'blink','file':'glasses-bun-iso-blink.png','rows':bclips}],'provenance':{'drawing':'Built-in image_gen: idle 1, walk 2, seat 1 serial calls. Generated drawing parts cleaned to native grid; fixed four facing heads, native glasses/eye/outline/hand repairs; no flat shear or runtime mirror. SW/NW walk body only anchor-correct mirror with fixed upper-left relight, independently generated heads and seated bodies.','blink':'Deterministic eye-only rederivation AFTER final motion via blink_resync.py.','headOffsets':'zero across rigs/transitions, walk [0,+1,0,-1] only.','asymmetry':'Independently drawn four-facing heads; symmetric twin buns and plain clothing. Side-swept locks kept per facing; phone remains same anatomical hand via separately authored SW reach. No readable labels or handed props baked.'}}
    save_json(OUT/'glasses-bun.iso.json',doc);save_json(OUT/'glasses-bun-iso.manifest.json',doc);save_json(RUN/'motion-joints.json',joints)
    print('38 normal rows published; run blink_resync next')

def load_export():
    doc=json.loads((OUT/'glasses-bun.iso.json').read_text(encoding='utf-8'));s=doc['sheets'][0];atlas=Image.open(OUT/s['file']).convert('RGBA')
    cells=[[atlas.crop((p*32,r*64,(p+1)*32,(r+1)*64)) for p in range(4)] for r in range(38)]
    return doc,s,cells

def verify():
    doc,s,cells=load_export();proof={'canonicalHeads':0,'connectedCells':0,'walk':{},'typingDistinct':{},'transitions':{},'propSkinDistances':[],'idleHeight':{}}
    skin=[col(c) for c in SKINHEX]
    for c in s['rows']:
        f=c['direction'];head=Image.open(RUN/f'head-{f}.png').convert('RGBA')
        for p,im in enumerate(cells[c['row']]):
            off=s['headOffsetsByClip'][c['name']+'/'+f][p];h=np.array(shift(head,off['x'],off['y']));mask=h[:,:,3]>0;a=np.array(im)
            assert np.array_equal(a[mask],h[mask]),('head drift',c['row'],p);proof['canonicalHeads']+=1
            comps=components(a[:,:,3]>0)
            if len(comps)==1:proof['connectedCells']+=1
            else:proof.setdefault('disconnected',[]).append({'row':c['row'],'phase':p,'components':comps})
            if c.get('handAnchorsByPhase'):
                sm=np.isin(a[:,:,3],255)&np.any(np.all(a[:,:,None,:]==np.array(skin)[None,None,:,:],axis=3),axis=2)&~mask;ys,xs=np.where(sm)
                for hp in c['handAnchorsByPhase'][p]:
                    dist=int(np.maximum(abs(xs-hp['x']),abs(ys-hp['y'])).min());assert dist<=1,(c,p,dist);proof['propSkinDistances'].append(dist)
    for k,f in enumerate(F):
        walk=cells[k+4];changes=[int(np.any(np.array(walk[p])!=np.array(walk[(p+1)%4]),axis=2).sum()) for p in range(4)]
        assert len({im.tobytes() for im in walk})==4
        proof['walk'][f]={'distinctFrames':4,'adjacentRGBAChangedTexels':changes,'wrap3to0':changes[-1],'wrapNoLargerThanInternalMax':changes[-1]<=max(changes[:3]),'y60GroundedEveryPhase':all(np.array(im)[60,:,3].any() for im in walk),'phaseNames':['contact','down','passing','up']}
        proof['typingDistinct'][f]=len({im.tobytes() for im in cells[k+8]})==4;assert proof['typingDistinct'][f]
        assert [im.tobytes() for im in cells[k+16]]==[im.tobytes() for im in cells[k+20]][::-1]
        assert cells[k+16][0].tobytes()==cells[k][0].tobytes() and cells[k+16][3].tobytes()==cells[k+12][0].tobytes()
        proof['transitions'][f]={'exactReversal':True,'idleAndNeutralRestEndpointsEqual':True}
        ys=np.where(np.array(cells[k][0])[:,:,3]>0)[0];proof['idleHeight'][f]=int(ys.max()-ys.min()+1)
    assert proof['canonicalHeads']==152
    proof['blinkNormalSHA256']=json.loads((OUT/'blink-resync-proof.json').read_text())['normalSHA256'];assert proof['blinkNormalSHA256']==sha(OUT/'glasses-bun-iso.png')
    save_json(RUN/'derivation-proof.json',proof);print(json.dumps(proof,indent=2))

def reviews():
    doc,s,cells=load_export();ink='#191E1E'
    board=Image.new('RGBA',(1050,930),BG);d=ImageDraw.Draw(board)
    for k,f in enumerate(F):
        y=8+k*228;d.text((8,y),f.upper()+' idle + walk contact/down/passing/up; 1x / 3x',fill=ink)
        for p,im in enumerate([cells[k][0]]+cells[4+k]):
            x=12+p*158;board.alpha_composite(im,(x,y+26));board.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+37,y+26))
        hero=Image.open(ROOT/'assets/art/iso/characters/dev-hoodie-v2/runs/v2/hero-dev-crop.png').convert('RGBA');hero.thumbnail((160,160));board.alpha_composite(hero,(820,y+36))
    board.save(OUT/'iso-contact.png')
    allrows=Image.new('RGBA',(2048,2200),BG);d=ImageDraw.Draw(allrows)
    for r,row in enumerate(cells):
        x=(r%4)*512;y=(r//4)*220;c=s['rows'][r];d.text((x+4,y+3),f"{r:02} {c['name']} {c['direction']} 1x/3x",fill=ink)
        for p,im in enumerate(row):
            xx=x+4+p*126;allrows.alpha_composite(im,(xx,y+24));allrows.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(xx+32,y+24))
    allrows.save(OUT/'iso-rows-review.png');allrows.crop((0,0,2048,1100)).save(RUN/'rows-review-top.png');allrows.crop((0,1100,2048,2200)).save(RUN/'rows-review-bottom.png')
    flat=Image.open(FLAT).convert('RGBA');vs=Image.new('RGBA',(1100,890),BG);d=ImageDraw.Draw(vs)
    for k,f in enumerate(F):
        y=8+k*220;d.text((8,y),f.upper()+' locked flat / iso idle / seated / pilot idle lineup',fill=ink)
        fc={'se':0,'sw':1,'ne':2,'nw':3}[f];flatcell=flat.crop((fc*32,0,(fc+1)*32,64))
        for j,im in enumerate([flatcell,cells[k][0],cells[k+8][0]]):
            x=8+j*175;vs.alpha_composite(im,(x,y+23));vs.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+34,y+23))
        for j,(folder,cid) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
            a=Image.open(ROOT/f'assets/art/iso/characters/{folder}/{cid}-iso.png').convert('RGBA');im=a.crop((0,k*64,32,(k+1)*64));x=540+j*174
            vs.alpha_composite(im,(x,y+23));vs.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+34,y+23))
    vs.save(OUT/'iso-vs-flat.png')
    wb=Image.new('RGBA',(1180,920),BG);d=ImageDraw.Draw(wb)
    for k,f in enumerate(F):
        y=4+k*228;d.text((6,y),f.upper()+' walk 0 1 2 3 0 (wrap) / 1x and 3x',fill=ink)
        for p,im in enumerate(cells[k+4]+[cells[k+4][0]]):
            x=8+p*145;wb.alpha_composite(im,(x,y+26));wb.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+32,y+26))
        frames=[]
        for im in cells[k+4]:
            bg=Image.new('RGBA',im.size,BG);bg.alpha_composite(im);frames.append(bg.resize((96,192),Image.Resampling.NEAREST).convert('RGB'))
        frames[0].save(OUT/f'walk-{f}-3x.gif',save_all=True,append_images=frames[1:],duration=[120,130,120,130],loop=0,disposal=2)
        strip=Image.new('RGBA',(384,192),BG)
        for p,im in enumerate(cells[k+4]):strip.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(96*p,0))
        strip.save(OUT/f'walk-{f}-3x.png')
        for j,(folder,cid) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
            a=Image.open(ROOT/f'assets/art/iso/characters/{folder}/{cid}-iso.png').convert('RGBA');row=a.crop((0,(k+4)*64,128,(k+5)*64));wb.alpha_composite(row,(790,y+28+j*64));d.text((930,y+35+j*64),cid,fill=ink)
    wb.save(RUN/'walk-wrap-and-pilots-review.png')
    # All 24 blink rows at 1x/3x, with normal row association labels.
    ba=Image.open(OUT/'glasses-bun-iso-blink.png').convert('RGBA');bb=Image.new('RGBA',(2048,1320),BG);d=ImageDraw.Draw(bb)
    for b,r in enumerate(s['blink']['normalRows']):
        x=(b%4)*512;y=(b//4)*220;d.text((x+4,y+3),f'blink {b:02} / normal {r:02}',fill=ink)
        for p in range(4):
            im=ba.crop((p*32,b*64,(p+1)*32,(b+1)*64));xx=x+4+p*126;bb.alpha_composite(im,(xx,y+24));bb.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(xx+32,y+24))
    bb.save(RUN/'blink-all-rows-review.png')
    print('all 38 normal + 24 blink rows and 4 GIFs reviewed/exported')

def seated():
    # Reuse the v3 workstation lane's published serialized-part measurement approach.
    spec=importlib.util.spec_from_file_location('roi_measure_readonly',ROOT/'assets/art/iso/furniture-v2/workstations/measure-A3-rev2.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
    doc,s,cells=load_export();sources={};snapshot=RUN/'furniture-v3-snapshot';snapshot.mkdir(exist_ok=True)
    def furniture(name,folder):
        root=ROOT/f'assets/art/iso/furniture-v3/{folder}';p=root/f'{name}.iso.json';data=p.read_bytes();sources[str(p.relative_to(ROOT))]=hashlib.sha256(data).hexdigest();(snapshot/(name+'.json')).write_bytes(data);d=json.loads(data)
        sheets={}
        for ss in d['sheets']:
            p=root/ss['file'];data=p.read_bytes();sources[str(p.relative_to(ROOT))]=hashlib.sha256(data).hexdigest();(snapshot/p.name).write_bytes(data);sheets[ss['id']]=(ss,Image.open(snapshot/p.name).convert('RGBA'))
        return d['furniture'][0],sheets
    desk,ds=furniture('desk','workstations');chair,cs=furniture('chair','chair')
    records=[];queues=[];panels=[];origin=(128,104)
    for c in s['rows'][8:12]:
        f=c['direction'];orientation=c['deskOrientation'];dv=m.variant(desk,orientation);pr=dv['seatProfiles'][0];ground=pr['seatGround'];depth=ground['i']+ground['j'];gp=m.project(ground['i'],ground['j']);co=(origin[0]+gp[0],origin[1]+gp[1]);contact=m.point(pr['contactOffset']);pos=(origin[0]+contact[0]-16,origin[1]+contact[1]-44);key=(depth,0,'actor','glasses-bun')
        cv=m.variant(chair,f);items=m.parts(dv,ds,origin,0,depth,'desk')+m.parts(cv,cs,co,depth,depth,'chair')
        assert [(pos[0]+h['x'],pos[1]+h['y']) for h in c['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in pr['handContacts']]
        queues.append({'worker':f,'desk':orientation,'actorKey':key,'actorDestination':pos,'items':[{'id':i['id'],'key':i['key'],'destination':i['pos']} for i in sorted(items,key=lambda i:i['key'])]})
        for p,im in enumerate(cells[c['row']]):
            a=np.array(im);alpha=a[:,:,3]>0;head=Image.open(RUN/f'head-{f}.png').convert('RGBA');hm=np.array(head)[:,:,3]>0;yy,xx=np.indices(alpha.shape);skin=np.zeros_like(alpha)
            for color in SKINHEX:skin|=np.all(a[:,:,:3]==tuple(bytes.fromhex(color)),axis=2)
            hands=np.zeros_like(alpha);dist=[]
            for h in c['handAnchors']:
                radius=np.maximum(abs(xx-h['x']),abs(yy-h['y']));sm=skin&alpha&~hm;hands|=sm&(radius<=2);dist.append(int(radius[sm].min()))
            torso=alpha&~hm&~hands&(xx>=11)&(xx<=22)&(yy<44)
            rois={'head':hm,'torso':torso,'hands':hands};canvas=Image.new('RGBA',(320,240),BG);labels=Image.new('L',canvas.size);cover=np.zeros_like(alpha)
            layers=items+[dict(id='actor',key=key,image=im,pos=pos)]
            for it in sorted(layers,key=lambda t:t['key']):
                canvas.alpha_composite(it['image'],it['pos']);labels.paste(1 if it['id']=='actor' else 2,it['pos'],it['image'].getchannel('A'))
                if it['id']!='actor' and it['key']>key:cover|=m.local_mask(np.array(it['image'])[:,:,3]>0,it,pos)
            owned=np.array(labels.crop((*pos,pos[0]+32,pos[1]+64)))==1;assert np.array_equal(owned&alpha,(~cover)&alpha)
            results={n:{'visible':int((mask&owned).sum()),'total':int(mask.sum()),'fraction':float((mask&owned).sum()/mask.sum())} for n,mask in rois.items()}
            records.append({'worker':f,'desk':orientation,'phase':p,**results,'handDistances':dist,'headGatePass':results['head']['fraction']>=(.85 if f in ('ne','nw') else .90),'handGatePass':results['hands']['fraction']==1})
            panels.append(canvas)
    summaries=[]
    for f in F:
        rr=[r for r in records if r['worker']==f];agg={'worker':f}
        for roi in ('head','torso','hands'):
            v=sum(r[roi]['visible'] for r in rr);t=sum(r[roi]['total'] for r in rr);agg[roi]={'visible':v,'total':t,'percent':v/t*100,'minimumPhasePercent':min(r[roi]['fraction'] for r in rr)*100}
        summaries.append(agg)
    board=Image.new('RGBA',(1400,1780),BG);d=ImageDraw.Draw(board)
    for k,f in enumerate(F):
        y=k*440;d.text((4,y+3),f.upper()+' worker, v3 desk/chair, all typing phases 1x/3x',fill='#191E1E')
        for p in range(4):
            scene=panels[k*4+p];board.alpha_composite(scene,(p*350,y+24));crop=scene.crop((65,45,235,163));board.alpha_composite(crop.resize((510,354),Image.Resampling.NEAREST),(p*350,y+145))
    # Separate overview avoids crop overlap, includes source-sized scenes and true 3x.
    board=Image.new('RGBA',(2048,1990),BG);d=ImageDraw.Draw(board)
    for k,f in enumerate(F):
        y=k*490;d.text((6,y+3),f.upper()+' worker - v3 desk+chair; phases 0/1/2/3 - 1x + 3x',fill='#191E1E')
        for p in range(4):
            scene=panels[k*4+p];x=p*512;crop=scene.crop((52,35,220,151));board.alpha_composite(crop,(x+4,y+22));board.alpha_composite(crop.resize((504,348),Image.Resampling.NEAREST),(x+4,y+140))
    board.save(OUT/'iso-seated-review.png');board.crop((0,0,2048,980)).save(RUN/'seated-front-review.png');board.crop((0,980,2048,1960)).save(RUN/'seated-back-review.png')
    payload={'sourceSHA256':sources,'scope':'Static Pillow serialized-part queue, v3 desk AND v3 chair. Source snapshots inside this lane. No WPF/DPI/runtime/picking claim.','roiDefinitions':{'head':'Exact opaque canonical generated-and-repaired head layer (source y<40), offset published per clip; same head denominator in every typing phase.','torso':'Opaque actor pixels excluding canonical head and hand skin, x11..22 inclusive and y<seatAnchor.y(44).','hands':'Exact palette skin pixels outside canonical head, Chebyshev radius<=2 around each of two frozen H anchors.'},'records':records,'summaries':summaries,'queues':queues,'allHeadGatesPass':all(r['headGatePass'] for r in records),'allHandGatesPass':all(r['handGatePass'] for r in records),'maxHandContactSkinDistance':max(max(r['handDistances']) for r in records),'liveSourceDrift':[p for p,h in sources.items() if sha(ROOT/p)!=h]}
    save_json(OUT/'seated-visibility.json',payload);print(json.dumps({'summaries':summaries,'allHeadGatesPass':payload['allHeadGatesPass'],'allHandGatesPass':payload['allHandGatesPass'],'liveSourceDrift':payload['liveSourceDrift']},indent=2))

def finish():
    doc,s,cells=load_export();proof=json.loads((RUN/'derivation-proof.json').read_text());vis=json.loads((OUT/'seated-visibility.json').read_text());gifproof={}
    for k,f in enumerate(F):
        im=Image.open(OUT/f'walk-{f}-3x.gif');durations=[]
        assert im.n_frames==4 and im.size==(96,192)
        for p in range(4):
            im.seek(p);durations.append(im.info['duration']);bg=Image.new('RGBA',(32,64),BG);bg.alpha_composite(cells[k+4][p]);expected=np.array(bg.resize((96,192),Image.Resampling.NEAREST).convert('RGB'))
            assert np.array_equal(np.array(im.convert('RGB')),expected),(f,p,'GIF colors/frames')
        assert durations==[120,130,120,130];gifproof[f]={'frames':4,'size':[96,192],'durationsMs':durations,'cycleMs':sum(durations),'exactNativeNearest3xRGB':True,'loop':0}
    save_json(RUN/'gif-proof.json',gifproof)
    vis['characterNormalSHA256']=sha(OUT/s['file']);vis['typingRGBAHash']=hashlib.sha256(b''.join(im.tobytes() for r in range(8,12) for im in cells[r])).hexdigest();vis['liveSourceDriftAtFinish']=[p for p,h in vis['sourceSHA256'].items() if sha(ROOT/p)!=h];save_json(OUT/'seated-visibility.json',vis)
    lines=['v3 desk + v3 chair: glasses-bun typing, 4 facings x 4 phases.','Head: canonical head alpha y<40, offset as published.','Torso: opaque actor x11..22,y<44, excluding head/hand skin.','Hands: skin outside head within Chebyshev radius 2 of frozen H.','All contacts use original source anchors and ordinary part depth queue.','', '| Worker | Head visible/total | Head % | Torso visible/total | Torso % | Hands visible/total | Hands % |','|---|---:|---:|---:|---:|---:|---:|']
    for q in vis['summaries']:
        h,t,a=[q[r] for r in ('head','torso','hands')];lines.append(f"| {q['worker'].upper()} | {h['visible']}/{h['total']} | {h['percent']:.2f} | {t['visible']}/{t['total']} | {t['percent']:.2f} | {a['visible']}/{a['total']} | {a['percent']:.2f} |")
    lines+=['','Numbers aggregate four phases; per-phase raw denominators in seated-visibility.json.','Minimum phase head equals the displayed head %; every hand/torso phase is 100%.',f"Frozen H distance max {vis['maxHandContactSkinDistance']} native pixels; live furniture drift {vis['liveSourceDriftAtFinish']}"]
    (OUT/'seated-visibility.txt').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    # Final visual sheet covers every changed native row after the last repair and eye-mask closeups.
    final=Image.new('RGBA',(2048,1100),BG);d=ImageDraw.Draw(final);changed=[4,5,6,7,25,27,29,31,35]
    for i,r in enumerate(changed):
        x=(i%4)*512;y=(i//4)*220;d.text((x+4,y+3),f"FINAL {r:02} {s['rows'][r]['name']} {s['rows'][r]['direction']} 1x/3x",fill='#191E1E')
        for p,im in enumerate(cells[r]):
            xx=x+4+p*126;final.alpha_composite(im,(xx,y+24));final.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(xx+32,y+24))
    ba=Image.open(OUT/'glasses-bun-iso-blink.png').convert('RGBA')
    for k,f in enumerate(('se','sw')):
        r=k;b=s['blink']['normalRows'].index(r);normal=cells[r][0];closed=ba.crop((0,b*64,32,(b+1)*64));mask=Image.open(OUT/f'glasses-bun-eyes-{f}.png').convert('RGBA').crop((0,r*64,32,(r+1)*64));x=512+k*512;y=440
        d.text((x+4,y+3),f.upper()+' normal / blink / eye mask (1x/3x)',fill='#191E1E')
        for j,im in enumerate([normal,closed,mask]):
            xx=x+4+j*160;final.alpha_composite(im,(xx,y+24));final.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(xx+32,y+24))
        for j,im in enumerate([normal,closed]):final.alpha_composite(im.crop((7,30,26,40)).resize((152,80),Image.Resampling.NEAREST),(x+j*176,y+140))
    ref=Image.open(RUN/'reference-board.png').convert('RGBA');final.alpha_composite(ref.crop((0,0,1200,365)),(0,700))
    final.save(RUN/'final-repair-eyes-reference-review.png')
    cmd=[sys.executable,'-B',str(ROOT/'tools/iso_check.py'),str(OUT),'--stage','final','--tolerance','0']
    cp=subprocess.run(cmd,cwd=ROOT,capture_output=True,encoding='utf-8',env={**__import__('os').environ,'PYTHONUTF8':'1','PYTHONDONTWRITEBYTECODE':'1'})
    (OUT/'validation.txt').write_text(cp.stdout+cp.stderr,encoding='utf-8');counts={k:cp.stdout.count('| '+k+' |') for k in ('PASS','SKIP','FAIL')}
    assert cp.returncode==0,(cp.returncode,cp.stdout,cp.stderr)
    timing=json.loads((RUN/'timing.json').read_text());end=datetime.datetime.now(datetime.timezone.utc);start=datetime.datetime.fromisoformat(timing['startUTC']);hours=(end-start).total_seconds()/3600
    timing.update(endUTC=end.isoformat(),laneHours=hours,definition='Single lane wall-clock occupied reading, serial generation wait, grid repairs, static review, validation. Initial reading before first clock excluded; no other lane or waits double counted.',builtInCalls=4,masterAttempts={'idle':1,'walk':2,'seat':1},imagesViewed=14)
    save_json(RUN/'timing.json',timing)
    publication={'checker':{**counts,'exitCode':cp.returncode,'command':cmd},'normalCells':152,'blinkCells':96,'canonicalHeads':proof['canonicalHeads'],'connectedCells':proof['connectedCells'],'paletteColors':len(np.unique(np.array(Image.open(OUT/s['file']))[:,:,:3][np.array(Image.open(OUT/s['file']))[:,:,3]>0],axis=0)),'idleHeight':proof['idleHeight'],'visibility':vis['summaries'],'sourceDrift':vis['liveSourceDriftAtFinish'],'gifs':gifproof,'laneHours':hours,'generationPromptFile':'runs/generation-prompts.json','masterSHA256':{n:sha(RUN/n) for n in ('idle-master-a1.png','walk-master-a2.png','seat-master-a1.png')},'filesSHA256':{p.name:sha(p) for p in OUT.glob('*') if p.is_file() and p.suffix in ('.png','.gif','.json')},'scope':'Only assets/art/iso/characters/glasses-bun writes by lane. Built-in image tool additionally manages its standard generated_images cache; no auth/config/other lane changes.'}
    save_json(RUN/'publication.json',publication)
    table='\n'.join(lines[6:12])
    note=f'''# glasses-bun ISO — C1/C2/C3 제작 결과

일반 38행/152셀(128×2432), blink 24행/96셀(128×1536), SE/SW eye mask, 동일 내용의 manifest 두 파일, 4개 iso 검수판, 네 방향 3× walk GIF·스트립을 이 폴더에 내보냈다. final checker **PASS {counts['PASS']} / SKIP {counts['SKIP']} / FAIL {counts['FAIL']}**, exit {cp.returncode}, tolerance 0. [실제 출력](validation.txt). SKIP은 캐릭터에 해당하지 않는 바닥 이음·가구 part 검사 및 blink 중복 face-lock 검사다.

Locked roster/README·face references, hero dev/professor crop과 최종 dev-hoodie-v2/punk-girl/professor를 먼저 읽고 비교했다. 쌍번 갈색 머리, 둥근 안경과 두 눈의 하이라이트, 크림 카디건·블라우스, 긴 테라코타 치마, 갈색 신발을 새 대각선 그림으로 제작했다. 42px idle 실루엣, 머리 레이어 21px로 몸의 1/3 이상이다. 모든 facing은 foot (16,60), pelvis (16,44), 고정 C/H와 transition 일정 그대로다. 사용한 RGB는 정확한 원본 identity 색과 S0 하이라이트 **17색**, alpha 0/255다.

내장 image_gen만 사용해 idle 네 방향 마스터 1회, walk SE/NE 8포즈 마스터 2회, seat 네 방향 마스터 1회, 총 **4회 직렬 생성**했다. walk 1차는 하강 자세 차이·배경 분리가 약해 2차 선택. 각 마스터 최대 3회 이내다. selected raw와 rejected walk 1차, [전체 프롬프트](runs/generation-prompts.json), 추출 box·공통 scale·고정 원점을 runs에 남겼다. 도구가 관리하는 기본 generated_images 캐시 외에 직접 쓴 파일은 이 lane뿐이다. 인증·기본 설정·공유 문서·소스·테스트·가구·pack·git은 변경하지 않았다.

생성 마스터를 연결 성분으로 추출하고 한 번 네이티브 격자로 축소했다. idle/seat는 BOX 면적 샘플 후 팔레트·이진 alpha로 정리했고, walk는 행 전체 공통 scale와 고정 column 원점/row 바닥을 사용했다. 프레임마다 bbox 피팅·재중심화하지 않았다. 머리·몸통·치마 주름·신발은 생성 그림에서 왔으며 안경/큰 눈·손바닥·소매·윤곽·접점/연결은 native pixels로 보정했다. 248개를 독립 생성했다고 주장하지 않는다.

보행은 contact/down/passing/up의 서로 다른 네 포즈, 치맛단 변화, body/head bob [0,+1,0,−1], 지지발 교대와 반대 팔 swing이다. SW/NW는 걷는 몸만 x′=32−x로 굽고 좌상단 명암을 보정했다. 머리와 착석은 각 facing의 독립 생성 레이어다. phone은 SW에서도 해부학적 같은 손으로 별도 reach를 그렸다. 머리는 clip별 고정 RGBA이며 walk의 명시적 ±1px 외에는 0px; 전환도 source head offset 0으로 누적 3px 해석 문제를 피했다. 마지막 SW 동작 보정에서 쉬는 반대 팔이 가슴을 가로지르지 않게 바꿨다.

모션 확정 **후** [blink_resync.py](blink_resync.py)가 final normal 셀 전체를 복사하고 12개 eye interior texel만 닫힌 눈으로 바꾼다. 모든 96셀의 non-eye RGBA는 normal과 동일하고 mask는 SE/SW뿐이다. 머리·머리 offset·눈 mask를 다른 몸 pose와 섞지 않는다. [blink 근거](blink-resync-proof.json).

## v3 책상 + v3 의자 가시율

workstations measure-A3-rev2/verify-desk-rig-v3 방식의 실제 serialized part/depth queue, 원래 ground key·source anchors·seat ties로 4방향×4 typing phase를 합성했다. 순서별 픽셀 ownership과 이후 occluder alpha 합집합을 교차 확인했다. 가구 입력 snapshot/hash와 개별 phase 분자는 [seated-visibility.json](seated-visibility.json), 합성은 [iso-seated-review.png](iso-seated-review.png)다. torso/hand 정의는 JSON과 txt에 명시했다.

{table}

위 분자/분모는 네 phase 합계다. head의 최소 phase 비율도 표와 같고 torso·손은 모든 phase 100%. 실제 두 H의 skin 거리는 전부 0px다. NE/NW head 85%, SE/SW 90% gate를 모두 통과했다. 완료 시 live furniture hash 차이: {len(vis['liveSourceDriftAtFinish'])}개. 다른 lane의 후속 가구 수정에는 해당하지 않는다.

## 수행한 검증과 남은 약점

[국소 근거](runs/derivation-proof.json): 152개 canonical head RGBA 비교·152개 연결 실루엣, 4방향 walk/typing 네 phase 구분, 모든 walk y60 접지, 32개 prop hand skin 접점 0px, sit/stand 정확한 역순과 idle/rest 끝점 일치. GIF는 최종 native의 정확한 3× RGB, 각 4프레임, 120/130/120/130ms·500ms 루프를 파일에서 확인했다. [GIF 근거](runs/gif-proof.json). 원본·생성본·일반 전 행·blink 전 행·v3 착석·최종 수정판 포함 **14개 이미지**를 1×/3×로 비교했다. 정적 검수는 독립적인 사람의 시각 승인을 대신하지 않는다.

정체성은 읽히지만 앞 얼굴은 약간 좁고 안경 원형·눈은 1×에서 작은 군집이다. 소매·손이 여전히 가늘고 일부 reach/typing의 전완이 곧게 보인다. 4프레임 압축 gait의 타이밍은 거칠고 두 발의 완전한 8단계 생체역학을 표현하지 못한다. 3→0 바뀐 픽셀 수는 내부 최대보다 SE 18, SW 25, NE 35, NW 37개 많다. 정적 wrap 비교를 했으나 자연스러운 재생 합격을 주장하지 않는다. sleep은 작은 seated doze라 충분한 sofa recline이 아니다. hero 수준의 부드러운 손·몸 볼륨은 아직 약하다.

WPF/app/DPI·runtime depth/picking/seat interruption, 실제 이동 속도·발 미끄러짐, GIF 플레이어에서의 연속 재생, sofa sleep 접촉, 실제 cup/paper/phone prop atlas 합성, 사람의 비대칭·정체성/모션 승인, pack 소비는 검증하지 못했다. 전체 dotnet test/빌드·git 작업·추가 에이전트는 실행하지 않았다.

## 실측 lane 시간·재현

시작 {timing['startUTC']} → 결과 기록 {timing['endUTC']}: **{hours:.4f} lane-hours ({hours*60:.2f}분)**. 단일 lane의 조사·직렬 생성 대기·native 보정·정적 검수·체커 점유 wall-clock이다. 첫 시계 관측 전의 초기 읽기는 제외했고, 다른 lane과 대기 시간을 중복 합산하지 않았다. 마지막 이미지 확인/채팅 보고 시간은 이 종료 시각 뒤다. [시계 기록](runs/timing.json). 승인된 생산 처리량으로 일반화하지 않는다.

workspace root에서 기존 sprite-gen venv Python의 `-B`로 `build_glasses_bun_lane.py normalize` → `build` → `blink_resync.py` → `verify` → `reviews` → `seated` → `finish` 순서. 모든 출력은 이 폴더 안이다. `finish`는 최종 checker와 GIF 직렬 확인·기록을 수행한다. builtin 그림을 다시 생성하지 않아도 선택 master로 재구성 가능하다.
'''
    (OUT/'NOTES-C1-C2-C3.md').write_text(note,encoding='utf-8');print(json.dumps({'checker':counts,'laneHours':hours,'sourceDrift':vis['liveSourceDriftAtFinish']},indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('command');args=parser.parse_args()
    globals()[args.command]()
