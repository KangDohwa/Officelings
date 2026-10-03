from pathlib import Path
from PIL import Image
import json, shutil, sys, hashlib
from sprite_gen.curate.curation import write_curation_atomic, apply_transform, pixel_snap_scale
from sprite_gen.spec.runio import publish_guard

root=Path(__file__).resolve().parent
run=root/'run'

if sys.argv[1]=='fit':
    request=json.loads((run/'sprite-request.json').read_text())
    payload={'version':1,'kind':'sprite-gen-curation','states':{}}
    measurements={}
    for state,spec in request['states'].items():
        frames=[Image.open(run/'frames'/state/f'frame-{i}.png').convert('RGBA') for i in range(spec['frames'])]
        boxes=[im.getbbox() for im in frames]
        assert all(boxes),state
        # One scale per row: preserves intentional within-row breathing and gait.
        scale=min(46/max(b[3]-b[1] for b in boxes),30/max(b[2]-b[0] for b in boxes),1.0)
        transforms={}
        for i,(im,b) in enumerate(zip(frames,boxes)):
            t={'scale':scale,'dy':60-(32+(b[3]-32)*scale)}
            baked=apply_transform(im,t,(32,64),snap_scale=pixel_snap_scale(request))
            bb=baked.getbbox()
            t['dy']+=60-bb[3]
            if bb[0]<1: t['dx']=1-bb[0]
            elif bb[2]>31: t['dx']=31-bb[2]
            transforms[str(i)]=t
        payload['states'][state]={'transforms':transforms}
        measurements[state]={'source_bboxes':boxes,'uniform_row_scale':scale}
    with publish_guard(run):
        write_curation_atomic(run,payload)
    (run/'qa/fit.json').write_text(json.dumps(measurements,indent=2)+'\n')
    print('Wrote canonical curation scale/bottom placement; source poses retained.')

elif sys.argv[1]=='export':
    canonical=json.loads((root.parents[1]/'worker-request.json').read_text())['states']
    mf=json.loads((run/'manifest.json').read_text())
    mf.update(game_input='granny-knit.png',sprite_sheet_alpha='granny-knit.png',
              sprite_sheet_alpha_report='granny-knit.report.json',base_image='base.png')
    mf['animation']['rows']['idle']['fps']=0
    sheet=Image.open(run/'sprite-sheet-alpha.png').convert('RGBA')
    assert sheet.size==(128,832)
    assert list(mf['animation']['rows'])==list(canonical)
    assert mf['chroma_key']['hex']=='#FF00FF'
    assert set(sheet.getchannel('A').getdata())=={0,255}
    records=[]
    for row,(state,spec) in enumerate(canonical.items()):
        entry=mf['animation']['rows'][state]
        assert (entry['row'],entry['frames'],entry['fps'],entry['loop'])==(row,spec['frames'],spec['fps'],spec['loop'])
        hashes=[]
        boxes=[]
        for i,box in enumerate(mf['frame_layout']['rows'][state]):
            assert box==dict(x=i*32,y=row*64,w=32,h=64)
            cell=sheet.crop((i*32,row*64,(i+1)*32,(row+1)*64))
            b=cell.getbbox()
            assert b and b[0]>=1 and b[2]<=31 and b[1]>=4 and b[3]==60,(state,i,b)
            boxes.append(b)
            hashes.append(hashlib.sha256(cell.tobytes()).hexdigest())
        assert len(hashes)==spec['frames']
        records.append({'state':state,'frames':len(hashes),'distinct_frames':len(set(hashes)),'bounds':boxes})
        for i in range(spec['frames'],4):
            assert sheet.crop((i*32,row*64,(i+1)*32,(row+1)*64)).getbbox() is None
    colors={p[:3] for p in sheet.getdata() if p[3]}
    assert not any(r>220 and b>220 and g<45 for r,g,b in colors)
    assert all(a or (r,g,b)==(0,0,0) for r,g,b,a in sheet.getdata())
    result={'sheet':[128,832],'cell':[32,64],'rows':13,'frames':sum(r['frames'] for r in records),
            'binary_alpha':True,'empty_trailing_cells':True,'safe_margins':True,'bottom_y':60,
            'visible_pure_chroma_pixels':0,'opaque_colors':len(colors),'states':records,
            'facelock_applied':False,'runtime_motion_continuity':'unverified'}
    (run/'qa/validation.json').write_text(json.dumps(result,indent=2)+'\n')
    shutil.copy2(run/'sprite-sheet-alpha.png',root/'granny-knit.png')
    shutil.copy2(run/'sprite-sheet-alpha.report.json',root/'granny-knit.report.json')
    (root/'granny-knit.manifest.json').write_text(json.dumps(mf,indent=2)+'\n')
    print(json.dumps({k:v for k,v in result.items() if k!='states'}))
    print('Distinct frame counts: '+', '.join(r['state']+'='+str(r['distinct_frames']) for r in records))

elif sys.argv[1]=='previews':
    from sprite_gen.util.gif_utils import save_clean_gif
    request=json.loads((run/'sprite-request.json').read_text())
    directory=run/'qa/gifs'
    directory.mkdir(exist_ok=True)
    report=[]
    for state,spec in request['states'].items():
        if state=='idle':continue  # Four static facing anchors, not an animation.
        frames=[Image.open(run/'exports/pngs'/state/f'frame-{i}.png').convert('RGBA') for i in range(spec['frames'])]
        out=directory/f'{state}.gif'
        save_clean_gif(frames,out,duration_ms=round(1000/spec['fps']),loop=0 if spec['loop'] else 1)
        report.append({'state':state,'frames':len(frames),'fps':spec['fps'],'loop':spec['loop'],
                       'file':str(out.relative_to(run)),'source':'exports/pngs final sheet cells'})
    (directory/'manifest.json').write_text(json.dumps(report,indent=2)+'\n')
    print('Exported 12 motion GIF previews from the final delivered cells; idle remains facing selection.')
