from pathlib import Path
from PIL import Image, ImageDraw
import json, sys

root=Path(__file__).resolve().parent
run=root/'run'
qa=run/'qa/review'
qa.mkdir(parents=True,exist_ok=True)
states=list(json.loads((root/'request.json').read_text())['states'])
groups=[states[:5], states[5:9], states[9:]]

if sys.argv[1]=='base':
    canvas=Image.new('RGB',(1120,700),(226,221,210))
    d=ImageDraw.Draw(canvas)
    im=Image.open(root/'base.png').convert('RGBA')
    im.thumbnail((650,650),Image.Resampling.NEAREST)
    canvas.paste(im,(8,30),im)
    d.text((8,8),'granny-knit | accepted base review',fill=(35,32,39))
    for n,name in enumerate(['afro-sweater','manager-suit','apron-baker','cat-hood']):
        p=root.parent/name/'C-contact.png'
        im=Image.open(p).convert('RGBA')
        im.thumbnail((200,300),Image.Resampling.NEAREST)
        x,y=680+(n%2)*220,24+(n//2)*335
        d.text((x,y),name+' | C-contact',fill=(35,32,39))
        canvas.paste(im,(x,y+20),im)
    canvas.save(root/'references/base-review.png')
elif sys.argv[1]=='raw':
    for page,group in enumerate(groups):
        if not all((run/'raw'/f'{s}.png').exists() for s in group): continue
        canvas=Image.new('RGB',(1120,len(group)*240),(226,221,210))
        d=ImageDraw.Draw(canvas)
        for n,s in enumerate(group):
            im=Image.open(run/'raw'/f'{s}.png').convert('RGBA')
            im.thumbnail((1100,210),Image.Resampling.NEAREST)
            d.text((8,n*240+5),s,fill=(35,32,39))
            canvas.paste(im,((1120-im.width)//2,n*240+24),im)
        canvas.save(qa/f'raw-{page+1}.png')
elif sys.argv[1]=='corrections':
    selected=['walk_left','walk_right','typing_back']
    canvas=Image.new('RGB',(1120,240*len(selected)),(226,221,210))
    d=ImageDraw.Draw(canvas)
    for n,s in enumerate(selected):
        im=Image.open(run/'raw'/f'{s}.png').convert('RGBA')
        im.thumbnail((1100,210),Image.Resampling.NEAREST)
        d.text((8,n*240+5),s+' | correction with own posture/direction reference',fill=(35,32,39))
        canvas.paste(im,((1120-im.width)//2,n*240+24),im)
    canvas.save(qa/'corrections.png')
elif sys.argv[1]=='pencil':
    canvas=Image.new('RGB',(1120,640),(226,221,210))
    d=ImageDraw.Draw(canvas)
    for n,(label,path) in enumerate([
            ('rejected: double-pencil V',run/'qa/rejected/v2-walk_right.png'),
            ('new right walk: single pencil',run/'raw/walk_right.png')]):
        im=Image.open(path).convert('RGBA')
        im.thumbnail((1100,270),Image.Resampling.NEAREST)
        d.text((8,n*320+6),label,fill=(35,32,39))
        canvas.paste(im,((1120-im.width)//2,n*320+25),im)
    canvas.save(qa/'right-pencil-comparison.png')
elif sys.argv[1]=='all':
    mf=json.loads((run/'manifest.json').read_text())
    sheet=Image.open(run/'sprite-sheet-alpha.png').convert('RGBA')
    canvas=Image.new('RGB',(1120,1500),(226,221,210))
    d=ImageDraw.Draw(canvas)
    for n,s in enumerate(states):
        col,row=divmod(n,7)
        x,y=col*560,row*214
        d.text((x+8,y+6),s,fill=(35,32,39))
        for i,b in enumerate(mf['frame_layout']['rows'][s]):
            cell=sheet.crop((b['x'],b['y'],b['x']+32,b['y']+64)).resize((96,192),Image.Resampling.NEAREST)
            canvas.paste(cell,(x+130+i*98,y+20),cell)
    canvas.save(qa/'final-all-preserved.png')
elif sys.argv[1] in ('final','final2'):
    mf=json.loads((run/'manifest.json').read_text())
    sheet=Image.open(run/'sprite-sheet-alpha.png').convert('RGBA')
    review_groups=groups if sys.argv[1]=='final' else [states[:7],states[7:]]
    for page,group in enumerate(review_groups):
        canvas=Image.new('RGB',(530,len(group)*214),(226,221,210))
        d=ImageDraw.Draw(canvas)
        for n,s in enumerate(group):
            d.text((8,n*214+6),s,fill=(35,32,39))
            for i,b in enumerate(mf['frame_layout']['rows'][s]):
                cell=sheet.crop((b['x'],b['y'],b['x']+b['w'],b['y']+b['h'])).resize((96,192),Image.Resampling.NEAREST)
                canvas.paste(cell,(130+i*98,n*214+20),cell)
        name='final' if sys.argv[1]=='final' else 'final-v2'
        canvas.save(qa/f'{name}-{page+1}.png')
        if page==0:
            contact=Image.new('RGB',(530,5*214+24),(226,221,210))
            contact.paste(canvas.crop((0,0,530,5*214)),(0,24))
            ImageDraw.Draw(contact).text((8,6),'granny-knit | idle + walk | 3x nearest',fill=(35,32,39))
            contact.save(root/'wave2-contact.png')
    print('Built 3x nearest final-cell review pages and wave2-contact.png')
