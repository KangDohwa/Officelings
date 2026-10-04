"""Always derive blink from FINAL normal pixels; only native eye interiors change."""
from pathlib import Path
import hashlib,json
import numpy as np
from PIL import Image
OUT=Path(__file__).resolve().parent
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    doc=json.loads((OUT/'glasses-bun.iso.json').read_text(encoding='utf-8'));s=doc['sheets'][0]
    normal=np.array(Image.open(OUT/s['file']).convert('RGBA'))
    eye=json.loads((OUT/'runs/eye-pixels.json').read_text())
    blink=np.zeros((1536,128,4),np.uint8);masks={f:np.zeros_like(normal) for f in ('se','sw')}
    frozen={p:sha(p) for p in [OUT/s['file'],OUT/'glasses-bun.iso.json',OUT/'glasses-bun-iso.manifest.json']+[OUT/f'runs/head-{f}.png' for f in ('se','sw','ne','nw')]}
    cells=[]
    for b,r in enumerate(s['blink']['normalRows']):
        c=s['rows'][r];f=c['direction'];offsets=s['headOffsetsByClip'][c['name']+'/'+f]
        starts=[min(y for x,y in eye[f] if x in xs) for xs in [sorted(set(x for x,y in eye[f]))[:2],sorted(set(x for x,y in eye[f]))[2:]]]
        for phase,off in enumerate(offsets):
            src=normal[r*64:(r+1)*64,phase*32:(phase+1)*32];dst=src.copy();mask=np.zeros((64,32),bool)
            for x,y in eye[f]:
                yy=y+off['y'];xx=x+off['x'];assert src[yy,xx,3]==255
                base=min(py for px,py in eye[f] if px==x)
                color='26130C' if y==base+1 else 'F7CD94'
                dst[yy,xx]=list(bytes.fromhex(color))+[255];mask[yy,xx]=True;masks[f][r*64+yy,phase*32+xx]=255
            assert np.array_equal(src[~mask],dst[~mask]);assert np.any(src[mask]!=dst[mask])
            blink[b*64:(b+1)*64,phase*32:(phase+1)*32]=dst
            cells.append({'normalRow':r,'blinkRow':b,'phase':phase,'eyeTexels':int(mask.sum()),'nonEyeBytesEqual':True})
    assert all(sha(p)==h for p,h in frozen.items())
    Image.fromarray(blink).save(OUT/'glasses-bun-iso-blink.png')
    for f,a in masks.items():Image.fromarray(a).save(OUT/f'glasses-bun-eyes-{f}.png')
    (OUT/'blink-resync-proof.json').write_text(json.dumps({'normalSHA256':sha(OUT/s['file']),'blinkSHA256':sha(OUT/'glasses-bun-iso-blink.png'),'eyeOnlyCells':cells,'frozenFiles':{str(p.relative_to(OUT)):v for p,v in frozen.items()}},indent=2)+'\n',encoding='utf-8')
    print('96 phase-matched eye-only blink cells derived from final normal')
if __name__=='__main__':main()
