from pathlib import Path
import json
from datetime import datetime, timezone
from PIL import Image, ImageDraw
OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[4]
RUN=OUT/'runs'
def im(p): return Image.open(ROOT/p).convert('RGBA')
bg='#E7D1AC'
board=Image.new('RGBA',(1024,720),bg);d=ImageDraw.Draw(board)
d.text((12,8),'CAP-TEEN LOCKED IDENTITY / HERO MOOD / FINISHED PILOTS - 1x and nearest3x',fill='#191E1E')
flat=im('assets/art/roster/cap-teen/locked/cap-teen.png')
for k in range(4):
 c=flat.crop((32*k,0,32*k+32,64));board.alpha_composite(c,(20+k*140,36));board.alpha_composite(c.resize((96,192),Image.Resampling.NEAREST),(52+k*140,36))
base=im('assets/art/roster/cap-teen/base.png');base.thumbnail((270,270));board.alpha_composite(base,(650,30))
hero=im('assets/art/iso-concept/iso-office-hero.png');hero.crop((650,480,900,730)).save(RUN/'hero-crop.png')
hero.thumbnail((400,225));board.alpha_composite(hero,(610,300))
for j,(folder,cid) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
 a=im(f'assets/art/iso/characters/{folder}/{cid}-iso.png');x=20+j*320;y=360
 d.text((x,y),folder,fill='#191E1E')
 for k in range(2):
  c=a.crop((0,k*64,32,k*64+64));board.alpha_composite(c,(x+k*138,y+24));board.alpha_composite(c.resize((96,192),Image.Resampling.NEAREST),(x+34+k*138,y+24))
board.save(RUN/'reference-overview.png')
(RUN/'started.json').write_text(json.dumps({'startUtc':'2026-10-04T06:33:20+00:00','preparationUtc':datetime.now(timezone.utc).isoformat()},indent=2),encoding='utf-8')
colors=flat.getcolors(flat.width*flat.height)
pal=['#%02X%02X%02X'%c[:3] for n,c in sorted(colors,reverse=True) if c[3]]
(RUN/'identity-palette.json').write_text(json.dumps(pal,indent=2),encoding='utf-8')
print('reference prepared; identity colors',len(pal))
