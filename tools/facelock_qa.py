"""Read-only pixel QA; 6x NEAREST before/after strips go to TEMP/facelock-qa.

Measure the full common-opaque eye boxes from source/output PNGs independently
of the processor's recorded eyePixels. The metric is max pairwise RGB-channel
range (0..255) after integer head alignment, not an image similarity score.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path

import numpy as np
from PIL import Image,ImageDraw

import facelock as f


def extract(sheet,m):
    return {s:[sheet[r['y']:r['y']+r['h'],r['x']:r['x']+r['w']].copy() for r in rr]
            for s,rr in m['frame_layout']['rows'].items()}


def difference(frames,offsets,boxes):
    aligned=[f.shift(a,-dx,-dy) for a,(dx,dy) in zip(frames,offsets)]
    mask=f.box_mask(aligned[0].shape,boxes)
    support=np.logical_and.reduce([a[:,:,3]>0 for a in aligned])
    mask &= support
    if not mask.any():
        raise AssertionError("empty eye comparison region")
    values=np.stack([a[mask,:3].astype(np.int16) for a in aligned])
    ranges=values.max(axis=0)-values.min(axis=0)
    return int(ranges.max()),int(np.any(ranges>0,axis=1).sum()),int(mask.sum())


def strip(name,state,before,after,path,head=False):
    scale=6
    y0,y1=0,64
    if head:
        y0=max(0,min(int(np.where(a[:,:,3]>0)[0].min()) for a in before)-1)
        y1=min(64,y0+38)
    height=(y1-y0)*scale+24
    canvas=Image.new('RGB',(32*scale*max(4,len(before)),height*2),'#ded9cf')
    draw=ImageDraw.Draw(canvas)
    for row,(label,frames) in enumerate((('BEFORE',before),('AFTER',after))):
        draw.text((4,row*height+5),f'{name} / {state} / {label} / 6x NEAREST',fill='#25232b')
        for i,a in enumerate(frames):
            im=Image.fromarray(a).crop((0,y0,32,y1)).resize((32*scale,(y1-y0)*scale),Image.Resampling.NEAREST)
            canvas.paste(im,(i*32*scale,row*height+24),im)
    canvas.save(path)


def check(report_path,outdir):
    report=f.read_json(report_path);name=report['character'];locked=report_path.parent
    if report['profile']=='automatic-unreviewed':
        print(f'REVIEW {name}: automatic face detection; view the strips before adopting output',flush=True)
    mp=(locked/report['sourceManifest']).resolve()
    sp=(locked/report['sourceSheet']).resolve() if report['sourceSheet'] else None
    source_frames=(locked/report['sourceFrames']).resolve() if report.get('sourceFrames') else None
    base=locked.parent.parent if locked.parent.name=='.generations' else locked
    roster=base.parent if base.parent.parent.name=='roster' else None
    original_m,original,old,blobs=f.load_source(name,mp,sp,roster,source_frames)
    for relative,digest in report['sourceHashes'].items():
        p=Path(relative);p=p if p.is_absolute() else f.ROOT/p
        assert hashlib.sha256(p.read_bytes()).hexdigest()==digest,f'{name}: stale output; rerun facelock'
    m=f.read_json(locked/report['outputManifest'])
    output=f.rgba(locked/report['outputSheet']);new=extract(output,m)
    assert original.shape==output.shape and np.array_equal(original[:,:,3],output[:,:,3]),f'{name}: alpha changed'
    assert original_m['animation']==m['animation'] and original_m['frame_layout']==m['frame_layout'],f'{name}: geometry/timing changed'
    maximum=0;rows=[]
    for state in f.FACE_ROWS:
        r=report['rows'][state];offsets=r['offsets'];boxes=r['eyeBoxes']
        before,changed,count=difference(old[state],offsets,boxes)
        after,remaining,_=difference(new[state],offsets,boxes)
        mouth_before=mouth_after=None
        if r['mouthBox']:
            mouth_before,_,_=difference(old[state],offsets,[r['mouthBox']])
            mouth_after,_,_=difference(new[state],offsets,[r['mouthBox']])
            assert mouth_after==0,f'{name}/{state}: mouth difference {mouth_after}'
        assert after==0,f'{name}/{state}: residual eye difference {after}'
        maximum=max(maximum,before)
        # Check every output byte outside the declared face patch, not just eyes.
        patch=np.zeros((64,32),dtype=bool)
        for y,x in r['patchRuns']:
            patch[y,x]=True
        for i,(a,b,(dx,dy)) in enumerate(zip(old[state],new[state],offsets)):
            mask=f.shift(patch,dx,dy)
            assert np.array_equal(a[~mask],b[~mask]),f'{name}/{state}/{i}: edited hair/body outside patch'
            if roster:
                exported=f.rgba(locked/f'curated/{state}-frame-{i}.png')
                assert np.array_equal(b,exported),f'{name}: curated output differs from sheet'
        print(f'{name:26} {state:14} eye max {before:3} -> {after} | differing pixels {changed:3} -> {remaining} / {count}',flush=True)
        rows.append({'state':state,'before':before,'after':after,'differingPixelsBefore':changed,'comparedPixels':count,
                     'mouthBefore':mouth_before,'mouthAfter':mouth_after,
                     'silhouetteExcludedPixels':r['silhouetteExcludedPixels']})
        strip(name,state,old[state],new[state],outdir/f'{name}-{state}.png')
        strip(name,state,old[state],new[state],outdir/f'{name}-{state}-heads.png',head=True)
    for i,direction in enumerate(('down','left','right')):
        r=report['rows']['idle_'+direction];state='walk_'+direction
        # Cross-state facing check: the idle slot and all four walk frames.
        before,_,count=difference([old['idle'][i]]+old[state],r['offsets']+report['rows'][state]['offsets'],r['eyeBoxes'])
        after,_,_=difference([new['idle'][i]]+new[state],r['offsets']+report['rows'][state]['offsets'],r['eyeBoxes'])
        assert after==0,f'{name}/idle+{state}: facing mismatch {after}'
        patch=np.zeros((64,32),dtype=bool)
        for y,x in r['patchRuns']:
            patch[y,x]=True
        mask=f.shift(patch,*r['offsets'][0])
        assert np.array_equal(old['idle'][i][~mask],new['idle'][i][~mask]),f'{name}: idle changed outside face patch'
        print(f'{name:26} {"idle+"+direction:14} eye max {before:3} -> {after} / {count}',flush=True)
        rows.append({'state':'idle+'+direction,'before':before,'after':after,'comparedPixels':count})
    for state in f.BACK:
        assert all(np.array_equal(a,b) for a,b in zip(old[state],new[state])),f'{name}: back row changed'
        print(f'{name:26} {state:14} SKIP (no eyes; byte-identical)')
    assert np.array_equal(old['idle'][3],new['idle'][3]),f'{name}: back idle changed'
    strip(name,'idle',old['idle'],new['idle'],outdir/f'{name}-idle.png')
    for direction,b in m.get('blink',{}).items():
        a=f.rgba(locked/b['file']);source=new[b['source']['state']][b['source']['frame']]
        assert a.shape==source.shape and np.array_equal(a[:,:,3],source[:,:,3]),f'{name}: blink alpha'
    if m.get('blink'):
        ff=[f.rgba(locked/m['blink'][d]['file']) for d in ('down','left','right')]
        strip(name,'blink',[new['walk_'+d][0] for d in ('down','left','right')],ff,outdir/f'{name}-blink.png')
    return {'character':name,'maxBefore':maximum,'maxAfter':0,'profile':report['profile'],'rows':rows}


def self_check():
    # An asymmetric, translated head plus unrelated animated pixels: assert the
    # scorer detects a corrupted output and body/alpha motion survives locking.
    a=np.zeros((64,32,4),dtype=np.uint8)
    a[8:23,8:23]=[210,150,100,255];a[8:14,8:20]=[60,30,20,255]
    a[17:20,12:14,:3]=[10,5,3];a[30:50,10:21]=[20,120,80,255]
    b=f.shift(a,1,2);b[19:22,13:15,:3]=[240,240,220];b[41,16,:3]=[50,60,70]
    boxes=[(11,16,15,21)]
    out,r=f.lock_row([a,b],boxes,None)
    assert r['offsets']==[[0,0],[1,2]],r['offsets']
    assert difference([a,b],r['offsets'],boxes)[0]>0
    assert difference(out,r['offsets'],boxes)[0]==0
    assert np.array_equal(out[1][:,:,3],b[:,:,3]) and np.array_equal(out[1][41,16],b[41,16])
    damaged=[v.copy() for v in out];damaged[1][19,13,0]^=1
    assert difference(damaged,r['offsets'],boxes)[0]>0
    auto_boxes,_=f.auto_profile(a)
    assert auto_boxes and auto_boxes[0][0]<=12<auto_boxes[0][2],auto_boxes
    print('self-check PASS: integer alignment, eye corruption detected, alpha/body preserved')


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--all',action='store_true',help='check every available locked character (default)')
    ap.add_argument('--character',action='append',help='limit QA, repeatable')
    ap.add_argument('--out',type=Path,default=Path(os.environ.get('TEMP','/tmp'))/'facelock-qa')
    ap.add_argument('--self-check',action='store_true')
    args=ap.parse_args()
    if args.self_check:
        self_check()
    args.out.mkdir(parents=True,exist_ok=True)
    reports=sorted((f.ART/'sheets/locked').glob('*.facelock.json'))+sorted((f.ART/'roster').glob('*/locked/*.facelock.json'))
    for mp in sorted((f.ART/'sheets/locked').glob('*.manifest.json'))+sorted((f.ART/'roster').glob('*/locked/*.manifest.json')):
        m=f.read_json(mp)
        if 'publication' in m:
            reports=[p for p in reports if p.name!=mp.name.replace('.manifest.json','.facelock.json')]
            reports.append(mp.parent/m['facelock']['report'])
    results=[];errors=[]
    for p in reports:
        if args.character and f.read_json(p)['character'] not in args.character:
            continue
        try:
            results.append(check(p,args.out))
        except (AssertionError,ValueError,OSError,KeyError) as e:
            errors.append(str(e));print('FAIL',e,flush=True)
    summary={'characters':results,'errors':errors,'scale':6,'metric':'max pairwise aligned RGB channel difference over common-opaque eye boxes'}
    (args.out/'report.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
    print(f'QA: characters={len(results)} failures={len(errors)}; strips/report: {args.out}')
    return 1 if errors or not results else 0


if __name__=='__main__':
    raise SystemExit(main())
