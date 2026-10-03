"""Copy review logic verbatim, then adapt paths and final-art reporting in this lane."""
from pathlib import Path
OUT=Path(__file__).resolve().parent;ROOT=OUT.parents[4]
src=(ROOT/'assets/art/iso/design-review-hands/measure.py').read_text(encoding='utf-8')
src=src[:src.index('def main():')]
src=src.replace('ROOT = OUT.parents[3]','ROOT = OUT.parents[4]')
src=src.replace("def source(path):\n", "def source(path):\n    if path.is_relative_to(ART / 'characters'):\n        path = OUT / 'rev2-source/actor-inputs' / path.relative_to(ART / 'characters')\n        if path.name.endswith('.iso.json'): path = path.with_name(path.name.replace('.iso.json', '.json'))\n    elif path.is_relative_to(OUT / 'rev2-source') and path.name.endswith('.iso.json'):\n        path = path.with_name(path.name.replace('.iso.json', '.json'))\n")
src=src.replace('return json.loads(source(path).read_text', 'return json.loads(source(path).read_text')
# Baseline classification on the new CRT uses the moved physical model; counts depend only on real alpha/queue.
src=src.replace('housing = box_mask((0,-.42,.43,.42,21,36)',"ci = -.125 if FURN == OUT else .25\n        housing = box_mask((ci-.25,-.42,ci+.18,.42,21,36)")
src=src.replace('base = box_mask((.19,-.20,.37,.20,18,21)', 'base = box_mask((ci-.06,-.20,ci+.12,.20,18,21)')
src=src.replace('    return items\n', '''    if var.get('screen'):
        desc=var['screen'];s,atlas=sheets[desc['sheetId']]
        c=next(c for c in s['rows'] if c['name']==desc['animation'] and c['direction']==desc['direction'])
        depth=ground_depth+sum(c['depthAnchor'].values());anchor=point(c.get('anchor',s['anchor']))
        items.append(dict(key=(depth,c['isoZOrder'],owner,c['partId']),id=c['partId'],image=cell(s,atlas,c),pos=(origin[0]-anchor[0],origin[1]-anchor[1]),seat=False,family=owner))
    return items
''',1)
src+=r'''
def main():
    global FURN
    records=[];panels={};queues=[];roi_evidence=[]
    for stage,root in [('before',OUT/'rev2-source'),('after',OUT)]:
        FURN=root;desk,ds=furniture('desk');chair,cs=furniture('chair')
        for folder,(name,run,skin) in PILOTS.items():
            base=ART/'characters'/folder;doc=load_json(base/(name+'.iso.json'));sheet=next(s for s in doc['sheets'] if s['kind']=='character');atlas=load_image(base/sheet['file'])
            for orientation in FACINGS:
                v=variant(desk,orientation);profile=v['seatProfiles'][0];facing=profile['isoFacing'];ground=profile['seatGround'];sd=sum(ground.values());origin=(96,88);g=project(ground['i'],ground['j']);chair_origin=(origin[0]+g[0],origin[1]+g[1]);cv=variant(chair,v['linkedChair']['orientation'])
                clip=next(c for c in sheet['rows'] if c['name']=='seated-typing-front' and c['direction']==facing);c=point(profile['contactOffset']);sa=point(clip['seatAnchor']);assert c==(g[0],g[1]-12) and sa==(16,44)
                pos=(origin[0]+c[0]-sa[0],origin[1]+c[1]-sa[1]);akey=(sd,0,'actor',folder)
                assert [(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in profile['handContacts']]
                items=parts(v,ds,origin,0,sd,'desk')+parts(cv,cs,chair_origin,sd,sd,'chair')
                queues.append(dict(stage=stage,character=folder,desk=orientation,worker=facing,actorKey=akey,actorDestination=pos,items=[dict(partId=i['id'],key=i['key'],destination=i['pos'],seatGroup=i['seat']) for i in sorted(items,key=lambda i:i['key'])]))
                head=canonical_head(folder,run,facing)
                for phase in range(4):
                    sprite=cell(sheet,atlas,clip,phase);offset=point(sheet['headOffsetsByClip'][clip['name']+'/'+facing][phase]);masks,dist=regions(sprite,shift(head,*offset),clip,skin,folder,facing)
                    counts,scene,dist=measure_scene(items,sprite,masks,pos,akey,orientation,facing,'baseline',clip,skin)
                    records.append(dict(stage=stage,character=folder,desk=orientation,worker=facing,phase=phase,handDistancesToFrozenH=dist,**counts))
                    if stage=='after':
                        roi_evidence.append(dict(character=folder,worker=facing,phase=phase,regions={n:[[int(x),int(y)] for y,x in zip(*np.where(mask))] for n,mask in masks.items()}))
                        assert counts['head']['fraction'] >= (.85 if facing in ('ne','nw') else .90)
                        assert counts['hands']['fraction']==1, (folder,facing,phase,counts['hands'])
                    if phase==0:panels[(stage,folder,orientation)]=scene
    for after in [r for r in records if r['stage']=='after']:
        before=next(r for r in records if r['stage']=='before' and all(r[k]==after[k] for k in ('character','worker','phase')))
        for roi in ('head','torso','hands','forearmSleeve'):assert before[roi]['total']==after[roi]['total']
        assert after['head']['visible']>=before['head']['visible'],('head regression',after)
    board=Image.new('RGBA',(1536,432),'#E7D1AC');d=ImageDraw.Draw(board)
    for ci,folder in enumerate(PILOTS):
        for di,orientation in enumerate(FACINGS):
            for si,stage in enumerate(('before','after')):
                im=panels[(stage,folder,orientation)].copy();ImageDraw.Draw(im).text((2,2),f'{folder} D:{orientation} {stage}',fill='#191E1E');board.alpha_composite(im,((di*2+si)*192,ci*144))
    save_pair('contact-before-after-A3-rev2',board)
    hero=load_image(ROOT/'assets/art/iso-concept/iso-office-hero.png')
    boxes=[(680,345,840,505),(970,525,1118,650),(970,660,1098,800)]
    for ci,folder in enumerate(PILOTS):
        board=Image.new('RGBA',(1230,1510),'#E7D1AC');d=ImageDraw.Draw(board)
        for di,orientation in enumerate(FACINGS):
            im=panels[('after',folder,orientation)]
            bx=(di%2)*615;by=(di//2)*490
            board.alpha_composite(im,(bx+400,by+20));board.alpha_composite(im.resize((576,432),Image.Resampling.NEAREST),(bx,by+55));d.text((bx+3,by+4),folder+' D:'+orientation+' 3x / native 1x inset',fill='#191E1E')
        for j,box in enumerate(boxes):
            crop=hero.crop(box);board.alpha_composite(crop,(j*410,1005));board.alpha_composite(crop.resize((crop.width*3,crop.height*3),Image.Resampling.NEAREST),(j*410,1170));d.text((j*410+3,982),'hero reference (original pixels + 3x)',fill='#191E1E')
        board.save(OUT/('quality-'+folder+'-A3-rev2.png'))
    for path,digest in SOURCES.items():assert hashlib.sha256((ROOT/path).read_bytes()).hexdigest()==digest,path
    payload=dict(method='Verbatim design-review-hands regions/measure_scene; actual before/after exported parts, fixed actor snapshots. Local cover cross-checked with full-scene ownership. Default state, no runtime claim.',records=records,queues=queues,roiPixelCoordinates=roi_evidence,armPaths=ARM_PATHS,sourcesSHA256=SOURCES,checks=dict(frames=96,afterFrames=48,headGates=True,headNonRegression=True,allHandsVisible=True,denominatorsPreserved=True,ownershipCrossCheck=True))
    (OUT/'measurements-A3-rev2.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    lines=['# A3 rev2 실제 파츠 가시성','', '고정한 최신 캐릭터 snapshot, 실제 default 파츠·ground queue. 손: 접점 반경2 피부, 전완/소매: 리뷰의 제작 관절 corridor, 몸통: x11..22 / pelvis 위의 alpha에서 머리·접점 피부 제외. 해부학 전체 부품 비율은 아니다. 모든 phase의 전후 분모를 보존하고 Pillow 전체 소유권과 alpha 합집합을 교차 확인했다.','', '| 캐릭터 | worker / desk | phase | 머리 | 몸통 ROI | 손 | 전완/소매 |','|---|---|---:|---:|---:|---:|---:|']
    for r in records:
        if r['stage']=='after':lines.append(f"| {r['character']} | {r['worker'].upper()} / {r['desk'].upper()} | {r['phase']} | {ratio(r['head'])} | {ratio(r['torso'])} | {ratio(r['hands'])} | {ratio(r['forearmSleeve'])} |")
    lines+=['','## 4phase 합산','', '| 캐릭터 | worker | 머리 | 몸통 ROI | 손 | 전완/소매 |','|---|---|---:|---:|---:|---:|']
    aggregate=[]
    for folder in PILOTS:
        for f in FACINGS:
            rr=[r for r in records if r['stage']=='after' and r['character']==folder and r['worker']==f];values={roi:{k:sum(r[roi][k] for r in rr) for k in ('visible','total')} for roi in ('head','torso','hands','forearmSleeve')};aggregate.append(dict(character=folder,worker=f,**values));lines.append(f"| {folder} | {f.upper()} | "+' | '.join(ratio(values[k]) for k in ('head','torso','hands','forearmSleeve'))+' |')
    lines+=['','전후 수치, ROI 좌표, 큐, 입력 SHA-256은 measurements-A3-rev2.json. 기준 머리 NW/NE≥85%, SE/SW≥90% 통과 및 전후 머리 감소 없음. A/C/H 좌표 정확 일치, 피부까지 거리≤1px. 런타임 instance ID의 동점 순서·WPF는 미검증.']
    (OUT/'measurements-A3-rev2.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps(dict(checks=payload['checks'],aggregate=aggregate),indent=2))
if __name__=='__main__':main()
'''
(OUT/'measure-A3-rev2.py').write_text(src,encoding='utf-8')
print('Copied review measurement logic, adapted only lane paths, moved CRT categorization, final-art report.')
