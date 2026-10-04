"""Bake generation-derived fixed parts and repaired articulated native limbs.
Run -B; all output paths are constrained to this lane.
"""
from pathlib import Path
import copy, json, hashlib, importlib.util
import numpy as np
from PIL import Image, ImageDraw
import normalize_labcoat_lane as n
OUT=Path(__file__).resolve().parent;ROOT=OUT.parents[4];CID='labcoat-tall'
spec=importlib.util.spec_from_file_location('labcoat_frozen_checker',ROOT/'tools/iso_check.py');c=importlib.util.module_from_spec(spec);spec.loader.exec_module(c)
FACINGS=list(c.FACINGS);HEADS={}
SKIN=['EB9D5C','E28E4D','F0A463','E9B07F']
def load(name):return Image.open(OUT/name).convert('RGBA')
def rgba(h):return tuple(bytes.fromhex(h.replace('#','')))+(255,)
def shift(im,dx=0,dy=0):return n.shift(im,dx,dy)
def mirror(im):return shift(im.transpose(Image.Transpose.FLIP_LEFT_RIGHT),1,0)
def relight(im):
    # Body mirror retains upper-left light through positional exact-color repaint.
    a=np.array(im);yy,xx=np.indices((64,32));mask=a[:,:,3]>0
    for src,dst in [('D6C5B7','E9DED1'),('E28E4D','EB9D5C'),('342520','34322E')]:
        chosen=mask&(xx<=16)&np.all(a[:,:,:3]==tuple(bytes.fromhex(src)),axis=2);a[chosen]=rgba(dst)
    for src,dst in [('FBF4E9','F6ECDE'),('F0A463','EB9D5C')]:
        chosen=mask&(xx>20)&np.all(a[:,:,:3]==tuple(bytes.fromhex(src)),axis=2);a[chosen]=rgba(dst)
    return Image.fromarray(a)
def guard(im):
    a=np.array(im);a[:,0]=0;a[61:]=0;a[a[:,:,3]==0]=0;return Image.fromarray(a)
def clean_detritus(im,protected):
    a=np.array(im);mask=a[:,:,3]>0;seen=np.zeros_like(mask)
    for y,x in zip(*np.where(mask)):
        if seen[y,x]:continue
        stack=[(y,x)];seen[y,x]=True;coords=[]
        while stack:
            yy,xx=stack.pop();coords.append((yy,xx))
            for dy,dx in [(-1,-1),(-1,0),(-1,1),(0,-1),(0,1),(1,-1),(1,0),(1,1)]:
                ay,ax=yy+dy,xx+dx
                if 0<=ay<64 and 0<=ax<32 and mask[ay,ax] and not seen[ay,ax]:seen[ay,ax]=True;stack.append((ay,ax))
        # Extracted loose dark fringe islands are not limbs; preserve all skin and heads.
        if len(coords)<=8 and not any(protected[yy,xx] for yy,xx in coords) and all(max(a[yy,xx,:3])<130 for yy,xx in coords):
            for yy,xx in coords:a[yy,xx]=0
    return Image.fromarray(a)
def headon(im,f,dy=0):
    if dy==-1:
        d=ImageDraw.Draw(im)
        d.line([(18,37),(18,40)],fill='#EB9D5C' if f in ('se','sw') else '#342520',width=3)
    head=shift(HEADS[f],0,dy);im.alpha_composite(head)
    return guard(clean_detritus(im,np.array(head)[:,:,3]>0))
def body(im,top=38):
    a=np.array(im);a[:top]=0;return Image.fromarray(a)
def trim_arms(im,front=True):
    a=np.array(im);yy,xx=np.indices((64,32))
    # Keep generated torso and dark trailing hair; remove limbs for joint repair.
    skin=np.zeros((64,32),bool)
    for col in SKIN:skin|=np.all(a[:,:,:3]==tuple(bytes.fromhex(col)),axis=2)
    light=np.max(a[:,:,:3],axis=2)>130
    side=(xx<12)|(xx>22)
    a[((skin|light)&side&(yy>=39)&(yy<50))]=0
    return Image.fromarray(a)
def limb(im,path,kind='coat',near=True):
    d=ImageDraw.Draw(im);d.line(path,fill='#1D1311',width=4 if kind=='coat' else 3)
    d.line(path,fill='#E9DED1' if kind=='coat' else '#E28E4D',width=2)
    if kind=='coat':
        d.line([(x-1,y) for x,y in path],fill='#FBF4E9' if near else '#D6C5B7',width=1)
    else:d.line([(x-1,y) for x,y in path],fill='#EB9D5C',width=1)
def palm(im,x,y,phase=0,flat=False,near=True):
    d=ImageDraw.Draw(im)
    if flat:
        d.line([(x-1,y-1),(x,y)],fill='#1D1311',width=3)
        d.point((x,y),fill='#EB9D5C');d.point((x,y-1),fill='#F0A463' if phase%2==0 else '#E28E4D')
        d.point((x-1,y-1),fill='#E9B07F' if near else '#EB9D5C')
    else:
        d.rectangle((x-1,y-1,x+1,y+2),fill='#1D1311')
        d.rectangle((x-1,y-1,x,y+1),fill='#EB9D5C')
        d.point((x-1,y),fill='#F0A463');d.point((x+1,y),fill='#E28E4D')
def shoe(im,x,y,near=True,heel=False):
    d=ImageDraw.Draw(im);d.line([(x-1,y-1),(x+2,y)],fill='#1D1311',width=3)
    d.line([(x,y-1),(x+2,y-1)],fill='#463C36' if near else '#342520',width=1)
    d.point((x,y-1),fill='#9F5626' if near else '#5D5149')
    if heel:d.point((x-1,y-2),fill='#5D5149')
def idle(f):
    source='se' if f in ('se','sw') else 'ne';im=trim_arms(body(load(source+'-idle-native.png')))
    limb(im,[(12,39),(10,42),(10,46)],near=True);palm(im,10,46)
    wrist=(24,44 if source=='se' else 40)
    limb(im,[(21,39),(23,41 if source=='se' else 39),wrist],near=False);palm(im,*wrist,near=False)
    if f in ('sw','nw'):im=relight(mirror(im))
    return headon(im,f)
ARMS=[([(12,39),(9,41),(8,44)],[(21,39),(23,40),(25,41)]),
      ([(12,40),(10,43),(10,45)],[(21,40),(24,42),(24,44)]),
      ([(12,39),(18,41),(26,43)],[(21,39),(15,42),(9,44)]),
      ([(12,38),(17,40),(23,43)],[(21,38),(15,40),(11,42)])]
LEGS=[([(18,51),(21,55),(24,59)],[(14,51),(11,54),(9,57)],(24,60),(9,58)),
      ([(18,52),(20,55),(20,59)],[(14,52),(11,55),(12,57)],(20,60),(12,58)),
      ([(18,51),(17,55),(16,59)],[(14,51),(21,52),(22,55)],(16,60),(22,56)),
      ([(18,50),(14,53),(11,56)],[(14,50),(19,54),(23,59)],(11,57),(23,60))]
def walk(f,p):
    source='se' if f in ('se','sw') else 'ne';raw=body(load(source+'-walk-'+str(p)+'-native.png'))
    a=np.array(raw);a[52:]=0;im=trim_arms(Image.fromarray(a));bob=[0,1,0,-1][p]
    # Generated coat/dress/hair clusters stay, translated as one articulated torso.
    im=shift(im,0,bob)
    near,far,nshoe,fshoe=LEGS[p]
    limb(im,far,'skin',False);shoe(im,*fshoe,False,p==2)
    limb(im,near,'skin',True);shoe(im,*nshoe,True,p==3)
    ap,bp=ARMS[p]
    if source=='ne':
        ap=[(32-x,y-1) for x,y in ap];bp=[(32-x,y-1) for x,y in bp]
    limb(im,bp,near=False);palm(im,*bp[-1],p,near=False)
    limb(im,ap,near=True);palm(im,*ap[-1],p)
    if f in ('sw','nw'):im=relight(mirror(im))
    return headon(im,f,bob)
SEAT_PATHS={'se':[[(12,39),(18,41),(27,44)],[(21,39),(25,40),(29,44)]],
            'ne':[[(13,39),(20,37),(27,32)],[(22,39),(26,36),(29,32)]]}
def seated(f,p=0,typing=False):
    source='se' if f in ('se','sw') else 'ne';im=trim_arms(body(load(source+'-seat-native.png')))
    d=ImageDraw.Draw(im)
    # Connect generated seated pelvis to frozen source pivot; retain bent generated legs.
    d.line([(15,42),(16,46)],fill='#296267' if source=='se' else '#E9DED1',width=3)
    if typing:
        paths=SEAT_PATHS[source]
        for j,path in enumerate(paths):
            path=copy.deepcopy(path);path[1]=(path[1][0]+[0,1,0,-1][p],path[1][1]+[0,0,1,0][p])
            limb(im,path,near=j==0)
            px,py=path[-1];palm(im,px,py-1 if source=='se' else py,p+j,flat=True,near=j==0)
        # Two contacts only 2px apart: keep the separator dark and both exact skin contacts.
        hx,hy=c.HANDS[source][0];d=ImageDraw.Draw(im);skinY=hy-1 if source=='se' else hy
        d.point((28,skinY),fill='#1D1311')
        d.point((27,skinY),fill='#EB9D5C');d.point((29,skinY),fill='#EB9D5C')
        d.point((26 if p%2==0 else 30,skinY-1),fill='#F0A463')
    else:
        limb(im,[(12,39),(12,43),(16,46)],near=True);palm(im,16,46)
        end=[(21,46),(23,44),(22,43),(21,45)][p]
        limb(im,[(21,39),(23,42),end],near=False);palm(im,*end,p,near=False)
    if f in ('sw','nw'):im=relight(mirror(im))
    return headon(im,f,-1)
def transition(f,p):
    if p==0:return idle(f)
    if p==3:return seated(f,0)
    im=body(seated(f,0));a=np.array(im);a[51:]=0;im=Image.fromarray(a)
    # Intermediate source soles and pelvis follow the frozen lowering schedule.
    sy,off,fy=c.SIT[p];d=ImageDraw.Draw(im);d.line([(16,sy-2),(16,sy+2)],fill='#296267' if f in ('se','sw') else '#E9DED1',width=3)
    for x,near in [(13,False),(20,True)]:
        limb(im,[(x,50),(x+(2 if near else -1),53),(x+1,fy-1)],'skin',near);shoe(im,x+1,fy,near)
    return headon(im,f,-1)
def action(f,name,p):
    source='se';im=trim_arms(body(load(source+'-idle-native.png')))
    # Each arm is authored in anatomical space before the opposite body bake.
    far=(24,44);near=(10,46)
    if name=='pickup':far=[(24,44),(26,47),(27,45),(24,43)][p]
    elif name=='report-handover':far=[(24,44),(26,43),(28,42),(27,42)][p]
    elif name=='coffee':far=[(24,44),(25,41),(25,39),(24,42)][p]
    elif name=='phone':near=[(10,43),(11,40),(11,41),(10,42)][p]
    elif name=='talk':far=[(24,44),(26,42),(25,40),(24,43)][p]
    elif name=='surprised':
        near=[(10,46),(7,42),(7,40),(8,41)][p];far=[(24,44),(27,41),(27,39),(26,40)][p]
    limb(im,[(12,39),(10,42),near],near=True);palm(im,*near,p)
    limb(im,[(21,39),(24,41),far],near=False);palm(im,*far,p,near=False)
    if f=='sw':im=relight(mirror(im));near=(32-near[0],near[1]);far=(32-far[0],far[1])
    grip=near if name=='phone' else far
    return headon(im,f),[{'x':grip[0],'y':grip[1]}]
def sleep(f,p):
    im=body(seated(f,0));d=ImageDraw.Draw(im)
    # Relaxed bent lower limbs; restrained seated doze, no unauthorised head rotation.
    if p%2:d.line([(15,47),(20,51)],fill='#296267',width=2)
    return headon(im,f,-1 if p<2 else 0)
def offsets(name):
    if name=='walk':return [0,1,0,-1]
    if name in ('seated-thinking','seated-typing-front'):return [-1]*4
    if name=='sleep':return [-1,-1,0,0]
    if name=='sit-down':return [0,-1,-1,-1]
    if name=='stand-up':return [-1,-1,-1,0]
    return [0]*4
def main():
    global HEADS
    HEADS={f:load('head-'+f+'.png') for f in FACINGS}
    source_palette=json.loads((OUT/'normalization-proof.json').read_text())['identityPalette']
    orig=np.array(Image.open(ROOT/'assets/art/roster/labcoat-tall/locked/labcoat-tall.png').convert('RGBA'));origin_colors={'#'+bytes(v).hex().upper() for v in np.unique(orig[:,:,:3][orig[:,:,3]>0],axis=0)}
    identity=[v for v in source_palette if v in origin_colors]
    atlas=Image.new('RGBA',(128,2432));rows=[];head_offsets={};paths={};gaits={}
    for row,(name,f,fps,loop) in enumerate(c.CONTRACT):
        clip=dict(name=name,direction=f,row=row,column=0,frames=4,fps=fps,loop=loop,sourceMaster='master-identity-attempt1.png')
        frames=[];grips=[]
        for p in range(4):
            if name=='idle':im=idle(f)
            elif name=='walk':im=walk(f,p)
            elif name=='seated-typing-front':im=seated(f,p,True)
            elif name=='seated-thinking':im=seated(f,p)
            elif name=='sit-down':im=transition(f,p)
            elif name=='stand-up':im=transition(f,3-p)
            elif name=='sleep':im=sleep(f,p)
            else:im,grip=action(f,name,p);grips.append(grip)
            frames.append(im);atlas.alpha_composite(im,(32*p,64*row))
        if name=='walk':
            clip.update(sourceMaster='master-walk-attempt2.png',phases=['contact','down','passing','up'],bodyBobByPhase=[0,1,0,-1])
            diffs=[int(np.any(np.array(frames[p])!=np.array(frames[(p+1)%4]),axis=2).sum()) for p in range(4)]
            gaits[f]=dict(adjacentChangedPixels=diffs,wrap=diffs[3],maxInternal=max(diffs[:3]),allFourDistinct=len({im.tobytes() for im in frames})==4)
            strip=Image.new('RGBA',(128,64),'#E7D1AC')
            for p,im in enumerate(frames):strip.alpha_composite(im,(32*p,0))
            strip.resize((384,192),Image.Resampling.NEAREST).save(OUT/('walk-'+f+'-strip-3x.png'))
            gifs=[]
            for im in frames:
                bg=Image.new('RGBA',(32,64),'#E7D1AC');bg.alpha_composite(im);gifs.append(bg.resize((96,192),Image.Resampling.NEAREST).convert('RGB'))
            gifs[0].save(OUT/('walk-'+f+'-3x.gif'),save_all=True,append_images=gifs[1:],duration=[120,130,120,130],loop=0,disposal=2)
        if name in ('seated-thinking','seated-typing-front','sleep'):clip['seatAnchor']={'x':16,'y':44}
        if name=='seated-typing-front':clip.update(deskOrientation={'se':'nw','sw':'ne','ne':'sw','nw':'se'}[f],seatProfile='front',handAnchors=[{'x':x,'y':y} for x,y in c.HANDS[f]])
        if name in ('sit-down','stand-up'):
            schedule=c.SIT if name=='sit-down' else c.SIT[::-1]
            clip['phaseContacts']=[dict(seatAnchor=dict(x=16,y=sy),contactOffset=dict(x=0,y=off),footY=fy) for sy,off,fy in schedule]
        if grips:clip['handAnchorsByPhase']=grips
        if name in ('pickup','report-handover','coffee','phone'):clip['prop']={'pickup':'report-paper','report-handover':'report-paper','coffee':'cup','phone':'phone'}[name]
        if f in ('sw','nw'):
            clip['mirrorOf']={'direction':'se' if f=='sw' else 'ne','axis':'x=16','integerCorrection':1,'scope':'body only; independent unmirrored canonical head; upper-left lighting repaired; phone anatomical hand retained'}
        rows.append(clip);head_offsets[name+'/'+f]=[dict(x=0,y=y) for y in offsets(name)]
    atlas.save(OUT/(CID+'-iso.png'))
    aliases=copy.deepcopy(json.loads((ROOT/'assets/art/iso/characters/punk-girl/punk-girl.iso.json').read_text())['sheets'][0]['aliases'])
    base=dict(cellW=32,cellH=64,anchor=dict(x=16,y=60),footAnchor=dict(x=16,y=60),scale=1,projection='iso2to1',identityPalette=identity)
    normal=dict(base,id=CID+'-normal',kind='character',file=CID+'-iso.png',rows=rows,aliases=aliases,headOffsetsByClip=head_offsets,headLayers={f:'head-'+f+'.png' for f in FACINGS},headRegionDescription='Generated cranial head, face, neck and upper hair through source y38; long trailing hair below is carried by generated torso.',faceLock={f:dict(eyeMask=CID+'-eyes-'+f+'.png') for f in FACINGS[:2]},blink=dict(sheetId=CID+'-blink',normalRows=c.BLINK_ROWS,durationMs=100),standingHandAnchors={f:dict(x=24 if f in ('se','ne') else 8,y=44 if f in ('se','sw') else 40) for f in FACINGS})
    blink=dict(base,id=CID+'-blink',kind='blink',file=CID+'-iso-blink.png',rows=[dict(name='blink-'+c.CONTRACT[r][0],direction=c.CONTRACT[r][1],row=b,column=0,frames=4,fps=c.CONTRACT[r][2],loop=c.CONTRACT[r][3]) for b,r in enumerate(c.BLINK_ROWS)])
    doc=dict(version=1,projection='iso2to1',requiredCapabilities=['iso2to1','renderItemsV1'],facings=FACINGS,characterCell=dict(width=32,height=64),footAnchor=dict(x=16,y=60),seatAnchor=dict(x=16,y=44),standingHeight=42,sheets=[normal,blink],characters=[dict(id='iso/character/'+CID,sheetId=CID+'-normal',seatProfiles=['front'])],production=dict(method='Built-in image generation; identity master attempt1; selected gait master attempt2; exact palette snap and binary alpha; native articulated limb/contact repair and deterministic derivation. Props separate.',headMotion='All canonical heads byte-identical at declared integer offsets; maximal source head displacement 1px. Four independent head facings; only bodies mirrored.',limitations=['Four-frame compressed gait requires human playback approval','Sleep is restrained seated doze','WPF/runtime, real prop and sofa integration unverified']))
    data=json.dumps(doc,ensure_ascii=False,indent=2)+'\n'
    for file in [CID+'.iso.json',CID+'-iso.manifest.json']:(OUT/file).write_text(data,encoding='utf-8')
    (OUT/'motion-proof.json').write_text(json.dumps({'gaits':gaits,'armControlPoints':ARMS,'legControlPoints':LEGS,'bodyBob':[0,1,0,-1],'heads':'canonical independent four directions','construction':'Real generation-derived torso/hair/coat/dress clusters, native arms and joint repair; head is not mirrored or rescaled per frame.'},indent=2)+'\n',encoding='utf-8')
    print('Built 38 normal rows / four walk GIFs. Blink must be derived AFTER this build.')
if __name__=='__main__':main()
