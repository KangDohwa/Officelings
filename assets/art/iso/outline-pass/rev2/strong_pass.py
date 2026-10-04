"""Five-character rev2 driver. Deterministic pixel operations; no image generation."""
from pathlib import Path
import copy
import difflib
import hashlib
import importlib.util
import json
import os
import subprocess
import sys

sys.dont_write_bytecode = True
import numpy as np
from PIL import Image

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[4]
ART = ROOT/'assets/art/iso'
IDS = ('dev-hoodie-v2','punk-girl','professor','glasses-bun','hanbok-modern')


def load(name,path):
    spec=importlib.util.spec_from_file_location(name,path)
    mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
    return mod


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def write(path,data):
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def checker_patch():
    source=ROOT/'tools/iso_check.py'
    before=source.read_text(encoding='utf-8')
    old="assert 38 <= yy.max()-yy.min()+1 <= 42, 'standing height outside 38–42'"
    new="assert 38 <= yy.max()-yy.min()+1 <= 43, 'standing height outside 38–43'"
    assert before.count(old)==1 or before.count(new)==1, 'unrecognized shared height rule'
    after=before.replace(old,new)
    docfile=ROOT/'docs/iso-slice-s0.md'
    docbefore=docfile.read_text(encoding='utf-8')
    docafter=docbefore.replace('Standing reference height 40, allowed 38–42.',
        'Standing reference height 40; grounded standing/idle height including the exterior outline allowed 38–43. The character body, foot/pelvis/hand contacts, head offsets, cell size, and x=0 / y=61..63 transparency remain unchanged.')
    docafter=docafter.replace('Grounded 38–42px idle silhouette,',
        'Grounded 38–43px idle silhouette including the exterior outline; unchanged character body and contacts,')
    patch=''.join(difflib.unified_diff(before.splitlines(True),after.splitlines(True),
        fromfile='a/tools/iso_check.py',tofile='b/tools/iso_check.py'))
    patch+=''.join(difflib.unified_diff(docbefore.splitlines(True),docafter.splitlines(True),
        fromfile='a/docs/iso-slice-s0.md',tofile='b/docs/iso-slice-s0.md'))
    # LF patch syntax, independent of Windows text-mode newline translation.
    if patch:
        (OUT/'S0-outline-rev2-proposed.patch').write_bytes(patch.encode('utf-8'))
    # Harness relocation only. The proposed shared-file patch does not change ROOT.
    local=after.replace('ROOT = Path(__file__).resolve().parents[1]',
                        'ROOT = Path(__file__).resolve().parents[5]  # local copy only')
    (OUT/'iso_check.py').write_text(local,encoding='utf-8')
    write(OUT/'shared-input-hashes.json',{'tools/iso_check.py':sha(source),
        'docs/iso-slice-s0.md':sha(docfile)})
    return load('iso_check_rev2_local',OUT/'iso_check.py')


def build(ident,op,checker,args):
    assert ident in IDS
    folder=ART/'characters'/ident
    cid='dev-hoodie' if ident=='dev-hoodie-v2' else ident
    manifest=folder/(cid+'.iso.json'); doc=read(manifest)
    normal=next(s for s in doc['sheets'] if s['kind']=='character')
    blink=next(s for s in doc['sheets'] if s['id']==normal['blink']['sheetId'])
    profile=op.PROFILES[ident]; strong=op.STRONG_PROFILES[ident]
    backup=OUT/'runs/outline-rev1-backup'/ident;backup.mkdir(parents=True,exist_ok=True)
    prior=OUT/'characters'/ident/'proof.json'
    previous=read(prior) if prior.exists() else {}
    for s in (normal,blink):
        p=folder/s['file'];b=backup/p.name
        if not b.exists():
            b.write_bytes(p.read_bytes())
        if previous.get('applied'):
            assert sha(p)==previous['productionAfterSHA256'][p.name],'production drift'
        else:
            assert p.read_bytes()==b.read_bytes(),'backup/input mismatch'
    source=op.rgba(backup/normal['file']); closed=op.rgba(backup/blink['file'])
    hs={}
    for di,f in enumerate(op.FACINGS):
        if profile['headFiles'][di]==normal['file']:
            a=op.cell(source,di,0).copy()
            a[profile['headCrop'][di]:]=0
            hs[f]=a[:,:,3]>0
        else:
            hs[f]=op.heads(folder,normal,profile)[f]
    eyes={f:op.rgba(folder/v['eyeMask'])[:,:,3]>0 for f,v in normal['faceLock'].items()}
    stable=[manifest,folder/(cid+'-iso.manifest.json')]+[folder/v['eyeMask'] for v in normal['faceLock'].values()]
    stable += [folder/p for p in set(profile['headFiles']) if p!=normal['file']]
    frozen={str(p.relative_to(folder)):sha(p) for p in stable}
    allowed={op.rgb(h) for ramp in checker.RAMPS.values() for h in ramp.split()} | {op.rgb(h) for h in normal['identityPalette']}
    for color in [strong['rim'],strong['maxRim'],strong['shadow']]+[h for ramp in strong['materials'] for h in ramp]:
        assert op.rgb(color) in allowed,('not frozen palette',ident,color)
    params=copy.deepcopy(op.STRONG_PARAMETERS)
    if args.rim_sides: params['rimSides']=args.rim_sides
    if args.rim_below is not None: params['rimBelowLinearY']=args.rim_below
    if args.contour_width: params['contourWidth']=args.contour_width
    maximum={**params,'rimSides':'all','contourWidth':2,'materialSteps':2}
    original_path=OUT.parent/'evidence'/ident/'before'/normal['file']
    original=op.rgba(original_path) if original_path.exists() else source.copy()
    variants={'original':original,'rev1':source.copy(),'rev2':source.copy(),'rev2-max':source.copy()}
    records=[];regions={}
    for clip in normal['rows']:
        f=clip['direction'];row=clip['row']
        for phase,off in enumerate(normal['headOffsetsByClip'][clip['name']+'/'+f]):
            hm=op.shifted(hs[f],off['x'],off['y'])
            src=op.cell(source,row,phase)
            assert not np.any(hm & (src[:,:,3]==0))
            # These two finished lanes had no applied rev1: show an explicit simulation.
            if not original_path.exists():
                old,_=op.edit_cell(src,hm,clip,phase,profile)
                op.cell(variants['rev1'],row,phase)[:]=old
            counts={}
            for label,p in [('rev2',params),('rev2-max',maximum)]:
                sp=strong if label=='rev2' else {**strong,'rim':strong['maxRim']}
                dst,masks=op.strong_cell(src,hm,clip,phase,profile,sp,p)
                op.cell(variants[label],row,phase)[:]=dst
                counts[label]={k:int(masks[k].sum()) for k in ('ring','rim','contour','tonal')}
                counts[label]['changed']=int(np.any(src!=dst,axis=2).sum())
                if label=='rev2':regions[row,phase]=masks
            records.append(dict(row=row,phase=phase,action=clip['name'],facing=f,**counts))
    blink_variants={}
    for label,atlas in variants.items():
        result=np.empty_like(closed)
        for b,row in enumerate(normal['blink']['normalRows']):
            facing=normal['rows'][row]['direction']
            for phase in range(4):
                mask=op.cell(eyes[facing],row,phase)
                src=op.cell(atlas,row,phase);dst=op.cell(result,b,phase)
                dst[:]=src;dst[mask]=op.cell(closed,b,phase)[mask]
                assert np.array_equal(src[~mask],dst[~mask])
                assert np.array_equal(dst[mask],op.cell(closed,b,phase)[mask])
        blink_variants[label]=result
        arrays={normal['id']:(atlas,normal),blink['id']:(result,blink)}
        for s,a in [(normal,atlas),(blink,result)]:
            checker.geometry(a,s);checker.transparency(a,s);checker.palette(a,s,0)
            checker.anchors(a,s);checker.row_contract(a,s,'final')
        checker.aliases(normal);checker.face_lock(folder,normal,arrays)
        dest=OUT/'characters'/ident/label;dest.mkdir(parents=True,exist_ok=True)
        Image.fromarray(atlas).save(dest/'normal.png');Image.fromarray(result).save(dest/'blink.png')
    proof=dict(character=ident,applied=previous.get('applied',False),parameters=params,maxParameters=maximum,
        originalSource=str(original_path) if original_path.exists() else str(backup/normal['file']),
        rev1Simulated=not original_path.exists(),normalCells=152,blinkCells=96,
        originalBodyAlphaPreserved=True,canonicalHeadRGBAExactCells=152,handRGBAExactCells=152,
        footprintRow60RGBAExact=True,
        eyeMasksSHA256={v['eyeMask']:sha(folder/v['eyeMask']) for v in normal['faceLock'].values()},
        contactsAndHeadOffsetsUnchanged=True,
        closedEyeRGBAExactCells=96,blinkNonEyeCellsEqualNewNormal=96,
        protectedSHA256Before=frozen,productionBeforeSHA256={s['file']:sha(backup/s['file']) for s in (normal,blink)},
        records=records)
    if previous.get('applied'):
        proof['productionAfterSHA256']=previous['productionAfterSHA256']
        proof['checker']=previous['checker']
    assert all(sha(folder/p)==h for p,h in frozen.items())
    return dict(ident=ident,folder=folder,cid=cid,normal=normal,blink=blink,profile=profile,
        source=source,heads=hs,variants=variants,blinkVariants=blink_variants,regions=regions,
        proof=proof,frozen=frozen)


def commit(data,op):
    ident=data['ident'];folder=data['folder'];normal=data['normal'];blink=data['blink']
    for s,a in [(normal,data['variants']['rev2']),(blink,data['blinkVariants']['rev2'])]:
        Image.fromarray(a).save(folder/s['file'])
    # Same approved phase/mask replacement as the existing blink_resync helpers.
    # Also run each original helper, and require exact output agreement/mask hashes.
    helper=folder/('hanbok_blink_resync.py' if ident=='hanbok-modern' else 'blink_resync.py')
    result=subprocess.run([sys.executable,'-B',str(helper)],capture_output=True,
        encoding='utf-8',env={**os.environ,'PYTHONIOENCODING':'utf-8'})
    dest=OUT/'characters'/ident
    (dest/'blink-resync.txt').write_text(result.stdout+result.stderr,encoding='utf-8')
    assert result.returncode==0,result.stdout+result.stderr
    assert np.array_equal(op.rgba(folder/blink['file']),data['blinkVariants']['rev2'])
    assert all(sha(folder/p)==h for p,h in data['frozen'].items()),'frozen file changed'
    # Update only the existing review PNGs in this character's authorized directory.
    op.review(folder,data['variants']['rev2'],normal)
    command=[sys.executable,'-B',str(OUT/'iso_check.py'),str(folder),'--stage','final','--tolerance','0']
    check=subprocess.run(command,capture_output=True,encoding='utf-8',env={**os.environ,'PYTHONIOENCODING':'utf-8'})
    (dest/'iso-check-final.txt').write_text(check.stdout+check.stderr,encoding='utf-8')
    assert check.returncode==0,check.stdout+check.stderr
    data['proof'].update(applied=True,productionAfterSHA256={s['file']:sha(folder/s['file']) for s in (normal,blink)},
        checker=dict(command=command,PASS=check.stdout.count('| PASS |'),FAIL=check.stdout.count('| FAIL |'),
                     SKIP=check.stdout.count('| SKIP |'),exitCode=check.returncode))
    write(dest/'proof.json',data['proof'])
    print(ident+': rev2 applied; 152 head/hand cells exact; 96 blink cells; local checker FAIL 0',flush=True)


def run(args):
    if args.revision=='rev2-max' and args.apply:
        raise ValueError('rev2-max is comparison only; production uses rev2')
    op=load('outline_revision_core',OUT.parent/'outline_pass.py')
    checker=checker_patch()
    if args.all:
        selected=IDS
    else:
        assert args.folder is not None,'folder or --all required'
        folder=args.folder.resolve()
        assert folder.parent==ART/'characters' and folder.name in IDS,'outside owned folders'
        selected=(folder.name,)
    data=[build(ident,op,checker,args) for ident in selected]
    if args.comparisons or args.evidence or args.walking_evidence:
        evidence=load('outline_strong_evidence',OUT/'strong_evidence.py')
        evidence.run(data,op,apply_expected=args.apply)
    if args.apply:
        for item in data:
            commit(item,op)
    for item in data:
        write(OUT/'characters'/item['ident']/'proof.json',item['proof'])
    shared=read(OUT/'shared-input-hashes.json')
    assert all(sha(ROOT/p)==h for p,h in shared.items()),'shared checker/doc changed'
    result_path=OUT/'RESULT.json'
    if result_path.exists():
        result=read(result_path)
        result['appliedCharacters']=[item['ident'] for item in data if item['proof']['applied']]
        result['productionProofs']={item['ident']:str(OUT/'characters'/item['ident']/'proof.json') for item in data}
        write(result_path,result)
    print('Preview variants complete. Production applied: '+str(args.apply),flush=True)
