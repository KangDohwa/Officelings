"""Deterministic, in-place selective outline pass; Pillow + numpy, Python -B.

Usage (from repository root, with a Python containing Pillow/numpy):
  python -B assets/art/iso/outline-pass/outline_pass.py CHARACTER_FOLDER --apply
  python -B assets/art/iso/outline-pass/outline_pass.py --evidence
  python -B assets/art/iso/outline-pass/outline_pass.py --walking-evidence

Without --apply the new normal/blink PNGs are previews beside this script.
All snapshots/evidence go here; only --apply writes the input folder. No pack,
furniture, source, tool, test, or git mutation. Repeating the same applied pass
is a no-op, checked against output hashes (no accumulated darkening).

PARAMETERS: 4-connected exterior boundary, replacement only (expansion=0).
The ONE shared exterior color #111924 must exist in iso_check.RAMPS; no other
color is added. Protect the complete canonical head RGBA at manifest offsets,
all eye-mask files, skin clusters plus a 1px collar, and 5x5 typing contact
neighborhoods. An interior line requires eight opaque neighbors and no adjacent
exterior outline; shadow only when source luminance is >= shadow + 18. Arm
overlap follows the approved arm paths, offset down 1px; hem follows the body
bob. Existing low-tone details are retained. The upper/left inner boundary can
receive an identity-palette rim if luminance <65, never a new exterior pixel.
No alpha pixel, geometry, head, hand, manifest, or ramp membership changes.

For a later character pass --profile PATH.json. It uses the same fields as a
PROFILES entry below: headFiles (four relative PNGs), headCrop (four optional
exclusive y ends; null means full head layer), skin (hex RGB without #), shadow,
rim, hem, seatedHem, armPaths {front/back: two polylines}. Author these from that
character's approved head/skin/arm data; do not infer a face mask from brightness.
Unknown characters require this explicit profile. Outputs record the full
parameter set. Full-head protection intentionally leaves hair/face contours
unchanged, even where a background has weak contrast.

--evidence uses the three saved pilot snapshots and READS the existing v3
workstation measurement functions. It uses live v3 desk AND chair serialized
parts, fixed ROIs, 48 typing cases, 1,536 walker cases, and actual integrated
native pixels. Integrated replacements are limited to pixels owned by the actor
in the serialized queue AND matching the original sprite texel. No runtime
repack/render claim; the original renderer PNG is never modified.
"""
from pathlib import Path
import argparse
import ast
import hashlib
import importlib.util
import json
import os
import subprocess
import sys

sys.dont_write_bytecode = True
import numpy as np
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[3]
ART = ROOT / 'assets/art/iso'
FACINGS = ('se', 'sw', 'ne', 'nw')
SCENE = Path('C:/Codex-Workspace/vd-lanes/packv3/artifacts/iso-packv3-final/iso-twelve-working-2x.png')
PROFILES = {
    'dev-hoodie-v2': dict(
        headFiles=['dev-hoodie-iso.png']*4, headCrop=[41,41,39,39],
        skin=['6D3713','8B4D15','AC5F2D','BA672C','C97E32'],
        shadow='#583F0C', rim='#4A4D5A', hem=50, seatedHem=45,
        armPaths=dict(front=[[(12,37),(17,41),(24,43)],[(21,37),(24,41),(28,42)]],
                      back=[[(12,36),(19,37),(24,34)],[(21,36),(25,36),(28,33)]])),
    'punk-girl': dict(
        headFiles=[f'runs/{f}-idle-native.png' for f in FACINGS], headCrop=[38]*4,
        skin=['E36854','F19B5B','FBC186','F7C4B7'],
        shadow='#322015', rim='#534B4C', hem=49, seatedHem=45,
        armPaths=dict(front=[[(13,38),(20,41),(23,42),(26,43)],[(21,38),(25,39),(27,41),(29,42)]],
                      back=[[(14,38),(22,37),(25,35),(27,33)],[(21,38),(26,37),(28,35),(29,33)]])),
    'professor': dict(
        headFiles=[f'runs/v1/head-{f}.png' for f in FACINGS], headCrop=[None]*4,
        skin=['744B31','C49F7C','EFA763','F3B06B','F6AD62'],
        shadow='#32261E', rim='#744B31', hem=50, seatedHem=45,
        armPaths=dict(front=[[(11,36),(17,41),(26,43)],[(21,35),(25,40),(29,43)]],
                      back=[[(11,35),(17,38),(26,33)],[(21,34),(25,37),(29,33)]])),
}
PARAMETERS = dict(exterior='#111924', connectivity=4, expansion=0,
                  skinCollar=1, contactRadius=2, shadowDelta=18, rimBelow=65,
                  armLineOffsetY=1, interiorOpaqueNeighbors=8)


def read(p):
    return json.loads(p.read_text(encoding='utf-8'))


def write_json(p, data):
    p.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def rgba(p):
    with Image.open(p) as im:
        return np.array(im.convert('RGBA'))


def rgb(h):
    return tuple(bytes.fromhex(h.lstrip('#')))


def lum(a):
    return np.dot(a[..., :3], [.2126,.7152,.0722])


def cell(a, row, phase):
    return a[row*64:(row+1)*64,phase*32:(phase+1)*32]


def shifted(mask, dx, dy):
    im = Image.fromarray(mask.astype(np.uint8)*255)
    out = Image.new('L', (32,64))
    out.paste(im, (dx,dy))
    return np.array(out)>0


def neighbors(mask, diagonal=False):
    ds = [(-1,0),(1,0),(0,-1),(0,1)]
    if diagonal:
        ds += [(-1,-1),(-1,1),(1,-1),(1,1)]
    return [shifted(mask,dx,dy) for dx,dy in ds]


def dilate(mask):
    return np.logical_or.reduce([mask]+neighbors(mask,True))


def load_module(name, p):
    spec = importlib.util.spec_from_file_location(name,p)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def heads(folder, normal, profile):
    result = {}
    for n,f in enumerate(FACINGS):
        p = folder/profile['headFiles'][n]
        a = rgba(p)
        if p.name == normal['file']:
            a = cell(a,n,0).copy()
        assert a.shape == (64,32,4), ('head size',p)
        end = profile['headCrop'][n]
        if end is not None:
            a[end:] = 0
        result[f] = a[:,:,3]>0
    return result


def locked(src, head, clip, profile):
    opaque = src[:,:,3]>0
    skin = np.zeros((64,32),bool)
    yy,xx = np.indices(skin.shape)
    for h in profile['skin']:
        skin |= np.all(src[:,:,:3]==rgb(h),axis=2)
    # Skin-color reuse in trousers must not misclassify an entire leg as a hand.
    skin &= opaque & ~head & (yy<52)
    protected = head | dilate(skin)
    hands = skin.copy()
    for h in clip.get('handAnchors',[]):
        contact = np.maximum(abs(xx-h['x']),abs(yy-h['y']))<=2
        protected |= contact
        hands |= contact & opaque & ~head
    return protected,hands


def edit_cell(src, head, clip, phase, profile):
    dst = src.copy()
    opaque = src[:,:,3]>0
    protected,hands = locked(src,head,clip,profile)
    edge = opaque & ~np.logical_and.reduce(neighbors(opaque))
    editable = edge & ~protected
    # Recolor existing contour texels, including low-contrast gaps. No dilation.
    dst[editable,:3] = rgb(PARAMETERS['exterior'])
    inner = opaque & ~edge & ~protected
    # Upper/left inner rim: one texel deep, only on sufficiently broad forms.
    rim = inner & (shifted(editable,1,0)|shifted(editable,0,1))
    rim &= (lum(src)<PARAMETERS['rimBelow'])
    rim &= np.logical_and.reduce(neighbors(opaque,True))
    dst[rim,:3] = rgb(profile['rim'])

    stroke = Image.new('L',(32,64))
    draw = ImageDraw.Draw(stroke)
    if clip['name']=='seated-typing-front':
        paths=profile['armPaths']['back' if clip['direction'] in ('ne','nw') else 'front']
        for path in paths:
            points=[(32-x if clip['direction'] in ('sw','nw') else x,y+1) for x,y in path]
            draw.line(points,fill=255,width=1)
    seated = clip['name'] in ('seated-typing-front','seated-thinking','sleep')
    hy = profile['seatedHem'] if seated else profile['hem']
    if clip['name']=='walk':
        hy += [0,1,0,-1][phase]
    elif clip['name'] in ('sit-down','stand-up'):
        hy = round(profile['hem'] + (profile['seatedHem']-profile['hem']) *
                   ((phase if clip['name']=='sit-down' else 3-phase)/3))
    draw.line([(11,hy),(22,hy)],fill=255,width=1)
    contour = (np.array(stroke)>0) & inner & ~rim
    contour &= np.logical_and.reduce(neighbors(opaque,True))
    contour &= ~dilate(editable)
    contour &= lum(src)>=lum(np.array(rgb(profile['shadow'])))+PARAMETERS['shadowDelta']
    dst[contour,:3] = rgb(profile['shadow'])
    assert np.array_equal(src[:,:,3],dst[:,:,3])
    assert np.array_equal(src[protected],dst[protected])
    return dst, dict(edge=editable,rim=rim,contour=contour,head=head,hands=hands,
                     protected=protected,fullEdge=edge)


def review(folder, a, normal):
    # Consistent native/2x/3x review; original review images are in the snapshot.
    bg='#E7D1AC'
    # Contact cells are cropped to their actual body to fit the review band.
    contact=Image.new('RGBA',(896,4*152),bg)
    d=ImageDraw.Draw(contact)
    for di,f in enumerate(FACINGS):
        for j,(row,phase) in enumerate([(di,0)]+[(4+di,p) for p in range(4)]):
            im=Image.fromarray(cell(a,row,phase)).crop((0,17,32,61))
            x,y=j*176,di*152
            d.text((x+3,y+1),f+' '+('idle' if j==0 else 'walk '+str(phase)),fill='#191E1E')
            for scale,dx in [(1,0),(2,34),(3,100)]:
                contact.alpha_composite(im.resize((32*scale,44*scale),Image.Resampling.NEAREST),(x+dx,y+16))
    contact.save(folder/'iso-contact.png')
    board=Image.new('RGBA',(4*448,10*220),bg)
    d=ImageDraw.Draw(board)
    for clip in normal['rows']:
        row=clip['row'];bx=(row%4)*448;by=(row//4)*220
        d.text((bx+3,by+2),f"{row:02} {clip['name']} {clip['direction']} / 1x + 3x",fill='#191E1E')
        for phase in range(4):
            im=Image.fromarray(cell(a,row,phase))
            board.alpha_composite(im,(bx+phase*112,by+22))
            board.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(bx+phase*112+16,by+22))
    board.save(folder/'iso-rows-review.png')


def apply(folder, profile, commit):
    profile=json.loads(json.dumps(profile))  # canonical lists, including path tuples
    folder=folder.resolve()
    cid='dev-hoodie' if folder.name=='dev-hoodie-v2' else folder.name
    manifest=folder/(cid+'.iso.json')
    doc=read(manifest)
    normal=next(s for s in doc['sheets'] if s['kind']=='character')
    blink=next(s for s in doc['sheets'] if s['id']==normal['blink']['sheetId'])
    assert (normal['cellW'],normal['cellH'],len(normal['rows']))==(32,64,38)
    assert len(blink['rows'])==24
    out=OUT/'evidence'/folder.name
    before=out/'before'
    out.mkdir(parents=True,exist_ok=True)
    prior=out/'proof.json'
    if prior.exists() and read(prior).get('applied'):
        proof=read(prior)
        assert proof['profile']==profile and proof['parameters']==PARAMETERS, 'parameter mismatch'
        assert all(sha((folder/k).read_bytes())==v for k,v in proof['productionAfterSHA256'].items()), 'input drift after pass'
        print(folder.name+': same parameters/output hashes; already applied, no-op')
        return
    source_paths=[manifest,folder/(cid+'-iso.manifest.json'),folder/normal['file'],folder/blink['file']]
    source_paths += [folder/v['eyeMask'] for v in normal['faceLock'].values()]
    source_paths += [folder/'iso-contact.png',folder/'iso-rows-review.png']
    before.mkdir(exist_ok=True)
    protected_hashes={p.name:sha(p.read_bytes()) for p in source_paths}
    for p in source_paths:
        target=before/p.name
        if target.exists():
            assert sha(target.read_bytes())==protected_hashes[p.name], 'saved baseline differs'
        else:
            target.write_bytes(p.read_bytes())
    old=rgba(folder/normal['file']);oldblink=rgba(folder/blink['file'])
    new=old.copy();head=heads(folder,normal,profile)
    masks={f:rgba(folder/v['eyeMask'])[:,:,3]>0 for f,v in normal['faceLock'].items()}
    checker=load_module('outline_palette_readonly',ROOT/'tools/iso_check.py')
    ramp={rgb(h) for ramp in checker.RAMPS.values() for h in ramp.split()}
    assert rgb(PARAMETERS['exterior']) in ramp
    identity={rgb(h) for h in normal['identityPalette']}
    assert rgb(profile['shadow']) in identity and rgb(profile['rim']) in identity
    records=[];headbytes=[];afterheadbytes=[];handbytes=[];afterhandbytes=[]
    for clip in normal['rows']:
        row=clip['row'];f=clip['direction']
        for phase,off in enumerate(normal['headOffsetsByClip'][clip['name']+'/'+f]):
            hm=shifted(head[f],off['x'],off['y'])
            src=cell(old,row,phase)
            assert not np.any(hm & (src[:,:,3]==0)), ('head not present',row,phase)
            dst,regions=edit_cell(src,hm,clip,phase,profile)
            cell(new,row,phase)[:]=dst
            assert not dst[:,0,3].any() and not dst[61:,:,3].any()
            headbytes.append(src[hm].tobytes());afterheadbytes.append(dst[hm].tobytes())
            handbytes.append(src[regions['hands']].tobytes());afterhandbytes.append(dst[regions['hands']].tobytes())
            edge=regions['edge'];full=regions['fullEdge'];yy,xx=np.where(full)
            def metrics(a):
                e=a[edge,:3];l=lum(e)
                # Native luminance contrast; an inner rim supplies the dark-bg cue.
                return dict(editableEdgeTexels=int(edge.sum()),boundaryTexels=int(full.sum()),
                            lockedBoundaryTexels=int((full & regions['protected']).sum()),
                            edgeLuminanceMean=round(float(l.mean()),3) if len(l) else None,
                            weakOnCream=int((abs(l-lum(np.array(rgb('#E7D1AC'))))<40).sum()),
                            weakOnWood=int((abs(l-lum(np.array(rgb('#C78251'))))<40).sum()),
                            weakOnDarkFloor=int((abs(l-lum(np.array(rgb('#262D38'))))<18).sum()),
                            edgeColorCount=len(np.unique(e,axis=0)))
            records.append(dict(row=row,phase=phase,action=clip['name'],facing=f,
                                exterior=int(edge.sum()),rim=int(regions['rim'].sum()),
                                interior=int(regions['contour'].sum()),
                                changed=int(np.any(src!=dst,axis=2).sum()),
                                before=metrics(src),after=metrics(dst)))
    result=oldblink.copy()
    for b,row in enumerate(normal['blink']['normalRows']):
        f=normal['rows'][row]['direction']
        for phase in range(4):
            eye=cell(masks[f],row,phase)
            src=cell(new,row,phase);dst=cell(result,b,phase)
            dst[:]=src;dst[eye]=cell(oldblink,b,phase)[eye]
            assert np.array_equal(dst[~eye],src[~eye])
            assert np.array_equal(dst[eye],cell(oldblink,b,phase)[eye])
    assert np.array_equal(old[:,:,3],new[:,:,3]) and np.array_equal(oldblink[:,:,3],result[:,:,3])
    assert sha(b''.join(headbytes))==sha(b''.join(afterheadbytes))
    assert sha(b''.join(handbytes))==sha(b''.join(afterhandbytes))
    allowed=identity|ramp
    assert {tuple(c) for c in np.unique(new[:,:,:3][new[:,:,3]>0],axis=0)}<=allowed
    assert all(sha(p.read_bytes())==protected_hashes[p.name] for p in source_paths), 'concurrent input change'
    Image.fromarray(new).save(out/'normal-after.png')
    Image.fromarray(result).save(out/'blink-after.png')
    if commit:
        # Build/check everything in memory before replacing any production pixel.
        Image.fromarray(new).save(folder/normal['file'])
        Image.fromarray(result).save(folder/blink['file'])
        review(folder,new,normal)
        # Refresh the folder's own existing resync proof using its stricter
        # approved-head checks. The generic equivalent above works for later
        # characters that do not yet have this helper.
        helper=folder/'blink_resync.py'
        if helper.exists():
            resync=subprocess.run([sys.executable,'-B',str(helper)],capture_output=True,
                                  encoding='utf-8',env={**os.environ,'PYTHONIOENCODING':'utf-8'})
            (out/'blink-resync.txt').write_text(resync.stdout+resync.stderr,encoding='utf-8')
            assert resync.returncode==0,resync.stdout+resync.stderr
            assert np.array_equal(rgba(folder/blink['file']),result),'existing blink logic disagrees'
    stable=[manifest,folder/(cid+'-iso.manifest.json')]+[folder/v['eyeMask'] for v in normal['faceLock'].values()]
    assert all(sha(p.read_bytes())==protected_hashes[p.name] for p in stable)
    proof=dict(character=folder.name,applied=commit,parameters=PARAMETERS,profile=profile,
               normalCells=152,blinkCells=96,all38RowsChanged=all(any(r['changed'] for r in records if r['row']==row) for row in range(38)),
               alphaByteIdentical=True,headRGBAByteIdentical=True,handRGBAByteIdentical=True,
               anchorsAndOffsetsAndPaletteByteIdentical=True,eyeMaskFilesByteIdentical=True,
               closedEyeTexelsByteIdentical=True,blinkNonEyeCellsEqualNewNormal=96,
               evidenceSHA256=dict(headBefore=sha(b''.join(headbytes)),headAfter=sha(b''.join(afterheadbytes)),
                                   handsBefore=sha(b''.join(handbytes)),handsAfter=sha(b''.join(afterhandbytes))),
               productionBeforeSHA256=protected_hashes,
               productionAfterSHA256={p.name:sha(p.read_bytes()) for p in source_paths},records=records)
    if commit:
        cmd=[sys.executable,'-B',str(ROOT/'tools/iso_check.py'),str(folder),'--stage','final','--tolerance','0']
        r=subprocess.run(cmd,capture_output=True,encoding='utf-8',env={**os.environ,'PYTHONIOENCODING':'utf-8'})
        (out/'iso-check-final.txt').write_text(r.stdout+r.stderr,encoding='utf-8')
        proof['checker']=dict(command=cmd,exitCode=r.returncode,passCount=r.stdout.count('| PASS |'),
                              skipCount=r.stdout.count('| SKIP |'),failCount=r.stdout.count('| FAIL |'))
        assert r.returncode==0,r.stdout+r.stderr
    write_json(prior,proof)
    print(folder.name+': '+str(sum(r['changed'] for r in records))+' normal texels; 152 heads/hands unchanged; '+str(proof.get('checker')))


def save_scales(name,board):
    for scale in (1,3):
        board.resize((board.width*scale,board.height*scale),Image.Resampling.NEAREST).save(OUT/(name+f'-{scale}x.png'))


def scene_ownership(items,sprite,pos,key,bg=None):
    size=(320,256)
    canvas=Image.new('RGBA',size,'#E7D1AC') if bg is None else bg.copy()
    cover=np.zeros((64,32),bool)
    for it in sorted(items+[dict(image=sprite,pos=pos,key=key,id='actor')],key=lambda i:i['key']):
        canvas.alpha_composite(it['image'],it['pos'])
        if it['id']!='actor' and it['key']>key:
            local=Image.new('L',(32,64))
            local.paste(it['image'].getchannel('A'),(it['pos'][0]-pos[0],it['pos'][1]-pos[1]))
            cover |= np.array(local)>0
    return canvas,~cover & (np.array(sprite)[:,:,3]>0)


def room_items(m,desk,ds,chair,cs):
    """Production world contacts; projection origin measured in supplied PNGs."""
    build_path=Path('C:/Codex-Workspace/vd-lanes/packv3/tools/build_iso_pack.py')
    tree=ast.parse(build_path.read_text(encoding='utf-8'))
    placements=next(ast.literal_eval(n.value) for n in tree.body if isinstance(n,ast.Assign)
                    and any(isinstance(t,ast.Name) and t.id=='PLACEMENTS' for t in n.targets))
    scene_origin=(360,104)
    items=[];seats={}
    for ident,family,i,j,f in placements:
        if family!='desk':continue
        gp=m.project(i,j);origin=(scene_origin[0]+gp[0],scene_origin[1]+gp[1])
        v=m.variant(desk,f);pr=v['seatProfiles'][0];wf=pr['isoFacing'];gd=pr['seatGround']
        ground=i+j;sd=ground+sum(gd.values());sg=m.project(gd['i'],gd['j'])
        co=(origin[0]+sg[0],origin[1]+sg[1])
        items += m.parts(v,ds,origin,ground,sd,ident)
        items += m.parts(m.variant(chair,wf),cs,co,sd,sd,ident+'-chair')
        c=m.point(pr['contactOffset']);pos=(origin[0]+c[0]-16,origin[1]+c[1]-44)
        seats[pos]=dict(id=ident,facing=wf,key=(sd,0,'actor','integrated'))
    return items,seats


def owned_in_room(items,src,pos,key):
    cover=np.zeros((64,32),bool)
    for it in items:
        if it['key']<=key:continue
        local=Image.new('L',(32,64))
        local.paste(it['image'].getchannel('A'),(it['pos'][0]-pos[0],it['pos'][1]-pos[1]))
        cover |= np.array(local)>0
    return ~cover & (src[:,:,3]>0)


def evidence():
    verifier=load_module('outline_v3_readonly',ART/'furniture-v3/workstations/verify-desk-rig-v3-lane.py')
    m=verifier.m
    desk,ds=verifier.furniture(ART/'furniture-v3/workstations','desk')
    chair,cs=verifier.furniture(ART/'furniture-v3/chair','chair')
    protected={p:sha(p.read_bytes()) for base in [ART/'furniture-v3/workstations',ART/'furniture-v3/chair']
               for p in base.glob('*') if p.is_file()}
    integrated=Image.open(SCENE).convert('RGBA')
    # UI text has a few non-nearest pixels; use the separately supplied native
    # render, and require exact 2x sprite blocks on every replacement below.
    native=Image.open(SCENE.with_name('iso-twelve-working-1x.png')).convert('RGBA')
    original2x=np.array(integrated);updated2x=original2x.copy()
    assert integrated.size==(native.width*2,native.height*2)
    actual=np.array(native);updated=actual.copy()
    # The production layout contains neighboring desks whose parts can also
    # cover a seated worker. Preserve that complete queue in scene composites.
    global_items,actual_seats=room_items(m,desk,ds,chair,cs)
    floorboard=Image.new('RGBA',(576,3*4*82),'#E7D1AC');fd=ImageDraw.Draw(floorboard)
    furnitureboard=Image.new('RGBA',(4*384,3*176),'#E7D1AC');fud=ImageDraw.Draw(furnitureboard)
    walkingboard=Image.new('RGBA',(4*384,3*176),'#E7D1AC');wd=ImageDraw.Draw(walkingboard)
    study=Image.new('RGBA',(3*384,4*176),'#E7D1AC');std=ImageDraw.Draw(study)
    typing=[];walking=[];matches=[]
    colors=['#E7D1AC','#C78251','#262D38','#111924']
    for ci,(folder,profile) in enumerate(PROFILES.items()):
        base=ART/'characters'/folder;eid=OUT/'evidence'/folder
        cid='dev-hoodie' if folder=='dev-hoodie-v2' else folder
        s=next(s for s in read(base/(cid+'.iso.json'))['sheets'] if s['kind']=='character')
        old=rgba(eid/'before'/s['file']);new=rgba(base/s['file']);hs=heads(base,s,profile)
        # Original contact/row sheets were visually reviewed separately; this
        # compact study preserves native pixels at every requested magnification.
        for di,f in enumerate(FACINGS):
            im=Image.fromarray(cell(old,di,0)).crop((0,17,32,61))
            bx,by=ci*384,di*176
            std.text((bx+2,by+2),folder+' '+f+' BEFORE 1x / 2x / 3x',fill='#191E1E')
            for scale,x in [(1,8),(2,60),(3,148)]:
                study.alpha_composite(im.resize((32*scale,44*scale),Image.Resampling.NEAREST),(bx+x,by+24))
        for bi,bg in enumerate(colors):
            y=(ci*4+bi)*82
            floorboard.paste(rgb(bg)+(255,),(0,y,576,y+82))
            fd.text((2,y+2),folder+' '+bg+' BEFORE / AFTER',fill='#F2EBD3' if bi>1 else '#111924')
            for di,f in enumerate(FACINGS):
                for rev,a in enumerate([old,new]):
                    floorboard.alpha_composite(Image.fromarray(cell(a,4+di,0)),(120+di*108+rev*42,y+16))
        for di,f in enumerate(FACINGS):
            v=m.variant(desk,f);pr=v['seatProfiles'][0];wf=pr['isoFacing'];gd=pr['seatGround'];sd=sum(gd.values())
            origin=(160,128);gp=m.project(gd['i'],gd['j']);co=(origin[0]+gp[0],origin[1]+gp[1])
            items=m.parts(v,ds,origin,0,sd,'desk')+m.parts(m.variant(chair,wf),cs,co,sd,sd,'chair')
            clip=next(c for c in s['rows'] if c['name']=='seated-typing-front' and c['direction']==wf)
            c=m.point(pr['contactOffset']);pos=(origin[0]+c[0]-16,origin[1]+c[1]-44);key=(sd,0,'actor',folder)
            assert [(pos[0]+h['x'],pos[1]+h['y']) for h in clip['handAnchors']]==[(origin[0]+h['x'],origin[1]+h['y']) for h in pr['handContacts']]
            for phase in range(4):
                sprites=[Image.fromarray(cell(a,clip['row'],phase)) for a in [old,new]]
                off=s['headOffsetsByClip'][clip['name']+'/'+wf][phase];hm=shifted(hs[wf],off['x'],off['y'])
                hmim=Image.new('RGBA',(32,64),'white')
                hmim.putalpha(Image.fromarray(hm.astype(np.uint8)*255))
                # Same skin ROIs and denominators as the existing measurement.
                regions,dist=m.regions(sprites[0],hmim,clip,profile['skin'],folder,wf)
                scenes=[];counts=[]
                for sprite in sprites:
                    sc,owned=scene_ownership(items,sprite,pos,key)
                    scenes.append(sc)
                    counts.append({n:dict(visible=int((roi&owned).sum()),total=int(roi.sum())) for n,roi in regions.items()})
                assert counts[0]==counts[1],('typing visibility regression',folder,f,phase)
                assert counts[1]['hands']['visible']==counts[1]['hands']['total']
                typing.append(dict(character=folder,desk=f,worker=wf,phase=phase,before=counts[0],after=counts[1],handDistances=dist,actorDestination=pos,actorKey=key))
                if phase==0:
                    for rev,sc in enumerate(scenes):
                        furnitureboard.alpha_composite(sc.crop((64,32,256,192)),(di*384+rev*192,ci*176+16))
                    fud.text((di*384+2,ci*176+2),folder+' desk '+f+' BEFORE / AFTER',fill='#111924')
                # Locate visible frozen head pixels in the actual integrated PNG.
                # Identity requires >=95% exposed-head source agreement, plus
                # exact changed texels after full-room desk/chair occlusion.
                src=np.array(sprites[0]);dst=np.array(sprites[1]);_,owned=scene_ownership(items,sprites[0],pos,key)
                candidates=[]
                # The full layout fixes the actual contact positions; this also
                # finds NE heads whose seed texel is hidden by a monitor.
                for (ax,ay),seat in actual_seats.items():
                    if seat['facing']!=wf:continue
                    if ay<0 or ax<0 or ay+64>actual.shape[0] or ax+32>actual.shape[1]:continue
                    patch=actual[ay:ay+64,ax:ax+32]
                    eq=np.all(patch[:,:,:3]==src[:,:,:3],axis=2)
                    hf=float(eq[hm].mean());bf=float(eq[owned].mean())
                    if hf>=.70 and bf>=.75:candidates.append((bf,int(ax),int(ay),eq))
                for bf,ax,ay,eq in candidates:
                    if any(q['character']==folder and q['position']==[ax,ay] for q in matches):continue
                    seat=actual_seats.get((ax,ay))
                    if seat is None or seat['facing']!=wf:continue
                    # Full room ownership, transported to the actor's local cell.
                    roomowned=owned_in_room(global_items,src,(ax,ay),seat['key'])
                    # Source agreement on the actually exposed, frozen head
                    # establishes identity even when neighbors cover >15%.
                    assert float(eq[hm&roomowned].mean())>=.95,('visible head source mismatch',folder,ax,ay)
                    changed=np.any(src!=dst,axis=2)&roomowned
                    if not np.all(eq[changed]):
                        # Another animation phase can have a similar head/body;
                        # accept only the phase with exact changed-texel matches.
                        continue
                    changed2x=np.repeat(np.repeat(changed,2,axis=0),2,axis=1)
                    src2x=np.repeat(np.repeat(src,2,axis=0),2,axis=1)
                    dst2x=np.repeat(np.repeat(dst,2,axis=0),2,axis=1)
                    patch2x=original2x[ay*2:ay*2+128,ax*2:ax*2+64]
                    assert np.array_equal(patch2x[changed2x],src2x[changed2x]), '2x ownership/source texels disagree'
                    updated2x[ay*2:ay*2+128,ax*2:ax*2+64][changed2x]=dst2x[changed2x]
                    updated[ay:ay+64,ax:ax+32][changed]=dst[changed]
                    matches.append(dict(character=folder,desk=f,facing=wf,phase=phase,position=[ax,ay],
                                        deskId=seat['id'],globalNeighborQueue=True,
                                        visibleSourceMatch=bf,changedTexels=int(changed.sum()),
                                        visibleHeadBefore=int((hm&roomowned).sum()),visibleHeadAfter=int((hm&roomowned).sum()),
                                        visibleHandsBefore=int((regions['hands']&roomowned).sum()),visibleHandsAfter=int((regions['hands']&roomowned).sum())))
            sitter=dict(key=key,id='seated',image=Image.fromarray(cell(new,clip['row'],0)),pos=pos)
            w,h=v['footprintW'],v['footprintH']
            probes=list(dict.fromkeys([(-1,0),(-1,h-1),(w,0),(w,h-1),(0,-1),(w-1,-1),(0,h),(w-1,h)]))+[(-1,-1),(w,h)]
            for pi,(i,j) in enumerate(probes):
                wg=m.project(i,j);wp=(origin[0]+wg[0]-16,origin[1]+wg[1]-60);wk=(i+j,0,'walker',folder)
                for facing in FACINGS:
                    wc=next(c for c in s['rows'] if c['name']=='walk' and c['direction']==facing)
                    for phase in range(4):
                        off=s['headOffsetsByClip']['walk/'+facing][phase];hm=shifted(hs[facing],off['x'],off['y'])
                        skinmask=locked(cell(old,wc['row'],phase),hm,wc,profile)[1]
                        scenes=[];counts=[]
                        for a in [old,new]:
                            sprite=Image.fromarray(cell(a,wc['row'],phase))
                            sc,owned=scene_ownership(items+[sitter],sprite,wp,wk)
                            counts.append(dict(head=int((hm&owned).sum()),hands=int((skinmask&owned).sum()),body=int(owned.sum())))
                            scenes.append(sc)
                        assert counts[0]==counts[1]
                        walking.append(dict(character=folder,desk=f,facing=facing,phase=phase,ground=[i,j],before=counts[0],after=counts[1]))
                        if pi==7 and facing==FACINGS[di] and phase==0:
                            for rev,sc in enumerate(scenes):
                                walkingboard.alpha_composite(sc.crop((64,32,256,192)),(di*384+rev*192,ci*176+16))
                            wd.text((di*384+2,ci*176+2),folder+' '+f+' WALK BEFORE / AFTER',fill='#111924')
    assert len(matches)==12,('expected all 12 actual seated pilots',len(matches),matches)
    save_scales('floor-before-after',floorboard)
    save_scales('v3-seated-before-after',furnitureboard)
    save_scales('v3-walking-before-after',walkingboard)
    study.save(OUT/'study-before-1x-2x-3x.png')
    for title,a in [('before',actual),('after',updated)]:
        im=Image.fromarray(a)
        for scale in (1,3):
            im.resize((im.width*scale,im.height*scale),Image.Resampling.NEAREST).save(OUT/(f'integrated-{title}-{scale}x.png'))
    integrated.save(OUT/'integrated-before-2x.png')
    Image.fromarray(updated2x).save(OUT/'integrated-after-2x.png')
    # Actual scene crops: seated replacements and native walking sprites over a
    # clear aisle in the same renderer background, with v3 serialized furniture.
    crops=Image.new('RGBA',(5*192,3*128),'#E7D1AC');cd=ImageDraw.Draw(crops)
    for ci,folder in enumerate(PROFILES):
        for di,q in enumerate([q for q in matches if q['character']==folder]):
            x,y=q['position'];box=(x-32,y-12,x+64,y+100)
            for rev,a in enumerate([actual,updated]):
                crops.alpha_composite(Image.fromarray(a).crop(box),(di*192+rev*96,ci*128+16))
            cd.text((di*192+2,ci*128+2),folder+' '+q['facing'],fill='#111924')
    save_scales('integrated-crops-before-after',crops)
    assert all(sha(p.read_bytes())==h for p,h in protected.items()),'furniture input changed'
    write_json(OUT/'measurements.json',dict(typing=typing,walking=walking,integrated=matches,
               checks=dict(typingCases=len(typing),walkerCases=len(walking),actualSeatedPositions=len(matches),
                           visibleHeadsAndHandsUnchanged=True,typingHands100Percent=True,
                           frozenFurnitureSHA256={str(p.relative_to(ROOT)):h for p,h in protected.items()}),
               scope='Static serialized v3 desk/chair ownership and original renderer PNG composite; no WPF runtime repack.'))
    print('evidence: 48 typing, '+str(len(walking))+' walker cases, 12 actual integrated seated positions; visibility unchanged')


def walking_evidence():
    """Actual moving actor positions from the supplied production arrival PNG."""
    verifier=load_module('outline_walking_v3_readonly',ART/'furniture-v3/workstations/verify-desk-rig-v3-lane.py')
    m=verifier.m
    desk,ds=verifier.furniture(ART/'furniture-v3/workstations','desk')
    chair,cs=verifier.furniture(ART/'furniture-v3/chair','chair')
    items,_=room_items(m,desk,ds,chair,cs)
    p=SCENE.with_name('iso-arrival-02.0s-1x.png')
    actual=rgba(p);updated=actual.copy()
    original2x=rgba(p.with_name('iso-arrival-02.0s-2x.png'));updated2x=original2x.copy()
    candidates={}
    for folder,profile in PROFILES.items():
        base=ART/'characters'/folder;eid=OUT/'evidence'/folder
        cid='dev-hoodie' if folder=='dev-hoodie-v2' else folder
        s=next(s for s in read(base/(cid+'.iso.json'))['sheets'] if s['kind']=='character')
        old=rgba(eid/'before'/s['file']);new=rgba(base/s['file']);hs=heads(base,s,profile)
        for clip in [c for c in s['rows'] if c['name']=='walk']:
            f=clip['direction']
            for phase in range(4):
                src=cell(old,clip['row'],phase);dst=cell(new,clip['row'],phase)
                off=s['headOffsetsByClip']['walk/'+f][phase];hm=shifted(hs[f],off['x'],off['y'])
                hy,hx=np.where(hm);positions=set()
                # Three seeds tolerate a monitor or tag covering one head texel.
                for q in [.20,.45,.70]:
                    sy,sx=int(hy[int(len(hy)*q)]),int(hx[int(len(hx)*q)])
                    cy,cx=np.where(np.all(actual[:,:,:3]==src[sy,sx,:3],axis=2))
                    positions.update((int(x-sx),int(y-sy)) for y,x in zip(cy,cx))
                for ax,ay in positions:
                    if ax<0 or ay<0 or ax+32>actual.shape[1] or ay+64>actual.shape[0]:continue
                    patch=actual[ay:ay+64,ax:ax+32]
                    eq=np.all(patch==src,axis=2)
                    if float(eq[hm].mean())<.80:continue
                    # Ground depth follows the visible foot anchor, independently
                    # of any desk's seated worker depth (rounded source pixels).
                    depth=(ay+60-104)/16
                    owned=owned_in_room(items,src,(ax,ay),(depth,0,'walker',folder))
                    if not owned.any() or float(eq[owned].mean())<.97:continue
                    changed=np.any(src!=dst,axis=2)&owned
                    if not np.all(eq[changed]):continue
                    changed2x=np.repeat(np.repeat(changed,2,axis=0),2,axis=1)
                    src2x=np.repeat(np.repeat(src,2,axis=0),2,axis=1)
                    patch2x=original2x[ay*2:ay*2+128,ax*2:ax*2+64]
                    if not np.array_equal(patch2x[changed2x],src2x[changed2x]):continue
                    hands=locked(src,hm,clip,profile)[1]
                    data=dict(character=folder,facing=f,phase=phase,position=[ax,ay],groundDepth=depth,
                              exactVisibleSourceFraction=float(eq[owned].mean()),changedTexels=int(changed.sum()),
                              visibleHeadBefore=int((hm&owned).sum()),visibleHeadAfter=int((hm&owned).sum()),
                              visibleHandsBefore=int((hands&owned).sum()),visibleHandsAfter=int((hands&owned).sum()))
                    score=data['exactVisibleSourceFraction']
                    key=(ax,ay)
                    if key not in candidates or candidates[key][0]<score:
                        candidates[key]=(score,data,dst.copy(),changed)
    assert {v[1]['character'] for v in candidates.values()}==set(PROFILES), 'need one exact walking match for each pilot'
    records=[]
    crops=Image.new('RGBA',(3*256,((len(candidates)+2)//3)*128),'#E7D1AC');d=ImageDraw.Draw(crops)
    for n,(_,data,dst,changed) in enumerate(sorted(candidates.values(),key=lambda v:(v[1]['character'],v[1]['position']))):
        ax,ay=data['position'];records.append(data)
        updated[ay:ay+64,ax:ax+32][changed]=dst[changed]
        changed2x=np.repeat(np.repeat(changed,2,axis=0),2,axis=1)
        dst2x=np.repeat(np.repeat(dst,2,axis=0),2,axis=1)
        updated2x[ay*2:ay*2+128,ax*2:ax*2+64][changed2x]=dst2x[changed2x]
        bx=(n%3)*256;by=(n//3)*128
        d.text((bx+2,by+2),data['character']+' '+data['facing']+' BEFORE / AFTER',fill='#111924')
        for rev,a in enumerate([actual,updated]):
            crops.alpha_composite(Image.fromarray(a).crop((ax-40,ay-16,ax+88,ay+96)),(bx+rev*128,by+16))
    for title,a in [('before',actual),('after',updated)]:
        for scale in (1,3):
            Image.fromarray(a).resize((actual.shape[1]*scale,actual.shape[0]*scale),Image.Resampling.NEAREST).save(OUT/(f'integrated-walking-{title}-{scale}x.png'))
    Image.fromarray(original2x).save(OUT/'integrated-walking-before-2x.png')
    Image.fromarray(updated2x).save(OUT/'integrated-walking-after-2x.png')
    save_scales('integrated-walking-crops-before-after',crops)
    write_json(OUT/'walking-integrated-measurements.json',dict(source=str(p),cases=records,
        visibleHeadsAndHandsUnchanged=True,exactChangedTexelsMatchOriginalAt1xAnd2x=True,
        scope='Exact matched walking atlas cells in actual PNG; other spawn/partly hidden actors are not claimed. Depth from rounded native foot projection; static composite only.'))
    print('integrated walking: '+str(len(records))+' exact actual pilot positions, 1x/2x source texels exact; heads/hands visibility unchanged')


def main():
    parser=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('folder',nargs='?',type=Path)
    parser.add_argument('--apply',action='store_true')
    parser.add_argument('--profile',type=Path)
    parser.add_argument('--evidence',action='store_true')
    parser.add_argument('--walking-evidence',action='store_true')
    args=parser.parse_args()
    if args.walking_evidence:
        walking_evidence()
    elif args.evidence:
        evidence()
    else:
        assert args.folder is not None,'character folder required'
        profile=read(args.profile) if args.profile else PROFILES.get(args.folder.name)
        assert profile is not None,'unknown character: provide approved --profile JSON'
        apply(args.folder,profile,args.apply)


if __name__=='__main__':
    main()
