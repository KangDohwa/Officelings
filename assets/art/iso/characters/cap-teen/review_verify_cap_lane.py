"""Own-lane QA, v3 alpha ownership measurements and review exports, no runtime writes."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,importlib.util,json,subprocess,sys,os
import numpy as np
from PIL import Image,ImageDraw
OUT=Path(__file__).resolve().parent;RUN=OUT/'runs';ROOT=OUT.parents[4]
F=['se','sw','ne','nw'];BG='#E7D1AC';INK='#191E1E'
def read(p):return json.loads(p.read_text(encoding='utf-8'))
def rgba(p):return Image.open(p).convert('RGBA')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def cell(a,r,p=0):return a.crop((p*32,r*64,(p+1)*32,(r+1)*64))
def shift(im,x=0,y=0):
 o=Image.new('RGBA',im.size);o.alpha_composite(im,(x,y));return o
def alpha(im):return np.array(im)[:,:,3]>0
def write_gif(frames,path):
 arrays=[np.array(im.convert('RGBA')) for im in frames]
 colors=np.unique(np.concatenate([a[:,:,:3][a[:,:,3]>0] for a in arrays]),axis=0)
 assert len(colors)<255
 palette=[0,0,0]+colors.flatten().tolist();palette+=[0]*(768-len(palette));indexed=[]
 for a in arrays:
  indices=np.zeros(a.shape[:2],np.uint8)
  for k,col in enumerate(colors,1):indices[(a[:,:,:3]==col).all(2)&(a[:,:,3]>0)]=k
  im=Image.fromarray(indices);im.putpalette(palette);im.info['transparency']=0;indexed.append(im)
 indexed[0].save(path,save_all=True,append_images=indexed[1:],duration=[120,130,120,130],loop=0,disposal=2,transparency=0,optimize=False)
def pastepair(b,im,x,y):
 b.alpha_composite(im,(x,y));b.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+34,y))
def components(mask):
 seen=np.zeros_like(mask);counts=[]
 for y,x in zip(*np.where(mask)):
  if seen[y,x]:continue
  stack=[(y,x)];seen[y,x]=True;count=0
  while stack:
   yy,xx=stack.pop();count+=1
   for dy,dx in [(-1,-1),(-1,0),(-1,1),(0,-1),(0,1),(1,-1),(1,0),(1,1)]:
    ny,nx=yy+dy,xx+dx
    if 0<=ny<64 and 0<=nx<32 and mask[ny,nx] and not seen[ny,nx]:seen[ny,nx]=True;stack.append((ny,nx))
  counts.append(count)
 return sorted(counts,reverse=True)
def verify(d,a):
 s=d['sheets'][0];heads={f:rgba(RUN/('head-'+f+'.png')) for f in F};report={'canonicalHeadCells':0,'connectedBodyCells':0,'distinctWalkPhases':{},'distinctTypingPhases':{},'walkWrapPixels':{},'transitionReversal':{},'standingHeights':{}}
 for r,c in enumerate(s['rows']):
  f=c['direction'];offs=s['headOffsetsByClip'][c['name']+'/'+f]
  for p in range(4):
   im=cell(a,r,p);head=shift(heads[f],offs[p]['x'],offs[p]['y']);hm=alpha(head)
   assert np.array_equal(np.array(im)[hm],np.array(head)[hm]),('changed canonical head',r,p)
   report['canonicalHeadCells']+=1
   islands=components(alpha(im));assert len(islands)==1,('detached body pixels',r,p,islands)
   report['connectedBodyCells']+=1
 for k,f in enumerate(F):
  yy,xx=np.where(alpha(cell(a,k)));report['standingHeights'][f]=int(yy.max()-yy.min()+1)
  for act,r0,target in [('walk',4,'distinctWalkPhases'),('typing',8,'distinctTypingPhases')]:
   imgs=[cell(a,r0+k,p) for p in range(4)];report[target][f]=len({im.tobytes() for im in imgs});assert report[target][f]==4
  walk=[np.array(cell(a,4+k,p)) for p in range(4)]
  changes=[int(np.any(walk[p]!=walk[(p+1)%4],axis=2).sum()) for p in range(4)]
  report['walkWrapPixels'][f]={'steps0123and30':changes,'wrapWithinInternalMax':changes[-1]<=max(changes[:-1])}
  assert np.array_equal(np.array(cell(a,16+k,0)),np.array(cell(a,k,0)))
  assert np.array_equal(np.array(cell(a,16+k,3)),np.array(cell(a,12+k,0)))
  report['transitionReversal'][f]=all(cell(a,16+k,p).tobytes()==cell(a,20+k,3-p).tobytes() for p in range(4));assert report['transitionReversal'][f]
  frames=[cell(a,4+k,p).resize((96,192),Image.Resampling.NEAREST) for p in range(4)]
  write_gif(frames,OUT/('walk-'+f+'-3x.gif'))
  strip=Image.new('RGBA',(384,192),BG)
  for p,im in enumerate(frames):strip.alpha_composite(im,(96*p,0))
  strip.save(OUT/('walk-'+f+'-3x.png'))
 (RUN/'derivation-proof.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print('QA',report)
 return heads
def reviews(d,a):
 s=d['sheets'][0];heroes=[]
 for p in ['assets/art/iso/characters/dev-hoodie-v2/runs/v2/hero-dev-crop.png','assets/art/iso/characters/punk-girl/runs/hero-worker-crop.png','assets/art/iso/characters/professor/runs/v1/hero-professor-crop.png']:
  im=rgba(ROOT/p);im=im.resize((round(im.width*40/im.height),40),Image.Resampling.NEAREST);heroes.append(im)
 pilotpaths=[('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]
 pilots=[rgba(ROOT/f'assets/art/iso/characters/{p}/{cid}-iso.png') for p,cid in pilotpaths]
 contact=Image.new('RGBA',(1160,908),BG);dr=ImageDraw.Draw(contact)
 dr.text((10,10),'CAP-TEEN / native 1x + NEAREST3x / IDLE, CONTACT, DOWN, PASSING, UP / 3 finished pilots and hero crops',fill=INK)
 for k,f in enumerate(F):
  y=36+k*216;dr.text((5,y+10),f.upper(),fill=INK)
  for p,im in enumerate([cell(a,k)]+[cell(a,4+k,p) for p in range(4)]):pastepair(contact,im,38+p*132,y)
  for j,pa in enumerate(pilots):
   im=cell(pa,k);contact.alpha_composite(im,(718+j*132,y));contact.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(752+j*132,y))
   contact.alpha_composite(heroes[j],(714+j*132,y+164));dr.text((716+j*132,y+204),pilotpaths[j][0],fill=INK)
 contact.save(OUT/'iso-contact.png')
 pages=[]
 for page,start in enumerate(range(0,38,10),1):
  b=Image.new('RGBA',(1340,1060),BG);dr=ImageDraw.Draw(b);dr.text((10,8),f'CAP-TEEN ALL STORED ROWS / PAGE {page} / normal 1x and 3x / props and bubbles separate',fill=INK)
  for n,r in enumerate(range(start,min(start+10,38))):
   x=(n%2)*670;y=30+(n//2)*202;c=s['rows'][r]
   dr.text((x+4,y+4),f'{r:02d} {c["name"]}',fill=INK);dr.text((x+4,y+22),c['direction'].upper(),fill=INK)
   for p in range(4):pastepair(b,cell(a,r,p),x+134+p*132,y)
  b.save(RUN/f'rows-review-page-{page}.png');pages.append(b)
 allrows=Image.new('RGBA',(2680,2120),BG)
 for k,b in enumerate(pages):allrows.alpha_composite(b,((k%2)*1340,(k//2)*1060))
 allrows.save(OUT/'iso-rows-review.png')
 flat=rgba(ROOT/'assets/art/roster/cap-teen/locked/cap-teen.png');blink=rgba(OUT/'cap-teen-iso-blink.png')
 compare=Image.new('RGBA',(1160,1180),BG);dr=ImageDraw.Draw(compare)
 dr.text((8,8),'LOCKED FLAT / ISO IDLE / ISO SEAT / PHASE-MATCHED BLINK / PILOTS AND HERO / 1x + 3x',fill=INK)
 for k,f in enumerate(F):
  y=34+k*216;flatcol=[2,1,3,3][k]
  ims=[cell(flat,0,flatcol),cell(a,k),cell(a,8+k),cell(blink,k) if k<2 else cell(a,8+k,2)]
  for j,im in enumerate(ims):pastepair(compare,im,14+j*170,y)
  for j,pa in enumerate(pilots):
   pastepair(compare,cell(pa,k),720+j*140,y)
   compare.alpha_composite(heroes[j],(721+j*140,y+164))
 dr.text((8,916),'FINAL TRANSITION REPAIR / four facings / lowering middle phases1,2 / original shirt folds retained / 1x +3x',fill=INK)
 for k,f in enumerate(F):
  for j,p in enumerate([1,2]):pastepair(compare,cell(a,16+k,p),12+k*286+j*132,944)
  dr.text((12+k*286,1146),f.upper()+' phase1 / phase2',fill=INK)
 compare.save(OUT/'iso-vs-flat.png')
def measure(d,a,heads):
 spec=importlib.util.spec_from_file_location('contact_method_readonly',ROOT/'assets/art/iso/furniture-v2/workstations/measure-A3-rev2.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
 snap=RUN/'furniture-v3-snapshot';snap.mkdir(exist_ok=True);sourcehash={}
 def furniture(folder,name):
  root=ROOT/f'assets/art/iso/furniture-v3/{folder}';path=root/(name+'.iso.json');raw=path.read_bytes();sourcehash[str(path.relative_to(ROOT))]=sha(path);(snap/(name+'.json')).write_bytes(raw)
  doc=json.loads(raw);sheets={}
  for s in doc['sheets']:
   p=root/s['file'];sourcehash[str(p.relative_to(ROOT))]=sha(p);(snap/s['file']).write_bytes(p.read_bytes());sheets[s['id']]=(s,rgba(snap/s['file']))
  return doc['furniture'][0],sheets
 desk,ds=furniture('workstations','desk');chair,cs=furniture('chair','chair');records=[];queues=[];s=d['sheets'][0]
 board=Image.new('RGBA',(1792,1100),BG);dr=ImageDraw.Draw(board)
 dr.text((8,8),'CAP-TEEN TYPING / V3 DESK + CHAIR / all16 cells / alpha-ownership head, torso, hand ROI / 1x + NEAREST3x',fill=INK)
 for di,f in enumerate(F):
  v=m.variant(desk,f);profile=v['seatProfiles'][0];wf=profile['isoFacing'];ground=profile['seatGround'];sd=sum(ground.values());origin=(160,128);gp=m.project(ground['i'],ground['j']);co=(origin[0]+gp[0],origin[1]+gp[1]);cv=m.variant(chair,wf)
  items=m.parts(v,ds,origin,0,sd,'desk')+m.parts(cv,cs,co,sd,sd,'chair');clip=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==wf)
  contact=m.point(profile['contactOffset']);pos=(origin[0]+contact[0]-16,origin[1]+contact[1]-44);key=(sd,0,'actor','cap-teen')
  assert [(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in profile['handContacts']]
  queues.append({'desk':f,'worker':wf,'actorPosition':pos,'actorKey':key,'items':[{'part':it['id'],'key':it['key'],'position':it['pos']} for it in sorted(items,key=lambda q:q['key'])]})
  for phase in range(4):
   sprite=m.cell(s,a,clip,phase);aa=np.array(sprite);op=alpha(sprite);off=m.point(s['headOffsetsByClip'][clip['name']+'/'+wf][phase]);hm=alpha(shift(heads[wf],*off));yy,xx=np.indices(op.shape)
   skin=np.zeros_like(op)
   for h in ['BD6C3D','DC7C3D','E8944B','EE9E51','ECA661']:skin|=np.all(aa[:,:,:3]==tuple(bytes.fromhex(h)),2)
   hands=np.zeros_like(op);dist=[]
   for h in clip['handAnchors']:
    radius=np.maximum(abs(xx-h['x']),abs(yy-h['y']));hands|=(radius<=2)&skin&op&~hm;dist.append(int(radius[skin&op&~hm].min()))
   assert max(dist)<=1 and hands.any()
   torso=op&~hm&~hands&(11<=xx)&(xx<=22)&(yy<44)
   masks={'head':hm,'torso':torso,'hands':hands};canvas=Image.new('RGBA',(320,256),BG);labels=Image.new('L',canvas.size);cover=np.zeros((64,32),bool)
   layers=items+[dict(key=key,image=sprite,pos=pos,id='actor')]
   for it in sorted(layers,key=lambda q:q['key']):
    canvas.alpha_composite(it['image'],it['pos']);labels.paste(1 if it['id']=='actor' else 2,it['pos'],it['image'].getchannel('A'))
    if it['id']!='actor' and it['key']>key:cover|=m.local_mask(alpha(it['image']),it,pos)
   owned=np.array(labels.crop((*pos,pos[0]+32,pos[1]+64)))==1;assert np.array_equal(owned&op,~cover&op)
   ratios={}
   for n,mask in masks.items():
    total=int(mask.sum());visible=int((mask&owned).sum());ratios[n]={'visible':visible,'total':total,'percent':round(visible*100/total,4)}
   headgate=ratios['head']['percent']>=(85 if wf in ['ne','nw'] else 90);handgate=ratios['hands']['percent']==100
   occluders=[]
   for it in items:
    if it['key']<=key:continue
    mask=m.local_mask(alpha(it['image']),it,pos);counts={n:int((rm&mask).sum()) for n,rm in masks.items()}
    if any(counts.values()):
     coords={n:[[int(x),int(y)] for y,x in zip(*np.where(rm&mask))] for n,rm in masks.items() if counts[n]}
     occluders.append({'part':it['id'],'key':it['key'],'counts':counts,'coordinates':coords})
   records.append({'desk':f,'worker':wf,'phase':phase,'rois':ratios,'handSkinDistances':dist,'headGatePASS':headgate,'handGatePASS':handgate,'occluders':occluders})
   crop=canvas.crop((64,32,256,176));x=di*448;y=30+phase*264
   board.alpha_composite(crop,(x,y+40))
   zoom=canvas.crop((pos[0]-14,pos[1]+16,pos[0]+46,pos[1]+80));board.alpha_composite(zoom.resize((180,192),Image.Resampling.NEAREST),(x+214,y+24))
   dr.text((x+4,y),f'DESK {f.upper()} / WORKER {wf.upper()} / phase {phase}',fill=INK)
   dr.text((x+4,y+236),' / '.join(n+' '+str(round(ratios[n]['percent'],1))+'%' for n in ['head','torso','hands']),fill=INK)
 board.save(OUT/'iso-seated-review.png')
 result={'measuredUtc':datetime.now(timezone.utc).isoformat(),'sourceSha256':sourcehash,'sourceStillMatches':{p:sha(ROOT/p)==h for p,h in sourcehash.items()},'method':'same S0 ground+seat split-part queue and final-alpha ownership cross-check as v3 verify script, using actual v3 desk AND v3 chair','roiDefinitions':{'head':'canonical head PNG alpha at declared offset; fixed denominator, no overlap-dependent cropping','torso':'opaque character, excluding canonical head and hands, x11..22 inclusive and y<44','hands':'skin colors in Chebyshev radius2 of frozen keyboard pair, excluding canonical head; contact skin distance<=1'},'records':records,'queues':queues}
 (RUN/'visibility-v3.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
 for wf in F:
  rows=[r for r in records if r['worker']==wf];print(wf,'head',min(r['rois']['head']['percent'] for r in rows),'torso',min(r['rois']['torso']['percent'] for r in rows),'hands',min(r['rois']['hands']['percent'] for r in rows))
 passed=all(r['headGatePASS'] and r['handGatePASS'] for r in records)
 print('V3 VISIBILITY GATE:', 'PASS' if passed else 'FAIL; see exact alpha ownership and occluders in visibility-v3.json')
 return passed
def main():
 d=read(OUT/'cap-teen.iso.json');a=rgba(OUT/'cap-teen-iso.png');heads=verify(d,a);reviews(d,a);vis=measure(d,a,heads)
 env=dict(os.environ,PYTHONIOENCODING='utf-8',PYTHONDONTWRITEBYTECODE='1')
 r=subprocess.run([sys.executable,'-B',str(ROOT/'tools/iso_check.py'),str(OUT),'--stage','final','--tolerance','0'],capture_output=True,text=True,encoding='utf-8',env=env)
 (OUT/'validation.txt').write_text(r.stdout+r.stderr,encoding='utf-8');print(r.stdout);assert r.returncode==0
 if not vis:sys.exit(2)
if __name__=='__main__':main()
