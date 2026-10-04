from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw
OUT=Path(__file__).resolve().parent; RUN=OUT/'runs'
def components(path):
 a=np.array(Image.open(path).convert('RGBA')); mask=a[:,:,3]>=(245 if path.name=='walk-master-a3.png' else 160)
 seen=np.zeros(mask.shape,bool); boxes=[]
 for y,x in zip(*np.where(mask)):
  if seen[y,x]:continue
  stack=[(y,x)];seen[y,x]=True; count=0; x0=x1=x;y0=y1=y
  while stack:
   yy,xx=stack.pop();count+=1;x0=min(x0,xx);x1=max(x1,xx);y0=min(y0,yy);y1=max(y1,yy)
   for dy,dx in [(-1,0),(1,0),(0,-1),(0,1)]:
    ny,nx=yy+dy,xx+dx
    if 0<=ny<mask.shape[0] and 0<=nx<mask.shape[1] and mask[ny,nx] and not seen[ny,nx]:seen[ny,nx]=True;stack.append((ny,nx))
  if count>1000:boxes.append((x0,y0,x1+1,y1+1,count))
 a[~mask]=0;a[mask,3]=255
 return Image.fromarray(a),boxes
def main():
 pal=np.array([tuple(bytes.fromhex(c[1:])) for c in json.loads((RUN/'identity-palette.json').read_text())],dtype=np.int32)
 records=[];previews=[]
 for category,count in [('idle',2),('walk',4),('seat',2)]:
  path=RUN/(category+'-master-'+('a3' if category=='walk' else 'a1')+'.png')
  if not path.exists():continue
  source,boxes=components(path);assert len(boxes)==count,(category,boxes)
  if count==4:
   boxes=sorted(boxes,key=lambda b:(b[1]+b[3])/2);boxes=sorted(boxes[:2])+sorted(boxes[2:])
  else:boxes.sort()
  scale=min(40/max(b[3]-b[1] for b in boxes),29/max(b[2]-b[0] for b in boxes))
  for k,b in enumerate(boxes):
   f='se' if k<(2 if count==4 else 1) else 'ne';p=(k%2)*2
   name=f+'-'+category+(f'-{p}' if count==4 else '')
   raw=source.crop(b[:4]);w,h=round(raw.width*scale),round(raw.height*scale)
   native=raw.resize((w,h),Image.Resampling.NEAREST);dest=(16-w//2,(57 if category=='seat' else 61)-h)
   result=Image.new('RGBA',(32,64));result.alpha_composite(native,dest);a=np.array(result);m=a[:,:,3]>0
   colors=a[:,:,:3][m].astype(np.int32);a[:,:,:3][m]=pal[((colors[:,None,:]-pal[None,:,:])**2).sum(2).argmin(1)]
   a[61:]=0;a[:,0]=0;a[a[:,:,3]==0]=0;result=Image.fromarray(a);result.save(RUN/(name+'-native.png'))
   records.append(dict(name=name,inputComponentBox=[int(v) for v in b],groupScale=float(scale),nativeSize=[int(w),int(h)],placement=[int(v) for v in dest]))
   previews.append((name,result))
   if count==4:
    # Adjacent down/up drawing source; fixed rig performs the integer bob and joint repair.
    result.save(RUN/(f+'-walk-'+str(p+1)+'-native.png'))
 board=Image.new('RGBA',(max(1,len(previews))*136,250),'#E7D1AC');d=ImageDraw.Draw(board)
 for k,(n,im) in enumerate(previews):
  x=k*136;d.text((x+3,4),n,fill='#191E1E');board.alpha_composite(im,(x+3,26));board.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+37,26))
 board.save(RUN/'native-masters-review.png');(RUN/'normalization.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
 print([(r['name'],r['nativeSize']) for r in records])
if __name__=='__main__':main()
