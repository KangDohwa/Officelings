"""A3 revision. Reads the immutable local snapshot; writes only this directory."""
from pathlib import Path
import copy
import hashlib
import json
import math
import numpy as np
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent
OLD = OUT / 'rev1-source'
FACINGS = ('se', 'sw', 'ne', 'nw')
TEAL = ('10282C', '235E5C', '20746C', '3C9F96', '5DB1A5')
METAL = ('191E1E', '262D38', '565958', '808985', '89918F')

def rgba(c):
    return (*bytes.fromhex(c), 255)

def read_doc(name):
    return json.loads((OLD / (name + '.json')).read_text(encoding='utf-8'))

def cell(atlas, c):
    x, y = c.get('column', 0)*128, c['row']*128
    return atlas.crop((x,y,x+128,y+128))

def put(atlas, c, image):
    atlas.paste(image, (c.get('column',0)*128,c['row']*128))

def check(image, desired):
    yy,xx=np.where(np.array(image)[:,:,3]>0)
    k=np.argmin((xx-desired[0])**2+(yy-desired[1])**2)
    return [dict(x=int(xx[k]),y=int(yy[k]),radius=0)]

def split_desk(doc):
    s=doc['sheets'][0]
    old=Image.open(OLD/s['file']).convert('RGBA')
    atlas=Image.new('RGBA',(old.width,old.height+128));atlas.paste(old,(0,0))
    evidence=[]
    for k,(f,n) in enumerate((('se','00'),('sw','02'))):
        c=next(c for c in s['rows'] if c['partId']==f'desk__front__{f}__{n}')
        a=np.array(cell(old,c));yy,xx=np.indices(a.shape[:2])
        if f=='se':
            top=yy<np.minimum(.5*xx+53,111-.5*xx)
            keyboard=(80<=xx)&(xx<=103)&(55<=yy)&(yy<=70)
            contact=(95,64)
        else:
            top=yy<np.minimum(.5*xx+46,127-.5*xx)
            keyboard=(48<=xx)&(xx<=82)&(73<=yy)&(yy<=85)
            contact=(63,80)
        select=(top|keyboard)&(a[:,:,3]>0)
        surface=a.copy();surface[~select]=0
        fascia=a.copy();fascia[select]=0
        assert not np.any((surface[:,:,3]>0)&(fascia[:,:,3]>0))
        assert np.array_equal(surface+fascia,a)
        nc=copy.deepcopy(c)
        nc.update(name='rear-14',partId=f'desk__rear__{f}__14',row=old.height//128,column=k,isoZOrder=-20)
        nc['anchorChecks']=check(Image.fromarray(surface),contact)
        nc['contactKind']='horizontal-desktop-keyboard'
        c['anchorChecks']=check(Image.fromarray(fascia),contact)
        c['contactKind']='physical-front-fascia'
        put(atlas,c,Image.fromarray(fascia));put(atlas,nc,Image.fromarray(surface));s['rows'].append(nc)
        for v in doc['furniture'][0]['variants']:
            if v['orientation']!=f:continue
            p=next(p for p in v['parts'] if p['partId']==c['partId'])
            npart=copy.deepcopy(p)
            npart.update(animation=nc['name'],partId=nc['partId'],isoZOrder=-20)
            v['parts'].insert(v['parts'].index(p),npart)
        evidence.append(dict(facing=f,front=c['partId'],surface=nc['partId'],surfacePixels=int(select.sum()),fasciaPixels=int((fascia[:,:,3]>0).sum()),disjoint=True,exactRGBAUnion=True))
    atlas.save(OUT/s['file'])
    doc['provenance']['A3rev1']='Two physical seat surfaces split at the fixed empty-object edge/keyboard boundary; anchors and ordinary ground depth preserved; seat rear -20 / fascia 10.'
    (OUT/'desk.iso.json').write_text(json.dumps(doc,indent=2)+'\n',encoding='utf-8')
    return evidence

def rotated(i,j,f):
    return {'se':(i,j),'sw':(-j,i),'ne':(j,-i),'nw':(-i,-j)}[f]

def project(i,j,z,f):
    i,j=rotated(i,j,f)
    return (math.floor(64+32*(i-j)+.5),math.floor(80+16*(i+j)-z+.5))

def roundpoly(points):
    # One texel bevel, authored in the face plane rather than a shifted source band.
    result=[]
    for n,p in enumerate(points):
        for q in (points[n-1],points[(n+1)%len(points)]):
            dx,dy=q[0]-p[0],q[1]-p[1];length=max(abs(dx),abs(dy),1)
            result.append((round(p[0]+dx/length),round(p[1]+dy/length)))
    return result

def box(bounds,f,ramp,rounded=False,seam=False):
    i0,j0,i1,j1,z0,z1=bounds
    ij=[rotated(i,j,f) for i,j in ((i0,j0),(i1,j0),(i1,j1),(i0,j1))]
    mi,mj=min(p[0] for p in ij),min(p[1] for p in ij)
    ma,mb=max(p[0] for p in ij),max(p[1] for p in ij)
    # Ground geometry rotates; the screen light does not.
    faces=[([(ma,mj,z0),(ma,mb,z0),(ma,mb,z1),(ma,mj,z1)],1),
           ([(mi,mb,z0),(ma,mb,z0),(ma,mb,z1),(mi,mb,z1)],2),
           ([(mi,mj,z1),(ma,mj,z1),(ma,mb,z1),(mi,mb,z1)],3)]
    out=Image.new('RGBA',(128,128));d=ImageDraw.Draw(out)
    for points,shade in faces:
        pts=[project(*p,'se') for p in points]
        pts=roundpoly(pts) if rounded else pts
        d.polygon(pts,fill=rgba(ramp[shade]),outline=rgba(ramp[0]))
        # Broad bevel under the upper rim; sparse piping, no noise/checker texture.
        if shade<3:
            p,q=project(*points[2],'se'),project(*points[3],'se')
            d.line([(p[0],p[1]+1),(q[0],q[1]+1)],fill=rgba(ramp[3]))
        if seam and shade<3:
            mid=tuple((points[0][k]+points[1][k])/2 for k in range(2))
            a,b=project(*mid,z0+2,'se'),project(*mid,z1-2,'se')
            if max(abs(a[0]-b[0]),abs(a[1]-b[1]))>=2:
                d.line([a,b],fill=rgba(ramp[1]))
        if shade==3:
            for edge in ((points[0],points[1]),(points[0],points[3])):
                d.line([project(*p,'se') for p in edge],fill=rgba(ramp[4]))
    return out

def reflected_mask(points,f):
    if f in ('sw','nw'):return [(128-x,y) for x,y in points]
    return points

def redraw_chair(f,original,rear_source,front_source):
    a=np.array(original);yy,xx=np.indices(a.shape[:2])
    back_points=([(39,48),(46,44),(60,39),(66,39),(70,44),(70,57),(47,70),(39,65)]
                 if f in ('se','sw') else [(39,48),(63,48),(80,59),(80,73),(69,79),(56,77),(39,71)])
    mask=Image.new('1',(128,128));ImageDraw.Draw(mask).polygon(reflected_mask(back_points,f),fill=1)
    teal=np.zeros((128,128),bool);metal=teal.copy()
    for c in TEAL+('0E1E1C','1D3A39','264F4B','338F88','4BAFA3','111924','122031'):
        teal|=np.all(a[:,:,:3]==tuple(bytes.fromhex(c)),axis=2)
    for c in METAL+('605E5C','898D7B','61282B'):
        metal|=np.all(a[:,:,:3]==tuple(bytes.fromhex(c)),axis=2)
    old_back=np.array(mask,dtype=bool)&(a[:,:,3]>0)
    arm_points=([(43,61),(67,75),(74,78),(73,68),(55,59)],[(65,50),(73,49),(90,58),(89,67),(66,60)]) if f in ('se','sw') else ([(58,47),(70,48),(72,63),(58,63)],[(68,55),(91,59),(91,74),(69,77)])
    arms=Image.new('1',(128,128));ad=ImageDraw.Draw(arms)
    for points in arm_points:ad.polygon(reflected_mask(points,f),fill=1)
    remove=old_back|(np.array(arms,dtype=bool)&(a[:,:,3]>0))
    retained=a.copy();retained[remove]=0
    rear=np.array(rear_source);front=np.array(front_source)
    rear[remove]=0;front[remove]=0
    # Reveal only previously hidden cushion texels on its actual z12 top plane.
    cushion=box((-.25,-.36,.35,.36,9,12),f,TEAL)
    ca=np.array(cushion);ca[(~remove)|(retained[:,:,3]>0)]=0
    base=Image.fromarray(rear);base.alpha_composite(Image.fromarray(ca));front=Image.fromarray(front)
    # Physical panel retains lower attachment z12; upper rim is rebuilt at z21.
    back=box((-.40,-.36,-.24,.36,12,21),f,TEAL,rounded=True,seam=True)
    # Compact low arms end at z13, above the unchanged z12 cushion.
    # The old tall generated supports must not survive as specks above the rim.
    arm=Image.new('RGBA',(128,128))
    for j in (-.37,.37):
        for i in (.00,.14):
            arm.alpha_composite(box((i-.025,j-.025,i+.025,j+.025,10,12),f,METAL))
        arm.alpha_composite(box((-.04,j-.035,.20,j+.035,12,13),f,METAL,rounded=True))
    if f in ('se','sw'):base.alpha_composite(back)
    else:front.alpha_composite(back)
    front.alpha_composite(arm)
    # Exclusive physical ownership, including the overlaps at arm/cushion joints.
    ra,fa=np.array(base),np.array(front);ra[fa[:,:,3]>0]=0
    # Frozen pelvis contact. This is the original cushion texel, never a debug mark.
    ra[68,64]=a[68,64];fa[68,64]=0
    assert tuple(ra[68,64])==tuple(a[68,64]) and ra[68,64,3]==255
    ra[ra[:,:,3]==0]=0;fa[fa[:,:,3]==0]=0
    return Image.fromarray(ra),Image.fromarray(fa)

def chairs(doc):
    s=doc['sheets'][0];old=Image.open(OLD/s['file']).convert('RGBA');atlas=old.copy()
    for f in FACINGS:
        rc=next(c for c in s['rows'] if c['direction']==f and c['name']=='rear-00')
        fc=next(c for c in s['rows'] if c['direction']==f and c['name']=='front-00')
        rear,front=cell(old,rc),cell(old,fc)
        original=rear.copy();original.alpha_composite(front)
        rear,front=redraw_chair(f,original,rear,front)
        for c,im in ((rc,rear),(fc,front)):
            desired=(c['anchorChecks'][0]['x'],c['anchorChecks'][0]['y'])
            c['anchorChecks']=check(im,desired);put(atlas,c,im)
        full=rear.copy();full.alpha_composite(front);full.save(OUT/f'chair-empty-{f}.png')
    atlas.save(OUT/s['file'])
    for v in doc['furniture'][0]['variants']:v['backHeight']=21
    doc['provenance']['A3rev1']='Backrest reauthored as rounded projected z12..21 panels; compact low arm caps/supports reauthored z12..13 / z10..12; fixed light, original cushion/base pixels outside physical masks retained. No upper-band shift.'
    (OUT/'chair.iso.json').write_text(json.dumps(doc,indent=2)+'\n',encoding='utf-8')

def verify(evidence):
    checks=[]
    hashes=json.loads((OLD/'hashes.json').read_text())
    untouched=[n for n in hashes if n not in ('desk.iso.json','desk-parts.png','chair.iso.json','chair-parts.png') and not n.startswith('chair-empty-')]
    for n in untouched:
        assert hashlib.sha256((OUT/n).read_bytes()).hexdigest()==hashes[n],n
    checks.append(dict(check='untouched screen/masks/desk-empty/shadows byte-identical',files=untouched))
    for family in ('desk','chair'):
        doc=json.loads((OUT/(family+'.iso.json')).read_text());old=read_doc(family)
        s=doc['sheets'][0];atlas=Image.open(OUT/s['file']).convert('RGBA');oa=Image.open(OLD/s['file']).convert('RGBA')
        for c0 in old['sheets'][0]['rows']:
            c=next(c for c in s['rows'] if c['partId']==c0['partId'])
            for k in ('partId','anchor','depthAnchor','isoZOrder','seatGroupId'):
                assert c.get(k)==c0.get(k),(c0['partId'],k)
            if family=='desk' and c['partId'] not in ('desk__front__se__00','desk__front__sw__02'):
                assert cell(atlas,c).tobytes()==cell(oa,c0).tobytes(),c['partId']
        checks.append(dict(check=family+' original part names/anchors/depth keys/ranks preserved'))
        for v0,v in zip(old['furniture'][0]['variants'],doc['furniture'][0]['variants']):
            for k in v0:
                if k!='parts':assert v[k]==v0[k],(family,k)
            images=[cell(atlas,next(c for c in s['rows'] if c['partId']==p['partId'])) for p in v['parts']]
            owner=np.zeros((128,128),int);full=Image.new('RGBA',(128,128))
            for im in images:owner+=np.array(im)[:,:,3]>0;full.alpha_composite(im)
            assert owner.max()==1,(family,v['orientation'],'overlap')
            if family=='desk':
                c=next(c for c in doc['sheets'][1]['rows'] if c['name']==v['screen']['animation'] and c['direction']==v['orientation'])
                full.alpha_composite(cell(Image.open(OUT/'desk-screen.png').convert('RGBA'),c))
            if v['state']=='default':
                assert full.tobytes()==Image.open(OUT/f'{family}-empty-{v["orientation"]}.png').convert('RGBA').tobytes(),(family,v['orientation'],'empty')
        checks.append(dict(check=family+' disjoint parts and exact empty reconstruction all orientations/states'))
    (OUT/'export-proof-A3-rev1.json').write_text(json.dumps(dict(surfaceSplit=evidence,checks=checks),indent=2)+'\n')
    print('PASS: exact source desk split, untouched atlas cells/screens, preserved contacts/metadata, disjoint reconstruction')

def empty_review():
    board=Image.new('RGBA',(1152,650),'#E7D1AC');d=ImageDraw.Draw(board)
    for j,f in enumerate(FACINGS):
        im=Image.open(OUT/f'chair-empty-{f}.png').convert('RGBA')
        board.alpha_composite(im,(j*288,25))
        board.alpha_composite(im.resize((384,384),Image.Resampling.NEAREST).crop((90,80,340,340)),(j*288,135))
        d.text((j*288+5,5),f+' revised 1x / 3x',fill='#191E1E')
    hero=Image.open(OUT.parents[2]/'iso-concept/iso-office-hero.png').convert('RGBA')
    for j,box in enumerate(((680,345,840,505),(830,345,990,505),(995,345,1155,505),(1220,598,1310,688))):
        board.alpha_composite(hero.crop(box),(j*288,440))
    board.save(OUT/'empty-review-A3-rev1.png')

if __name__=='__main__':
    evidence=split_desk(read_doc('desk'));chairs(read_doc('chair'));verify(evidence);empty_review()
