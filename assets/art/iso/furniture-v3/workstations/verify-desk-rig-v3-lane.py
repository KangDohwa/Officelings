"""Serialized alpha ownership, fixed rev2 pilot ROIs, all-phase walkers. Lane only."""
from pathlib import Path
import hashlib, importlib.util, json, sys
import numpy as np
from PIL import Image, ImageDraw
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent; ROOT=OUT.parents[4]; OLD=ROOT/'assets/art/iso/furniture-v2/workstations'
spec=importlib.util.spec_from_file_location('rev2_roi_readonly',OLD/'measure-A3-rev2.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
def read(p):return json.loads(p.read_text(encoding='utf-8'))
def opaque(im):return np.array(im)[:,:,3]>0
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def furniture(root,name):
    d=read(root/(name+'.iso.json')); return d['furniture'][0],{s['id']:(s,Image.open(root/s['file']).convert('RGBA')) for s in d['sheets']}
def scene(items,sprite,pos,key,regions):
    canvas=Image.new('RGBA',(320,256),'#E7D1AC'); labels=Image.new('L',canvas.size); cover=np.zeros((64,32),bool)
    layers=items+[dict(key=key,image=sprite,pos=pos,id='actor',family='actor')]
    for it in sorted(layers,key=lambda t:t['key']):
        canvas.alpha_composite(it['image'],it['pos']);labels.paste(1 if it['id']=='actor' else 2,it['pos'],it['image'].getchannel('A'))
        if it['id']!='actor' and it['key']>key:cover|=m.local_mask(opaque(it['image']),it,pos)
    owned=np.array(labels.crop((*pos,pos[0]+32,pos[1]+64)))==1
    assert np.array_equal(owned&opaque(sprite),~cover&opaque(sprite))
    results={}
    for n,mask in regions.items():
        total=int(mask.sum()); visible=int((mask&owned).sum()); assert visible==int((mask&~cover).sum())
        results[n]=dict(visible=visible,total=total,fraction=visible/total)
    return results,canvas
def connected(mask):
    seen=np.zeros_like(mask);counts=[]
    for y,x in zip(*np.where(mask)):
        if seen[y,x]:continue
        stack=[(y,x)];seen[y,x]=True;count=0
        while stack:
            yy,xx=stack.pop();count+=1
            for dy,dx in [(-1,-1),(-1,0),(-1,1),(0,-1),(0,1),(1,-1),(1,0),(1,1)]:
                ay,ax=yy+dy,xx+dx
                if 0<=ay<128 and 0<=ax<128 and mask[ay,ax] and not seen[ay,ax]:seen[ay,ax]=True;stack.append((ay,ax))
        counts.append(count)
    return sorted(counts,reverse=True)
def pair(name,board):
    board.save(OUT/(name+'-1x.png'));board.resize((board.width*3,board.height*3),Image.Resampling.NEAREST).save(OUT/(name+'-3x.png'))
def main():
    doc=read(OUT/'desk.iso.json');old=read(OLD/'desk.iso.json');sheet,ss=doc['sheets']
    atlas=Image.open(OUT/sheet['file']).convert('RGBA');scr=Image.open(OUT/ss['file']).convert('RGBA')
    states=[];summaries=[];static={};full_by_f={}; checks=[]
    assert {(c['name'],c['direction'],c['partId'],c['row'],c.get('column',0)) for c in sheet['rows']}=={(c['name'],c['direction'],c['partId'],c['row'],c.get('column',0)) for c in old['sheets'][0]['rows']},'part IDs/storage changed'
    for ov,v in zip(old['furniture'][0]['variants'],doc['furniture'][0]['variants']):
        for k in ov:
            if k!='parts':assert v[k]==ov[k],(v['orientation'],k,'frozen field changed')
        f=v['orientation'];body=Image.new('RGBA',(128,128));owners=np.zeros((128,128),np.uint8)
        for p in v['parts']:
            c=next(c for c in sheet['rows'] if c['partId']==p['partId']); assert all(p[k]==c[k] for k in ['depthAnchor','isoZOrder'])
            im=m.cell(sheet,atlas,c);owners+=opaque(im);body.alpha_composite(im)
        assert owners.max()==1,(f,'duplicated body pixel')
        digest=hashlib.sha256(body.tobytes()).hexdigest();assert static.setdefault(f,digest)==digest
        sc=next(c for c in ss['rows'] if c['direction']==f and c['name']==v['screen']['animation'])
        if sc['state']=='working':assert sc['frames']==4 and sc['fps']==3
        mask=opaque(Image.open(OUT/f'desk-screen-mask-{f}.png').convert('RGBA'))
        monitor=np.zeros_like(mask)
        for c in sheet['rows']:
            if c['direction']==f and '__monitor__' in c['partId']:
                assert c['isoZOrder']==0 and not c.get('seatGroupId');monitor|=opaque(m.cell(sheet,atlas,c))
        assert len(connected(monitor|mask))==1,(f,'disconnected display/neck/foot')
        for phase in range(sc['frames']):
            im=m.cell(ss,scr,sc,phase);assert np.array_equal(opaque(im),mask);assert not np.any(mask&(owners>0))
            full=body.copy();full.alpha_composite(im)
            anchor=m.point(sc['anchor'])
            top=m.polygon_mask([m.model_project(p,f,anchor) for p in [(-.43,-.43,18),(.625,-.43,18),(.625,1.22,18),(-.43,1.22,18)]])
            assert np.all(opaque(full)[top]),(f,'desktop hole')
            if v['state']=='default':
                assert full.tobytes()==Image.open(OUT/f'desk-empty-{f}.png').convert('RGBA').tobytes()
                full_by_f[f]=full
            assert set(np.unique(np.array(full)[:,:,3]))<={0,255}
            states.append(dict(desk=f,state=v['state'],phase=phase,bodySHA256=digest,maskExact=True,bodyDisjoint=True,overlayDisjoint=True,monitorConnected=True))
        for prop in ['rear-15','rear-16']:
            pc=next(c for c in sheet['rows'] if c['direction']==f and c['name']==prop)
            assert not np.any(opaque(m.cell(sheet,atlas,pc))&(monitor|mask))
    records=[];walkers=[];transitions=[];panels={};queues=[]
    desk,ds=furniture(OUT,'desk');prior,pds=furniture(OLD,'desk');chair,cs=furniture(OLD,'chair')
    protected={str(p.relative_to(ROOT)):sha(p) for p in OLD.glob('desk*') if p.is_file()}
    protected.update({str(p.relative_to(ROOT)):sha(p) for p in OLD.glob('chair*') if p.is_file()})
    walkboard=Image.new('RGBA',(768,432),'#E7D1AC')
    for ci,(folder,(name,run,skin)) in enumerate(m.PILOTS.items()):
        ad=m.load_json(m.ART/'characters'/folder/(name+'.iso.json'))
        s=next(s for s in ad['sheets'] if s['kind']=='character');a=m.load_image(m.ART/'characters'/folder/s['file'])
        for di,f in enumerate(m.FACINGS):
            v=m.variant(desk,f);pv=m.variant(prior,f);pr=v['seatProfiles'][0];wf=pr['isoFacing'];ground=pr['seatGround'];sd=sum(ground.values())
            origin=(160,128);gp=m.project(ground['i'],ground['j']);co=(origin[0]+gp[0],origin[1]+gp[1]);cv=m.variant(chair,wf)
            items=m.parts(v,ds,origin,0,sd,'desk')+m.parts(cv,cs,co,sd,sd,'chair')
            beforeitems=m.parts(pv,pds,origin,0,sd,'desk')+m.parts(cv,cs,co,sd,sd,'chair')
            clip=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==wf)
            assert m.point(clip['seatAnchor'])==(16,44)
            c=m.point(pr['contactOffset']);pos=(origin[0]+c[0]-16,origin[1]+c[1]-44);key=(sd,0,'actor',folder)
            assert [(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in pr['handContacts']]
            # Both frozen contacts are physically present on keyboard/mouse lips.
            cell_anchor=m.point(next(c for c in sheet['rows'] if c['direction']==f)['anchor'])
            kpart=next(p for p in v['parts'] if p.get('seatGroupId') and p['isoZOrder']==-20)
            kc=next(c for c in sheet['rows'] if c['partId']==kpart['partId']);kim=m.cell(sheet,atlas,kc)
            for h in pr['handContacts']:assert kim.getpixel((cell_anchor[0]+h['x'],cell_anchor[1]+h['y']))[3]==255
            head=m.canonical_head(folder,run,wf)
            for phase in range(4):
                sprite=m.cell(s,a,clip,phase);off=m.point(s['headOffsetsByClip'][clip['name']+'/'+wf][phase]);regions,dist=m.regions(sprite,m.shift(head,*off),clip,skin,folder,wf)
                b,bs=scene(beforeitems,sprite,pos,key,regions);n,ns=scene(items,sprite,pos,key,regions)
                for roi in regions:assert b[roi]['total']==n[roi]['total']
                assert n['head']['fraction']>=(.85 if wf in ('ne','nw') else .90),(folder,f,phase,'head gate',n['head'])
                assert n['head']['visible']>=b['head']['visible'],(folder,f,phase,'head regression',b['head'],n['head'])
                assert n['hands']['fraction']==1,(folder,f,phase,'hands',n['hands'])
                records.append(dict(character=folder,desk=f,worker=wf,phase=phase,before=b,after=n,handDistances=dist,actorDestination=pos,actorKey=key))
                if phase==0:panels[folder,f]=(bs.crop((64,32,256,176)),ns.crop((64,32,256,176)))
            queues.append(dict(character=folder,desk=f,items=[dict(partId=i['id'],key=i['key'],destination=i['pos'],seatGroup=i['seat']) for i in sorted(items,key=lambda i:i['key'])]))
            # Walkers retain their own ground key in all facings and phases.
            sitter=dict(key=(sd,0,'seated',folder),id='seated',family='seated',image=m.cell(s,a,clip),pos=pos)
            w,h=v['footprintW'],v['footprintH']
            probes=list(dict.fromkeys([(-1,0),(-1,h-1),(w,0),(w,h-1),(0,-1),(w-1,-1),(0,h),(w-1,h)]))+[(-1,-1),(w,h)]
            assert len(probes)==8
            for pi,(i,j) in enumerate(probes):
                wg=m.project(i,j);wp=(origin[0]+wg[0]-16,origin[1]+wg[1]-60);wk=(i+j,0,'walker',folder)
                for facing in m.FACINGS:
                    wc=next(c for c in s['rows'] if c['name']=='walk' and c['direction']==facing);wh=m.canonical_head(folder,run,facing)
                    for phase in range(4):
                        sprite=m.cell(s,a,wc,phase);off=m.point(s['headOffsetsByClip']['walk/'+facing][phase]);hm=opaque(m.shift(wh,*off));assert not np.any(hm&~opaque(sprite))
                        n,ns=scene(items+[sitter],sprite,wp,wk,{'head':hm,'body':opaque(sprite)})
                        if wk>max(it['key'] for it in items+[sitter]):assert n['body']['fraction']==1
                        walkers.append(dict(character=folder,desk=f,walker=facing,phase=phase,ground=[i,j],ownDepth=i+j,seatDepth=sd,**n,ownershipCrossCheck=True))
                        if pi==(ci%8) and facing==m.FACINGS[di] and phase==0:
                            crop=ns.crop((64,32,256,176));walkboard.alpha_composite(crop,(di*192,ci*144));ImageDraw.Draw(walkboard).text((di*192+2,ci*144+2),folder+' '+f+' own-depth walker',fill='#191E1E')
            # Head/feet transition checks are relevant to the taller screen.
            low=[];rise=[]
            for action in ['sit-down','stand-up']:
                tc=next(c for c in s['rows'] if c['name']==action and c['direction']==wf)
                for phase,pc in enumerate(tc['phaseContacts']):
                    sa=m.point(pc['seatAnchor']);of=m.point(pc['contactOffset']);tp=(co[0]+of[0]-sa[0],co[1]+of[1]-sa[1])
                    assert tp[1]+pc['footY']==co[1]
                    sprite=m.cell(s,a,tc,phase);off=m.point(s['headOffsetsByClip'][action+'/'+wf][phase]);hm=opaque(m.shift(head,*off));assert not np.any(hm&~opaque(sprite))
                    n,ns=scene(items,sprite,tp,key,{'head':hm});b,bs=scene(beforeitems,sprite,tp,key,{'head':hm})
                    assert n['head']['fraction']>=(.85 if wf in ('ne','nw') else .90),(folder,f,action,phase,n['head'])
                    assert n['head']['visible']>=b['head']['visible'],(folder,f,action,phase,'transition regression')
                    transitions.append(dict(character=folder,desk=f,action=action,phase=phase,head=n['head'],beforeHead=b['head'],feetGrounded=True))
                    (low if action=='sit-down' else rise).append((sprite.tobytes(),tp))
            assert low==rise[::-1]
    board=Image.new('RGBA',(1536,432),'#E7D1AC')
    for ci,folder in enumerate(m.PILOTS):
        for di,f in enumerate(m.FACINGS):
            for pi,im in enumerate(panels[folder,f]):
                board.alpha_composite(im,((2*di+pi)*192,ci*144));ImageDraw.Draw(board).text(((2*di+pi)*192+2,ci*144+2),folder+' '+f+' '+('v2' if pi==0 else 'v3'),fill='#191E1E')
    pair('contact-before-after-desk-rig-v3',board);pair('walkers-desk-rig-v3',walkboard)
    empty=Image.new('RGBA',(1536,840),'#E7D1AC');d=ImageDraw.Draw(empty)
    for di,f in enumerate(m.FACINGS):
        for ri,p in enumerate([OLD/f'desk-empty-{f}.png',OUT/f'desk-empty-{f}.png']):
            im=Image.open(p).convert('RGBA');bx=di*384;by=ri*280
            empty.alpha_composite(im,(bx,by+20));crop=im.crop((16,15,112,105));empty.alpha_composite(crop.resize((288,270),Image.Resampling.NEAREST),(bx+94,by+12));d.text((bx+2,by+2),f+' '+('v2' if ri==0 else 'v3')+' 1x / 3x',fill='#191E1E')
        v=m.variant(desk,f);wi=[i for i in m.parts(v,ds,(76 if f in ('se','nw') else 52,72),0,0,'desk') if '__screen__' not in i['id']]
        work=Image.new('RGBA',(128,128))
        for it in wi:work.alpha_composite(it['image'],(0,0))
        wc=next(c for c in ss['rows'] if c['direction']==f and c['state']=='working');work.alpha_composite(m.cell(ss,scr,wc,0))
        empty.alpha_composite(work,(di*384,600));empty.alpha_composite(work.crop((32,15,104,76)).resize((216,183),Image.Resampling.NEAREST),(di*384+128,590));d.text((di*384+2,570),'v3 working 1x / 3x',fill='#191E1E')
    empty.save(OUT/'empty-before-after-desk-rig-v3.png')
    # Three comparison sources shown with original/native pixels plus nearest 3x.
    hero=Image.open(ROOT/'assets/art/iso-concept/iso-office-hero.png').convert('RGBA')
    integrated=Image.open('C:/Codex-Workspace/vd-lanes/isowire/artifacts/iso-integration-final/iso-demo-24.0s-2x.png').convert('RGBA')
    ref=Image.new('RGBA',(1536,1410),'#E7D1AC');ref.alpha_composite(empty,(0,0));rd=ImageDraw.Draw(ref)
    hc=hero.crop((685,350,840,510));ic=integrated.crop((604,291,772,465)).resize((84,87),Image.Resampling.NEAREST)
    for bx,crop,title in [(0,hc,'hero original crop / 3x'),(640,ic,'recent integrated render: native 1x / 3x')]:
        rd.text((bx+2,855),title,fill='#191E1E');ref.alpha_composite(crop,(bx,880));ref.alpha_composite(crop.resize((crop.width*3,crop.height*3),Image.Resampling.NEAREST),(bx+170,880))
    ref.save(OUT/'comparison-v2-hero-integrated-desk-rig-v3.png')
    for folder in m.PILOTS:
        rows=[r for r in records if r['character']==folder]
        for facing in m.FACINGS:
            rr=[r for r in rows if r['worker']==facing]
            agg={roi:{k:sum(r['after'][roi][k] for r in rr) for k in ['visible','total']} for roi in ['head','torso','hands','forearmSleeve']}
            summaries.append(dict(character=folder,worker=facing,**agg))
    assert all(sha(ROOT/p)==digest for p,digest in protected.items())
    drift=[]
    for p,digest in m.SOURCES.items():
        if '/rev2-source/actor-inputs/' not in p:continue
        rel=p.split('/rev2-source/actor-inputs/')[1];live=ROOT/'assets/art/iso/characters'/rel
        if live.name.endswith('.json'):live=live.with_name(live.name[:-5]+'.iso.json')
        if live.exists() and sha(live)!=digest:drift.append(dict(path=str(live.relative_to(ROOT)),frozenSHA256=digest,liveSHA256=sha(live)))
    payload=dict(checks=dict(serializedStateFrames=len(states),bodyByteIdenticalAcrossStates=True,disjointBodyAndScreen=True,desktopNoHoles=True,monitorConnected=True,allPartIdsAndStorageRetained=True,allFrozenVariantFieldsRetained=True,pilotTypingFrames=len(records),hands100percent=True,headGatesAndNonRegression=True,roiDenominatorsPreserved=True,contactTexelsKeyboardMouse=True,walkerCases=len(walkers),walkersOwnGroundDepth=True,ownershipCrossCheck=True,transitionFrames=len(transitions),transitionHeadGatesAndNonRegression=True,transitionFeetAndReverse=True,readOnlyV2InputsUnchanged=True),states=states,records=records,summaries=summaries,queues=queues,walkers=walkers,transitions=transitions,sourceSHA256=m.SOURCES,protectedV2SHA256=protected,liveActorDrift=drift,scope='Static Pillow serialized-part queue; fixed A3 rev2 actor snapshots and read-only v2 chair. No WPF/DPI/runtime ID ties/nav/continuous-playback claim.')
    (OUT/'measurements-desk-rig-v3.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    lines=['DESK RIG v3 — 실제 직렬화된 아틀라스 검증','',json.dumps(payload['checks'],ensure_ascii=False,indent=2),'','고정된 rev2 캐릭터 snapshot·기존 v2 의자 기준. 실제 head alpha/offset, 동일 torso·hand·forearm ROI 분모.','머리: NE/NW worker 85% 이상, SE/SW worker 90% 이상 및 v2 대비 비감소. 손: 모든 48 typing frame 100%.','', '| 캐릭터 | worker | 머리 | torso ROI | 손 ROI | forearm/sleeve ROI |','|---|---|---:|---:|---:|---:|']
    for r in summaries:
        def ratio(k):
            d=r[k];return f"{d['visible']}/{d['total']} ({100*d['visible']/d['total']:.1f}%)"
        lines.append(f"| {r['character']} | {r['worker'].upper()} | "+' | '.join(ratio(k) for k in ['head','torso','hands','forearmSleeve'])+' |')
    lines+=['','48개 개별 phase와 보행자 1,536건·sit/stand 96 frame의 원시 분자/분모는 measurements-desk-rig-v3.json에 기록.',f'현재 character 원본과 snapshot의 SHA 차이: {len(drift)}개.','검증 범위: 정적 직렬화 아틀라스/레이어 queue. WPF 실제 렌더링·DPI·picking·연속 이동은 미실행.']
    (OUT/'measurements-desk-rig-v3.txt').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps(payload['checks'],indent=2));print('Live actor drift:',len(drift))

if __name__=='__main__':main()
