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
SCENARIOS = ('baseline', 'head-up1', 'chair-down4', 'chair-down8',
             'chair-rear', 'desk-seat-rear', 'desk-seat-rear+chair-down4',
             'desk-seat-rear+chair-down8', 'head-over-chair',
             'head-up4', 'head-up8', 'desk-surface-rear',
             'desk-surface-rear+chair-down4', 'desk-surface-rear+chair-down5',
             'desk-surface-rear+chair-down6', 'desk-surface-rear+chair-down7',
             'desk-surface-rear+chair-down8')


def source(path):
    if path.is_relative_to(ART / 'characters'):
        relative=path.relative_to(ART/'characters')
        path=OUT/'rev1-source/actor-inputs'/relative
        path=path.with_name(path.name.replace('.iso.json','.manifest.json'))
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
        items.append({'key': (depth, p['isoZOrder'], owner, p['partId']),
                      'id': p['partId'], 'image': cell(s, atlas, c),
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


def regions(sprite, head, clip, skin_hex):
    a = np.array(sprite)
    opaque = a[:, :, 3] > 0
    hm = np.array(head)[:, :, 3] > 0
    assert not np.any(hm & ~opaque), 'canonical head must exist in serialized actor'
    yy, xx = np.indices(opaque.shape)
    skin = np.zeros_like(opaque)
    for color in skin_hex:
        skin |= np.all(a[:, :, :3] == tuple(bytes.fromhex(color)), axis=2)
    hands = np.zeros_like(opaque)
    distances = []
    for h in clip['handAnchors']:
        radius = np.maximum(abs(xx - h['x']), abs(yy - h['y']))
        hands |= (radius <= 2) & skin & opaque & ~hm
        distances.append(int(radius[skin & opaque & ~hm].min()))
    assert max(distances) <= 1, distances
    torso = opaque & ~hm & ~hands & (11 <= xx) & (xx <= 22) & (yy < clip['seatAnchor']['y'])
    return {'head': hm, 'torso': torso, 'hands': hands}, distances


def local_alpha(item, actor_pos):
    local = Image.new('RGBA', (32, 64))
    local.alpha_composite(item['image'], (item['pos'][0] - actor_pos[0], item['pos'][1] - actor_pos[1]))
    return np.array(local)[:, :, 3] > 0


def split_surface(item):
    """Fixed source geometry proxy, chosen on the empty desk, never on actor masks."""
    a = np.array(item['image']); yy, xx = np.indices(a.shape[:2])
    if '__se__' in item['id']:
        top = yy < np.minimum(.5 * xx + 53, 111 - .5 * xx)
        keyboard = (80 <= xx) & (xx <= 103) & (55 <= yy) & (yy <= 70)
    else:
        top = yy < np.minimum(.5 * xx + 46, 127 - .5 * xx)
        keyboard = (48 <= xx) & (xx <= 82) & (73 <= yy) & (yy <= 85)
    select = (top | keyboard) & (a[:, :, 3] > 0)
    surface = a.copy(); surface[~select] = 0
    fascia = a.copy(); fascia[select] = 0
    assert not np.any((surface[:, :, 3] > 0) & (fascia[:, :, 3] > 0))
    assert np.array_equal(surface + fascia, a), 'split must reconstruct original bytes'
    return Image.fromarray(surface), Image.fromarray(fascia)


def simulate(items, sprite, masks, actor_pos, actor_key, scenario, back):
    changed = [dict(i) for i in items]
    if back:
        if 'desk-surface-rear' in scenario:
            for i in list(changed):
                if i['family'] == 'desk' and i['seat']:
                    surface, fascia = split_surface(i)
                    i['image'] = fascia
                    changed.append(dict(i, image=surface, id=i['id'] + ':surface',
                                        key=(actor_key[0], -20, 'desk', i['id'] + ':surface')))
        for i in changed:
            chair_front = i['id'].startswith('chair__front__')
            if chair_front and 'chair-down' in scenario:
                n = int(scenario.split('chair-down')[1])
                # Geometric surrogate: shorten upper backrest, preserve all y>=68 seat/base pixels.
                a = np.array(i['image']); upper = a.copy(); upper[68:] = 0
                a[:68] = 0
                moved = np.array(shift(Image.fromarray(upper), dy=n)); moved[68:] = 0
                im = Image.fromarray(a); im.alpha_composite(Image.fromarray(moved)); i['image'] = im
            if (chair_front and scenario == 'chair-rear') or (
                i['family'] == 'desk' and i['seat'] and 'desk-seat-rear' in scenario
            ):
                i['key'] = (actor_key[0], -20, i['key'][2], i['key'][3])
        if scenario.startswith('head-up'):
            n = int(scenario[7:])
            a = np.array(sprite); head = a.copy(); head[~masks['head']] = 0
            a[masks['head']] = 0
            sprite = Image.fromarray(a); sprite.alpha_composite(shift(Image.fromarray(head), dy=-n))
            moved_mask = np.zeros_like(masks['head']); moved_mask[:-n] = masks['head'][n:]
            masks = dict(masks, head=moved_mask)
    covered = np.zeros((64, 32), bool)
    owner = np.full((64, 32), '', dtype=object)
    overlaps = []
    for i in sorted(changed, key=lambda i: i['key']):
        if i['key'] <= actor_key:
            continue
        alpha = local_alpha(i, actor_pos)
        if back and scenario == 'head-over-chair' and i['family'] == 'chair':
            alpha = alpha & ~masks['head']  # diagnostic layer bypass; desk occlusion remains
        owner[alpha] = i['id']
        covered |= alpha
        if np.any(alpha & masks['head']):
            overlaps.append({'partId': i['id'], 'headOverlap': int((alpha & masks['head']).sum())})
    counts = {name: {'visible': int((mask & ~covered).sum()), 'total': int(mask.sum())}
              for name, mask in masks.items()}
    counts['headTopmostOccluders'] = {str(p): int(((owner == p) & masks['head']).sum())
                                    for p in np.unique(owner[masks['head'] & covered])}
    counts['headOverlaps'] = overlaps  # not additive: independently overlapping parts
    chair_cover = np.zeros((64, 32), bool)
    for i in changed:
        if i['key'] > actor_key and i['family'] == 'chair':
            chair_cover |= local_alpha(i, actor_pos)
    counts['chairOnlyHeadVisible'] = int((masks['head'] & ~chair_cover).sum())
    scene = Image.new('RGBA', (192, 144), '#E7D1AC')
    for i in sorted(changed + [{'key': actor_key, 'image': sprite, 'pos': actor_pos}], key=lambda i: i['key']):
        scene.alpha_composite(i['image'], i['pos'])
    if back and scenario == 'head-over-chair':
        # Redraw only head pixels covered by chair but not by a later desk part.
        lift = np.array(sprite); chair_mask = np.zeros((64, 32), bool); desk_mask = chair_mask.copy()
        for i in changed:
            if i['key'] > actor_key:
                if i['family'] == 'chair': chair_mask |= local_alpha(i, actor_pos)
                else: desk_mask |= local_alpha(i, actor_pos)
        lift[~(masks['head'] & chair_mask & ~desk_mask)] = 0
        scene.alpha_composite(Image.fromarray(lift), actor_pos)
    return counts, scene


def save_pair(name, image):
    image.save(OUT / (name + '-1x.png'))
    image.resize((image.width * 3, image.height * 3), Image.Resampling.NEAREST).save(OUT / (name + '-3x.png'))


def ratio(m):
    return f"{m['visible']}/{m['total']} ({100*m['visible']/m['total']:.1f}%)" if m['total'] else '0/0 (N/A)'


def before_furniture(name):
    base=OUT/'rev1-source'
    doc=load_json(base/(name+'.json'))
    sheets={s['id']:(s,load_image(base/s['file'])) for s in doc['sheets']}
    return doc['furniture'][0],sheets


def main():
    # Preserve the review's canonical head and fixed torso/skin-contact ROIs.
    # Evaluate serialized real parts; no simulation, head lift, or shifted chair.
    records=[];panels={};queues=[];reference_drift=[]
    before_reference=load_json(ART/'design-review/measurements.json')['records']
    for stage,reader in (('before',before_furniture),('after',furniture)):
        desk,ds=reader('desk');chair,cs=reader('chair')
        for folder,(name,run,skin) in PILOTS.items():
            base=ART/'characters'/folder
            doc=load_json(base/(name+'.iso.json'))
            sheet=next(s for s in doc['sheets'] if s['kind']=='character')
            atlas=load_image(base/sheet['file'])
            for orientation in FACINGS:
                v=variant(desk,orientation);profile=next(p for p in v['seatProfiles'] if p['id']=='front')
                facing=profile['isoFacing'];ground=profile['seatGround'];sd=ground['i']+ground['j']
                origin=(96,88);g=project(ground['i'],ground['j'])
                assert point(v['linkedChair']['chairOffset'])==g
                cv=variant(chair,v['linkedChair']['orientation'])
                clip=next(c for c in sheet['rows'] if c['name']=='seated-typing-front' and c['direction']==facing)
                assert clip['deskOrientation']==orientation
                c=point(profile['contactOffset']);sa=point(clip['seatAnchor'])
                assert c==(g[0],g[1]-12) and sa==(16,44)
                pos=(origin[0]+c[0]-sa[0],origin[1]+c[1]-sa[1])
                assert [(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in profile['handContacts']]
                akey=(sd,0,'actor',folder)
                items=parts(v,ds,origin,0,sd,'desk')+parts(cv,cs,(origin[0]+g[0],origin[1]+g[1]),sd,sd,'chair')
                queues.append(dict(stage=stage,character=folder,desk=orientation,worker=facing,actorDestination=pos,actorKey=akey,
                    items=[dict(partId=i['id'],key=i['key'],destination=i['pos']) for i in sorted(items,key=lambda i:i['key'])]))
                head=canonical_head(folder,run,facing)
                for phase in range(4):
                    sprite=cell(sheet,atlas,clip,phase)
                    offset=point(sheet['headOffsetsByClip'][clip['name']+'/'+facing][phase])
                    masks,distances=regions(sprite,shift(head,*offset),clip,skin)
                    counts,scene=simulate(items,sprite,masks,pos,akey,'baseline',facing in ('ne','nw'))
                    # Independent full-scene layer-ownership raster verifies all three ROIs.
                    labels=Image.new('L',(192,144));owners=np.full((64,32),'',dtype=object)
                    for i in sorted(items+[dict(key=akey,image=sprite,pos=pos)],key=lambda i:i['key']):
                        labels.paste(1 if i['key']==akey else 2,i['pos']+(i['pos'][0]+i['image'].width,i['pos'][1]+i['image'].height),i['image'].getchannel('A'))
                        if i['key']>akey:owners[local_alpha(i,pos)]=i['id']
                    owned=np.array(labels.crop((pos[0],pos[1],pos[0]+32,pos[1]+64)))==1
                    for region,mask in masks.items():
                        assert int((owned&mask).sum())==counts[region]['visible'],(stage,folder,facing,phase,region)
                        counts[region+'TopmostOccluders']={str(p):int(((owners==p)&mask).sum()) for p in np.unique(owners[mask&~owned])}
                        assert sum(counts[region+'TopmostOccluders'].values())==counts[region]['total']-counts[region]['visible']
                    if stage=='before':
                        old=next(r for r in before_reference if r['character']==folder and r['desk']==orientation and r['phase']==phase and r['scenario']=='baseline')
                        for region in masks:
                            if counts[region]!=old[region]:reference_drift.append(dict(character=folder,worker=facing,phase=phase,region=region,review=old[region],snapshot=counts[region]))
                    record=dict(stage=stage,character=folder,desk=orientation,worker=facing,phase=phase,handDistances=distances,**counts)
                    records.append(record);panels[stage,folder,orientation,phase]=scene
    for row in records:
        if row['stage']=='after':
            old=next(r for r in records if r['stage']=='before' and all(r[k]==row[k] for k in ('character','desk','phase')))
            for region in ('head','torso','hands'):assert row[region]['total']==old[region]['total']
    # All four views x all four phases, before/after adjacent.
    board=Image.new('RGBA',(1536,1728),'#E7D1AC')
    for ci,folder in enumerate(PILOTS):
        for di,orientation in enumerate(FACINGS):
            for phase in range(4):
                for si,stage in enumerate(('before','after')):
                    im=panels[stage,folder,orientation,phase].copy()
                    ImageDraw.Draw(im).text((2,2),f'{folder} {orientation} p{phase} {stage}',fill='#191E1E')
                    board.alpha_composite(im,((di*2+si)*192,(ci*4+phase)*144))
    save_pair('contact-before-after-A3-rev1',board)
    hero=load_image(ROOT/'assets/art/iso-concept/iso-office-hero.png')
    boxes=((680,345,840,505),(995,345,1155,505),(830,345,990,505))
    for ci,folder in enumerate(PILOTS):
        # Unscaled 1x sprite inset + exact 3x beside the original hero crop.
        board=Image.new('RGBA',(1504,672),'#E7D1AC')
        d=ImageDraw.Draw(board)
        for di,orientation in enumerate(FACINGS):
            y=di*168
            for si,stage in enumerate(('before','after')):
                im=panels[stage,folder,orientation,0].crop((12,24,180,136))
                x=si*672
                board.alpha_composite(im,(x,y+25))
                board.alpha_composite(im.resize((504,336),Image.Resampling.NEAREST).crop((0,0,504,140)),(x+168,y+25))
                # Full 3x contact fits its own larger board below; here crop owns upper contact.
                d.text((x+2,y+2),folder+' '+orientation+' '+stage+' 1x / 3x',fill='#191E1E')
            board.alpha_composite(hero.crop(boxes[ci]),(1344,y))
        board.save(OUT/(f'quality-{folder}-A3-rev1.png'))
        # Full 3x, no cropping/resampling in the inspection board.
        full=Image.new('RGBA',(1536,1440),'#E7D1AC');fd=ImageDraw.Draw(full)
        for di,orientation in enumerate(FACINGS):
            y=di*360
            for si,stage in enumerate(('before','after')):
                im=panels[stage,folder,orientation,0].crop((12,24,180,136))
                full.alpha_composite(im,(si*688,y+24))
                full.alpha_composite(im.resize((504,336),Image.Resampling.NEAREST),(si*688+176,y+24))
                fd.text((si*688+2,y+2),stage+' '+orientation+' 1x / 3x',fill='#191E1E')
            full.alpha_composite(hero.crop(boxes[ci]),(1376,y+25))
        full.save(OUT/(f'view-{folder}-A3-rev1.png'))
    final_board=Image.new('RGBA',(2912,1080),'#E7D1AC');fd=ImageDraw.Draw(final_board)
    for ci,folder in enumerate(PILOTS):
        y=ci*360
        for di,orientation in enumerate(FACINGS):
            x=di*688;im=panels['after',folder,orientation,0].crop((12,24,180,136))
            final_board.alpha_composite(im,(x,y+24))
            final_board.alpha_composite(im.resize((504,336),Image.Resampling.NEAREST),(x+176,y+24))
            fd.text((x+2,y+2),folder+' desk '+orientation+' 1x / 3x',fill='#191E1E')
        final_board.alpha_composite(hero.crop(boxes[ci]),(2752,y+25))
    final_board.save(OUT/'final-quality-A3-rev1.png')
    payload=dict(definition='Unchanged design-review canonical head + clip offset; torso actor alpha non-head/non-hand 11<=x<=22,y<44; hands exact skin within Chebyshev radius2 of anchors. Before/after use real serialized parts and identical frozen actor snapshot, no simulation.',sourceHashes=SOURCES,referenceDrift=reference_drift,queues=queues,records=records)
    (OUT/'measurements-A3-rev1.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    lines=['# A3 rev1 실제 파츠 가림 측정','','4 typing phase 합산. 고정 canonical head 및 원래 torso/skin-contact ROI를 사용하며 분모를 보존했다. ROI는 수동 분리한 전체 해부학 영역이 아니다.','','| 캐릭터 | desk / worker | 수정 전 머리 | 수정 후 머리 | 수정 후 몸통 ROI | 수정 후 손 ROI |','|---|---|---:|---:|---:|---:|']
    for folder in PILOTS:
        for orientation in FACINGS:
            before=[r for r in records if r['stage']=='before' and r['character']==folder and r['desk']==orientation]
            after=[r for r in records if r['stage']=='after' and r['character']==folder and r['desk']==orientation]
            values=[{k:sum(r[name][k] for r in rows) for k in ('visible','total')} for rows,name in ((before,'head'),(after,'head'),(after,'torso'),(after,'hands'))]
            lines.append(f'| {folder} | {orientation.upper()} / {after[0]["worker"].upper()} | '+' | '.join(map(ratio,values))+' |')
    lines+=['','## 수정 후 손 ROI topmost 가림 원인 (phase 0)','','| 캐릭터 / worker | part: 가림 픽셀 |','|---|---|']
    for r in records:
        if r['stage']=='after' and r['phase']==0:
            lines.append(f'| {r["character"]} / {r["worker"].upper()} | '+(', '.join(f'{p}: {n}' for p,n in r['handsTopmostOccluders'].items()) or '없음')+' |')
    (OUT/'measurements-A3-rev1.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    for p,digest in SOURCES.items():assert hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==digest,p
    assert len(records)==96
    if reference_drift:lines+=['','기존 리뷰 이후 캐릭터 원본이 변경됨: '+str(len(reference_drift))+'개 ROI/phase 기록 차이. 전후 비교에는 같은 고정 캐릭터 snapshot을 사용했다. 상세: JSON referenceDrift.']
    (OUT/'measurements-A3-rev1.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print('\n'.join(lines));print('PASS: 96 real-part records, independent ownership all ROIs, fixed before/after actor inputs and denominators, exact C/H and skin <=1px, unchanged snapshot/input hashes')


if __name__=='__main__':
    main()

