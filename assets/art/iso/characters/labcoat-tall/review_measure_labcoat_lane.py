"""Read-only v3 furniture, local ownership/alpha cross-check and 1x/3x boards."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib, importlib.util, json, math, os, subprocess, sys
import numpy as np
from PIL import Image,ImageDraw
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent;ROOT=OUT.parents[4];CID='labcoat-tall'
spec=importlib.util.spec_from_file_location('labcoat_readonly_measure',ROOT/'assets/art/iso/furniture-v2/workstations/measure-A3-rev2.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
FACINGS=['se','sw','ne','nw'];SKIN=['EB9D5C','E28E4D','F0A463','E9B07F'];BG='#E7D1AC'
def read(p):return json.loads(p.read_text(encoding='utf-8'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def load(p):return Image.open(p).convert('RGBA')
def alpha(im):return np.array(im)[:,:,3]>0
def cell(a,row,p):return a.crop((32*p,64*row,32*p+32,64*row+64))
def furniture(root,name):
    d=read(root/(name+'.iso.json'));return d['furniture'][0],{s['id']:(s,load(root/s['file'])) for s in d['sheets']}
def regions(sprite,head,clip):
    a=np.array(sprite);op=a[:,:,3]>0;hm=alpha(head);assert not np.any(hm&~op)
    yy,xx=np.indices((64,32));skin=np.zeros((64,32),bool);hands=skin.copy()
    for color in SKIN:skin|=np.all(a[:,:,:3]==tuple(bytes.fromhex(color)),axis=2)
    distances=[]
    for h in clip['handAnchors']:
        rr=np.maximum(abs(xx-h['x']),abs(yy-h['y']));hands|=(rr<=2)&skin&op&~hm
        distances.append(int(rr[skin&op&~hm].min()))
    torso=op&~hm&~hands&(xx>=11)&(xx<=22)&(yy<44)
    return {'head':hm,'torso':torso,'hands':hands},distances
def scene(items,sprite,pos,key,masks,size=(192,144)):
    labels=Image.new('L',size);out=Image.new('RGBA',size,BG);covers={k:np.zeros((64,32),bool) for k in ('chair','desk')}
    owners=np.full((64,32),'',object)
    layers=items+[dict(key=key,image=sprite,pos=pos,id='actor',family='actor')]
    for item in sorted(layers,key=lambda t:t['key']):
        out.alpha_composite(item['image'],item['pos']);labels.paste(1 if item['family']=='actor' else 2,item['pos'],item['image'].getchannel('A'))
        if item['family']!='actor' and item['key']>key:
            lm=m.local_mask(alpha(item['image']),item,pos);covers[item['family']]|=lm;owners[lm]=item['id']
    owned=np.array(labels.crop((*pos,pos[0]+32,pos[1]+64)))==1
    assert np.array_equal(owned&alpha(sprite),~(covers['chair']|covers['desk'])&alpha(sprite))
    results={}
    for n,mask in masks.items():
        total=int(mask.sum());visible=int((mask&owned).sum());assert visible==int((mask&~(covers['chair']|covers['desk'])).sum())
        hidden=mask&~owned
        results[n]=dict(visible=visible,total=total,fraction=visible/total if total else None,chairHidden=int((mask&covers['chair']).sum()),deskHidden=int((mask&covers['desk']).sum()),occluders={str(p):int((hidden&(owners==p)).sum()) for p in np.unique(owners[hidden])})
    return results,out
def connected(mask):
    seen=np.zeros_like(mask);counts=[];h,w=mask.shape
    for y,x in zip(*np.where(mask)):
        if seen[y,x]:continue
        stack=[(y,x)];seen[y,x]=True;n=0
        while stack:
            y,x=stack.pop();n+=1
            for dy,dx in [(-1,-1),(-1,0),(-1,1),(0,-1),(0,1),(1,-1),(1,0),(1,1)]:
                ay,ax=y+dy,x+dx
                if 0<=ay<h and 0<=ax<w and mask[ay,ax] and not seen[ay,ax]:seen[ay,ax]=True;stack.append((ay,ax))
        counts.append(n)
    return sorted(counts,reverse=True)
def paired(board,im,x,y):
    board.alpha_composite(im,(x,y));board.alpha_composite(im.resize((im.width*3,im.height*3),Image.Resampling.NEAREST),(x+im.width+8,y))
def references(board,y,hero=True):
    d=ImageDraw.Draw(board)
    for i,(folder,cid) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
        im=load(ROOT/f'assets/art/iso/characters/{folder}/{cid}-iso.png').crop((0,0,32,64));x=i*140
        d.text((x+2,y),folder,fill='#191E1E');paired(board,im,x+2,y+18)
    if hero:
        im=load(ROOT/'assets/art/iso-concept/iso-office-hero.png').crop((1100,765,1215,924));x=460
        d.text((x,y),'hero labcoat (reference)',fill='#191E1E');board.alpha_composite(im,(x,y+20))
        crop=load(ROOT/'assets/art/iso-concept/iso-office-hero.png').crop((680,345,840,505));crop.thumbnail((160,160),Image.Resampling.NEAREST);board.alpha_composite(crop,(x+130,y+20))
def boards(s,atlas):
    board=Image.new('RGBA',(2152,2440),BG);d=ImageDraw.Draw(board)
    for row,clip in enumerate(s['rows']):
        x=(row%4)*538;y=(row//4)*220;d.text((x+3,y+3),f'{row:02d} {clip["name"]} / {clip["direction"]}',fill='#191E1E');strip=atlas.crop((0,64*row,128,64*row+64));paired(board,strip,x+3,y+20)
    references(board,2210);board.save(OUT/'iso-rows-review.png')
    # Split same board to inspect all 152 cells without display reduction.
    board.crop((0,0,2152,1100)).save(OUT/'rows-review-top.png');board.crop((0,1100,2152,2440)).save(OUT/'rows-review-bottom.png')
    bl=load(OUT/(CID+'-iso-blink.png'));bb=Image.new('RGBA',(1614,1960),BG);d=ImageDraw.Draw(bb)
    for row in range(24):
        x=(row%3)*538;y=(row//3)*220;d.text((x+3,y+3),f'blink {row:02d} / normal {s["blink"]["normalRows"][row]}',fill='#191E1E');paired(bb,bl.crop((0,64*row,128,64*row+64)),x+3,y+20)
    references(bb,1760);bb.save(OUT/'blink-review.png')
    motion=Image.new('RGBA',(1320,1040),BG);d=ImageDraw.Draw(motion)
    for k,f in enumerate(FACINGS):
        x=(k%2)*650;y=(k//2)*400;d.text((x+5,y+4),f.upper()+' CONTACT / DOWN / PASSING / UP / 3->0',fill='#191E1E')
        strip=atlas.crop((0,(4+k)*64,128,(4+k)*64+64));paired(motion,strip,x+5,y+30)
        im3=cell(atlas,4+k,3);im0=cell(atlas,4+k,0);motion.alpha_composite(im3.resize((96,192),Image.Resampling.NEAREST),(x+150,y+220));motion.alpha_composite(im0.resize((96,192),Image.Resampling.NEAREST),(x+260,y+220))
    references(motion,820);motion.save(OUT/'motion-loop-review.png')
    vs=Image.new('RGBA',(1250,520),BG);d=ImageDraw.Draw(vs);flat=load(ROOT/'assets/art/roster/labcoat-tall/locked/labcoat-tall.png')
    for k,f in enumerate(FACINGS):
        x=k*260;d.text((x+5,5),f+' flat / iso',fill='#191E1E');fc=flat.crop((k*32,0,k*32+32,64));ic=cell(atlas,k,0)
        paired(vs,fc,x+5,30);paired(vs,ic,x+130,30)
    references(vs,290);vs.save(OUT/'iso-vs-flat.png')
    eyeBoard=Image.new('RGBA',(1200,820),BG);d=ImageDraw.Draw(eyeBoard)
    for k,f in enumerate(['se','sw']):
        row=k;normal=cell(atlas,row,0);closed=cell(bl,k,0);mask=load(OUT/(CID+'-eyes-'+f+'.png')).crop((0,k*64,32,k*64+64))
        x=k*580;d.text((x+5,5),f+' normal / closed / eye-only mask (native / 3x)',fill='#191E1E')
        for j,im in enumerate([normal,closed,mask]):paired(eyeBoard,im,x+5+j*175,30)
        # Source-pixel enlargement to inspect catchlights and closed lids.
        face=normal.crop((7,27,26,36));cl=closed.crop((7,27,26,36))
        eyeBoard.alpha_composite(face.resize((190,90),Image.Resampling.NEAREST),(x+5,245));eyeBoard.alpha_composite(cl.resize((190,90),Image.Resampling.NEAREST),(x+215,245))
    contact=load(OUT/'iso-contact.png').crop((0,0,768,330));eyeBoard.alpha_composite(contact,(0,360));eyeBoard.save(OUT/'eyes-contact-review.png')
    combined=Image.new('RGBA',(1250,1340),BG);combined.alpha_composite(eyeBoard,(0,0));combined.alpha_composite(vs,(0,820));combined.save(OUT/'identity-eyes-contact-review.png')
def main():
    doc=read(OUT/(CID+'.iso.json'));s=doc['sheets'][0];atlas=load(OUT/s['file']);heads={f:load(OUT/('head-'+f+'.png')) for f in FACINGS}
    # Local pixel assertions complement the public checker.
    headcells=0;disconnected=[];skin_grips=[];allgait=True;alltyping=True
    for clip in s['rows']:
        frames=[]
        for p in range(4):
            sprite=cell(atlas,clip['row'],p);frames.append(sprite);off=clip['name']+'/'+clip['direction'];v=s['headOffsetsByClip'][off][p];head=m.shift(heads[clip['direction']],v['x'],v['y']);mask=alpha(head)
            assert np.array_equal(np.array(sprite)[mask],np.array(head)[mask]),(clip['name'],p,'head bytes');headcells+=1
            cc=connected(alpha(sprite))
            if len(cc)>1:disconnected.append(dict(row=clip['row'],phase=p,components=cc))
            if 'handAnchorsByPhase' in clip:
                a=np.array(sprite);yy,xx=np.indices((64,32));skin=np.zeros((64,32),bool)
                for color in SKIN:skin|=np.all(a[:,:,:3]==tuple(bytes.fromhex(color)),axis=2)&(a[:,:,3]>0)&~mask
                for h in clip['handAnchorsByPhase'][p]:
                    distance=int(np.maximum(abs(xx-h['x']),abs(yy-h['y']))[skin].min());assert distance<=1;skin_grips.append(distance)
        if clip['name']=='walk':allgait&=len({im.tobytes() for im in frames})==4
        if clip['name']=='seated-typing-front':alltyping&=len({im.tobytes() for im in frames})==4
    for k,f in enumerate(FACINGS):
        for p in range(4):assert cell(atlas,16+k,p).tobytes()==cell(atlas,20+k,3-p).tobytes()
    assert allgait and alltyping
    deskroot=ROOT/'assets/art/iso/furniture-v3/workstations';chairroot=ROOT/'assets/art/iso/furniture-v3/chair'
    desk,ds=furniture(deskroot,'desk');chair,cs=furniture(chairroot,'chair')
    inputs=[deskroot/'desk.iso.json',chairroot/'chair.iso.json']+[root/s['file'] for root in [deskroot,chairroot] for s in read(root/('desk.iso.json' if root==deskroot else 'chair.iso.json'))['sheets']]
    hashes={str(p.relative_to(ROOT)):sha(p) for p in inputs};records=[];queues=[];contact=Image.new('RGBA',(768,620),BG);seatedboard=Image.new('RGBA',(1560,1320),BG)
    for di,orientation in enumerate(FACINGS):
        v=m.variant(desk,orientation);profile=v['seatProfiles'][0];f=profile['isoFacing'];sg=profile['seatGround'];sd=sum(sg.values());origin=(96,88);g=m.project(sg['i'],sg['j']);co=(origin[0]+g[0],origin[1]+g[1]);cv=m.variant(chair,f)
        items=m.parts(v,ds,origin,0,sd,'desk')+m.parts(cv,cs,co,sd,sd,'chair');key=(sd,0,'actor',CID)
        clip=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==f);c=profile['contactOffset'];pos=(origin[0]+c['x']-16,origin[1]+c['y']-44)
        assert [(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in profile['handContacts']]
        queues.append(dict(desk=orientation,worker=f,actorDestination=pos,actorKey=key,items=[dict(id=it['id'],key=it['key'],destination=it['pos']) for it in sorted(items,key=lambda it:it['key'])]))
        for p in range(4):
            spr=cell(atlas,clip['row'],p);v=s['headOffsetsByClip'][clip['name']+'/'+f][p];hm=m.shift(heads[f],v['x'],v['y']);masks,dist=regions(spr,hm,clip);count,im=scene(items,spr,pos,key,masks)
            records.append(dict(desk=orientation,worker=f,phase=p,handContactError=dist,**count))
            x=(di%2)*780;y=(di//2)*650+p*145;seatedboard.alpha_composite(im,(x,y+18));seatedboard.alpha_composite(im.resize((576,432),Image.Resampling.NEAREST),(x+200,(di//2)*650+30)) if p==0 else None
            ImageDraw.Draw(seatedboard).text((x+3,y+2),f'D:{orientation} worker:{f} phase:{p}',fill='#191E1E')
            if p==0:
                contact.alpha_composite(im,(di*192,20));cross=im.copy();d=ImageDraw.Draw(cross)
                for px,py,color in [(co[0],co[1],'#FBF4E9'),(pos[0]+16,pos[1]+44,'#5DB1A5')]+[(pos[0]+h['x'],pos[1]+h['y'],'#BF6554') for h in clip['handAnchors']]:d.line((px-2,py,px+2,py),fill=color);d.line((px,py-2,px,py+2),fill=color)
                contact.alpha_composite(cross,(di*192,185));d=ImageDraw.Draw(contact);d.text((di*192+2,3),f'{f.upper()} / desk {orientation}',fill='#191E1E')
    references(contact,390);contact.save(OUT/'iso-contact.png');seatedboard.save(OUT/'iso-seated-review.png')
    aggregate={}
    for f in FACINGS:
        rr=[r for r in records if r['worker']==f];aggregate[f]={}
        for roi in ['head','torso','hands']:
            total=sum(r[roi]['total'] for r in rr);visible=sum(r[roi]['visible'] for r in rr);aggregate[f][roi]=dict(total=total,visible=visible,fraction=visible/total if total else None,minPhaseFraction=min(r[roi]['fraction'] for r in rr))
    assert all(sha(ROOT/p)==h for p,h in hashes.items()),'Furniture changed during review'
    payload=dict(measuredUTC=datetime.now(timezone.utc).isoformat(),method='Same v3 measurement approach: declared cranial-head alpha; central torso ROI x11..22 above pelvis excluding head/hand skin; hands = actual skin within Chebyshev radius2 of both frozen H points. Full-scene ownership cross-checked with later-layer alpha union; original part depth queue.',sourcesSHA256=hashes,records=records,aggregate=aggregate,queues=queues,checks=dict(headGates=all(r['head']['fraction']>=(.85 if r['worker'] in ('ne','nw') else .90) for r in records),hands100=all(r['hands']['fraction']==1 for r in records),headCellsByteIdentical=headcells,allGaitsDistinct=bool(allgait),allTypingDistinct=bool(alltyping),sitStandExactReverse=True,connectedBodyCells=152-len(disconnected),disconnected=disconnected,propSkinGripCount=len(skin_grips),maxPropGripError=max(skin_grips)))
    (OUT/'visibility-proof.json').write_text(json.dumps(payload,indent=2)+'\n',encoding='utf-8')
    (OUT/'visibility.txt').write_text('\n'.join(f'{f.upper()}: '+', '.join(f'{roi} {100*a["fraction"]:.2f}% ({a["visible"]}/{a["total"]}), min phase {100*a["minPhaseFraction"]:.2f}%' for roi,a in vals.items()) for f,vals in aggregate.items())+'\n',encoding='utf-8')
    boards(s,atlas)
    # Actual refreshed motion + v3-composition detail in a single review image.
    mb=load(OUT/'motion-loop-review.png').crop((0,0,1320,810));final=Image.new('RGBA',(1560,1780),BG);final.alpha_composite(mb,(0,0))
    final.alpha_composite(seatedboard.crop((200,30,776,462)),(0,830));final.alpha_composite(seatedboard.crop((980,30,1556,462)),(780,830))
    # Front workers: retain native seated rows + 3x composite crops.
    final.alpha_composite(seatedboard.crop((200,680,776,1112)),(0,1300));final.alpha_composite(seatedboard.crop((980,680,1556,1112)),(780,1300))
    final.save(OUT/'final-motion-seated-review.png')
    print(json.dumps(dict(aggregate=aggregate,checks=payload['checks']),indent=2))
if __name__=='__main__':main()
