"""Read-only v3 furniture queue + own final-art verification/review exports."""
from hanbok_export import *
MEASURE=module('hanbok_v3_measure_readonly',ROOT/'assets/art/iso/furniture-v2/workstations/measure-A3-rev2.py')
def cell(atlas,r,p=0):return atlas.crop((p*32,r*64,(p+1)*32,(r+1)*64))
def opaque(im):return np.array(im)[:,:,3]>0
def furniture(folder,name):
    root=ROOT/'assets/art/iso/furniture-v3'/folder
    doc=read(root/(name+'.iso.json'));sheets={s['id']:(s,load(root/s['file'])) for s in doc['sheets']}
    return doc['furniture'][0],sheets
def scene(items,sprite,pos,key,regions):
    canvas=Image.new('RGBA',(320,256),BG);labels=Image.new('L',canvas.size);cover=np.zeros((64,32),bool)
    for it in sorted(items+[dict(key=key,image=sprite,pos=pos,id='actor')],key=lambda t:t['key']):
        canvas.alpha_composite(it['image'],it['pos']);labels.paste(1 if it['id']=='actor' else 2,it['pos'],it['image'].getchannel('A'))
        if it['id']!='actor' and it['key']>key:cover |= MEASURE.local_mask(opaque(it['image']),it,pos)
    owned=np.array(labels.crop((*pos,pos[0]+32,pos[1]+64)))==1
    assert np.array_equal(owned&opaque(sprite),~cover&opaque(sprite))
    results={}
    for n,mask in regions.items():
        total=int(mask.sum());visible=int((mask&owned).sum())
        results[n]=dict(visible=visible,total=total,fraction=visible/total if total else None)
    return canvas,results
def region_masks(sprite,head,clip):
    a=np.array(sprite);op=opaque(sprite);hm=opaque(head);yy,xx=np.indices((64,32));skin=np.zeros_like(op)
    for h in ('C9896A','FBD3A8','FBDDBC'):skin|=np.all(a[:,:,:3]==tuple(bytes.fromhex(h)),axis=2)
    hands=np.zeros_like(op);distance=[]
    for h in clip['handAnchors']:
        radius=np.maximum(abs(xx-h['x']),abs(yy-h['y']))
        hands|=(radius<=2)&skin&op&~hm
        distance.append(int(radius[skin&op&~hm].min()))
    torso=op&~hm&~hands&(xx>=11)&(xx<=22)&(yy<44)
    return dict(head=hm,torso=torso,hands=hands),distance
def measure():
    doc=read(OUT/(CID+'.iso.json'));s=doc['sheets'][0];a=load(OUT/s['file'])
    desk,ds=furniture('workstations','desk');chair,cs=furniture('chair','chair')
    records=[];queues=[];board=Image.new('RGBA',(768,688),BG);draw=ImageDraw.Draw(board)
    for di,df in enumerate(F):
        v=MEASURE.variant(desk,df);pr=v['seatProfiles'][0];wf=pr['isoFacing'];ground=pr['seatGround'];sd=sum(ground.values())
        origin=(160,128);gp=MEASURE.project(ground['i'],ground['j']);co=(origin[0]+gp[0],origin[1]+gp[1]);cv=MEASURE.variant(chair,wf)
        items=MEASURE.parts(v,ds,origin,0,sd,'desk')+MEASURE.parts(cv,cs,co,sd,sd,'chair')
        clip=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==wf)
        cp=MEASURE.point(pr['contactOffset']);pos=(origin[0]+cp[0]-16,origin[1]+cp[1]-44);key=(sd,0,'actor',CID)
        assert [(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in pr['handContacts']]
        for p in range(4):
            sprite=cell(a,clip['row'],p);off=s['headOffsetsByClip'][clip['name']+'/'+wf][p]
            head=shift(load(RUN/('head-'+wf+'.png')),off['x'],off['y'])
            regions,dist=region_masks(sprite,head,clip);im,ratios=scene(items,sprite,pos,key,regions)
            assert max(dist)<=1
            records.append(dict(desk=df,worker=wf,phase=p,actorPosition=pos,headOffset=off,handDistances=dist,**ratios))
            x=di*192;y=p*172;board.alpha_composite(im.crop((64,32,256,176)),(x,y+26))
            draw.text((x+2,y+2),df.upper()+' desk / '+wf.upper()+' worker / p'+str(p),fill=O)
            draw.text((x+2,y+13),'H %.1f%% T %.1f%% hand %.1f%%'%(100*ratios['head']['fraction'],100*ratios['torso']['fraction'],100*ratios['hands']['fraction']),fill=O)
        queues.append(dict(desk=df,worker=wf,items=[dict(id=i['id'],key=i['key'],pos=i['pos'],seatGroup=i['seat']) for i in sorted(items,key=lambda i:i['key'])]))
    summaries=[]
    for f in F:
        rr=[r for r in records if r['worker']==f];agg={}
        for roi in ('head','torso','hands'):
            visible=sum(r[roi]['visible'] for r in rr);total=sum(r[roi]['total'] for r in rr)
            agg[roi]=dict(visible=visible,total=total,percent=100*visible/total)
        agg['worker']=f;agg['headGate']=90 if f in ('se','sw') else 85
        agg['allPhaseHeadGate']=all(r['head']['fraction']*100>=agg['headGate'] for r in rr)
        agg['allPhaseHands100']=all(r['hands']['fraction']==1 for r in rr);summaries.append(agg)
    inputs={}
    for folder,name in [('workstations','desk'),('chair','chair')]:
        root=ROOT/'assets/art/iso/furniture-v3'/folder;d=read(root/(name+'.iso.json'))
        for p in [root/(name+'.iso.json')]+[root/s['file'] for s in d['sheets']]:inputs[str(p.relative_to(ROOT))]=sha(p)
    payload=dict(records=records,summaries=summaries,queues=queues,inputSHA256=inputs,roiDefinitions=dict(head='exact canonical generated head alpha at declared clip offset; complete head including neck through source y40',torso='opaque actor excluding head/hands, source x11..22 and y<44',hands='skin RGB C9896A/FBD3A8/FBDDBC within Chebyshev radius2 of both frozen H contacts, excluding canonical head'),scope='Static serialized v3 desk + v3 chair parts queue, explicit anchors and ordinary/seat ground depth; pixel ownership cross-checked with occlusion union. No WPF/runtime claim.')
    savejson(OUT/'seated-visibility.json',payload);board.save(OUT/'iso-seated-review-1x.png')
    board.resize((2304,2064),Image.Resampling.NEAREST).save(OUT/'iso-seated-review.png')
    print(json.dumps(summaries,indent=2))
def verify():
    doc=read(OUT/(CID+'.iso.json'));s=doc['sheets'][0];a=load(OUT/s['file']);proof=read(RUN/'derivation-proof.json')
    proof['connectedSilhouettes']=[];proof['gaitPhasesDistinct']={};proof['typingPhasesDistinct']={};proof['loopPixelChanges']={};proof['gripDistances']={};proof['transitionChecks']={}
    yy,xx=np.indices((64,32))
    for r,clip in enumerate(s['rows']):
        f=clip['direction'];name=clip['name']
        for p in range(4):
            im=cell(a,r,p);arr=np.array(im);off=s['headOffsetsByClip'][name+'/'+f][p]
            hm=opaque(shift(load(RUN/('head-'+f+'.png')),off['x'],off['y']))
            groups=components(opaque(im));proof['connectedSilhouettes'].append(dict(row=r,phase=p,sizes=[len(g) for g in groups]))
            if name in PROP_PATH:
                grip=clip['handAnchorsByPhase'][p][0];skin=np.zeros((64,32),bool)
                for color in ('C9896A','FBD3A8','FBDDBC'):skin|=np.all(arr[:,:,:3]==tuple(bytes.fromhex(color)),axis=2)
                skin &= opaque(im)&~hm;dist=int(np.maximum(abs(xx-grip['x']),abs(yy-grip['y']))[skin].min())
                proof['gripDistances'][name+'/'+f+'/'+str(p)]=dist
    for i,f in enumerate(F):
        walkrow=[np.array(cell(a,4+i,p)) for p in range(4)];typerow=[cell(a,8+i,p) for p in range(4)]
        proof['gaitPhasesDistinct'][f]=len({x.tobytes() for x in walkrow})
        proof['typingPhasesDistinct'][f]=len({x.tobytes() for x in typerow})
        proof['loopPixelChanges'][f]=[int(np.any(walkrow[p]!=walkrow[(p+1)%4],axis=2).sum()) for p in range(4)]
        low=[cell(a,16+i,p).tobytes() for p in range(4)];rise=[cell(a,20+i,p).tobytes() for p in range(4)]
        proof['transitionChecks'][f]=dict(reverseExact=low==rise[::-1],idleEndpoint=low[0]==cell(a,i,0).tobytes(),restEndpoint=low[3]==cell(a,12+i,0).tobytes())
    proof['disconnectedCells']=[c for c in proof['connectedSilhouettes'] if len(c['sizes'])>1]
    savejson(RUN/'derivation-proof.json',proof)
    assert all(x==4 for x in proof['gaitPhasesDistinct'].values())
    assert all(x==4 for x in proof['typingPhasesDistinct'].values())
    assert all(all(v.values()) for v in proof['transitionChecks'].values())
    assert not proof['disconnectedCells'],proof['disconnectedCells']
    assert max(proof['gripDistances'].values())<=1
    assert all(v[-1]<=max(v[:-1]) for v in proof['loopPixelChanges'].values())
    print('Local verification: disconnected',len(proof['disconnectedCells']),'max grip distance',max(proof['gripDistances'].values()))
    print('Loop changes',proof['loopPixelChanges'])
def review():
    doc=read(OUT/(CID+'.iso.json'));s=doc['sheets'][0];a=load(OUT/s['file']);blink=load(OUT/doc['sheets'][1]['file'])
    # One common reference strip on every page provides 1x/3x pilot comparisons and hero crop.
    ref=Image.new('RGBA',(1200,225),BG);d=ImageDraw.Draw(ref)
    hero=load(ROOT/'assets/art/iso-concept/iso-office-hero.png').crop((685,350,990,610));hero.thumbnail((210,180));ref.alpha_composite(hero,(940,25))
    for i,(folder,name) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
        pilot=load(ROOT/'assets/art/iso/characters'/folder/(name+'-iso.png'));im=cell(pilot,0)
        d.text((i*290+4,4),folder+' 1x / nearest 3x',fill=O)
        ref.alpha_composite(im,(i*290+10,35));ref.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(i*290+70,25))
    pages=[]
    for n in range(4):
        page=Image.new('RGBA',(1200,1500),BG);d=ImageDraw.Draw(page);page.alpha_composite(ref,(0,1275))
        for j,r in enumerate(range(n*10,min(38,n*10+10))):
            x=(j%2)*600;y=(j//2)*250;clip=s['rows'][r]
            d.text((x+4,y+4),'%02d %s / %s'%(r,clip['name'],clip['direction']),fill=O)
            for p in range(4):
                im=cell(a,r,p);page.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+8+p*96,y+35))
                page.alpha_composite(im,(x+420+p*32,y+50))
        page.save(RUN/('rows-review-'+str(n+1)+'.png'));pages.append(page)
    full=Image.new('RGBA',(1200,6000),BG)
    for n,page in enumerate(pages):full.alpha_composite(page,(0,n*1500))
    full.save(OUT/'iso-rows-review.png')
    contact=Image.new('RGBA',(1200,1100),BG);d=ImageDraw.Draw(contact);contact.alpha_composite(ref,(0,875))
    for i,f in enumerate(F):
        y=i*215;d.text((8,y+5),f.upper()+' IDLE / contact / down / passing / up / 3 -> 0',fill=O)
        for j,rp in enumerate([(i,0)]+[(4+i,p) for p in range(4)]+[(4+i,0)]):
            im=cell(a,*rp);contact.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(j*125+10,y+23))
            contact.alpha_composite(im,(825+j*40,y+60))
        frames=[]
        for p in range(4):
            fr=Image.new('RGBA',(32,64),BG);fr.alpha_composite(cell(a,4+i,p));frames.append(fr.resize((96,192),Image.Resampling.NEAREST).convert('RGB'))
        frames[0].save(OUT/('walk-'+f+'-3x.gif'),save_all=True,append_images=frames[1:],duration=[120,130,120,130],loop=0,disposal=2)
        strip=Image.new('RGBA',(384,192),BG)
        for p,fr in enumerate(frames):strip.alpha_composite(fr.convert('RGBA'),(p*96,0))
        strip.save(OUT/('walk-'+f+'-3x.png'))
    contact.save(OUT/'iso-contact.png')
    flat=load(FLAT/'hanbok-modern.png');comp=Image.new('RGBA',(1200,740),BG);d=ImageDraw.Draw(comp);comp.alpha_composite(ref,(0,515))
    for i,f in enumerate(F):
        x=i*290;d.text((x+5,5),'FLAT / ISO '+f.upper()+' / seated',fill=O)
        fi=flat.crop(([2,1,3,3][i]*32,0,[3,2,4,4][i]*32,64))
        for j,im in enumerate([fi,cell(a,i),cell(a,8+i)]):comp.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+j*96,25))
        for j,im in enumerate([fi,cell(a,i),cell(a,8+i)]):comp.alpha_composite(im,(x+15+j*64,260))
        if i<2:
            comp.alpha_composite(cell(blink,i).resize((96,192),Image.Resampling.NEAREST),(x+96,300));d.text((x+5,470),'OPEN normal / CLOSED eye-only',fill=O)
    comp.save(OUT/'iso-vs-flat.png')
    br=Image.new('RGBA',(1200,600),BG);d=ImageDraw.Draw(br)
    for j,(r,p) in enumerate([(0,0),(1,0),(4,1),(5,3),(8,2),(9,0),(24,1),(30,2)]):
        x=(j%4)*290;y=(j//4)*250;b=CHECK.BLINK_ROWS.index(r)
        d.text((x+5,y+5),'normal row '+str(r)+' p'+str(p)+' / blink',fill=O)
        for k,im in enumerate([cell(a,r,p),cell(blink,b,p)]):
            br.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+10+k*110,y+25))
    br.save(OUT/'blink-resync-review.png')
    print('Wrote all review boards and four 3x walk GIFs')
if __name__=='__main__':
    for action in sys.argv[1:]:globals()[action]()

