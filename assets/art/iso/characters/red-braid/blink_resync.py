"""Deterministic eye-only copies of FINAL normal motion. Lane-only writes."""
from pathlib import Path
import hashlib,json,sys
import numpy as np
from PIL import Image,ImageDraw
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent
ROWS=[0,1,4,5,8,9,12,13,16,17,20,21,24,25,26,27,28,29,30,31,34,35,36,37]
EYES={'se':[(14,32,17,36),(19,32,22,35)],'sw':[(9,32,12,35),(15,33,18,36)]}
def main():
    doc=json.loads((OUT/'red-braid.iso.json').read_text(encoding='utf-8'));s,b=doc['sheets']
    assert s['blink']['normalRows']==ROWS
    source=OUT/s['file'];before=hashlib.sha256(source.read_bytes()).hexdigest()
    normal=Image.open(source).convert('RGBA');blink=Image.new('RGBA',(128,1536))
    masks={f:Image.new('RGBA',normal.size) for f in EYES};checks=0
    for br,r in enumerate(ROWS):
        c=s['rows'][r];f=c['direction'];head=Image.open(OUT/f'head-{f}.png').convert('RGBA')
        # Closed-eye patch is fixed to the facing-specific canonical diagonal face.
        closed=head.copy();d=ImageDraw.Draw(closed)
        for x0,y0,x1,y1 in EYES[f]:
            d.rectangle((x0,y0,x1-1,y1-1),fill='#FBC68D')
            d.line([(x0,y0+1),(x1-1,y0+1)],fill='#613A33')
        closed.save(OUT/f'head-closed-{f}.png')
        for p,off in enumerate(s['headOffsetsByClip'][c['name']+'/'+f]):
            x,y=p*32,r*64;src=normal.crop((x,y,x+32,y+64));dst=src.copy()
            for x0,y0,x1,y1 in EYES[f]:
                for ey in range(y0,y1):
                    for ex in range(x0,x1):
                        xx,yy=ex+off['x'],ey+off['y']
                        assert src.getpixel((xx,yy))[3]==255,(r,p,xx,yy)
                        masks[f].putpixel((x+xx,y+yy),(255,255,255,255))
                        dst.putpixel((xx,yy),closed.getpixel((ex,ey)))
            mask=np.array(masks[f].crop((x,y,x+32,y+64)))[:,:,3]>0
            assert np.array_equal(np.array(src)[~mask],np.array(dst)[~mask])
            blink.paste(dst,(x,br*64));checks+=1
    assert hashlib.sha256(source.read_bytes()).hexdigest()==before
    blink.save(OUT/b['file'])
    for f,m in masks.items():m.save(OUT/s['faceLock'][f]['eyeMask'])
    (OUT/'blink-resync-proof.json').write_text(json.dumps({'normalSHA256':before,'blinkSHA256':hashlib.sha256((OUT/b['file']).read_bytes()).hexdigest(),'eyeOnlyComparisons':checks,'normalUnchanged':True,'rows':ROWS,'maskDefinition':EYES},indent=2)+'\n',encoding='utf-8')
    print('96 final normal/blink comparisons: exact non-eye RGBA; facing eye patches only.')
if __name__=='__main__':main()
