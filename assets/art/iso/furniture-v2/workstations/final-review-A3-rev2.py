"""Correct native/3x panel spacing and produce one native contact review beside hero crops."""
from pathlib import Path
import importlib.util
from PIL import Image,ImageDraw
OUT=Path(__file__).resolve().parent;ROOT=OUT.parents[4]
spec=importlib.util.spec_from_file_location('m',OUT/'measure-A3-rev2.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
desk,ds=m.furniture('desk');chair,cs=m.furniture('chair');hero=m.load_image(ROOT/'assets/art/iso-concept/iso-office-hero.png');boxes=[(680,345,840,505),(970,525,1118,650),(970,660,1098,800)]
native=Image.new('RGBA',(1920,990),'#E7D1AC');nd=ImageDraw.Draw(native)
for ci,(folder,(name,run,skin)) in enumerate(m.PILOTS.items()):
    board=Image.new('RGBA',(1536,1510),'#E7D1AC');d=ImageDraw.Draw(board);base=m.ART/'characters'/folder;doc=m.load_json(base/(name+'.iso.json'));s=next(s for s in doc['sheets'] if s['kind']=='character');a=m.load_image(base/s['file'])
    for di,orientation in enumerate(m.FACINGS):
        v=m.variant(desk,orientation);p=v['seatProfiles'][0];ground=p['seatGround'];sd=sum(ground.values());g=m.project(ground['i'],ground['j']);f=p['isoFacing'];origin=(96,88);co=(96+g[0],88+g[1]);clip=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==f);sprite=m.cell(s,a,clip);head=m.shift(m.canonical_head(folder,run,f),*m.point(s['headOffsetsByClip']['seated-typing-front/'+f][0]));masks,_=m.regions(sprite,head,clip,skin,folder,f);items=m.parts(v,ds,origin,0,sd,'desk')+m.parts(m.variant(chair,f),cs,co,sd,sd,'chair');_,im,_=m.measure_scene(items,sprite,masks,(co[0]-16,co[1]-56),(sd,0,'actor',folder),orientation,f,'baseline',clip,skin)
        bx=di%2*768;by=di//2*490;board.alpha_composite(im.resize((576,432),Image.Resampling.NEAREST),(bx,by+40));board.alpha_composite(im,(bx+576,by+40));d.text((bx+3,by+4),folder+' D:'+orientation+' 3x / 1x side by side',fill='#191E1E')
        native.alpha_composite(im,(di*192,ci*144));nd.text((di*192+2,ci*144+2),folder+' '+f+' native 1x',fill='#191E1E')
    for j,box in enumerate(boxes):
        crop=hero.crop(box);board.alpha_composite(crop,(j*512,1008));board.alpha_composite(crop.resize((crop.width*3,crop.height*3),Image.Resampling.NEAREST),(j*512,1170));d.text((j*512+3,983),'hero original pixels / exact 3x',fill='#191E1E')
    board.save(OUT/('quality-'+folder+'-A3-rev2.png'))
for j,box in enumerate(boxes):
    crop=hero.crop(box);native.alpha_composite(crop,(j*640,490));native.alpha_composite(crop.resize((crop.width*3,crop.height*3),Image.Resampling.NEAREST),(j*640+160,490));nd.text((j*640+3,467),'hero native / 3x',fill='#191E1E')
native.save(OUT/'native-and-hero-A3-rev2.png')
import numpy as np
spec=importlib.util.spec_from_file_location('v',OUT/'verify-A3-rev2.py');vfy=importlib.util.module_from_spec(spec);spec.loader.exec_module(vfy)
combined=Image.new('RGBA',(1792,2960),'#E7D1AC');cd=ImageDraw.Draw(combined)
def crop_object(im):
    a=np.array(im);mask=np.any(a[:,:,:3]!=[231,209,172],axis=2);y,x=np.where(mask);box=(max(0,int(x.min())-4),max(0,int(y.min())-4),min(im.width,int(x.max())+5),min(im.height,int(y.max())+5));return im.crop(box)
for ci,(folder,(name,run,skin)) in enumerate(m.PILOTS.items()):
    base=m.ART/'characters'/folder;doc=m.load_json(base/(name+'.iso.json'));s=next(s for s in doc['sheets'] if s['kind']=='character');a=m.load_image(base/s['file'])
    for di,orientation in enumerate(m.FACINGS):
        dv=m.variant(desk,orientation);p=dv['seatProfiles'][0];ground=p['seatGround'];sd=sum(ground.values());g=m.project(ground['i'],ground['j']);f=p['isoFacing'];origin=(160,128);co=(160+g[0],128+g[1]);items=m.parts(dv,ds,origin,0,sd,'desk')+m.parts(m.variant(chair,f),cs,co,sd,sd,'chair')
        clip=next(c for c in s['rows'] if c['name']=='sit-down' and c['direction']==f);phase=1;pc=clip['phaseContacts'][phase];sa=m.point(pc['seatAnchor']);off=m.point(pc['contactOffset']);pos=(co[0]+off[0]-sa[0],co[1]+off[1]-sa[1]);sprite=m.cell(s,a,clip,phase);head=m.shift(m.canonical_head(folder,run,f),*m.point(s['headOffsetsByClip']['sit-down/'+f][phase]));_,scene=vfy.overlay(items,sprite,pos,(sd,0,'actor',folder),vfy.opaque(head));im=crop_object(scene);bx=di*448;by=1840+ci*370;combined.alpha_composite(im.resize((im.width*3,im.height*3),Image.Resampling.NEAREST),(bx+4,by+25));cd.text((bx+4,by+4),folder+' '+orientation+' lowering phase1 3x',fill='#191E1E')
        if ci==0:
            typing=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==f);sitter=dict(key=(sd,0,'seated',folder),id='seated',family='seated',image=m.cell(s,a,typing),pos=(co[0]-16,co[1]-56));w,h=dv['footprintW'],dv['footprintH']
            for pi,(i,j) in enumerate(((-1,0),(w,h-1))):
                wf=m.FACINGS[(di+pi)%4];wc=next(c for c in s['rows'] if c['name']=='walk' and c['direction']==wf);phase=(di+pi)%4;sprite=m.cell(s,a,wc,phase);head=m.shift(m.canonical_head(folder,run,wf),*m.point(s['headOffsetsByClip']['walk/'+wf][phase]));gp=m.project(i,j);pos=(160+gp[0]-16,128+gp[1]-60);_,scene=vfy.overlay(items+[sitter],sprite,pos,(i+j,0,'walker',folder),vfy.opaque(head));im=crop_object(scene);bx=pi*896;by=di*455;combined.alpha_composite(im.resize((im.width*3,im.height*3),Image.Resampling.NEAREST),(bx+4,by+25));cd.text((bx+4,by+4),f'D:{orientation} walker {wf} p{phase} ground({i},{j}), own depth={i+j}',fill='#191E1E')
combined.save(OUT/'walkers-transitions-review-A3-rev2.png')
print('Exact native panels + disjoint side-by-side 3x panel spacing. Art unchanged.')
