"""Re-derive every blink cell from FINAL normal bytes, replacing only eye masks.
Deterministic, no baseline body copies; all writes stay in this directory.
"""
from pathlib import Path
import hashlib, json
import numpy as np
from PIL import Image
from normalize_labcoat_lane import EYES
OUT=Path(__file__).resolve().parent;CID='labcoat-tall'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    doc=json.loads((OUT/(CID+'.iso.json')).read_text());s=doc['sheets'][0]
    normalpath=OUT/s['file'];before=sha(normalpath);normal=np.array(Image.open(normalpath).convert('RGBA'))
    blink=np.zeros((1536,128,4),np.uint8);masks={f:np.zeros_like(normal) for f in ['se','sw']};proof=[]
    for b,r in enumerate(s['blink']['normalRows']):
        clip=s['rows'][r];f=clip['direction'];assert f in masks
        for p in range(4):
            src=normal[r*64:r*64+64,p*32:p*32+32];dst=src.copy();eye=np.zeros((64,32),bool)
            off=s['headOffsetsByClip'][clip['name']+'/'+f][p];assert off['x']==0 and abs(off['y'])<=1
            for x,y,w,h in EYES[f]:
                y+=off['y'];eye[y:y+h,x:x+w]=True
                dst[y:y+h,x:x+w]=(*bytes.fromhex('EB9D5C'),255)
                dst[y+1,x:x+w]=(*bytes.fromhex('342520'),255)
            assert 0<int(eye.sum())<=32 and np.all(src[eye,3]==255)
            assert np.array_equal(src[~eye],dst[~eye]);assert np.any(src[eye]!=dst[eye])
            masks[f][r*64:r*64+64,p*32:p*32+32][eye]=(255,255,255,255)
            blink[b*64:b*64+64,p*32:p*32+32]=dst
            proof.append(dict(normalRow=r,blinkRow=b,phase=p,facing=f,eyePixels=int(eye.sum()),nonEyeRGBAExact=True))
    assert before==sha(normalpath),'Normal changed during blink derivation'
    Image.fromarray(blink).save(OUT/(CID+'-iso-blink.png'))
    for f,a in masks.items():Image.fromarray(a).save(OUT/(CID+'-eyes-'+f+'.png'))
    (OUT/'blink-resync-proof.json').write_text(json.dumps(dict(normalSHA256=before,blinkSHA256=sha(OUT/(CID+'-iso-blink.png')),cells=proof,nonEyeCellsByteIdentical=96,normalUnmodified=True,method='dst = FINAL normal cell; replace only closed-eye glyph in phase-aligned SE/SW mask'),indent=2)+'\n',encoding='utf-8')
    print('Blink: 24 rows / 96 final-normal copies, non-eye RGBA exactly identical.')
if __name__=='__main__':main()
