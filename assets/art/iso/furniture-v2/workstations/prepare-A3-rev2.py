"""Snapshot only required inputs; every write remains in this lane."""
from pathlib import Path
import hashlib, json, shutil
from PIL import Image, ImageDraw

OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[4]
SNAP=OUT/'rev2-source'
SNAP.mkdir(exist_ok=True)
names=[p.name for p in OUT.glob('*.png') if p.name.startswith(('desk-','chair-')) and not 'review' in p.name]
names += ['desk.iso.json','chair.iso.json','desk-shadow.iso.json','chair-shadow.iso.json']
for name in names:
    target=SNAP/(name.replace('.iso.json','.json'))
    if not target.exists(): shutil.copy2(OUT/name,target)
actors={
 'dev-hoodie-v2':['dev-hoodie.iso.json','dev-hoodie-iso.png','runs/v2/se-idle-native.png','runs/v2/ne-idle-native.png','runs/motion-rev1/build_motion.py'],
 'punk-girl':['punk-girl.iso.json','punk-girl-iso.png',*[f'runs/{f}-idle-native.png' for f in ('se','sw','ne','nw')],'runs/motion-rev1/redraw.py'],
 'professor':['professor.iso.json','professor-iso.png',*[f'runs/v1/head-{f}.png' for f in ('se','sw','ne','nw')],'runs/motion-rev1/build_motion.py']}
for folder, files in actors.items():
    for name in files:
        src=ROOT/'assets/art/iso/characters'/folder/name
        dst=SNAP/'actor-inputs'/folder/name.replace('.iso.json','.json')
        dst.parent.mkdir(parents=True,exist_ok=True)
        if not dst.exists(): shutil.copy2(src,dst)
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
protected={str(p.relative_to(ROOT)).replace('\\','/'):digest(p) for folder in ('lounge','utility') for p in (ROOT/'assets/art/iso/furniture-v2'/folder).rglob('*') if p.is_file()}
protected.update({str((OUT/n).relative_to(ROOT)).replace('\\','/'):digest(OUT/n) for n in names if n.startswith('chair-') or n.startswith('chair.') or n.startswith('desk-shadow')})
if not (SNAP/'protected-hashes.json').exists(): (SNAP/'protected-hashes.json').write_text(json.dumps(protected,indent=2)+'\n')
doc=json.loads((SNAP/'desk.json').read_text());sheet=doc['sheets'][0];atlas=Image.open(SNAP/sheet['file']).convert('RGBA')
board=Image.new('RGBA',(1536,550),'#E7D1AC');d=ImageDraw.Draw(board)
for di,f in enumerate(('se','sw','ne','nw')):
    full=Image.open(SNAP/f'desk-empty-{f}.png').convert('RGBA');crt=Image.new('RGBA',(128,128))
    for c in sheet['rows']:
        if c['direction']==f and '__monitor__' in c['partId']:
            x=c.get('column',0)*128;y=c['row']*128;crt.alpha_composite(atlas.crop((x,y,x+128,y+128)))
    for row,im in enumerate((full,crt)):
        board.alpha_composite(im,(di*384,row*265+20));board.alpha_composite(im.resize((384,384),Image.Resampling.NEAREST).crop((15,55,350,295)),(di*384,row*265+45))
        d.text((di*384+4,row*265+3),f+' '+('empty' if row==0 else 'source CRT'),fill='#191E1E')
board.save(OUT/'source-art-A3-rev2.png')
print('Snapshot: desk/chair production, 21 actor inputs, protected lounge/utility/chair/shadow hashes. Outputs only workstations.')
