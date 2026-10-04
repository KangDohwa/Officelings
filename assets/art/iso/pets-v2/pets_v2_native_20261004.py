"""New lane helper. All writes are confined to this directory. No generation/API calls."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import numpy as np
import json, hashlib, sys, math

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[3]
FACINGS = ('se', 'sw', 'ne', 'nw')
NEAREST = Image.Resampling.NEAREST
FLATS = {'dog': ROOT/'assets/art/pets/shiba/shiba.png',
         'hamster': ROOT/'assets/art/pets/hamster-ball/hamster-ball.png'}
COLORS = {'dog': ['#191E1E','#724029','#B58043','#ECA662','#FAECC9','#E7D1AC','#BF6554'],
          'hamster': ['#191E1E','#9A5C3A','#CE894F','#ECA662','#F0BA39','#FAECC9','#E7D1AC','#BF6554']}

def rgb(c): return tuple(int(c[i:i+2],16) for i in (1,3,5))
def rgba(c): return (*rgb(c),255)
def write_json(name,data): (OUT/name).write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def image(a): return Image.fromarray(a.astype(np.uint8))

def largest(mask):
    seen=np.zeros(mask.shape,bool); best=[]
    for sy,sx in zip(*np.where(mask)):
        if seen[sy,sx]: continue
        stack=[(int(sy),int(sx))]; seen[sy,sx]=True; comp=[]
        while stack:
            y,x=stack.pop(); comp.append((y,x))
            for dy,dx in ((0,1),(0,-1),(1,0),(-1,0),(-1,-1),(-1,1),(1,-1),(1,1)):
                yy,xx=y+dy,x+dx
                if 0<=yy<mask.shape[0] and 0<=xx<mask.shape[1] and mask[yy,xx] and not seen[yy,xx]:
                    seen[yy,xx]=True;stack.append((yy,xx))
        if len(comp)>len(best): best=comp
    result=np.zeros(mask.shape,bool)
    if best:
        y,x=zip(*best);result[y,x]=True
    return result

def quantize(im,pet):
    a=np.array(im);p=np.array([rgb(c) for c in COLORS[pet]],dtype=np.int32)
    idx=np.argmin(np.sum((a[:,:,:3].astype(np.int32)[:,:,None,:]-p[None,None,:,:])**2,axis=3),axis=2)
    a[:,:,:3]=p[idx];mask=largest(a[:,:,3]>=128);a[:,:,3]=np.where(mask,255,0);a[~mask]=0
    return a

def extract(pet):
    master=Image.open(OUT/f'{pet}-master.png').convert('RGBA');w,h=master.size
    frames=[];records=[]
    for row in range(3):
        line=[]
        for phase in range(4):
            box=(round(w*phase/4),round(h*row/3),round(w*(phase+1)/4),round(h*(row+1)/3))
            a=np.array(master.crop(box));mask=largest(a[:,:,3]>=192)
            yy,xx=np.where(mask);assert len(yy)>100,'master component missing'
            crop=(int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1))
            a[~mask]=0;piece=image(a).crop(crop)
            targeth=(17 if pet=='dog' else 15) if row<2 else (11 if pet=='dog' else 10)
            targetw=min(24,round(piece.width*targeth/piece.height))
            native=quantize(piece.resize((targetw,targeth),Image.Resampling.BOX),pet)
            canvas=np.zeros((32,32,4),np.uint8);left=16-targetw//2;top=29-targeth
            canvas[top:29,left:left+targetw]=native
            line.append(canvas)
            records.append({'row':row,'phase':phase,'tileBox':box,'componentBoxInTile':crop,'targetSize':[targetw,targeth]})
        frames.append(line)
    write_json(f'{pet}-extraction.json',records)
    return frames

def preview_extraction(pet,frames):
    board=Image.new('RGBA',(4*160,3*160),'#E7D1AC')
    for r,line in enumerate(frames):
        for p,a in enumerate(line): board.alpha_composite(image(a).resize((160,160),NEAREST),(p*160,r*160))
    board.save(OUT/f'{pet}-native-master-review.png')

def light(a,pet):
    """Repaint fur after mirroring, keeping screen-left upper surfaces light."""
    a=a.copy();yy,xx=np.indices((32,32));mask=a[:,:,3]>0
    if pet=='dog':
        fur=np.isin(a[:,:,0],(181,236)) & mask
        a[fur & ((xx<=17)|(yy<=19))]=rgba('#ECA662')
        a[fur & (xx>=19)&(yy>=20)]=rgba('#B58043')
    else:
        fur=((a[:,:,:3]==rgb('#CE894F')).all(2)|(a[:,:,:3]==rgb('#F0BA39')).all(2)) & mask
        a[fur & ((xx<=15)&(yy<=21))]=rgba('#F0BA39')
        a[fur & (xx>=19)&(yy>=23)]=rgba('#CE894F')
    return a

def mirrored(a):
    # Frozen x'=32-x, with a transparent x=0 guard. No runtime flip.
    b=np.zeros_like(a);b[:,1:]=a[:,31:0:-1];return b

def body_from_master(a,pet,facing):
    b=a.copy();b[25:]=0
    if facing=='se': b[23:, :14]=0
    if pet=='dog' and facing=='se':
        # Restore the generated eyes/nose and cream cheek as readable native clusters.
        for x,y in ((18,23),(19,23),(20,23),(19,24),(20,24)):
            if b[y,x,3]: b[y,x]=rgba('#FAECC9')
        for x,y in ((19,22),(22,22),(22,23)): b[y,x]=rgba('#191E1E')
    return b

def translate(a,dy=0):
    b=np.zeros_like(a)
    if dy<0:b[:dy]=a[-dy:]
    elif dy>0:b[dy:]=a[:-dy]
    else:b=a.copy()
    return b

def legs(pet,facing,phase,bob):
    """Two diagonal pairs: extension, passing, opposite extension, passing.

    Root-relative screen stride has dx:dy = 2:1; y28 remains a planted sole.
    Endpoints encode the short trot/scurry rather than shifting a whole bitmap.
    """
    if facing=='se':
        data=[('far-rear',(11,22),[(13,25),(11,25),(9,23),(11,23)],True),
              ('far-front',(21,24),[(19,26),(21,26),(22,28),(21,28)],False),
              ('near-rear',(9,22),[(7,23),(9,23),(11,25),(9,25)],False),
              ('near-front',(18,24),[(20,28),(19,28),(16,26),(18,26)],True)]
    else:
        data=[('far-front',(20,20),[(18,21),(20,21),(22,23),(20,23)],False),
              ('far-rear',(14,24),[(15,28),(14,28),(12,26),(14,26)],True),
              ('near-front',(23,21),[(25,24),(23,24),(21,22),(23,22)],True),
              ('near-rear',(11,24),[(9,26),(11,26),(13,28),(12,28)],False)]
    layer=Image.new('RGBA',(32,32));d=ImageDraw.Draw(layer);records=[]
    dark=rgba('#724029' if pet=='dog' else '#9A5C3A')
    body=rgba('#ECA662' if pet=='dog' else '#CE894F')
    cream=rgba('#FAECC9');shade=rgba('#E7D1AC')
    for name,root,endpoints,pair_a in data:
        toe=endpoints[phase];hip=(root[0],root[1]+bob)
        planted=(phase in (0,1)) if pair_a else (phase in (2,3))
        # A bent knee in swing; it changes independently of body bob.
        knee=((hip[0]+toe[0])//2,hip[1]+(1 if planted else 0))
        d.line([hip,knee,toe],fill=dark,width=2)
        d.line([hip,knee,(toe[0],toe[1]-1)],fill=body,width=1)
        d.line([(toe[0],toe[1]),(toe[0]+1,toe[1])],fill=cream if name.startswith('near') else shade,width=1)
        records.append({'leg':name,'pair':'A' if pair_a else 'B','hip':hip,'knee':knee,
                        'toe':toe,'planted':planted})
    return np.array(layer),records

def overlay(base,top):
    a=base.copy();mask=top[:,:,3]>0;a[mask]=top[mask];return a

def wag(a,pet,facing,phase):
    if pet!='dog':return a
    # Move only the generated curled-tail component, preserving its fur swirl.
    a=a.copy();box=(6,11,14,18) if facing=='se' else (7,17,14,23)
    x0,y0,x1,y1=box;part=a[y0:y1,x0:x1].copy();a[y0:y1,x0:x1]=0
    dx=(0,1,0,-1)[phase];a[y0:y1,x0+dx:x1+dx]=part
    return a

def motion(pet,frames):
    rows=[];motion_records=[]
    for state in ('idle','walk'):
        for facing in FACINGS:
            source_facing='se' if facing in ('se','sw') else 'ne'
            source=frames[0 if source_facing=='se' else 1][0]
            body=body_from_master(source,pet,source_facing)
            line=[];phase_records=[]
            for phase in range(4):
                bob=(0,-1,0,-1)[phase] if state=='walk' else 0
                limbs,record=legs(pet,source_facing,phase if state=='walk' else 1,bob)
                a=overlay(limbs,translate(body,bob))
                a=wag(a,pet,source_facing,phase)
                if facing in ('sw','nw'):
                    a=mirrored(a)
                    for r in record:
                        for key in ('hip','knee','toe'):r[key]=(32-r[key][0],r[key][1])
                a=light(a,pet);a[29:]=0;a[:,0]=0;a[:,31]=0
                # Remove resampling islands; the whole body remains one component.
                mask=largest(a[:,:,3]>0);a[~mask]=0
                line.append(a);phase_records.append({'phase':phase,'bodyBobY':bob,'legs':record})
            rows.append(line)
            if state=='walk':motion_records.append({'direction':facing,'phases':phase_records})
    sleep=frames[2][0].copy();line=[]
    for phase in range(4):
        a=sleep.copy()
        if phase in (1,2):
            # Lift only the upper back on inhalation. Paws/chin stay grounded.
            mask=(a[:,:,3]>0);back=a[18:23,:17].copy();a[18:23,:17]=0
            a[17:22,:17]=back
        a=light(a,pet);m=largest(a[:,:,3]>0);a[~m]=0;line.append(a)
    rows.append(line)
    write_json(f'{pet}-gait-contacts.json',{'phaseOrder':['A-extension','A-passing','B-extension','B-passing'],
        'walkFps':8,'screenAxis':[2,1],'rows':motion_records,
        'limits':'Authored root/toe schedule, image continuity and cyclic replay. No runtime movement/stride velocity claim.'})
    return rows

def atlas(rows):
    return np.concatenate([np.concatenate(line,axis=1) for line in rows],axis=0)

def shadow_sheet(pet,rows):
    line=[]
    for row in range(9):
        phases=[]
        for phase in range(4):
            a=np.zeros((128,128,4),np.uint8);yy,xx=np.indices((128,128))
            wide=9 if pet=='dog' else 8
            # All ground-outline diagonals have horizontal:vertical = 2:1.
            cast=(np.abs(xx-70)+2*np.abs(yy-83))<=wide+2
            contact=(np.abs(xx-64)+2*np.abs(yy-80))<=wide
            a[cast]=(*rgb('#111924'),48);a[contact]=(*rgb('#111924'),96)
            phases.append(a)
        line.append(phases)
    return atlas(line)

def manifest(pet,rows):
    m=json.loads((ROOT/'assets/art/iso/pets/cat.iso.json').read_text(encoding='utf-8'))
    for sheet in m['sheets']:
        sheet['id']=sheet['id'].replace('cat',pet);sheet['file']=sheet['file'].replace('cat',pet)
        for clip in sheet['rows']:
            clip.pop('mirrorOf',None)
            if sheet['kind']=='shadow':continue
            cells=rows[clip['row']]
            # Choose a physical y28 sole within one texel in EVERY phase.
            candidates=[]
            for x in range(2,30):
                if all(c[28,max(0,x-1):min(32,x+2),3].any() for c in cells):candidates.append(x)
            assert candidates, (pet,clip['row'],'no common physical sole')
            x=min(candidates,key=lambda v:abs(v-16))
            clip['anchorChecks']=[{'x':x,'y':28,'radius':1}]
            clip['contactKind']='curled-ground-contact' if clip['row']==8 else 'grounded-paw'
            if clip['direction'] in ('sw','nw'):
                clip['mirrorOf']={'direction':'se' if clip['direction']=='sw' else 'ne',
                    'operation':'xPrime=32-x','integerCorrectionX':1,'lightRepaint':'fixed screen-upper-left'}
        if sheet['kind']=='pet':sheet['identityPalette']=COLORS[pet]
    p=m['pets'][0];p['id']='iso/'+pet;p['name']='Office Shiba' if pet=='dog' else 'Office golden hamster'
    p['sheetId']='iso-'+pet+'-normal';p['shadow']['sheetId']='iso-'+pet+'-shadow'
    m['provenance']={'identity':str(FLATS[pet].relative_to(ROOT)).replace('\\','/'),
        'generation':'1 sequential built-in image_gen master, attempt 1, 4x3 transparent poses',
        'master':pet+'-master.png','extraction':pet+'-extraction.json',
        'derivation':'Native grid quantization; canonical generated head/torso; authored bent-leg diagonal-pair gait, bob 0/-1/0/-1; 2:1 ground vectors; reflected SW/NW with fixed screen-upper-left repaint; curled sleep upper-back breathing.',
        'identityDifference':None if pet=='dog' else 'Same golden/cream hamster; glass ball omitted to expose requested walking paws.',
        'runtimeVerified':False}
    write_json(pet+'.iso.json',m)

def checker_extra(pet,rows):
    source=np.array(Image.open(FLATS[pet]).convert('RGBA'))
    sourcecolors={tuple(v) for v in source[:,:,:3][source[:,:,3]>0]}
    assert all(rgb(c) in sourcecolors for c in COLORS[pet]),'identity palette not from flat animal'
    checks=[]
    for row,line in enumerate(rows):
        for phase,a in enumerate(line):
            mask=a[:,:,3]>0;yy,xx=np.where(mask)
            assert mask[28].any() and not mask[29:].any(),(pet,row,phase,'ground/padding')
            assert 0<xx.min() and xx.max()<31
            assert np.array_equal(largest(mask),mask),(pet,row,phase,'island')
            if row<8:assert 14<=yy.max()-yy.min()+1<=18,(pet,row,phase,'height',yy.max()-yy.min()+1)
        distinct=len({a.tobytes() for a in line})
        deltas=[int(np.any(line[p]!=line[(p+1)%4],axis=2).sum()) for p in range(4)]
        legdeltas=[int(np.any(line[p][24:]!=line[(p+1)%4][24:],axis=2).sum()) for p in range(4)]
        if 4<=row<=7:
            assert distinct==4 and min(legdeltas)>0,(pet,row,'distinct leg phases')
            assert deltas[3]<=max(deltas[:3])*1.4,(pet,row,'3->0 jump',deltas)
        checks.append({'row':row,'distinctFrames':distinct,'cyclicPixelDeltas':deltas,
            'legRoiCyclicDeltas':legdeltas,'grounded':True,'connected':True})
    write_json(pet+'-additional-checks.json',{'sourceIdentityColorsExact':True,'rows':checks,
        'scope':'Pixel geometry, palette provenance, lower-leg phase changes, cyclic image continuity. Does not certify live renderer playback.'})
    gait=json.loads((OUT/f'{pet}-gait-contacts.json').read_text(encoding='utf-8'))
    grounded=[]
    for n,record in enumerate(gait['rows']):
        for phase in record['phases']:
            a=rows[4+n][phase['phase']]
            contact=[l for l in phase['legs'] if l['planted'] and l['toe'][1]==28]
            assert contact,'no scheduled grounded leg'
            for l in contact:
                x,y=l['toe'];assert a[y,x,3]==255,(pet,record['direction'],phase['phase'],'sole absent')
            grounded.append({'facing':record['direction'],'phase':phase['phase'],
                'plantedGroundSoles':[l['toe'] for l in contact],'bodyBobY':phase['bodyBobY']})
    write_json(pet+'-contact-proof.json',{'all16WalkPhasesGrounded':True,'phases':grounded})

def reviews(pet,rows,shadows):
    a=image(atlas(rows));cat=Image.open(ROOT/'assets/art/iso/pets/cat-normal.png').convert('RGBA')
    for factor in (1,3):
        width=520 if factor==1 else 1390;height=330 if factor==1 else 910
        board=Image.new('RGBA',(width,height),'#E7D1AC');d=ImageDraw.Draw(board)
        d.text((8,6),f'{pet.upper()} / CAT / {factor}x native nearest / rows 0-8',fill='#111924')
        board.alpha_composite(a.resize((128*factor,288*factor),NEAREST),(12,30))
        board.alpha_composite(cat.resize((128*factor,288*factor),NEAREST),(24+128*factor,30))
        flat=Image.open(FLATS[pet]).convert('RGBA').crop((0,0,128,32))
        board.alpha_composite(flat.resize((128*factor,32*factor),NEAREST),(36+256*factor,50))
        d.text((36+256*factor,30),'FLAT IDENTITY',fill='#111924')
        board.save(OUT/f'{pet}-review-{factor}x.png')
    combined=Image.new('RGBA',(1050,910),'#E7D1AC')
    combined.alpha_composite(Image.open(OUT/f'{pet}-review-1x.png'),(0,0))
    combined.alpha_composite(Image.open(OUT/f'{pet}-review-3x.png').crop((0,0,800,910)),(250,0))
    combined.save(OUT/f'{pet}-review-1x3x.png')
    # Actual hero pixels remain intact; sample pets are placed in a separate margin.
    hero=Image.open(ROOT/'assets/art/iso-concept/iso-office-hero.png').convert('RGBA')
    review=Image.new('RGBA',(1000,820),'#282B3D');review.alpha_composite(hero.resize((960,540),NEAREST),(20,20))
    d=ImageDraw.Draw(review);d.text((20,575),'HERO CONTEXT ABOVE / NATIVE 1x + NEAREST 3x / CAT, SHIBA, HAMSTER',fill='#F2EBD3')
    for n,name in enumerate(('cat','dog','hamster')):
        file=ROOT/'assets/art/iso/pets/cat-normal.png' if name=='cat' else OUT/f'{name}-normal.png'
        if not file.exists():continue
        png=Image.open(file).convert('RGBA');x=80+n*290
        d.text((x,605),name.upper(),fill='#F2EBD3')
        for p in range(4):
            c=png.crop((32*p,128,32*p+32,160));review.alpha_composite(c,(x+32*p,628))
            review.alpha_composite(c.resize((96,96),NEAREST),(x+64*p,681))
    review.save(OUT/f'{pet}-vs-identity-1x3x.png')
    # Four-second replay: walk8fps, idle/sleep1fps, including repeated 3->0.
    gifs=[]
    for tick in range(32):
        frame=Image.new('RGBA',(760,380),'#E7D1AC');d=ImageDraw.Draw(frame)
        d.text((8,8),pet.upper()+' / idle 1fps / trot 8fps / sleep 1fps',fill='#111924')
        for r in range(9):
            p=tick%4 if 4<=r<8 else (tick//8)%4
            x=20+(r%4)*180;y=45+(r//4)*110
            c=image(rows[r][p]);s=image(shadows[r*128:(r+1)*128,p*128:(p+1)*128])
            block=Image.new('RGBA',(128,128));block.alpha_composite(s)
            block.alpha_composite(c,(48,52));crop=block.crop((44,48,84,88)).resize((120,120),NEAREST)
            frame.alpha_composite(crop,(x,y));d.text((x,y),f'r{r} p{p}',fill='#111924')
        gifs.append(frame.convert('RGB'))
    gifs[0].save(OUT/f'{pet}-motion-3x.gif',save_all=True,append_images=gifs[1:],duration=[120,130]*16,loop=0,disposal=2)

def fixture(pet):
    furn=ROOT/'assets/art/iso/furniture-v2/workstations'
    desk=json.loads((furn/'desk.iso.json').read_text(encoding='utf-8'))
    chair=json.loads((furn/'chair.iso.json').read_text(encoding='utf-8'))
    png=Image.open(OUT/f'{pet}-normal.png');sh=Image.open(OUT/f'{pet}-shadow.png')
    def project(i,j):return round(32*(i-j)),round(16*(i+j))
    def parts(m,facing,origin,offset=(0,0)):
        result=[]
        for s in m['sheets']:
            if s['kind']!='furniture':continue
            im=Image.open(furn/s['file']).convert('RGBA');w,h=s['cellW'],s['cellH']
            for c in s['rows']:
                if c['direction']!=facing:continue
                a=c.get('anchor',s['anchor']);x,y=project(*offset);da=c['depthAnchor']
                cell=im.crop((c.get('column',0)*w,c['row']*h,(c.get('column',0)+1)*w,(c['row']+1)*h))
                result.append((sum(offset)+da['i']+da['j'],c['isoZOrder'],c['partId'],cell,(origin[0]+x-a['x'],origin[1]+y-a['y'])))
        return result
    board=Image.new('RGBA',(480,360),'#282B3D')
    for num,f in enumerate(FACINGS):
        panel=Image.new('RGBA',(240,180),'#282B3D');origin=(120,75);d=ImageDraw.Draw(panel)
        for i in range(-1,2):
            for j in range(-1,2):
                x,y=project(i,j);cx,cy=origin[0]+x,origin[1]+y
                d.polygon([(cx,cy-16),(cx+32,cy),(cx,cy+16),(cx-32,cy)],fill='#B96537',outline='#804A2F')
        v=next(v for v in desk['furniture'][0]['variants'] if v['orientation']==f)
        app=v['seatProfiles'][0]['approach'];seat=(app['x'],app['y'])
        items=parts(desk,f,origin)+parts(chair,v['linkedChair']['orientation'],origin,seat)
        for name,i,j,r in [('rear',-.55,.25,4+num),('front',1.35,1.25,4+num),('sleep',1.35,-.55,8)]:
            x,y=project(i,j);cell=png.crop((0,r*32,32,r*32+32));shadow=sh.crop((0,r*128,128,r*128+128))
            panel.alpha_composite(shadow,(origin[0]+x-64,origin[1]+y-80))
            items.append((i+j,0,pet+'-'+name,cell,(origin[0]+x-16,origin[1]+y-28)))
        for _,_,_,im,pos in sorted(items,key=lambda v:v[:3]):panel.alpha_composite(im,pos)
        d=ImageDraw.Draw(panel);d.text((5,5),f'{f.upper()} / {pet} own i+j',fill='#F2EBD3')
        d.text((5,160),'static parts fixture / no runtime',fill='#F2EBD3')
        board.alpha_composite(panel,((num%2)*240,(num//2)*180))
    board.save(OUT/f'{pet}-workstation-fixture.png')
    review=Image.new('RGBA',(1220,580),'#282B3D');review.alpha_composite(board,(8,25))
    review.alpha_composite(board.crop((0,0,240,180)).resize((720,540),NEAREST),(500,25))
    d=ImageDraw.Draw(review);d.text((8,6),'1x / FOUR DIRECTIONS',fill='#F2EBD3');d.text((500,6),'3x / SE DETAIL',fill='#F2EBD3')
    review.save(OUT/f'{pet}-workstation-review-1x3x.png')

if __name__=='__main__':
    pets=sys.argv[1:] or ['dog','hamster']
    for pet in pets:
        frames=extract(pet);preview_extraction(pet,frames);rows=motion(pet,frames)
        image(atlas(rows)).save(OUT/f'{pet}-normal.png');shadows=shadow_sheet(pet,rows)
        image(shadows).save(OUT/f'{pet}-shadow.png');manifest(pet,rows);checker_extra(pet,rows)
        print(pet,'native atlas, contacts, loop checks passed')
    for pet in pets:
        a=np.array(Image.open(OUT/f'{pet}-normal.png'));rows=[[a[r*32:(r+1)*32,p*32:(p+1)*32] for p in range(4)] for r in range(9)]
        reviews(pet,rows,np.array(Image.open(OUT/f'{pet}-shadow.png')));fixture(pet)
