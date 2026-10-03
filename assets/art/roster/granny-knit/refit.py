from pathlib import Path
from PIL import Image
import json, sys, shutil
from sprite_gen.curate.curation import apply_transform, write_curation_atomic
from sprite_gen.frames.extract import binarize_alpha, build_shared_palette, apply_palette
from sprite_gen.spec.runio import publish_guard

root=Path(__file__).resolve().parent
run=root/'run'
qa=run/'qa'
rejected=qa/'rejected'

if sys.argv[1]=='reset':
    for name in ['sprite-sheet-alpha.png','manifest.json','sprite-sheet-alpha.report.json','curation.json']:
        shutil.copy2(run/name,rejected/('pixel-unfake-'+name))
    shutil.copy2(run/'frames/frames-manifest.json',rejected/'pixel-unfake-frames-manifest.json')
    for path in [root/'request.json',run/'sprite-request.json']:
        request=json.loads(path.read_text())
        request['fit']['pixel_unfake']=False
        request['fit']['resample']='kcentroid'
        path.write_text(json.dumps(request,indent=2)+'\n')
    with publish_guard(run):
        write_curation_atomic(run,{'kind':'sprite-gen-curation','version':1,'states':{}})
    print('Saved rejected pixel-unfake output; canonical extraction now uses kcentroid.')

elif sys.argv[1]=='postfit':
    mf=json.loads((run/'manifest.json').read_text())
    fm=json.loads((run/'frames/frames-manifest.json').read_text())
    rows={r['state']:r for r in fm['rows']}
    records=[]
    for state,rects in mf['frame_layout']['rows'].items():
        sources=[binarize_alpha(Image.open(run/rows[state]['files'][i]).convert('RGBA')) for i in range(len(rects))]
        boxes=[im.getbbox() for im in sources]
        # Equal scale throughout each row; keep genuine gesture/stride differences.
        target_height=int(sys.argv[2]) if len(sys.argv)>2 else 46
        scale=min(target_height/max(b[3]-b[1] for b in boxes),30/max(b[2]-b[0] for b in boxes),1.0)
        for i,(src,b,rect) in enumerate(zip(sources,boxes,rects)):
            t={'scale':scale,'dy':60-(32+(b[3]-32)*scale)}
            out=apply_transform(src,t,(32,64),snap_scale=1)
            bb=out.getbbox()
            t['dy']+=60-bb[3]
            if bb[0]<1:t['dx']=1-bb[0]
            elif bb[2]>31:t['dx']=31-bb[2]
            out=binarize_alpha(apply_transform(src,t,(32,64),snap_scale=1))
            records.append({'state':state,'index':i,'source':rows[state]['files'][i],
                            'rect':rect,'transform':t,'image':out})
    palette=build_shared_palette([r['image'] for r in records],32)
    atlas=Image.new('RGBA',(128,832))
    for record in records:
        im=apply_palette(record.pop('image'),palette)
        b=im.getbbox()
        assert b and b[0]>=1 and b[2]<=31 and b[1]>=4 and b[3]==60,(record['state'],record['index'],b)
        record['bbox']=b
        path=run/'exports/pngs'/record['state']
        path.mkdir(parents=True,exist_ok=True)
        im.save(path/f"frame-{record['index']}.png")
        atlas.alpha_composite(im,(record['rect']['x'],record['rect']['y']))
    atlas.save(run/'sprite-sheet-alpha.png')
    mf['palette_size']=len(palette)
    mf['curation_applied']=True
    mf['postprocess']={'source':'canonical component extraction with fit.resample=kcentroid',
        'recipe':'qa/final-fit.json','alpha_threshold':128,'transform_sampling':'nearest','palette_size':len(palette),
        'maximum_content_height':target_height}
    (run/'manifest.json').write_text(json.dumps(mf,indent=2)+'\n')
    evidence={'library_functions':['binarize_alpha','apply_transform(snap_scale=1)','build_shared_palette','apply_palette'],
              'palette':palette,'frames':records,'raw_source_poses_unchanged':True}
    (qa/'final-fit.json').write_text(json.dumps(evidence,indent=2)+'\n')
    report=json.loads((run/'sprite-sheet-alpha.report.json').read_text())
    report={'original_compose':report,'postprocess':mf['postprocess'],'sheet':[128,832],'frames':42,'binary_alpha':True}
    (run/'sprite-sheet-alpha.report.json').write_text(json.dumps(report,indent=2)+'\n')
    print('Nearest fit and one shared 32-color palette applied to 42 canonical extracted cells.')

elif sys.argv[1]=='normalize-report':
    mf=json.loads((run/'manifest.json').read_text())
    evidence=json.loads((qa/'final-fit.json').read_text())
    atlas=Image.open(run/'sprite-sheet-alpha.png').convert('RGBA')
    rects=mf['frame_layout']['rows']['report']
    cells=[atlas.crop((b['x'],b['y'],b['x']+32,b['y']+64)) for b in rects]
    target=min(im.getbbox()[3]-im.getbbox()[1] for im in cells)
    for i,(src,rect) in enumerate(zip(cells,rects)):
        b=src.getbbox()
        scale=target/(b[3]-b[1])
        t={'scale':scale,'dy':60-(32+(b[3]-32)*scale)}
        im=binarize_alpha(apply_transform(src,t,(32,64),snap_scale=1))
        t['dy']+=60-im.getbbox()[3]
        im=apply_palette(binarize_alpha(apply_transform(src,t,(32,64),snap_scale=1)),evidence['palette'])
        bb=im.getbbox()
        assert bb[3]-bb[1]==target and bb[3]==60 and bb[0]>=1 and bb[2]<=31
        atlas.paste(im,(rect['x'],rect['y']))
        im.save(run/'exports/pngs/report'/f'frame-{i}.png')
        for record in evidence['frames']:
            if record['state']=='report' and record['index']==i:
                record['report_normalization']=t
                record['bbox']=bb
    evidence['report_normalized_height']=target
    (qa/'final-fit.json').write_text(json.dumps(evidence,indent=2)+'\n')
    atlas.save(run/'sprite-sheet-alpha.png')
    mf['postprocess']['report_normalized_height']=target
    (run/'manifest.json').write_text(json.dumps(mf,indent=2)+'\n')
    report=json.loads((run/'sprite-sheet-alpha.report.json').read_text())
    report['postprocess']['report_normalized_height']=target
    (run/'sprite-sheet-alpha.report.json').write_text(json.dumps(report,indent=2)+'\n')
    print(f'Report cell heights normalized to {target}px; other 39 cells and shared palette unchanged.')
