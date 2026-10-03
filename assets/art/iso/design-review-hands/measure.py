"""Read-only source review; every output stays beside this script. Python -B + Pillow/numpy."""
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[3]
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
        housing = box_mask((0,-.42,.43,.42,21,36),orientation,anchor)
        base = box_mask((.19,-.20,.37,.20,18,21),orientation,anchor)
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
    source(ART/'design-review/measure.py')
    for p in [ROOT/'docs/iso-slice-s0.md',ROOT/'runs/furniture-v2/geometry.py',ROOT/'runs/furniture-v2/build.py',FURN/'CHANGES-A3-rev1.md',FURN/'measurements-A3-rev1.md',ART/'design-review/REPORT.md']:
        source(p)
    desk,ds=furniture('desk');chair,cs=furniture('chair');records=[];queues=[];panels={};roi_evidence=[]
    contact=Image.new('RGBA',(768,1728),'#E7D1AC');roi_board=Image.new('RGBA',(768,432),'#E7D1AC')
    for ci,(folder,(name,run,skin)) in enumerate(PILOTS.items()):
        base=ART/'characters'/folder;doc=load_json(base/(name+'.iso.json'));sheet=next(s for s in doc['sheets'] if s['kind']=='character');atlas=load_image(base/sheet['file'])
        source(base/('runs/motion-rev1/redraw.py' if folder=='punk-girl' else 'runs/motion-rev1/build_motion.py'))
        for di,orientation in enumerate(FACINGS):
            v=variant(desk,orientation);profile=next(p for p in v['seatProfiles'] if p['id']=='front');facing=profile['isoFacing'];ground=profile['seatGround'];sd=ground['i']+ground['j'];origin=(96,88);g=project(ground['i'],ground['j']);chair_origin=(origin[0]+g[0],origin[1]+g[1])
            assert point(v['linkedChair']['chairOffset'])==g
            cv=variant(chair,v['linkedChair']['orientation']);clip=next(c for c in sheet['rows'] if c['name']=='seated-typing-front' and c['direction']==facing)
            c=point(profile['contactOffset']);sa=point(clip['seatAnchor']);assert c==(g[0],g[1]-12) and sa==(16,44)
            pos=(origin[0]+c[0]-sa[0],origin[1]+c[1]-sa[1]);akey=(sd,0,'actor',folder)
            assert [(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in profile['handContacts']]
            items=parts(v,ds,origin,0,sd,'desk')+parts(cv,cs,chair_origin,sd,sd,'chair')
            queues.append({'character':folder,'desk':orientation,'worker':facing,'actorDestination':pos,'actorKey':akey,'handAnchors':clip['handAnchors'],'handContacts':profile['handContacts'],'items':[{'partId':i['id'],'key':i['key'],'destination':i['pos'],'seatGroup':i['seat']} for i in sorted(items,key=lambda i:i['key'])]})
            head=canonical_head(folder,run,facing)
            for phase in range(4):
                sprite=cell(sheet,atlas,clip,phase);offset=point(sheet['headOffsetsByClip'][clip['name']+'/'+facing][phase]);masks,distances=regions(sprite,shift(head,*offset),clip,skin,folder,facing)
                roi_evidence.append({'character':folder,'worker':facing,'phase':phase,'handPixelsXY':[[int(x),int(y)] for y,x in zip(*np.where(masks['hands']))],'forearmSleevePixelsXY':[[int(x),int(y)] for y,x in zip(*np.where(masks['forearmSleeve']))]})
                for scenario in SCENARIOS:
                    counts,scene,after_distances=measure_scene(items,sprite,masks,pos,akey,orientation,facing,scenario,clip,skin)
                    records.append({'character':folder,'desk':orientation,'worker':facing,'phase':phase,'scenario':scenario,'handDistancesToFrozenH':after_distances,**counts})
                    if phase==0:panels[(folder,orientation,scenario)]=scene.copy()
                    if scenario=='baseline':
                        d=ImageDraw.Draw(scene);d.text((3,3),f'{folder} D:{orientation} W:{facing} p{phase}',fill='#191E1E');contact.alpha_composite(scene,(di*192,(ci*4+phase)*144))
                if phase==0:
                    colors=np.zeros((64,32,4),np.uint8)
                    for key,color in [('head',(255,90,150,255)),('forearmSleeve',(80,150,255,255)),('hands',(255,220,80,255))]:colors[masks[key]]=color
                    im=Image.new('RGBA',(192,144),'#E7D1AC');im.alpha_composite(sprite,(24,38));im.alpha_composite(Image.fromarray(colors),(100,38));ImageDraw.Draw(im).text((3,3),folder+' '+facing,fill='#191E1E');roi_board.alpha_composite(im,(di*192,ci*144))
    save_pair('contact',contact);save_pair('regions',roi_board)
    for scenario in SCENARIOS:
        if scenario=='baseline':continue
        board=Image.new('RGBA',(384,432),'#E7D1AC')
        for ci,folder in enumerate(PILOTS):
            for di,orientation in enumerate(('nw','ne')):
                im=panels[(folder,orientation,scenario)].copy();d=ImageDraw.Draw(im);d.text((3,3),folder+' '+('se' if orientation=='nw' else 'sw'),fill='#191E1E');d.text((3,15),scenario,fill='#191E1E');board.alpha_composite(im,(di*192,ci*144))
        save_pair('proposal-'+scenario,board)
    selected=('baseline','surface-rear','hands-up1','surface-rear+monitor-inward8','surface-rear+monitor-inward12','arms-over-all');board=Image.new('RGBA',(192*len(selected),864),'#E7D1AC')
    for ci,folder in enumerate(PILOTS):
        for di,orientation in enumerate(('nw','ne')):
            for si,scenario in enumerate(selected):
                im=panels[(folder,orientation,scenario)].copy();d=ImageDraw.Draw(im);d.text((3,3),folder+' W:'+('se' if orientation=='nw' else 'sw'),fill='#191E1E');d.text((3,15),scenario,fill='#191E1E');board.alpha_composite(im,(si*192,(ci*2+di)*144))
    save_pair('proposals',board)
    board=Image.new('RGBA',(768,864),'#E7D1AC')
    for ci,folder in enumerate(PILOTS):
        for di,orientation in enumerate(FACINGS):
            for si,scenario in enumerate(('baseline','surface-rear+monitor-inward12')):
                im=panels[(folder,orientation,scenario)].copy();d=ImageDraw.Draw(im)
                d.text((3,3),folder+' D:'+orientation,fill='#191E1E');d.text((3,15),scenario,fill='#191E1E')
                board.alpha_composite(im,(di*192,(ci*2+si)*144))
    save_pair('inward12-all-directions',board)
    hero=load_image(ROOT/'assets/art/iso-concept/iso-office-hero.png');boxes={'BVT face-visible':(970,525,1118,650),'MINT face-visible':(970,660,1098,800),'CH side view':(812,525,970,650)};board=Image.new('RGBA',(474,160),'#E7D1AC');d=ImageDraw.Draw(board)
    for j,(name,box) in enumerate(boxes.items()):board.alpha_composite(hero.crop(box),(j*158,20));d.text((j*158+2,2),name,fill='#191E1E')
    save_pair('hero-camera-crops',board)
    board=Image.new('RGBA',(1152,512),'#E7D1AC');d=ImageDraw.Draw(board);source_part_detail=[]
    for row,orientation in enumerate(('ne','nw')):
        v=variant(desk,orientation);items=parts(v,ds,(96,88),0,0,'desk');select=[i for i in items if i['seat'] or '__monitor__' in i['id']]
        for col,i in enumerate(select):
            board.alpha_composite(i['image'].resize((256,256),Image.Resampling.NEAREST),(col*288,row*256));d.text((col*288,row*256+2),i['id'],fill='#191E1E')
            pm=physical_masks(i,orientation);source_part_detail.append({'partId':i['id'],'categories':{k:int(v.sum()) for k,v in pm.items()}})
    board.save(OUT/'blocking-parts-2x.png')
    # Holdover ratios use old snapshots. Compare, do not require equality after character drift.
    old=load_json(FURN/'measurements-A3-rev1.json');old_records=old.get('records',[]);drift=[]
    for r in records:
        if r['scenario']!='baseline':continue
        matches=[o for o in old_records if o.get('character')==r['character'] and o.get('desk')==r['desk'] and o.get('phase')==r['phase'] and o.get('stage')=='after']
        if matches:
            o=matches[-1]
            if o.get('hands') and (o['hands']['visible'],o['hands']['total'])!=(r['hands']['visible'],r['hands']['total']):drift.append({'character':r['character'],'worker':r['worker'],'phase':r['phase'],'oldHands':o['hands'],'currentHands':r['hands']})
    assert len(records)==48*len(SCENARIOS)
    for r in records:
        baseline=next(b for b in records if b['scenario']=='baseline' and all(b[k]==r[k] for k in ('character','worker','phase')))
        for region in ('hands','forearmSleeve'):
            assert r[region]['total']==baseline[region]['total'], (r['scenario'],region)
    for p,digest in SOURCES.items():assert hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==digest,p
    payload={'method':'Current PNG/manifest; frozen contact skin ROI and authored limb corridors; local alpha exclusion cross-checked against whole-scene Pillow ownership. Static default desk state only. No runtime validation.','sourcesSHA256':SOURCES,'queues':queues,'records':records,'roiPixelCoordinates':roi_evidence,'armPaths':ARM_PATHS,'heroCropBoxes':boxes,'sourcePartCategories':source_part_detail,'oldSnapshotHandDrift':drift,'checks':{'records':len(records),'baselineFrames':48,'sourceHashesUnchanged':True,'ownershipAllRegionsAllScenarios':True,'splitExactRGBAUnion':True}}
    (OUT/'measurements.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    lines=['# 손·전완/소매 측정','', '최신 원본, default 상태, 3캐릭터 × worker 4방향 × 4phase. 손은 접점 반경2 피부 ROI; 전완/소매는 제작 관절 경로의 corridor ROI. 전체 해부학 마스크가 아니다. 두 ROI의 분모는 시나리오 간 보존된다.','', '## baseline: 개별 phase','', '| 캐릭터 | desk / worker | phase | 손 피부 | 전완/소매 | 머리 | 피부까지 거리 |','|---|---|---:|---:|---:|---:|---|']
    for r in records:
        if r['scenario']=='baseline':lines.append(f"| {r['character']} | {r['desk'].upper()} / {r['worker'].upper()} | {r['phase']} | {ratio(r['hands'])} | {ratio(r['forearmSleeve'])} | {ratio(r['head'])} | {r['handDistancesToFrozenH']} |")
    lines+=['','## 4phase 합산','', '| 캐릭터 | worker | 손 피부 | 전완/소매 |','|---|---|---:|---:|']
    aggregate=[]
    for folder in PILOTS:
        for facing in FACINGS:
            rr=[r for r in records if r['scenario']=='baseline' and r['character']==folder and r['worker']==facing];values={k:{t:sum(r[k][t] for r in rr) for t in ('visible','total')} for k in ('hands','forearmSleeve')};aggregate.append({'character':folder,'worker':facing,**values});lines.append(f"| {folder} | {facing.upper()} | {ratio(values['hands'])} | {ratio(values['forearmSleeve'])} |")
    lines+=['','## SE/SW 제안 효과: 24phase 합산 / per-frame 최저–최고','', '| 안 | 손 피부 | 손 % 범위 | 전완/소매 | 전완 % 범위 | 원래 H 거리 범위 |','|---|---:|---:|---:|---:|---:|']
    summary=[]
    for scenario in SCENARIOS:
        rr=[r for r in records if r['scenario']==scenario and r['worker'] in ('se','sw')];values={k:{t:sum(r[k][t] for r in rr) for t in ('visible','total')} for k in ('hands','forearmSleeve')};ranges={k:[100*min(r[k]['fraction'] for r in rr),100*max(r[k]['fraction'] for r in rr)] for k in ('hands','forearmSleeve')};dist=[min(min(r['handDistancesToFrozenH']) for r in rr),max(max(r['handDistancesToFrozenH']) for r in rr)];summary.append({'scenario':scenario,**values,'rangesPct':ranges,'frozenHDistanceRange':dist});lines.append(f"| {scenario} | {ratio(values['hands'])} | {ranges['hands'][0]:.1f}–{ranges['hands'][1]:.1f} | {ratio(values['forearmSleeve'])} | {ranges['forearmSleeve'][0]:.1f}–{ranges['forearmSleeve'][1]:.1f} | {dist[0]}–{dist[1]} |")
    lines+=['','## baseline 가림 기여: 4phase 합산, topmost라 중복 없음','', '| 캐릭터 / worker | ROI | category | partId |','|---|---|---|---|']
    for folder in PILOTS:
        for facing in FACINGS:
            rr=[r for r in records if r['scenario']=='baseline' and r['character']==folder and r['worker']==facing]
            for key in ('hands','forearmSleeve'):
                cats={};pp={}
                for r in rr:
                    for k,n in r[key]['topmostCategories'].items():cats[k]=cats.get(k,0)+n
                    for k,n in r[key]['topmostParts'].items():pp[k]=pp.get(k,0)+n
                lines.append(f"| {folder} / {facing.upper()} | {key} | {cats or '없음'} | {pp or '없음'} |")
    (OUT/'measurements.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    (OUT/'summary.json').write_text(json.dumps({'aggregate':aggregate,'proposals':summary},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'checks':payload['checks'],'baseline':aggregate,'proposals':summary},ensure_ascii=False,indent=2))


if __name__=='__main__':
    main()
