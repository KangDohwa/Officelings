"""Static occupied-seat walker probes; no claim of runtime or gait acceptance."""
import runpy
import json
from pathlib import Path
from PIL import Image, ImageDraw

OUT=Path(__file__).resolve().parent
m=runpy.run_path(str(OUT/'measure-A3-rev1.py'))
desk,ds=m['furniture']('desk');chair,cs=m['furniture']('chair')
doc=m['load_json'](m['ART']/'characters/dev-hoodie-v2/dev-hoodie.iso.json')
s=next(s for s in doc['sheets'] if s['kind']=='character')
atlas=m['load_image'](m['ART']/'characters/dev-hoodie-v2'/s['file'])
board=Image.new('RGBA',(768,576),'#E7D1AC');records=[]
for di,orientation in enumerate(m['FACINGS']):
    v=m['variant'](desk,orientation);profile=v['seatProfiles'][0]
    ground=profile['seatGround'];sd=ground['i']+ground['j'];g=m['project'](ground['i'],ground['j'])
    origin=(96,88);facing=profile['isoFacing'];cv=m['variant'](chair,facing)
    items=m['parts'](v,ds,origin,0,sd,'desk')+m['parts'](cv,cs,(origin[0]+g[0],origin[1]+g[1]),sd,sd,'chair')
    clip=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==facing)
    actor=dict(key=(sd,0,'actor','seated'),image=m['cell'](s,atlas,clip),pos=(origin[0]+g[0]-16,origin[1]+g[1]-56))
    w,h=v['footprintW'],v['footprintH']
    probes=((-1,0),(-1,h-1),(w,0),(w,h-1))
    for pi,(i,j) in enumerate(probes):
        wf=m['FACINGS'][pi]
        wc=next(c for c in s['rows'] if c['name']=='walk' and c['direction']==wf)
        gp=m['project'](i,j);key=(i+j,0,'walker',str(pi))
        walker=dict(key=key,image=m['cell'](s,atlas,wc),pos=(origin[0]+gp[0]-16,origin[1]+gp[1]-60))
        assert key[0]==i+j
        im=Image.new('RGBA',(192,144),'#E7D1AC')
        for item in sorted(items+[actor,walker],key=lambda i:i['key']):im.alpha_composite(item['image'],item['pos'])
        ImageDraw.Draw(im).text((2,2),f'{orientation} ({i},{j}) walker={i+j} seat={sd}',fill='#191E1E')
        board.alpha_composite(im,(di*192,pi*144))
        records.append(dict(desk=orientation,walkerGrid=[i,j],walkerDepth=i+j,seatDepth=sd,queue=[dict(key=item['key'],destination=item['pos']) for item in sorted(items+[actor,walker],key=lambda i:i['key'])]))
board.save(OUT/'walkers-A3-rev1-1x.png')
board.resize((2304,1728),Image.Resampling.NEAREST).save(OUT/'walkers-A3-rev1-3x.png')
(OUT/'walkers-A3-rev1.json').write_text(json.dumps(dict(scope='16 occupied-seat static near/far end probes; walker uses own ground key. Pillow simulation only; no WPF/runtime/motion assertion.',records=records),indent=2)+'\n')
print('16 static walker probes; own ground depths retained')
