"""Local serialized-art, state and overlap verification; no runtime or shared writes."""
from pathlib import Path
import hashlib, importlib.util, json, subprocess
import numpy as np
from PIL import Image, ImageDraw
OUT=Path(__file__).resolve().parent;ROOT=OUT.parents[4];OLD=OUT/'rev2-source'
spec=importlib.util.spec_from_file_location('review_a3',OUT/'measure-A3-rev2.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
def read(p):return json.loads(p.read_text(encoding='utf-8'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def opaque(im):return np.array(im)[:,:,3]>0
def connected(mask):
    seen=np.zeros_like(mask);components=[]
    for y,x in zip(*np.where(mask)):
        if seen[y,x]:continue
        stack=[(y,x)];seen[y,x]=True;count=0
        while stack:
            a,b=stack.pop();count+=1
            for dy,dx in ((-1,-1),(-1,0),(-1,1),(0,-1),(0,1),(1,-1),(1,0),(1,1)):
                yy,xx=a+dy,b+dx
                if 0<=yy<128 and 0<=xx<128 and mask[yy,xx] and not seen[yy,xx]:seen[yy,xx]=True;stack.append((yy,xx))
        components.append(count)
    return sorted(components,reverse=True)
def overlay(items,sprite,pos,key,mask):
    layers=items+[dict(key=key,image=sprite,pos=pos,id='probe',family='actor')]
    canvas=Image.new('RGBA',(320,256),'#E7D1AC');labels=Image.new('L',canvas.size)
    cover=np.zeros((64,32),bool)
    for item in sorted(layers,key=lambda t:t['key']):
        canvas.alpha_composite(item['image'],item['pos']);labels.paste(1 if item['id']=='probe' else 2,item['pos'],item['image'].getchannel('A'))
        if item['id']!='probe' and item['key']>key:cover|=m.local_mask(opaque(item['image']),item,pos)
    owned=np.array(labels.crop((*pos,pos[0]+32,pos[1]+64)))==1
    assert np.array_equal(owned&opaque(sprite),~cover&opaque(sprite))
    visible=int((mask&owned).sum());total=int(mask.sum())
    if key>max(i['key'] for i in items):assert visible==total,'front walker hidden'
    return dict(visible=visible,total=total,fraction=visible/total),canvas
def main():
    doc=read(OUT/'desk.iso.json');old=read(OLD/'desk.json');sheet,ss=doc['sheets'];atlas=Image.open(OUT/sheet['file']).convert('RGBA');screens=Image.open(OUT/ss['file']).convert('RGBA');clips={c['partId']:c for c in sheet['rows']};oldclips={c['partId']:c for c in old['sheets'][0]['rows']};proof=read(OUT/'repaint-proof-A3-rev2.json');states=[];static_by_f={};checks=[]
    for ov,v in zip(old['furniture'][0]['variants'],doc['furniture'][0]['variants']):
        for k in ov:
            if k!='parts':assert ov[k]==v[k],(v['orientation'],k)
        f=v['orientation'];owner=np.zeros((128,128),np.uint8);body=Image.new('RGBA',(128,128))
        for p in v['parts']:
            c=clips[p['partId']];assert all(p[k]==c[k] for k in ('partId','depthAnchor','isoZOrder'))
            im=m.cell(sheet,atlas,c);owner+=opaque(im);body.alpha_composite(im)
        assert owner.max()==1,(f,'duplicate body pixels')
        digest=hashlib.sha256(body.tobytes()).hexdigest()
        assert static_by_f.setdefault(f,digest)==digest,(f,'state body drift')
        sc=next(c for c in ss['rows'] if c['name']==v['screen']['animation'] and c['direction']==f);mask=opaque(Image.open(OUT/f'desk-screen-mask-{f}.png').convert('RGBA'))
        for phase in range(sc['frames']):
            im=m.cell(ss,screens,sc,phase);assert np.array_equal(opaque(im),mask);assert not np.any(mask&(owner>0))
            full=body.copy();full.alpha_composite(im)
            if v['state']=='default':assert full.tobytes()==Image.open(OUT/f'desk-empty-{f}.png').convert('RGBA').tobytes()
            anchor=m.point(sc['anchor']);vertices=[m.model_project(p,f,anchor) for p in [(-.43,-.43,18),(.625,-.43,18),(.625,1.22,18),(-.43,1.22,18)]];top=m.polygon_mask([(round(x),round(y)) for x,y in vertices])
            assert np.all(opaque(full)[top]),(f,'exposed desktop hole')
            crt=np.zeros((128,128),bool)
            for c in sheet['rows']:
                if c['direction']==f and '__monitor__' in c['partId']:crt|=opaque(m.cell(sheet,atlas,c))
            crt|=mask;comp=connected(crt);assert len(comp)==1,(f,'floating CRT',comp)
            props=[opaque(m.cell(sheet,atlas,clips[f'desk__rear__{f}__{n}'])) for n in ('15','16')]
            assert not np.any(props[0]&props[1]) and not any(np.any(p&crt) for p in props)
            states.append(dict(desk=f,state=v['state'],phase=phase,bodySHA256=digest,disjointBody=True,maskExact=True,desktopCovered=True,CRTcomponents=comp,propSilhouettesDisjoint=True))
    for f in m.FACINGS:
        d=next(p for p in proof if p['orientation']==f);di,dj=d['gridDelta'];dx,dy=d['pixelDelta']
        for c in [c for c in sheet['rows']+ss['rows'] if c['direction']==f and ('__monitor__' in c['partId'] or '__screen__' in c['partId'])]:
            os=old['sheets'][0 if '__monitor__' in c['partId'] else 1];oc=next(x for x in os['rows'] if x['name']==c['name'] and x['direction']==f)
            assert c['anchor']==oc['anchor'] and c['isoZOrder']==oc['isoZOrder'] and not c.get('seatGroupId')
            if '__monitor__' in c['partId']:assert c['isoZOrder']==0
            assert c['depthAnchor']==dict(i=round(oc['depthAnchor']['i']+di,4),j=round(oc['depthAnchor']['j']+dj,4))
        surface=clips[f'desk__rear__{f}__14'];assert surface['isoZOrder']==-20 and surface['seatGroupId']=='seat'
        front=next(c for c in sheet['rows'] if c['direction']==f and c.get('seatGroupId') and c['isoZOrder']==10);assert front['depthAnchor']==surface['depthAnchor']
    protected=read(OLD/'protected-hashes.json');assert all(sha(ROOT/p)==digest for p,digest in protected.items()),'protected furniture changed'
    drift=[]
    for p in (OLD/'actor-inputs').rglob('*'):
        if p.is_file():
            relative=p.relative_to(OLD/'actor-inputs');live=ROOT/'assets/art/iso/characters'/relative
            if live.name.endswith('.json'):live=live.with_name(live.name[:-5]+'.iso.json')
            if live.exists() and sha(live)!=sha(p):drift.append(dict(live=str(live.relative_to(ROOT)),snapshotSHA256=sha(p),liveSHA256=sha(live)))
    # All pilots, all walk facings/phases, eight near/far/end positions for every desk.
    walkers=[];transitions=[];walkboard=Image.new('RGBA',(768,432),'#E7D1AC');transboard=Image.new('RGBA',(1536,1728),'#E7D1AC')
    m.FURN=OUT;desk,ds=m.furniture('desk');chair,cs=m.furniture('chair')
    before={}
    for root in (OLD,OUT):
        m.FURN=root;dd,dss=m.furniture('desk');cc,css=m.furniture('chair');before[root]=(dd,dss,cc,css)
    m.FURN=OUT
    for ci,(folder,(name,run,skin)) in enumerate(m.PILOTS.items()):
        base=m.ART/'characters'/folder;ad=m.load_json(base/(name+'.iso.json'));s=next(s for s in ad['sheets'] if s['kind']=='character');a=m.load_image(base/s['file'])
        for di,orientation in enumerate(m.FACINGS):
            v=m.variant(desk,orientation);profile=v['seatProfiles'][0];ground=profile['seatGround'];sd=sum(ground.values());gp=m.project(ground['i'],ground['j']);origin=(160,128);co=(origin[0]+gp[0],origin[1]+gp[1]);facing=profile['isoFacing'];cv=m.variant(chair,facing)
            items=m.parts(v,ds,origin,0,sd,'desk')+m.parts(cv,cs,co,sd,sd,'chair')
            seated=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==facing)
            sitter=dict(key=(sd,0,'seated',folder),id='seated',family='seated',image=m.cell(s,a,seated),pos=(co[0]-16,co[1]-56))
            w,h=v['footprintW'],v['footprintH'];probes=list(dict.fromkeys([(-1,0),(-1,h-1),(w,0),(w,h-1),(0,-1),(w-1,-1),(0,h),(w-1,h)]))+[(-1,-1),(w,h)];assert len(probes)==len(set(probes))==8
            for pi,(i,j) in enumerate(probes):
                for wf in m.FACINGS:
                    wc=next(c for c in s['rows'] if c['name']=='walk' and c['direction']==wf);head=m.canonical_head(folder,run,wf);g=m.project(i,j);pos=(origin[0]+g[0]-16,origin[1]+g[1]-60);key=(i+j,0,'walker',folder)
                    for phase in range(4):
                        sprite=m.cell(s,a,wc,phase);off=m.point(s['headOffsetsByClip']['walk/'+wf][phase]);hm=opaque(m.shift(head,*off));assert not np.any(hm&~opaque(sprite))
                        count,scene=overlay(items+[sitter],sprite,pos,key,hm);walkers.append(dict(character=folder,desk=orientation,worker=wf,phase=phase,ground=[i,j],ownGroundDepth=key[0],seatDepth=sd,head=count,ownershipCrossCheck=True))
                        if wf==m.FACINGS[pi%4] and phase==pi%4 and pi in (0,3):
                            small=scene.crop((64,32,256,176));walkboard.alpha_composite(small,(di*192,ci*144));ImageDraw.Draw(walkboard).text((di*192+3,ci*144+2),folder+' D:'+orientation+' own-ground walker',fill='#191E1E')
            lowered=[];raised=[]
            for action in ('sit-down','stand-up'):
                clip=next(c for c in s['rows'] if c['name']==action and c['direction']==facing);head=m.canonical_head(folder,run,facing)
                for phase,pc in enumerate(clip['phaseContacts']):
                    anchor=m.point(pc['seatAnchor']);offset=m.point(pc['contactOffset']);pos=(co[0]+offset[0]-anchor[0],co[1]+offset[1]-anchor[1]);key=(sd,0,'actor',folder);sprite=m.cell(s,a,clip,phase);off=m.point(s['headOffsetsByClip'][action+'/'+facing][phase]);hm=opaque(m.shift(head,*off))
                    assert not np.any(hm&~opaque(sprite));assert pos[1]+pc['footY']==co[1],'transition sole drift'
                    count,scene=overlay(items,sprite,pos,key,hm);assert count['fraction']>=(.85 if facing in ('ne','nw') else .90),(folder,facing,action,phase,count)
                    bd,bds,bc,bcs=before[OLD];bv=m.variant(bd,orientation);bcv=m.variant(bc,facing);olditems=m.parts(bv,bds,origin,0,sd,'desk')+m.parts(bcv,bcs,co,sd,sd,'chair');prior,_=overlay(olditems,sprite,pos,key,hm);assert count['visible']>=prior['visible'],'transition head regression'
                    transitions.append(dict(character=folder,desk=orientation,worker=facing,action=action,phase=phase,head=count,beforeHead=prior,destination=pos,groundDepth=sd,sourceSeatAnchor=anchor,contactOffset=offset,footY=pc['footY'],ownershipCrossCheck=True))
                    (lowered if action=='sit-down' else raised).append((sprite.tobytes(),pos))
                    if action=='sit-down':
                        small=scene.crop((64,32,256,176));bx=di*384;by=(ci*4+phase)*144;transboard.alpha_composite(small,(bx,by));transboard.alpha_composite(small,(bx+192,by));ImageDraw.Draw(transboard).text((bx+2,by+2),folder+' '+orientation+' lower p'+str(phase),fill='#191E1E')
            assert lowered==raised[::-1],(folder,facing,'transition reversal')
    walkboard.save(OUT/'walkers-A3-rev2-1x.png');walkboard.resize((2304,1296),Image.Resampling.NEAREST).save(OUT/'walkers-A3-rev2-3x.png');transboard.save(OUT/'transitions-A3-rev2-1x.png')
    # A compact 3x transition view selects phases 0 and 3 beside the four-phase native board.
    zoom=Image.new('RGBA',(2304,2592),'#E7D1AC')
    for ci in range(3):
        for di in range(4):
            for pi,phase in enumerate((0,3)):
                crop=transboard.crop((di*384,(ci*4+phase)*144,di*384+192,(ci*4+phase+1)*144));im=crop.resize((576,432),Image.Resampling.NEAREST);zoom.alpha_composite(im,(di*576,(ci*2+pi)*432))
    zoom.save(OUT/'transitions-A3-rev2-3x.png')
    payload=dict(checks=dict(serializedStateFrames=len(states),disjointReconstruction=True,allScreenMasksAndBodyStates=True,desktopNoHoles=True,CRTConnected=True,propMasksDisjointAndContactValid=True,allMonitorDepthDeltas=True,unchangedVariantsACH=True,protectedFurnitureFiles=len(protected),protectedFurnitureUnchanged=True,walkerCases=len(walkers),transitionFrames=len(transitions),independentOwnership=True,transitionFeetAndReverse=True,transitionHeadGatesAndNonRegression=True),states=states,walkers=walkers,transitions=transitions,actorInputDrift=drift,protectedSHA256=protected,scope='Independent Pillow alpha ownership versus depth-union. Static full-phase probes; no WPF, DPI, runtime ID tie ordering, collision/nav path or continuous playback claim.')
    (OUT/'export-overlap-proof-A3-rev2.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(payload['checks'],indent=2));print('Actor drift:',len(drift))
if __name__=='__main__':main()
