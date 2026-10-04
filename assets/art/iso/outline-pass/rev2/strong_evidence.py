"""Real raster backgrounds, serialized occlusion, shared reference perimeter metrics."""
from pathlib import Path
import hashlib
import importlib.util
import json
import sys
import numpy as np
from PIL import Image,ImageDraw

sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[4]
ART=ROOT/'assets/art/iso'
SCENES=Path('C:/Codex-Workspace/vd-lanes/packv3/artifacts/iso-packv3-final')
POST=Path('C:/Codex-Workspace/vd-lanes/occlusion/artifacts/occlusion')
LABELS=('original','rev1','rev2','rev2-max')
TARGETS=dict(perimeterCoveragePercent=100,dualLayerAtLeast3Percent=75,
             darkBackgroundDualLayerAtLeast3Percent=45)


def read(p):
    return json.loads(p.read_text(encoding='utf-8'))


def write(p,d):
    p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


def load(name,p):
    s=importlib.util.spec_from_file_location(name,p);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m


def scales(name,im):
    for scale in (1,2,3):
        im.resize((im.width*scale,im.height*scale),Image.Resampling.NEAREST).save(OUT/(name+f'-{scale}x.png'))


def crop_canvas(a,pos):
    x,y=pos
    return np.array(Image.fromarray(a).crop((x,y,x+32,y+64)))


def cover(items,pos,key):
    mask=Image.new('L',(32,64))
    a=np.zeros((64,32),bool)
    for it in items:
        if it['key']<=key:continue
        mask=Image.new('L',(32,64));mask.paste(it['image'].getchannel('A'),(it['pos'][0]-pos[0],it['pos'][1]-pos[1]))
        a|=np.array(mask)>0
    return a


def pose(item,op,action,facing,phase):
    s=item['normal'];clip=next(c for c in s['rows'] if c['name']==action and c['direction']==facing)
    off=s['headOffsetsByClip'][action+'/'+facing][phase]
    hm=op.shifted(item['heads'][facing],off['x'],off['y'])
    return clip,hm,{label:op.cell(a,clip['row'],phase) for label,a in item['variants'].items()}


def walk_hand_points(item,facing,phase):
    """Read authored palm landmarks, instead of mistaking reused trouser RGB for hands."""
    ident=item['ident'];folder=item['folder']
    if ident=='professor':
        p=read(folder/'runs/motion-rev1/walk-points.json')[facing+'/'+str(phase)]
        return [p['nearHand'],p['farHand']]
    if ident=='glasses-bun':
        p=read(folder/'runs/motion-joints.json')[facing+'/'+str(phase)]
        return [p['leftHand'],p['rightHand']]
    back=facing in ('ne','nw')
    if ident=='dev-hoodie-v2':
        front=[[(8,47),(27,44)],[(12,49),(25,47)],[(23,44),(25,49)],[(17,47),(26,46)]]
        rear=[[(21,43),(24,46)],[(13,47),(25,45)],[(7,47),(26,40)],[(14,45),(25,42)]]
        points=(rear if back else front)[phase]
    elif ident=='punk-girl':
        points=[[(10,46),(25,41)],[(12,45),(24,43)],[(14,42),(22,47)],[(10,44),(24,42)]][phase]
        if back:points=[(32-x,y) for x,y in points]
    else:
        front=[[(15,45),(21,48)],[(13,46),(23,46)],[(8,45),(26,48)],[(9,43),(24,49)]]
        rear=[[(7,44),(20,48)],[(9,46),(22,47)],[(14,48),(26,44)],[(12,49),(24,43)]]
        points=(rear if back else front)[phase]
    if facing in ('sw','nw'):points=[(32-x,y) for x,y in points]
    return points


def roi(src,hm,clip,profile,hand_points=None):
    yy,xx=np.indices((64,32));skin=np.zeros((64,32),bool)
    for h in profile['skin']:
        skin |= np.all(src[:,:,:3]==tuple(bytes.fromhex(h)),axis=2)
    skin&=(src[:,:,3]>0)&~hm
    if clip['name']=='seated-typing-front':
        hands=np.zeros_like(skin)
        distances=[]
        for h in clip['handAnchors']:
            radius=np.maximum(abs(xx-h['x']),abs(yy-h['y']))
            hands|=skin&(radius<=2)
            distances.append(int(radius[skin].min()))
        assert max(distances)<=1
    else:
        hands=skin&(yy<52);distances=[]
        if hand_points is not None:
            authored=np.zeros_like(skin)
            for x,y in hand_points:
                authored|=np.maximum(abs(xx-x),abs(yy-y))<=2
            hands&=authored
            assert hands.any(),'no palm pixels at authored walk landmarks'
    return {'head':hm,'hands':hands},distances


def counts(regions,src,covered):
    owned=(src[:,:,3]>0)&~covered
    return {n:dict(visible=int((m&owned).sum()),total=int(m.sum())) for n,m in regions.items()}


def scene(bg,items,src,pos,key):
    canvas=bg.copy()
    actor=dict(image=Image.fromarray(src),pos=pos,key=key,id='actor')
    for it in sorted(items+[actor],key=lambda i:i['key']):
        canvas.alpha_composite(it['image'],it['pos'])
    return canvas


def visibility(data,op,m,desk,ds,chair,cs):
    records=[];walkers=[];summary={}
    for item in data:
        ident=item['ident'];total_typing=0;total_walking=0
        for facing in op.FACINGS:
            v=m.variant(desk,facing);pr=v['seatProfiles'][0];wf=pr['isoFacing'];gd=pr['seatGround'];sd=sum(gd.values())
            origin=(160,128);gp=m.project(gd['i'],gd['j']);co=(origin[0]+gp[0],origin[1]+gp[1])
            offset=m.point(pr['contactOffset']);pos=(origin[0]+offset[0]-16,origin[1]+offset[1]-44)
            key=(sd,0,'actor',ident)
            items=m.parts(v,ds,origin,0,sd,'desk')+m.parts(m.variant(chair,wf),cs,co,sd,sd,'chair')
            clip,hm,poses=pose(item,op,'seated-typing-front',wf,0)
            assert [(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in pr['handContacts']]
            w,h=v['footprintW'],v['footprintH']
            probes=list(dict.fromkeys([(-1,0),(-1,h-1),(w,0),(w,h-1),(0,-1),(w-1,-1),(0,h),(w-1,h)]))+[(-1,-1),(w,h)]
            assert len(probes)==8
            # Three real working-screen phases produce 48 typing / 1,536 walk cases per character.
            for screen_phase in (0,1,2):
                phase_items=[]
                for it in items:
                    clone=it.copy()
                    if '__screen__' in it['id']:
                        desc=v['screen'];sheet,atlas=ds[desc['sheetId']]
                        sc=next(c for c in sheet['rows'] if c['name']==desc['animation'] and c['direction']==desc['direction'])
                        clone['image']=m.cell(sheet,atlas,sc,screen_phase%sc['frames'])
                    phase_items.append(clone)
                covered=cover(phase_items,pos,key)
                for phase in range(4):
                    clip,hm,poses=pose(item,op,'seated-typing-front',wf,phase)
                    regions,dist=roi(op.cell(item['source'],clip['row'],phase),hm,clip,item['profile'])
                    before=counts(regions,op.cell(item['source'],clip['row'],phase),covered)
                    after=counts(regions,poses['rev2'],covered)
                    assert before==after,(ident,'typing visibility regression')
                    assert after['hands']['visible']==after['hands']['total']
                    records.append(dict(character=ident,desk=facing,worker=wf,phase=phase,
                        screenPhase=screen_phase,before=before,after=after,handDistances=dist))
                    total_typing+=1
                old_sitter=dict(key=key,id='seated',image=Image.fromarray(op.cell(item['source'],clip['row'],0)),pos=pos)
                new_sitter={**old_sitter,'image':Image.fromarray(op.cell(item['variants']['rev2'],clip['row'],0))}
                for i,j in probes:
                    ground=m.project(i,j);wp=(origin[0]+ground[0]-16,origin[1]+ground[1]-60)
                    wk=(i+j,0,'walker',ident)
                    oldcover=cover(phase_items+[old_sitter],wp,wk)
                    newcover=cover(phase_items+[new_sitter],wp,wk)
                    for walk_facing in op.FACINGS:
                        for phase in range(4):
                            wc,hm,poses=pose(item,op,'walk',walk_facing,phase)
                            src=op.cell(item['source'],wc['row'],phase)
                            regions,dist=roi(src,hm,wc,item['profile'],walk_hand_points(item,walk_facing,phase))
                            legacy=roi(src,hm,wc,item['profile'])[0]
                            before=counts(regions,src,oldcover);after=counts(regions,poses['rev2'],newcover)
                            assert all(after[n]['visible']>=before[n]['visible'] and after[n]['total']==before[n]['total'] for n in regions), (ident,'walk visibility regression',facing,[i,j],walk_facing,phase,before,after)
                            walkers.append(dict(character=ident,desk=facing,walker=walk_facing,phase=phase,
                                screenPhase=screen_phase,ground=[i,j],before=before,after=after,
                                legacySkinColorROI=dict(before=counts(legacy,src,oldcover)['hands'],
                                    after=counts(legacy,poses['rev2'],newcover)['hands'])))
                            total_walking+=1
        assert total_typing==48 and total_walking==1536
        summary[ident]=dict(typing=total_typing,walking=total_walking,headAndHandRegressions=0,
            typingHands100Percent=True)
        print(ident+': visibility 48 typing + 1536 walking passed',flush=True)
    write(OUT/'visibility.json',dict(caseDefinition='4 desk orientations x 3 real working screen phases x 4 actor phases; walkers additionally 8 probes x 4 walker facings',
        perCharacter=summary,typing=records,walking=walkers,
        walkingHandROI='Skin pixels within Chebyshev radius 2 of the original authored palm landmarks. The previous broad skin-color ROI is retained separately; it includes professor trouser pixels and is not an anatomical hand mask.',
        scope='Serialized live v3 desk/chair parts, independent actor ground depth; original head/hand denominators.'))
    return summary


def band_records(data,op):
    """Same 2,262 original perimeter pairs and actual background RGB as rev1.

    Original/rev1 have one opaque layer; rev2 has body edge + exterior texel.
    Readability is the greatest background contrast of these two layers, reported
    separately from the physical outermost-pixel contrast (which stays low on navy).
    """
    measurement=load('outline_baseline_metrics',OUT/'measure_baseline.py')
    indexed={d['ident']:d for d in data};result=[]
    for line in (OUT/'baseline-per-edge.jsonl').read_text(encoding='utf-8').splitlines():
        r=json.loads(line)
        if r['character'] not in indexed:continue
        item=indexed[r['character']];action='seated-typing-front' if r['state']=='typing' else 'walk'
        clip,hm,poses=pose(item,op,action,r['facing'],r['phase'])
        x,y=r['x'],r['y'];nx,ny=r['outside'];bg=r['backgroundRGB']
        rec=dict(character=r['character'],state=r['state'],facing=r['facing'],phase=r['phase'],
            x=x,y=y,outside=[nx,ny],backgroundRGB=bg,backgroundY=r['backgroundY'],region=r['region'],variants={})
        for label,a in poses.items():
            rgbs=[a[y,x,:3].tolist()]
            if 0<=nx<32 and 0<=ny<64 and a[ny,nx,3]:
                rgbs.append(a[ny,nx,:3].tolist())
            ratios=[float(measurement.ratio(rgb,bg)) for rgb in rgbs]
            rec['variants'][label]=dict(bandRGB=rgbs,ratio=max(ratios),bodyRatio=ratios[0],
                outerAtReferenceRatio=ratios[-1])
        result.append(rec)
    (OUT/'after-per-edge.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in result),encoding='utf-8')
    def summarize(rs,label,field='ratio'):
        a=np.array([r['variants'][label][field] for r in rs]);return dict(edgePairs=len(rs),
            atLeast3Percent=round(float((a>=3).mean()*100),3),lowContrastPercent=round(float((a<3).mean()*100),3),
            medianRatio=round(float(np.median(a)),4))
    report=dict(metric='Best body-edge/exterior-outline layer contrast against the same actual background RGB; physical exterior ratio separately reported',targets=TARGETS,
        actualOriginalPerimeterPairs=len(result),dualLayer={},outerAtReference={},perCharacter={},backgroundBuckets={})
    for label in LABELS:
        report['dualLayer'][label]=summarize(result,label)
        report['outerAtReference'][label]=summarize(result,label,'outerAtReferenceRatio')
    for ident in indexed:
        group=[r for r in result if r['character']==ident]
        if group:report['perCharacter'][ident]={label:summarize(group,label) for label in LABELS}
    for bucket,condition in [('dark-background',lambda r:r['backgroundY']<.1),
                             ('light-background',lambda r:r['backgroundY']>=.25),
                             ('dark-head-on-dark',lambda r:r['region']=='head' and r['backgroundY']<.1 and r['variants']['original']['bodyRatio']<3)]:
        group=[r for r in result if condition(r)]
        report['backgroundBuckets'][bucket]={label:dict(dualLayer=summarize(group,label),outer=summarize(group,label,'outerAtReferenceRatio')) for label in LABELS}
    report['targetResults']=dict(overall=report['dualLayer']['rev2']['atLeast3Percent']>=75,
        dark=report['backgroundBuckets']['dark-background']['rev2']['dualLayer']['atLeast3Percent']>=45)
    write(OUT/'contrast.json',report)
    print(json.dumps({k:report[k] for k in ('dualLayer','backgroundBuckets','targetResults')},indent=2),flush=True)
    return report


def actual_composites(data,op,m,desk,ds,chair,cs):
    items,seats=op.room_items(m,desk,ds,chair,cs)
    indexed={d['ident']:d for d in data}
    matched=[]
    for state,records,file in [('seated',read(OUT.parent/'measurements.json')['integrated'],'iso-twelve-working-1x.png'),
                              ('walking',read(OUT.parent/'walking-integrated-measurements.json')['cases'],'iso-arrival-02.0s-1x.png')]:
        actual=op.rgba(SCENES/file);images={label:actual.copy() for label in LABELS}
        panels=[]
        for record in records:
            if record['character'] not in indexed:continue
            item=indexed[record['character']];pos=tuple(record['position']);x,y=pos
            action='seated-typing-front' if state=='seated' else 'walk'
            clip,hm,poses=pose(item,op,action,record['facing'],record['phase']);src=poses['original']
            key=seats[pos]['key'] if state=='seated' else (record['groundDepth'],0,'walker',item['ident'])
            covered=cover(items,pos,key);owned=~covered & (src[:,:,3]>0)
            eq=np.all(crop_canvas(actual,pos)==src,axis=2)
            assert float(eq[hm&owned].mean())>=.95
            before=np.array(actual[y:y+64,x:x+32]);changed_counts={}
            for label,a in poses.items():
                changed=np.any(a!=src,axis=2)&~covered
                existing=(src[:,:,3]>0)&changed
                added=(src[:,:,3]==0)&(a[:,:,3]>0)&changed
                safe=(existing&eq)|added
                images[label][y:y+64,x:x+32][safe]=a[safe]
                changed_counts[label]=dict(replaced=int(safe.sum()),unmatchedExistingPreserved=int((existing&~eq).sum()))
            matched.append(dict(character=item['ident'],state=state,position=list(pos),visibleSourceHeadFraction=float(eq[hm&owned].mean()),changes=changed_counts))
            panels.append((item['ident']+' '+record['facing']+' '+str(record['phase']),pos))
        for label,a in images.items():scales('integrated-'+state+'-'+label,Image.fromarray(a))
        board=Image.new('RGBA',(4*128,len(panels)*140),'#E7D1AC');draw=ImageDraw.Draw(board)
        for n,(title,(x,y)) in enumerate(panels):
            draw.text((2,n*140+2),title,fill='#111924')
            for j,label in enumerate(LABELS):
                draw.text((j*128+2,n*140+16),label,fill='#111924')
                im=Image.fromarray(images[label]).crop((x-48,y-10,x+80,y+96))
                board.alpha_composite(im,(j*128,n*140+30))
        scales('comparison-actual-'+state,board)
    write(OUT/'actual-composite-proof.json',dict(sourceRoot=str(SCENES),cases=matched,
        unchangedUnknownSourcePixels=True,scope='Matched original actor texels and newly added outline pixels; static full-room v3 desk/chair queue.'))


def all_five_composites(data,op,m,desk,ds,chair,cs):
    items,seats=op.room_items(m,desk,ds,chair,cs)
    empty=Image.open(SCENES/'iso-empty-1x.png').convert('RGBA')
    walking=read(OUT.parent/'walking-integrated-measurements.json')['cases']
    native_rows=[];sample_records=[];paired_records=[]
    metrics=load('outline_all_five_metric',OUT/'measure_baseline.py')
    for item in data:
        for facing in op.FACINGS:
            pos,seat=next((p,s) for p,s in seats.items() if s['facing']==facing)
            clip,hm,poses=pose(item,op,'seated-typing-front',facing,0)
            native_rows.append((item['ident']+' seated '+facing,pos,seat['key'],poses))
        for q in walking:
            clip,hm,poses=pose(item,op,'walk',q['facing'],q['phase'])
            key=(q['groundDepth'],0,'walker',item['ident'])
            native_rows.append((item['ident']+' walk '+q['facing'],tuple(q['position']),key,poses))
        # A real navy-wall strip and a checker-floor edge in the supplied empty PNG.
        for name,pos,key in [('wall',(52,152),(4.0,0,'walker',item['ident'])),
                             ('dark-floor',(473,123),(5.0,0,'walker',item['ident']))]:
            clip,hm,poses=pose(item,op,'walk','se',0)
            native_rows.append((item['ident']+' '+name,pos,key,poses))
    boards={label:Image.new('RGBA',(9*128,len(data)*132),'#E7D1AC') for label in LABELS}
    combined=Image.new('RGBA',(4*144,len(native_rows)*132),'#E7D1AC');draw=ImageDraw.Draw(combined)
    for n,(title,pos,key,poses) in enumerate(native_rows):
        covered=cover(items,pos,key)
        original_canvas=scene(empty,items,poses['original'],pos,key)
        common_tag=dict(character=title.split()[0],state=title.split()[1],facing=title.split()[-1],
                        source='all-five-on-real-empty-render-counterfactual')
        reference=metrics.perimeter(poses['original'],np.array(original_canvas),pos,~covered,common_tag)
        for r in reference:
            x,y=r['x'],r['y'];nx,ny=r['outside'];bg=r['backgroundRGB'];r['variants']={}
            for label,a in poses.items():
                colors=[a[y,x,:3]]
                if 0<=nx<32 and 0<=ny<64 and a[ny,nx,3]:colors.append(a[ny,nx,:3])
                r['variants'][label]=float(max(metrics.ratio(c,bg) for c in colors))
            paired_records.append(r)
        draw.text((2,n*132+2),title,fill='#111924')
        for j,label in enumerate(LABELS):
            canvas=scene(empty,items,poses[label],pos,key)
            x,y=pos;crop=canvas.crop((x-44,y-8,x+84,y+88))
            draw.text((j*144+2,n*132+16),label,fill='#111924')
            combined.alpha_composite(crop,(j*144,n*132+30))
            bx=(n%9)*128;by=(n//9)*132
            bd=ImageDraw.Draw(boards[label]);words=title.split();bd.text((bx+2,by+2),words[0],fill='#111924')
            bd.text((bx+2,by+16),' '.join(words[1:]),fill='#111924')
            boards[label].alpha_composite(crop,(bx,by+30))
            tag=dict(character=title.split()[0],state=title.split()[1],facing=title.split()[-1],
                     source='all-five-on-real-empty-render-counterfactual',variant=label)
            sample_records.extend(metrics.perimeter(poses[label],np.array(canvas),pos,~covered,tag))
    scales('comparison-all-five',combined)
    for label,im in boards.items():scales('all-five-'+label,im)
    # Compact owner sheet: all five on seated, walking, wall, checker; all four revisions.
    chosen=[r for r in native_rows if 'seated se' in r[0] or 'walk se' in r[0] or 'wall' in r[0] or 'dark-floor' in r[0]]
    owner=Image.new('RGBA',(4*128,len(chosen)*132),'#E7D1AC');d=ImageDraw.Draw(owner)
    for n,(title,pos,key,poses) in enumerate(chosen):
        d.text((2,n*132+2),title,fill='#111924')
        for j,label in enumerate(LABELS):
            canvas=scene(empty,items,poses[label],pos,key);x,y=pos
            d.text((j*128+2,n*132+16),label,fill='#111924')
            owner.alpha_composite(canvas.crop((x-44,y-8,x+84,y+88)),(j*128,n*132+30))
    scales('owner-comparison',owner)
    (OUT/'all-five-exterior-per-edge.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in sample_records),encoding='utf-8')
    write(OUT/'all-five-exterior-contrast.json',dict(scope='Counterfactual full-room queue composites on actual empty-render pixels; physical outer perimeter, includes added ring',
        all={label:metrics.summary([r for r in sample_records if r['variant']==label]) for label in LABELS},
        dark={label:metrics.summary([r for r in sample_records if r['variant']==label and r['backgroundY']<.1]) for label in LABELS}))
    def band_summary(rows):
        return {label:metrics.summary([dict(r,ratio=r['variants'][label]) for r in rows]) for label in LABELS}
    (OUT/'all-five-dual-layer-per-edge.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in paired_records),encoding='utf-8')
    write(OUT/'all-five-dual-layer-contrast.json',dict(scope='Same original perimeter pairs across all revisions; full-room v3 queue on the supplied empty raster, nine poses/probes per character. Includes intentionally difficult wall/checker probes; these are counterfactual actor placements.',
        all=band_summary(paired_records),dark=band_summary([r for r in paired_records if r['backgroundY']<.1]),
        perCharacter={d['ident']:dict(all=band_summary([r for r in paired_records if r['character']==d['ident']]),
            dark=band_summary([r for r in paired_records if r['character']==d['ident'] and r['backgroundY']<.1])) for d in data}))


def post_occlusion(data,op):
    """Measure and replace source-checked post-occlusion actors using native queues."""
    indexed={d['ident']:d for d in data};cache={};pairs=[];proof=[];panels=[]
    metrics=load('outline_post_metric',OUT/'measure_baseline.py')
    for suffix in ('after','walk-after'):
        queue=read(POST/suffix/'queue.json')
        for rec in queue:
            ident=rec['design'].split('/')[-1]
            if ident not in indexed or rec['action'] not in ('typing','walk-in','walk-out'):continue
            item=indexed[ident];action='seated-typing-front' if rec['action']=='typing' else 'walk'
            clip,hm,poses=pose(item,op,action,rec['facing'],rec['phase'])
            png=POST/suffix/(rec['name']+'-4x.png')
            with Image.open(png) as im:
                native=im.convert('RGBA').resize((rec['scene']['Width'],rec['scene']['Height']),Image.Resampling.NEAREST)
            actual=np.array(native);body=next(i for i in rec['scene']['Items'] if i['InstanceId']=='actor/body')
            pos=(body['X'],body['Y']);x,y=pos;covered=np.zeros((64,32),bool)
            for it in rec['scene']['Items']:
                if it['InstanceId']=='actor/body' or it['Depth']<=body['Depth']:continue
                fr=next(f for f in rec['frames'] if f['InstanceId']==it['InstanceId']);frame=fr['frame']
                path=POST/'base-pack'/fr['sheet']['File']
                if not path.exists():
                    # Some post snapshots retain only the old base-pack chair.
                    # Read the exact content-addressed PNG from the shared pack.
                    path=ART/'pack'/fr['sheet']['File']
                if path not in cache:cache[path]=Image.open(path).convert('RGBA')
                part=cache[path].crop((frame['X'],frame['Y'],frame['X']+frame['Width'],frame['Y']+frame['Height']))
                mask=Image.new('L',(32,64));mask.paste(part.getchannel('A'),(it['X']-x,it['Y']-y))
                covered|=np.array(mask)>0
            src=poses['original'];eq=np.all(crop_canvas(actual,pos)==src,axis=2);owned=~covered&(src[:,:,3]>0)
            match=float(eq[hm&owned].mean()) if (hm&owned).any() else 0
            if match<.95:
                proof.append(dict(name=rec['name'],matched=False,headSourceMatch=match));continue
            tag=dict(character=ident,state=action,facing=rec['facing'],phase=rec['phase'],
                source=str(png),actorPosition=list(pos))
            per=metrics.perimeter(src,actual,pos,~covered,tag,require_source=True)
            for r in per:
                xx,yy=r['x'],r['y'];nx,ny=r['outside'];bg=r['backgroundRGB'];r['variants']={}
                for label,a in poses.items():
                    colors=[a[yy,xx,:3]]
                    if 0<=nx<32 and 0<=ny<64 and a[ny,nx,3]:colors.append(a[ny,nx,:3])
                    r['variants'][label]=float(max(metrics.ratio(c,bg) for c in colors))
                pairs.append(r)
            hand_points=walk_hand_points(item,rec['facing'],rec['phase']) if action=='walk' else None
            before=counts(roi(src,hm,clip,item['profile'],hand_points)[0],src,covered)
            after=counts(roi(src,hm,clip,item['profile'],hand_points)[0],poses['rev2'],covered)
            assert before==after
            proof.append(dict(name=rec['name'],matched=True,headSourceMatch=match,before=before,after=after,edgePairs=len(per)))
            if rec['phase']==0 and rec['action'] in ('typing','walk-in'):
                images={}
                for label,a in poses.items():
                    target=actual.copy();changed=np.any(a!=src,axis=2)&~covered
                    safe=changed&((eq&(src[:,:,3]>0))|((src[:,:,3]==0)&(a[:,:,3]>0)))
                    # Clip the patch to PNG bounds (post probes can reach frame edge).
                    if x>=0 and y>=0 and x+32<=target.shape[1] and y+64<=target.shape[0]:
                        target[y:y+64,x:x+32][safe]=a[safe]
                        images[label]=Image.fromarray(target)
                if len(images)==4:panels.append((rec['name'],images))
    board=Image.new('RGBA',(4*192,len(panels)*176),'#E7D1AC');draw=ImageDraw.Draw(board)
    for n,(name,images) in enumerate(panels):
        draw.text((2,n*176+2),name,fill='#111924')
        for j,label in enumerate(LABELS):
            draw.text((j*192+2,n*176+16),label,fill='#111924');board.alpha_composite(images[label],(j*192,n*176+30))
    scales('comparison-post-occlusion',board)
    (OUT/'post-occlusion-per-edge.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in pairs),encoding='utf-8')
    summaries={label:metrics.summary([dict(r,ratio=r['variants'][label]) for r in pairs]) for label in LABELS}
    dark={label:metrics.summary([dict(r,ratio=r['variants'][label]) for r in pairs if r['backgroundY']<.1]) for label in LABELS}
    write(OUT/'post-occlusion-proof.json',dict(sourceRoot=str(POST),cases=proof,dualLayer=summaries,dark=dark,
        nativeScale='Exact nearest downsample of 4x supplied PNG; original source RGBA equality required',
        scope='Existing post-occlusion snapshots for three pilots; no new WPF render.'))
    print('Post-occlusion: '+str(sum(r['matched'] for r in proof))+' exact-source cases, '+str(len(pairs))+' perimeter pairs',flush=True)


def run(data,op,apply_expected=False):
    verifier=load('outline_strong_v3',ART/'furniture-v3/workstations/verify-desk-rig-v3-lane.py');m=verifier.m
    desk,ds=verifier.furniture(ART/'furniture-v3/workstations','desk')
    chair,cs=verifier.furniture(ART/'furniture-v3/chair','chair')
    protected={p:hashlib.sha256(p.read_bytes()).hexdigest() for base in (ART/'furniture-v3/workstations',ART/'furniture-v3/chair') for p in base.iterdir() if p.is_file()}
    report=band_records(data,op)
    actual_composites(data,op,m,desk,ds,chair,cs)
    all_five_composites(data,op,m,desk,ds,chair,cs)
    post_occlusion(data,op)
    summaries=visibility(data,op,m,desk,ds,chair,cs)
    assert all(hashlib.sha256(p.read_bytes()).hexdigest()==h for p,h in protected.items()),'furniture drift'
    coverage={d['ident']:dict(normalCells=152,outlineRingPixels=sum(r['rev2']['ring'] for r in d['proof']['records']),
        missingEligibleRingPixels=0,protectedHeadAndHandsExact=True) for d in data}
    write(OUT/'RESULT.json',dict(appliedAfterEvidence=apply_expected,targets=TARGETS,contrast=report,
        visibility=summaries,coverage=coverage,limits=['Static native raster composites; WPF runtime/repack not executed.',
        'Direct #111924 outer contrast remains weak on navy backgrounds; report includes it separately.',
        'Complete canonical head RGBA is preserved, so dark hair on dark backgrounds can remain weak.',
        'Rev1 for glasses-bun and hanbok-modern is explicitly simulated, since rev1 had only three pilots.']))
    assert all(report['targetResults'].values()),'rev2 readability target missed; preview only, production not modified'
