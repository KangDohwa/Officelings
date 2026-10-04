"""Deterministic final-motion blink: copy normal then replace eye-mask pixels only."""
from pathlib import Path
import hashlib,json
import numpy as np
from PIL import Image
OUT=Path(__file__).resolve().parent
def main():
 d=json.loads((OUT/'cap-teen.iso.json').read_text(encoding='utf-8'));s=d['sheets'][0]
 a=np.array(Image.open(OUT/s['file']).convert('RGBA'));b=np.zeros((1536,128,4),np.uint8)
 definition=json.loads((OUT/'runs/eye-definition.json').read_text());masks={f:np.array(Image.open(OUT/v['eyeMask']))[:,:,3]>0 for f,v in s['faceLock'].items()}
 closed_skin=tuple(bytes.fromhex(definition['skin']))+(255,);closed_line=tuple(bytes.fromhex(definition['closedLine']))+(255,)
 eyes=definition['seOrigins'];proof=[]
 for br,r in enumerate(s['blink']['normalRows']):
  f=s['rows'][r]['direction'];off=s['headOffsetsByClip'][s['rows'][r]['name']+'/'+f]
  for p in range(4):
   src=a[r*64:(r+1)*64,p*32:(p+1)*32];mask=masks[f][r*64:(r+1)*64,p*32:(p+1)*32]
   dst=src.copy();dst[mask]=closed_skin
   for x,y in eyes:
    for dx in range(2):dst[y+off[p]['y']+1,32-x-dx if f=='sw' else x+dx]=closed_line
   assert np.array_equal(dst[~mask],src[~mask]);assert np.all(src[mask,3]==255)
   b[br*64:(br+1)*64,p*32:(p+1)*32]=dst
   proof.append({'normalRow':r,'blinkRow':br,'phase':p,'eyeTexels':int(mask.sum()),'nonEyeRGBAIdentical':True})
 Image.fromarray(b).save(OUT/'cap-teen-iso-blink.png')
 (OUT/'blink-resync-proof.json').write_text(json.dumps({'normalSha256':hashlib.sha256((OUT/s['file']).read_bytes()).hexdigest(),'blinkSha256':hashlib.sha256((OUT/'cap-teen-iso-blink.png').read_bytes()).hexdigest(),'phaseMatchedCells':96,'cells':proof},indent=2)+'\n',encoding='utf-8')
 print('96 blink cells: copy final normal, replace exactly 12 marked eye texels; non-eye RGBA identical.')
if __name__=='__main__':main()
