"""Cap-teen drawing-derived fixed rig. Writes only this lane. Run Python -B."""
from pathlib import Path
import hashlib, importlib.util, json
import numpy as np
from PIL import Image, ImageDraw
OUT=Path(__file__).resolve().parent;RUN=OUT/'runs';ROOT=OUT.parents[4];CID='cap-teen'
spec=importlib.util.spec_from_file_location('iso_check_readonly',ROOT/'tools/iso_check.py');check=importlib.util.module_from_spec(spec);spec.loader.exec_module(check)
F=check.FACINGS
def c(h):return tuple(bytes.fromhex(h))+(255,)
O=c('1C0D0E');T=[c(h) for h in ['CE7928','DF942B','E6A12F','FAC347','F3C66C']]
S=[c(h) for h in ['BD6C3D','DC7C3D','E8944B','EE9E51','ECA661']]
P=[c(h) for h in ['302225','654D2A','786F3A']];R=[c(h) for h in ['B0271F','D33725','E04326']];CR=c('F6E4C0')
def empty():return Image.new('RGBA',(32,64))
def shift(im,x=0,y=0):
 out=empty();out.alpha_composite(im,(x,y));return out
def region(im,box):
 out=empty();out.paste(im.crop(box),box[:2]);return out
def clean(im):
 a=np.array(im);a[:,0]=0;a[61:]=0;a[a[:,:,3]==0]=0;return Image.fromarray(a)
def line(im,pts,col,w=1):ImageDraw.Draw(im).line(pts,fill=col,width=w)
def rect(im,box,col):ImageDraw.Draw(im).rectangle(box,fill=col)
def poly(im,pts,col):ImageDraw.Draw(im).polygon(pts,fill=col)
def mirror(im,relight=True):
 a=np.array(shift(im.transpose(Image.Transpose.FLIP_LEFT_RIGHT),1));original=a.copy()
 if relight:
  for ramp in [T,S,P,R,[c('2D2C40'),c('3B3E54'),c('53596B')]]:
   for i,col in enumerate(ramp):
    if i<1:continue
    yy,xx=np.where((original==col).all(2))
    for y,x in zip(yy,xx):
     if x<16:a[y,x]=ramp[min(len(ramp)-1,i+1)]
     elif i>1:a[y,x]=ramp[i-1]
 return clean(Image.fromarray(a))
def arm(im,shoulder,elbow,hand,near=True):
 line(im,[shoulder,elbow],O,5);line(im,[shoulder,elbow],T[2] if near else T[1],3)
 line(im,[(shoulder[0]-1,shoulder[1]),(elbow[0]-1,elbow[1])],T[3] if near else T[2])
 line(im,[elbow,hand],O,3);line(im,[elbow,hand],S[2] if near else S[0],2)
 x,y=hand;poly(im,[(x-1,y-1),(x+1,y-1),(x+1,y+1),(x,y+2),(x-1,y+1)],S[1])
 line(im,[(x-1,y),(x,y+1)],S[3] if near else S[2]);im.putpixel((x,y),S[4] if near else S[2])
MASTER={f:Image.open(RUN/(f+'-idle-native.png')).convert('RGBA') for f in ('se','ne')}
HEAD={f:region(MASTER[f],(0,0,32,38)) for f in ('se','ne')}
# Both visible eyes: 2x3 clusters, square catchlights. Repair only once before derivation.
EYE_ORIGINS=[(17,33),(22,32)];EYE_COORDS=[(x+dx,y+dy) for x,y in EYE_ORIGINS for dx in range(2) for dy in range(3)]
for x,y in EYE_ORIGINS:
 rect(HEAD['se'],(x,y,x+1,y+2),c('3D1B13'));HEAD['se'].putpixel((x,y),CR);HEAD['se'].putpixel((x+1,y+1),O)
HEAD['sw']=mirror(HEAD['se']);HEAD['nw']=mirror(HEAD['ne'])
# Catchlights stay on screen-upper-left after a baked facing mirror.
for x,y in EYE_ORIGINS:
 for dx in range(2):HEAD['sw'].putpixel((32-x-dx,y),c('3D1B13'))
 HEAD['sw'].putpixel((31-x,y),CR)
for f in F:HEAD[f].save(RUN/('head-'+f+'.png'))
GRIPS={'se':(24,44),'sw':(8,44),'ne':(24,40),'nw':(8,40)}
IDLE={};REST={}
for f in ('se','ne'):
 im=region(MASTER[f],(0,38,32,61));a=np.array(im);a[38:50,:12]=0;a[38:50,23:]=0;im=Image.fromarray(a)
 arm(im,(12,40),(10,44),(11,47),False);arm(im,(22,40),(24,43),GRIPS[f]);im.alpha_composite(HEAD[f]);IDLE[f]=clean(im)
for f in ('sw','nw'):
 bf='se' if f=='sw' else 'ne';IDLE[f]=mirror(IDLE[bf]);IDLE[f].alpha_composite(HEAD[f])
PROP={
 'pickup':[(24,44),(27,49),(27,46),(24,44)],
 'report-handover':[(24,44),(26,43),(28,42),(24,44)],
 'coffee':[(24,44),(24,40),(25,36),(24,44)],
 'phone':[(24,44),(25,40),(25,35),(25,35)],
 'talk':[(24,44),(26,42),(25,39),(24,44)],
 'surprised':[(24,44),(26,40),(26,39),(24,42)]}
def shoe(im,x,y,lift=False,near=True):
 # Compact red sneaker with cream toe, two-pixel ankle and sole, no baked shadow.
 poly(im,[(x-2,y-3),(x+1,y-3),(x+2,y-1),(x+4,y-1),(x+4,y),(x+2,y+1),(x-2,y+1)],O)
 rect(im,(x-1,y-2,x+1,y-1),R[2] if near else R[0]);line(im,[(x,y-2),(x+1,y-2)],CR)
 line(im,[(x-1,y+1),(x+3,y+1)],CR);line(im,[(x+2,y),(x+3,y)],CR)
 if lift:im.putpixel((x-2,y+1),R[0])
def walk(f,p):
 # Preserve the real generated hands, knees, calves and sneaker volumes.
 bf='se' if f in ('se','sw') else 'ne';back=bf=='ne';bob=[0,1,0,-1][p]
 source=Image.open(RUN/(bf+f'-walk-{0 if p<2 else 2}-native.png')).convert('RGBA')
 upper_source=Image.open(RUN/(bf+'-walk-0-native.png')).convert('RGBA') if p==3 else source
 upper=shift(region(upper_source,(0,38,32,51)),y=bob);lower=region(source,(0,51,32,61));im=empty()
 if p==1:
  # Compression and heel lift from contact; the near shoe stays on y60.
  split=16 if not back else 17
  moving=region(lower,(0,51,split,61)) if not back else region(lower,(split,51,32,61))
  planted=region(lower,(split,51,32,61)) if not back else region(lower,(0,51,split,61))
  lower=empty();lower.alpha_composite(planted);lower.alpha_composite(shift(moving,x=1 if not back else -1,y=-1))
 elif p==3:
  # Rising before next contact; swing calf moves one integer pixel along its arc.
  moving=region(lower,(0,51,15,61)) if not back else region(lower,(19,51,32,61))
  planted=region(lower,(15,51,32,61)) if not back else region(lower,(0,51,19,61))
  lower=empty();lower.alpha_composite(planted);lower.alpha_composite(shift(moving,x=1 if not back else -1,y=1))
 im.alpha_composite(lower);im.alpha_composite(upper)
 # Reconnect the 1px bob under the immutable generated neck, and hip seam only.
 rect(im,(16,37,19,40),S[2]);rect(im,(14,48+bob,20,51),P[1])
 if p in (1,3):line(im,[(15,50),(19,50)],P[2])
 # Native hand cleanup keeps a readable warm knuckle against the mustard sleeve.
 if not back:
  wrist=[(10,43),(11,44),(22,44),(23,43)][p]
 else:wrist=[(23,42),(22,43),(25,43),(24,42)][p]
 if im.getpixel(wrist)[3]:im.putpixel(wrist,S[3])
 # The largest imported shoe is already planted; maintain its common source baseline.
 a=np.array(im)
 if not a[60,4:29,3].any():
  yy,xx=np.where(a[57:60,:,3]>0);x=int(xx[np.argmax(yy)]);line(im,[(x-1,60),(x+2,60)],CR)
 if f in ('sw','nw'):im=mirror(im)
 im.alpha_composite(HEAD[f]);return clean(im),0
def rest(f,p=0,typing=False,sleep=False):
 bf='se' if f in ('se','sw') else 'ne';source=Image.open(RUN/(bf+'-seat-native.png')).convert('RGBA')
 im=region(source,(0,37,32,57));a=np.array(im);a[37:47,:12]=0;a[37:47,23:]=0;im=Image.fromarray(a)
 rect(im,(13,38,22,40),T[2]);rect(im,(14,42,19,45),P[1])
 if typing:
  y=43 if bf=='se' else 32
  # Shoulders connect to forward elbows; two distinct palms reach exact contacts.
  elbows=[(19,42),(25,42)] if bf=='se' else [(19,38),(25,36)]
  for idx in [0,1]:
   wrist=(26 if idx==0 else 29,y);arm(im,(12 if idx==0 else 22,39),elbows[idx],wrist,idx==1)
  for idx,x in enumerate([27,29]):
   yy=y+([[0,0],[-1,0],[0,-1],[0,0]] if bf=='se' else [[0,0],[-1,0],[0,-1],[1,0]])[p][idx]
   # Skin contact lip within radius1; asymmetric knuckles produce alternating presses.
   rect(im,(x-1,yy-1,x,yy),S[2]);im.putpixel((x,y),S[3]);im.putpixel((x-1,yy-1),S[4])
  im.putpixel((28,y),O)
  if bf=='se':
   # A raised fingertip is within1px of H. Palm lower pixels stop at the keyboard lip.
   a=np.array(im);a[44:47,25:]=0;im=Image.fromarray(a);line(im,[(25,44),(30,44)],O)
   im.putpixel((25,42+p%2),S[3])
 else:
  arm(im,(12,39),(12,42),(16,45),False)
  wrist=[(22,44),(23,41),(24,38),(22,43)][p];arm(im,(22,38),(24,41),wrist)
 if sleep:
  lower=region(im,(0,47,32,57));a=np.array(im);a[47:]=0;im=Image.fromarray(a);im.alpha_composite(shift(lower,1,-1))
 if f in ('sw','nw'):im=mirror(im)
 im.alpha_composite(shift(HEAD[f],y=-1))
 if typing:
  for x,y in check.HANDS[f]:im.putpixel((x,y-1 if f in ('se','sw') else y),S[3])
 return clean(im),-1
def frame(f,action,p):
 if action=='walk':return walk(f,p)
 if action in ('seated-thinking','seated-typing-front','sleep'):return rest(f,p,action=='seated-typing-front',action=='sleep')
 if action in ('sit-down','stand-up'):
  q=3-p if action=='stand-up' else p
  if q==0:return IDLE[f].copy(),0
  if q==3:return rest(f,0)
  im=empty();low=region(IDLE[f] if q==1 else rest(f,0)[0],(0,46,32,61 if q==1 else 57))
  if q==2:low=shift(low,y=2)
  im.alpha_composite(low);im.alpha_composite(region(IDLE[f],(0,38,32,48)))
  # Keep the generated shirt folds; only reconnect the hip seam.
  rect(im,(14,47,20,49),P[1]);hy=0 if q==1 else -1
  if hy==-1:rect(im,(16,36,19,39),S[2])
  im.alpha_composite(shift(HEAD[f],y=hy));return clean(im),hy
 if action=='idle':return IDLE[f].copy(),0
 bf='se' if f in ('se','sw') else 'ne';im=region(IDLE[bf],(0,38,32,61));a=np.array(im);a[38:50,:12]=0;a[38:50,23:]=0;im=Image.fromarray(a)
 left=(11,47);right=PROP[action][p]
 if action=='surprised':left=[(11,47),(9,42),(9,40),(10,44)][p]
 # Phone remains in anatomical right hand, even after a facing mirror.
 if action=='phone' and f=='sw':left=[(8,44),(7,40),(7,35),(7,35)][p];right=(24,44)
 arm(im,(12,40),(10,43),left,False);arm(im,(22,39),(24,42),right)
 if f in ('sw','nw'):im=mirror(im)
 im.alpha_composite(HEAD[f]);return clean(im),0
def rename(v):
 if isinstance(v,str):return v.replace('dev-hoodie-v2',CID).replace('dev-hoodie',CID)
 if isinstance(v,list):return [rename(x) for x in v]
 if isinstance(v,dict):return {k:rename(x) for k,x in v.items()}
 return v
def main():
 doc=rename(json.loads((ROOT/'assets/art/iso/characters/dev-hoodie-v2/dev-hoodie.iso.json').read_text(encoding='utf-8')))
 normal,blink=doc['sheets'];pal=json.loads((RUN/'identity-palette.json').read_text())
 for s in doc['sheets']:s['identityPalette']=pal
 normal['headOffsetsByClip']={};normal['file']=CID+'-iso.png';normal['id']=CID+'-normal';blink['id']=CID+'-blink';normal['blink']['sheetId']=blink['id'];blink['file']=CID+'-iso-blink.png'
 atlas=Image.new('RGBA',(128,2432));masks={f:Image.new('RGBA',atlas.size) for f in ('se','sw')};offsets={}
 for r,(name,f,fps,loop) in enumerate(check.CONTRACT):
  clip=normal['rows'][r];clip.pop('motionRevision',None);clip.pop('handRepair',None)
  category='walk' if name=='walk' else 'seat' if name.startswith('seated') or name=='sleep' else 'idle'
  clip['sourceMaster']='runs/'+category+'-master-'+('a3' if category=='walk' else 'a1')+'.png'
  if 'mirrorOf' in clip:clip['mirrorOf']['repair']=['x=32-x, baked +1px anchor restoration','upper-left material relighting','symmetric headphones/cap; no lettering; strap only visible front','phone uses same anatomical right hand']
  for p in range(4):
   im,hy=frame(f,name,p);atlas.paste(im,(p*32,r*64));offsets[r,p]=hy
   normal['headOffsetsByClip'].setdefault(name+'/'+f,[]).append({'x':0,'y':hy})
   if f in masks:
    for x,y in EYE_COORDS:
     if f=='sw':x=32-x
     masks[f].putpixel((p*32+x,r*64+y+hy),(255,255,255,255))
  if name in PROP:
   hands=PROP[name]
   if f=='sw':hands=[(32-x,y) for x,y in hands] if name!='phone' else [(24,44),(25,40),(25,35),(25,35)]
   clip['handAnchorsByPhase']=[[{'x':x,'y':y}] for x,y in hands]
  if name=='walk':clip['motionRevision']={'phases':['contact','down','passing','up'],'bodyBobPixels':[0,1,0,-1],'headOffset':0,'repair':'real generated contact/passing limbs and torso retained; integer compression, heel/swing arc, hip/neck seam and knuckle repair; four-frame compressed gait'}
 atlas.save(OUT/(CID+'-iso.png'))
 for f,m in masks.items():m.save(OUT/(CID+'-eyes-'+f+'.png'))
 doc['production']={'method':'built-in image generation, serial key-pose masters; component extraction; shared scale per master, palette snap and binary alpha; fixed generated head/body clusters plus native joint/contact repairs','stage':'final','standingReferenceHeight':40,'sourceIdentity':'assets/art/roster/cap-teen/locked/cap-teen.png','light':'upper-left','seatProfiles':['front'],'rejectedSeatProfiles':['end'],'headOffsets':'idle/walk/action0; stable seated/sleep-1; sit0,0,-1,-1; stand reversed; global maximum shift1px','visualAcceptance':'local static review; runtime and independent motion acceptance not verified'}
 text=json.dumps(doc,indent=2)+'\n'
 for fn in [CID+'.iso.json',CID+'-iso.manifest.json']:(OUT/fn).write_text(text,encoding='utf-8')
 (RUN/'eye-definition.json').write_text(json.dumps({'seOrigins':EYE_ORIGINS,'sePixels':EYE_COORDS,'skin':'E8944B','closedLine':'66311E'},indent=2),encoding='utf-8')
 print('Published 152 normal cells, eye masks, frozen row metadata. Run blink_resync.py AFTER motion final.')
if __name__=='__main__':main()
