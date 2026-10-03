"""Read-only source review; every output stays beside this script. Python -B + Pillow/numpy."""
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[4]
ART = ROOT / 'assets/art/iso'
FURN = ART / 'furniture-v2/workstations'
FACINGS = ('se', 'sw', 'ne', 'nw')
PILOTS = {
    'dev-hoodie-v2': ('dev-hoodie', 'runs/v2', ('6D3713', '8B4D15', 'AC5F2D', 'BA672C', 'C97E32')),
    'punk-girl': ('punk-girl', 'runs', ('E36854', 'F19B5B', 'FBC186', 'F7C4B7')),
    'professor': ('professor', 'runs/v1', ('744B31', 'C49F7C', 'EFA763', 'F3B06B', 'F6AD62')),
}
SOURCES = {}


def source(path):
    if path.is_relative_to(ART / 'characters'):
        path = OUT / 'rev2-source/actor-inputs' / path.relative_to(ART / 'characters')
        if path.name.endswith('.iso.json'): path = path.with_name(path.name.replace('.iso.json', '.json'))
    elif path.is_relative_to(OUT / 'rev2-source') and path.name.endswith('.iso.json'):
        path = path.with_name(path.name.replace('.iso.json', '.json'))
    SOURCES[str(path.relative_to(ROOT)).replace('\\', '/')] = hashlib.sha256(path.read_bytes()).hexdigest()
    return path


def load_json(path):
    return json.loads(source(path).read_text(encoding='utf-8'))


def load_image(path):
    return Image.open(source(path)).convert('RGBA')


def point(p):
    return p['x'], p['y']


def project(i, j):
    return math.floor(32 * (i - j) + .5), math.floor(16 * (i + j) + .5)


def shift(image, dx=0, dy=0):
    out = Image.new('RGBA', image.size)
    out.alpha_composite(image, (dx, dy))
    return out


def mirrored(image):
    return shift(image.transpose(Image.Transpose.FLIP_LEFT_RIGHT), dx=1)


def cell(sheet, atlas, clip, phase=0):
    w, h = sheet['cellW'], sheet['cellH']
    x, y = (clip.get('column', 0) + phase) * w, clip['row'] * h
    return atlas.crop((x, y, x + w, y + h))


def furniture(name):
    doc = load_json(FURN / (name + '.iso.json'))
    sheets = {s['id']: (s, load_image(FURN / s['file'])) for s in doc['sheets']}
    return doc['furniture'][0], sheets


def variant(family, facing):
    return next(v for v in family['variants'] if v['orientation'] == facing and v['state'] == 'default')


def parts(var, sheets, origin, ground_depth, seat_depth, owner):
    items = []
    for p in var['parts']:
        s, atlas = sheets[p['sheetId']]
        c = next(c for c in s['rows'] if c['name'] == p['animation'] and c['direction'] == p['direction'])
        assert all(p[k] == c[k] for k in ('partId', 'depthAnchor', 'isoZOrder')), p['partId']
        depth = seat_depth if p.get('seatGroupId') else ground_depth + sum(p['depthAnchor'].values())
        anchor = point(c.get('anchor', s['anchor']))
        part_image = cell(s, atlas, c)
        assert set(np.unique(np.array(part_image)[:,:,3])) <= {0,255}, p['partId']
        items.append({'key': (depth, p['isoZOrder'], owner, p['partId']),
                      'id': p['partId'], 'image': part_image,
                      'pos': (origin[0] - anchor[0], origin[1] - anchor[1]),
                      'seat': bool(p.get('seatGroupId')), 'family': owner})
    if var.get('screen'):
        desc=var['screen'];s,atlas=sheets[desc['sheetId']]
        c=next(c for c in s['rows'] if c['name']==desc['animation'] and c['direction']==desc['direction'])
        depth=ground_depth+sum(c['depthAnchor'].values());anchor=point(c.get('anchor',s['anchor']))
        items.append(dict(key=(depth,c['isoZOrder'],owner,c['partId']),id=c['partId'],image=cell(s,atlas,c),pos=(origin[0]-anchor[0],origin[1]-anchor[1]),seat=False,family=owner))
    return items


def canonical_head(folder, run, facing):
    """Alpha boundaries from each pilot's builder, never inferred from furniture overlap."""
    base = ART / 'characters' / folder / run
    if folder == 'professor':
        return load_image(base / ('head-' + facing + '.png'))
    f = facing if folder == 'punk-girl' else ('se' if facing in ('se', 'sw') else 'ne')
    im = load_image(base / (f + '-idle-native.png'))
    end = 38 if folder == 'punk-girl' else (41 if f == 'se' else 39)
    out = Image.new('RGBA', (32, 64))
    out.paste(im.crop((0, 0, 32, end)), (0, 0))
    if folder == 'dev-hoodie-v2' and f == 'se':
        # build.py's one-time eye/headphone/neck repairs can extend canonical alpha.
        d = ImageDraw.Draw(out)
        for x, y in ((15, 33), (20, 32)):
            d.rectangle((x, y, x + 1, y + 2), fill='white')
        d.line([(18, 36), (19, 36)], fill='white')
        d.rectangle((13, 38, 15, 40), fill='white')
        d.rectangle((21, 37, 23, 39), fill='white')
        d.line([(16, 39), (19, 40), (21, 38)], fill='white')
    return mirrored(out) if folder != 'punk-girl' and facing in ('sw', 'nw') else out


def polygon_mask(points, size=(128, 128)):
    yy, xx = np.indices(size, dtype=float); xx += .5; yy += .5
    mask = np.zeros(size, bool)
    for a, b in zip(points, points[1:] + points[:1]):
        if a[1] != b[1]:
            crossing = (a[1] > yy) != (b[1] > yy)
            mask ^= crossing & (xx < (b[0]-a[0])*(yy-a[1])/(b[1]-a[1])+a[0])
    return mask


def rotated(i, j, facing):
    return {'se': (i, j), 'sw': (1-j, i), 'ne': (j, -i), 'nw': (-i, 1-j)}[facing]


def model_project(p, facing, anchor):
    i, j = rotated(p[0], p[1], facing)
    return 32*(i-j)+anchor[0], 16*(i+j)-p[2]+anchor[1]


def box_mask(bounds, facing, anchor):
    i0,j0,i1,j1,z0,z1 = bounds
    corners = [(i0,j0),(i1,j0),(i1,j1),(i0,j1)]
    faces = [[(*p,z1) for p in corners]]
    faces += [[(*a,z0),(*b,z0),(*b,z1),(*a,z1)] for a,b in zip(corners,corners[1:]+corners[:1])]
    mask = np.zeros((128,128),bool)
    for face in faces:
        mask |= polygon_mask([model_project(p,facing,anchor) for p in face])
    return mask


ARM_PATHS = {
    'dev-hoodie-v2': {
        'front': [[(12,37),(17,41),(24,43)],[(21,37),(24,41),(28,42)]],
        'back': [[(12,36),(19,37),(24,34)],[(21,36),(25,36),(28,33)]]},
    'punk-girl': {
        'front': [[(13,38),(20,41),(23,42),(26,43)],[(21,38),(25,39),(27,41),(29,42)]],
        'back': [[(14,38),(22,37),(25,35),(27,33)],[(21,38),(26,37),(28,35),(29,33)]]},
    'professor': {
        'front': [[(11,36),(17,41),(26,43)],[(21,35),(25,40),(29,43)]],
        'back': [[(11,35),(17,38),(26,33)],[(21,34),(25,37),(29,33)]]},
}
SCENARIOS = ('baseline','keyboard-rear','surface-rear','monitor-base-rear',
             'hands-up1','arms-hands-up1','keyboard-down1','keyboard-camera8',
             'monitor-side8','surface-rear+monitor-side8','surface-rear+monitor-side12',
             'monitor-inward8','surface-rear+monitor-inward8',
             'surface-rear+monitor-inward12',
             'arms-over-strip','arms-over-all','monitor-absent')


def regions(sprite, head, clip, skin_hex, folder, facing):
    a = np.array(sprite); opaque = a[:,:,3] == 255; hm = np.array(head)[:,:,3] > 0
    assert set(np.unique(a[:,:,3])) <= {0,255}
    assert not np.any(hm & ~opaque), 'canonical head not present'
    yy,xx = np.indices(opaque.shape); skin = np.zeros_like(opaque); hands = skin.copy()
    for color in skin_hex:
        skin |= np.all(a[:,:,:3] == tuple(bytes.fromhex(color)),axis=2)
    distances = []
    for h in clip['handAnchors']:
        radius = np.maximum(abs(xx-h['x']),abs(yy-h['y']))
        hands |= (radius <= 2) & skin & opaque & ~hm
        distances.append(int(radius[skin & opaque & ~hm].min()))
    assert max(distances) <= 1
    corridor = Image.new('L',(32,64)); d = ImageDraw.Draw(corridor)
    paths = ARM_PATHS[folder]['back' if facing in ('ne','nw') else 'front']
    for path in paths:
        if facing in ('sw','nw'):
            path = [(32-x,y) for x,y in path]
        width = 5 if folder == 'dev-hoodie-v2' else 4
        d.line(path[:2],fill=255,width=width)
        d.line(path[1:],fill=255,width=4 if folder == 'punk-girl' else 3)
    arms = (np.array(corridor)>0) & opaque & ~hm & ~hands
    torso = opaque & ~hm & ~hands & (11 <= xx) & (xx <= 22) & (yy < clip['seatAnchor']['y'])
    return {'hands':hands,'forearmSleeve':arms,'head':hm,'torso':torso}, distances


def local_mask(mask, item, actor_pos):
    im = Image.fromarray(mask.astype(np.uint8)*255)
    out = Image.new('L',(32,64))
    out.paste(im,(item['pos'][0]-actor_pos[0],item['pos'][1]-actor_pos[1]))
    return np.array(out)>0


def physical_masks(item, orientation):
    a = np.array(item['image']); opaque = a[:,:,3]>0
    if 'categoryMasks' in item:
        return {k:v & opaque for k,v in item['categoryMasks'].items()}
    if item['family'] == 'chair':
        return {'chair':opaque}
    anchor = (-item['pos'][0]+96,-item['pos'][1]+88)
    if '__monitor__' in item['id'] or '__screen__' in item['id']:
        ci = -.125 if FURN == OUT else .25
        housing = box_mask((ci-.25,-.42,ci+.18,.42,21,36),orientation,anchor)
        base = box_mask((ci-.06,-.20,ci+.12,.20,18,21),orientation,anchor)
        return {'monitorBase':opaque & base & ~housing,
                'monitorHousing':opaque & ~(base & ~housing)}
    if item['seat']:
        top = polygon_mask([model_project(p,orientation,anchor) for p in
                            [(-.43,-.43,18),(.625,-.43,18),(.625,1.22,18),(-.43,1.22,18)]])
        keyboard = polygon_mask([model_project(p,orientation,anchor) for p in
                                 [(.555,-.22,18),(.695,-.22,18),(.695,.22,18),(.555,.22,18)]])
        # Contact keycaps are raster-snapped, so retain both explicit texels at H.
        hx,hy = model_project((.625,0,18),orientation,anchor)
        for x in (round(hx)-1,round(hx)+1):
            keyboard[round(hy),x] = True
        return {'keyboard':opaque & keyboard,'desktopSurface':opaque & top & ~keyboard,
                'fasciaOrTrim':opaque & ~(top | keyboard)}
    return {'otherDesk':opaque}


def move_mask(mask, dy):
    out = np.zeros_like(mask)
    if dy > 0: out[dy:] = mask[:-dy]
    elif dy < 0: out[:dy] = mask[-dy:]
    else: out[:] = mask
    assert out.sum() == mask.sum(), 'ROI moved outside actor cell'
    return out


def translate_categories(item, dx, dy):
    item['categoryMasks'] = {k:np.array(shift(Image.fromarray(v.astype(np.uint8)*255).convert('RGBA'),dx,dy))[:,:,0]>0
                             for k,v in item['categoryMasks'].items()}


def move_pixels(sprite, selected, dy):
    a = np.array(sprite); layer = a.copy(); layer[~selected] = 0; a[selected] = 0
    out = Image.fromarray(a); out.alpha_composite(shift(Image.fromarray(layer),dy=dy))
    return out


def measure_scene(items, sprite, masks, pos, akey, orientation, facing, scenario, clip, skin):
    changed = [dict(i,categoryMasks=physical_masks(i,orientation)) for i in items]; masks = dict(masks); lift = None
    front = facing in ('se','sw')
    if front:
        for i in list(changed):
            pm = physical_masks(i,orientation)
            select = None
            if i['family']=='desk' and i['seat'] and ('surface-rear' in scenario or scenario=='keyboard-rear'):
                select = pm['keyboard']
                if 'surface-rear' in scenario: select = select | pm['desktopSurface']
            if scenario=='monitor-base-rear' and '__monitor__' in i['id']:
                select = pm['monitorBase']
            if select is not None and select.any():
                a=np.array(i['image']); rear=a.copy();rear[~select]=0;a[select]=0
                assert np.array_equal(a+rear,np.array(i['image']))
                i['image']=Image.fromarray(a)
                changed.append(dict(i,image=Image.fromarray(rear),id=i['id']+':rear',key=(akey[0],-20,'desk',i['id']+':rear')))
            if 'monitor-side' in scenario and ('__monitor__' in i['id'] or '__screen__' in i['id']):
                n=int(scenario.split('monitor-side')[1]); dx=n if facing=='se' else -n
                i['image']=shift(i['image'],dx=dx,dy=-n//2)
                translate_categories(i,dx,-n//2)
                # Translation is along desk width, not a rank bypass. Ordinary ground keys move too.
                delta=-n/32
                i['key']=(i['key'][0]+delta,i['key'][1],i['key'][2],i['key'][3])
            if scenario=='monitor-absent' and ('__monitor__' in i['id'] or '__screen__' in i['id']):
                i['image']=Image.new('RGBA',i['image'].size)
        dy = {'hands-up1':-1,'arms-hands-up1':-1,'keyboard-down1':1,'keyboard-camera8':8}.get(scenario,0)
        if dy:
            selected=masks['hands'] | (masks['forearmSleeve'] if scenario=='arms-hands-up1' else False)
            sprite=move_pixels(sprite,selected,dy); masks['hands']=move_mask(masks['hands'],dy)
            if scenario=='arms-hands-up1': masks['forearmSleeve']=move_mask(masks['forearmSleeve'],dy)
            if scenario.startswith('keyboard-'):
                for i in changed:
                    if i['family']=='desk' and i['seat']:
                        pm=physical_masks(i,orientation);i['image']=move_pixels(i['image'],pm['keyboard'],dy)
                        cats=dict(i['categoryMasks']);cats['keyboard']=move_mask(pm['keyboard'],dy)
                        for category in ('desktopSurface','fasciaOrTrim'):
                            cats[category] = cats[category] & ~cats['keyboard']
                        i['categoryMasks']=cats
        if scenario.startswith('arms-over-'):
            selected=masks['hands']|masks['forearmSleeve'];a=np.array(sprite);a[~selected]=0
            key=(akey[0],15,'actor',akey[3]+':arms') if scenario=='arms-over-strip' else (max(i['key'][0] for i in changed)+.001,0,'actor',akey[3]+':arms')
            lift={'key':key,'image':Image.fromarray(a),'pos':pos,'id':'actor:arms','family':'actor'}
    if 'monitor-inward' in scenario:
        n = int(scenario.split('monitor-inward')[1])
        vector = {'se': (1,0), 'sw': (0,1), 'ne': (0,-1), 'nw': (-1,0)}[facing]
        delta = n / 32
        dx,dy = project(delta*vector[0],delta*vector[1])
        for i in changed:
            if '__monitor__' in i['id'] or '__screen__' in i['id']:
                i['image'] = shift(i['image'],dx=dx,dy=dy)
                translate_categories(i,dx,dy)
                i['key'] = (i['key'][0]+delta*sum(vector),i['key'][1],i['key'][2],i['key'][3])
    # The old central torso proxy may include removed sleeve pixels in motion probes.
    # It is auxiliary only; requested hand and limb denominators stay fixed.
    masks['torso'] = masks['torso'] & (np.array(sprite)[:,:,3]>0)
    all_items=changed+[{'key':akey,'image':sprite,'pos':pos,'id':'actor','family':'actor'}]
    if lift: all_items.append(lift)
    ordered=sorted(all_items,key=lambda i:i['key'])
    labels=Image.new('L',(192,144));scene=Image.new('RGBA',(192,144),'#E7D1AC')
    part_owner=np.full((64,32),'',object); categories=np.full((64,32),'',object)
    independent_cover=np.zeros((64,32),bool)
    for i in ordered:
        scene.alpha_composite(i['image'],i['pos'])
        labels.paste(1 if i['family']=='actor' else 2,i['pos'],i['image'].getchannel('A'))
        if i['family']=='actor':
            if i['id']=='actor:arms':
                alpha=local_mask(np.array(i['image'])[:,:,3]>0,i,pos);part_owner[alpha]='';categories[alpha]='';independent_cover[alpha]=False
            continue
        if i['key']<=akey: continue
        alpha=local_mask(np.array(i['image'])[:,:,3]>0,i,pos)
        part_owner[alpha]=i['id'];independent_cover|=alpha
        for category,mask in physical_masks(i,orientation).items():
            categories[local_mask(mask,i,pos)]=category
    owned=np.array(labels.crop((*pos,pos[0]+32,pos[1]+64)))==1
    counts={}
    for name,mask in masks.items():
        visible=int((mask & owned).sum());total=int(mask.sum())
        assert visible==int((mask & ~independent_cover).sum()), (scenario,name)
        occluders={str(p):int(((part_owner==p)&mask&~owned).sum()) for p in np.unique(part_owner[mask & ~owned])}
        category_counts={str(p):int(((categories==p)&mask&~owned).sum()) for p in np.unique(categories[mask & ~owned])}
        assert sum(occluders.values())==total-visible
        assert sum(category_counts.values())==total-visible
        counts[name]={'visible':visible,'total':total,'fraction':visible/total if total else None,'topmostParts':occluders,'topmostCategories':category_counts}
    a=np.array(sprite);skinmask=np.zeros((64,32),bool);yy,xx=np.indices(skinmask.shape)
    for color in skin: skinmask|=np.all(a[:,:,:3]==tuple(bytes.fromhex(color)),axis=2)&(a[:,:,3]>0)
    distances=[int(np.maximum(abs(xx-h['x']),abs(yy-h['y']))[skinmask & ~masks['head']].min()) for h in clip['handAnchors']]
    return counts,scene,distances


def save_pair(name, im):
    im.save(OUT/(name+'-1x.png'))
    im.resize((im.width*3,im.height*3),Image.Resampling.NEAREST).save(OUT/(name+'-3x.png'))


def ratio(m):
    return f"{m['visible']}/{m['total']} ({100*m['visible']/m['total']:.1f}%)" if m['total'] else 'N/A'



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
