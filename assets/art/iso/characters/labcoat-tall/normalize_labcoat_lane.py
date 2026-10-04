"""One fixed native-grid scale per master row, binary alpha and exact source colors.
Every write is confined to this character lane; use python -B.
"""
from pathlib import Path
import json, hashlib
import numpy as np
from PIL import Image, ImageDraw
OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[4]
COLORS=['1D1311','342520','34322E','5D5149','463C36','FBF4E9','F6ECDE','E9DED1','D6C5B7','ADA098','EB9D5C','E28E4D','F0A463','E9B07F','9F5626','296267','2E656A','10282C','235E5C','3C9F96']
PAL=np.array([list(bytes.fromhex(c)) for c in COLORS],np.int16)
def save(name,im): im.save(OUT/name)
def shift(im,dx=0,dy=0):
    out=Image.new('RGBA',(32,64));out.alpha_composite(im,(dx,dy));return out
def snap(im):
    a=np.array(im.convert('RGBA'));mask=a[:,:,3]>=128
    out=np.zeros_like(a);c=a[:,:,:3][mask].astype(np.int16)
    if len(c):
        ix=np.argmin(np.sum((c[:,None,:].astype(np.int32)-PAL[None,:,:])**2,axis=2),axis=1)
        out[:,:,:3][mask]=PAL[ix];out[:,:,3][mask]=255
    return Image.fromarray(out)
EYES={'se':[(16,31,3,3),(22,30,2,3)],'sw':[(10,30,2,3),(16,31,3,3)]}
def repair_eyes(im,f):
    d=ImageDraw.Draw(im)
    for x,y,w,h in EYES.get(f,[]):
        d.rectangle((x,y,x+w-1,y+h-1),fill='#1D1311')
        d.point((x,y),fill='#FBF4E9');d.point((x+w-1,y+1),fill='#9F5626')
        d.point((x+w-1,y+2),fill='#E9B07F')
    return im
def normalize_row(raw,row,labels,scale,baseline,proof):
    w,h=raw.size;pieces=[]
    for col,label in enumerate(labels):
        x0,x1=round(col*w/4),round((col+1)*w/4)
        y0,y1=round(row*h/2),round((row+1)*h/2)
        slot=raw.crop((x0,y0,x1,y1));a=np.array(slot);mask=a[:,:,3]>=128
        y,x=np.where(mask);bb=(int(x.min()),int(y.min()),int(x.max()+1),int(y.max()+1))
        cut=slot.crop(bb);sz=(round(cut.width*scale),round(cut.height*scale))
        small=snap(cut.resize(sz,Image.Resampling.NEAREST));xpos=16-sz[0]//2;ypos=baseline+1-sz[1]
        out=Image.new('RGBA',(32,64));out.alpha_composite(small,(xpos,ypos))
        a=np.array(out);a[:,0]=0;a[61:]=0;out=Image.fromarray(a)
        save(label+'-native.png',out);pieces.append(out)
        proof.append(dict(label=label,sourceSlot=[x0,y0,x1,y1],opaqueCrop=bb,scale=scale,nativeSize=sz,nativePlacement=[xpos,ypos]))
    return pieces
def main():
    raw=Image.open(OUT/'master-identity-attempt1.png').convert('RGBA');a=np.array(raw);h,w=a.shape[:2]
    heights=[]
    for col in range(4):
        mask=a[:h//2,round(col*w/4):round((col+1)*w/4),3]>=128;y,x=np.where(mask);heights.append(int(y.max()-y.min()+1))
    scale=42/max(heights);proof=[]
    normalize_row(raw,0,['se-idle','sw-idle','nw-idle','ne-idle'],scale,60,proof)
    normalize_row(raw,1,['se-seat','sw-seat','nw-seat','ne-seat'],scale,56,proof)
    for f in ['se','sw','ne','nw']:
        im=repair_eyes(Image.open(OUT/(f+'-idle-native.png')).convert('RGBA'),f);save(f+'-idle-native.png',im)
        a=np.array(im);a[39:]=0;save('head-'+f+'.png',Image.fromarray(a))
    if (OUT/'master-walk-attempt1.png').exists():
        path=OUT/('master-walk-attempt2.png' if (OUT/'master-walk-attempt2.png').exists() else 'master-walk-attempt1.png')
        raw=Image.open(path).convert('RGBA');a=np.array(raw);h,w=a.shape[:2];hs=[]
        for row in range(2):
            for col in range(4):
                mm=a[round(row*h/2):round((row+1)*h/2),round(col*w/4):round((col+1)*w/4),3]>=128;yy,xx=np.where(mm);hs.append(int(yy.max()-yy.min()+1))
        ws=42/max(hs)
        normalize_row(raw,0,['se-walk-'+str(p) for p in range(4)],ws,60,proof)
        normalize_row(raw,1,['ne-walk-'+str(p) for p in range(4)],ws,60,proof)
    (OUT/'normalization-proof.json').write_text(json.dumps({'identityPalette':['#'+c for c in COLORS],'operations':proof,'method':'One common nearest-neighbor scale for all identity poses, one common scale for all gait poses. Per-master integer initial placement; no resizing/recentering during derived motion.'},indent=2)+'\n',encoding='utf-8')
    board=Image.new('RGBA',(960,560),'#E7D1AC');d=ImageDraw.Draw(board)
    for k,f in enumerate(['se','sw','ne','nw']):
        d.text((k*240+8,8),f+' / idle / seat / head',fill='#191E1E')
        for n,label in enumerate([f+'-idle-native',f+'-seat-native','head-'+f]):
            im=Image.open(OUT/(label+'.png')).convert('RGBA');board.alpha_composite(im,(k*240+8+n*68,35));board.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(k*240+(n%2)*115,110+(n//2)*210))
    board.save(OUT/'native-master-review.png')
if __name__=='__main__':main()
