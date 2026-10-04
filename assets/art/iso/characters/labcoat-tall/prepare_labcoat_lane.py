from pathlib import Path
from datetime import datetime, timezone
import json
from PIL import Image, ImageDraw
OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[4]
def load(p): return Image.open(ROOT/p).convert('RGBA')
def main():
    (OUT/'started.json').write_text(json.dumps({'startUTC':'2026-10-04T07:10:21Z','preparedUTC':datetime.now(timezone.utc).isoformat()},indent=2)+'\n',encoding='utf-8')
    flat=load('assets/art/roster/labcoat-tall/locked/labcoat-tall.png')
    base=load('assets/art/roster/labcoat-tall/base.png')
    board=Image.new('RGBA',(1000,580),'#E7D1AC');d=ImageDraw.Draw(board)
    d.text((10,8),'LABCOAT-TALL LOCKED IDENTITY / native + 3x / accepted ISO pilots',fill='#191E1E')
    for k in range(4):
        c=flat.crop((k*32,0,k*32+32,64))
        board.alpha_composite(c,(10+k*125,45));board.alpha_composite(c.resize((96,192),Image.Resampling.NEAREST),(45+k*125,45))
    base.thumbnail((250,250),Image.Resampling.NEAREST);board.alpha_composite(base,(730,35))
    for n,(folder,cid) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
        im=load(f'assets/art/iso/characters/{folder}/{cid}-iso.png')
        d.text((10+n*325,260),folder,fill='#191E1E')
        for k in range(4):
            c=im.crop((0,k*64,32,k*64+64))
            board.alpha_composite(c,(10+n*325+k*78,280))
            board.alpha_composite(c.resize((72,144),Image.Resampling.NEAREST),(10+n*325+k*78,370))
    board.save(OUT/'identity-pilots-reference.png')
    # Store source front/back crops, preserving native face references.
    for k,name in enumerate(['down','left','right','up']):flat.crop((32*k,0,32*k+32,64)).save(OUT/f'flat-{name}-reference.png')
if __name__=='__main__':main()
