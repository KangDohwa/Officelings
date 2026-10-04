"""Final 38-row fixed-head bake from generated hanbok drawings."""
from hanbok_production import *
GRIPS={'se':(24,44),'sw':(8,44),'ne':(24,40),'nw':(8,40)}
EYES={'se':[(18,34),(22,33)],'sw':[(13,34),(9,33)]}
B=CHECK.BLINK_ROWS
def part(im,box):
    r=blank();r.paste(im.crop(box),box[:2]);return r
def line(im,pts,c,w=1):ImageDraw.Draw(im).line(pts,fill=c,width=w)
def rect(im,box,c):ImageDraw.Draw(im).rectangle(box,fill=c)
def mirror(im):
    r=shift(im.transpose(Image.Transpose.FLIP_LEFT_RIGHT),1)
    a=np.array(r);yy,xx=np.indices((64,32))
    for low,high in [('E57053','F9A17E'),('574A3F','AD8870')]:
        lo=tuple(bytes.fromhex(low));hi=tuple(bytes.fromhex(high))
        mask=np.all(a[:,:,:3]==lo,axis=2)&(xx<16);a[mask,:3]=hi
        mask=np.all(a[:,:,:3]==hi,axis=2)&(xx>=16);a[mask,:3]=lo
    return Image.fromarray(a)
def palm(im,pt,p=0,small=False):
    x,y=pt
    if small:
        rect(im,(x-1,y-1,x,y),SK);im.putpixel((x-1,y-1),SH);return
    # Two distinct palms plus a knuckle/thumb pixel, within the native hand corridor.
    rect(im,(x-1,y-1,x+1,y),SL);rect(im,(x-1,y-1,x,y),SK)
    im.putpixel((x-1,y-1),SH);im.putpixel((x+1,y-1 if p%2 else y),SK)
def arm(im,start,elbow,end,p=0):
    line(im,[start,elbow],O,5);line(im,[(x-1,y) for x,y in [start,elbow]],C('F07B5F'),3)
    line(im,[(start[0]-1,start[1]),(elbow[0]-1,elbow[1])],C('F9A17E'),1)
    line(im,[elbow,end],O,3);line(im,[elbow,end],SL,2);palm(im,end,p)
def finalize(im):
    a=np.array(im);a[:,0]=0;a[61:]=0;a[a[:,:,3]==0]=0;return Image.fromarray(a)
def prepare():
    global HEAD,IDLE,SEAT,RAW_WALK
    HEAD={};IDLE={};SEAT={};RAW_WALK={}
    for f in F:
        im=load(RUN/(f+'-idle-native.png'))
        # Gold pin and original 3/4 facial landmarks restored once, then frozen.
        rect(im,(11,22,18,23),O);line(im,[(11,22),(18,22)],C('ECA662'))
        im.putpixel((11,22),C('FFFFFF'));im.putpixel((18,23),C('B96537'))
        if f in EYES:
            for x,y in EYES[f]:
                rect(im,(x,y,x+1,y+2),C('100804'));im.putpixel((x,y),C('FFFFFF'))
                im.putpixel((x+1,y+2),C('835A47'))
            line(im,[(19,38),(20,38)] if f=='se' else [(11,38),(12,38)],C('CC684C'))
        h=part(im,(0,0,32,41));HEAD[f]=h;h.save(RUN/('head-'+f+'.png'))
        x,y=GRIPS[f]
        arm(im,(22,42) if f in ('se','ne') else (10,42),(24,43) if f in ('se','ne') else (8,43),(x,y))
        im.alpha_composite(h);IDLE[f]=finalize(im)
        # Preserve generated pelvis/thigh/calf/sneaker drawings at a fixed source grid.
        seat=load(RUN/(f+'-seat-native.png'));a=np.array(seat);a[:41]=0
        seat=Image.fromarray(a);seat.alpha_composite(shift(h,0,-1))
        # Pelvis continuity repair, true contact rather than a marker.
        rect(seat,(14,43,19,46),C('9F8372'));line(seat,[(14,44),(19,44)],C('AD8870'))
        rect(seat,(15,39,19,43),SK if f in ('se','sw') else C('F07B5F'))
        seat.alpha_composite(shift(h,0,-1));SEAT[f]=seat
    for f in ('se','ne'):
        RAW_WALK[f]=[load(RUN/(f+'-walk-'+str(p)+'-native.png')) for p in range(4)]
def seated(f,name,p):
    im=SEAT[f].copy();a=np.array(im)
    # Remove generated old arms only outside the locked head and pelvis corridor.
    a[41:48,:11]=0;a[41:48,23:]=0;im=Image.fromarray(a)
    if name=='seated-typing-front':
        # Hand contact ROI deliberately remains at frozen height in all four phases.
        # Palm width is three texels; fingertips press alternately inside this cluster.
        if f in ('se','sw'):
            pts=[((12,41),(18,46),(27,44)),((21,41),(25,43),(29,44))]
        else:
            pts=[((12,42),(27,38),(27,32)),((22,42),(30,36),(29,32))]
        if f in ('sw','nw'):pts=[tuple((32-x,y) for x,y in path) for path in pts]
        for q,(start,elbow,end) in enumerate(pts):
            elbow=(elbow[0],elbow[1]+([0,1,0,-1][p] if q==0 else [0,0,1,-1][p]))
            arm(im,start,elbow,end,p+q)
        # Head copied last preserves all hair and face bytes, including behind arms.
        im.alpha_composite(shift(HEAD[f],0,-1))
        for q,(x,y) in enumerate(CHECK.HANDS[f]):
            rect(im,(x-1 if q==0 else x,y-1,x if q==0 else x+1,y),SK)
            im.putpixel((x-1 if q==0 else x,y-1),SH)
            # Distinct keypress/knuckle pixels at or ABOVE the physical fascia.
            im.putpixel((x,y),SK if (p+q)%2 else SH)
            im.putpixel((x,y-1),[SK,SL,SH,SK][(p+q)%4])
        hx,hy=CHECK.HANDS[f][0];gapx=hx+1
        for gy in (hy-1,hy):im.putpixel((gapx,gy),O)
    else:
        pts=[((12,42),(13,44),(16,45)),((22,42),(23,43),[(23,44),(25,43),(25,41),(24,44)][p])]
        if f in ('sw','nw'):pts=[tuple((32-x,y) for x,y in path) for path in pts]
        for start,elbow,end in pts:arm(im,start,elbow,end,p)
        im.alpha_composite(shift(HEAD[f],0,-1))
    im.alpha_composite(shift(HEAD[f],0,-1))
    return finalize(im)
def walk(f,p):
    base='se' if f in ('se','sw') else 'ne';im=shift(RAW_WALK[base][p],[-2,-1,1,2][p]);a=np.array(im);a[:41]=0
    # Body torso bob 0,+1,0,-1. Legs retain their generated knee and ankle changes.
    bob=[0,1,0,-1][p];body=Image.fromarray(a)
    torso=part(body,(0,41,32,49));legs=part(body,(0,49,32,61))
    im=blank();im.alpha_composite(legs);im.alpha_composite(shift(torso,0,bob))
    a=np.array(im);a[41:49,:11]=0;a[41:49,23:]=0;im=Image.fromarray(a)
    rect(im,(13,43+bob,21,48+bob),C('9F8372'))
    # Retain generated coral front, collar and ribbon rather than replacing the drawing.
    im.alpha_composite(shift(part(body,(12,41,23,46)),0,bob))
    rect(im,(14,47+bob,19,50),C('9F8372'));line(im,[(14,48),(19,48)],C('AD8870'))
    if base=='se':
        ends=[[(15,45),(21,48)],[(13,46),(23,46)],[(8,45),(26,48)],[(9,43),(24,49)]][p]
    else:
        ends=[[(7,44),(20,48)],[(9,46),(22,47)],[(14,48),(26,44)],[(12,49),(24,43)]][p]
    for q,start in enumerate([(11,42+bob),(22,42+bob)]):
        end=ends[q];elbow=(round((start[0]+end[0])/2),round((start[1]+end[1])/2)+1)
        arm(im,start,elbow,end,p+q)
    if f in ('sw','nw'):im=mirror(im)
    im.alpha_composite(shift(HEAD[f],0,bob))
    # A sampled source sole can sit one texel above the common floor after shared scaling.
    a=np.array(im)
    if not a[60,4:29,3].any():
        ys,xs=np.where(a[52:61,:,3]>0)
        y=52+int(ys.max());xs=np.where(a[y,:,3]>0)[0]
        for x in xs:a[y+1:61,x]=a[y,x]
        im=Image.fromarray(a)
    return finalize(im),bob
PROP_PATH={
 'pickup':[(24,44),(27,48),(27,45),(24,44)],
 'report-handover':[(24,44),(27,43),(28,42),(24,44)],
 'coffee':[(24,44),(26,41),(27,37),(24,44)],
 'phone':[(24,44),(26,40),(27,36),(27,36)],
}
def gesture(f,name,p):
    im=IDLE[f].copy();a=np.array(im);a[41:49,:10]=0;a[40:49,24:]=0;im=Image.fromarray(a)
    right=f=='se'
    if name in PROP_PATH:
        end=PROP_PATH[name][p]
        if f=='sw':
            # Anatomical right hand remains the right arm: explicit SW landmarks.
            end=[(12,44),(7,40),(6,36),(6,36)][p] if name=='phone' else (32-end[0],end[1])
        start=(22,42) if right else (10,42)
    elif name=='talk':
        end=[(25,45),(27,41),(26,39),(24,43)][p];start=(22,42)
        if not right:end=(32-end[0],end[1]);start=(10,42)
    else:
        end=[(24,44),(27,40),(27,38),(25,41)][p];start=(22,42)
        if not right:end=(32-end[0],end[1]);start=(10,42)
    elbow=(round((start[0]+end[0])/2),max(42,round((start[1]+end[1])/2))+1)
    arm(im,start,elbow,end,p)
    im.alpha_composite(HEAD[f]);palm(im,end,p,True)
    # Mask restoration is final even when a gesture passes close to the jaw.
    im.alpha_composite(HEAD[f])
    return finalize(im),end
def transition(f,p):
    if p==0:return IDLE[f].copy(),0
    if p==3:return seated(f,'seated-thinking',0),-1
    seat=seated(f,'seated-thinking',0);idle=IDLE[f]
    # Drawing-derived thigh bends; the source contact schedule remains exact.
    im=blank()
    if p==1:
        im.alpha_composite(part(idle,(0,41,32,61)))
        im.alpha_composite(shift(part(seat,(0,43,32,51)),0,2))
    else:
        im.alpha_composite(shift(part(seat,(0,41,32,57)),0,2))
    rect(im,(14,40 if p==1 else 43,19,47),C('AD8870'))
    rect(im,(15,39,19,43),SK if f in ('se','sw') else C('F07B5F'))
    im.alpha_composite(shift(HEAD[f],0,-1))
    return finalize(im),-1
def build():
    prepare()
    template=read(ROOT/'assets/art/iso/characters/dev-hoodie-v2/dev-hoodie.iso.json')
    doc=json.loads(json.dumps(template).replace('dev-hoodie-v2','hanbok-modern').replace('dev-hoodie','hanbok-modern'))
    normal,blink=doc['sheets'];normal['id']=CID+'-normal';normal['file']=CID+'-iso.png'
    blink['id']=CID+'-blink';blink['file']=CID+'-iso-blink.png';normal['blink']['sheetId']=blink['id']
    doc['characters'][0]['id']='iso/character/'+CID;doc['characters'][0]['sheetId']=normal['id']
    identity=[h for h in PALETTE_HEX if '#'+h in read(RUN/'flat-palette.json')]
    for s in doc['sheets']:s['identityPalette']=['#'+h for h in identity]
    normal['headOffsetsByClip']={};atlas=Image.new('RGBA',(128,2432));headchecks=0
    grips={}
    for r,(name,f,fps,loop) in enumerate(CHECK.CONTRACT):
        clip=normal['rows'][r]
        for key in ('mirrorOf','handRepair','motionProvenance','sourceMaster'):clip.pop(key,None)
        offsets=[]
        for p in range(4):
            hy=0
            if name=='idle':im=IDLE[f].copy()
            elif name=='walk':im,hy=walk(f,p)
            elif name in ('seated-thinking','seated-typing-front'):
                im=seated(f,name,p);hy=-1
            elif name=='sleep':
                im=seated(f,'seated-thinking',0);hy=-1
                # Authored back recline derivation of generated bent legs, no sofa pixels.
                lower=part(im,(0,47,32,57));a=np.array(im);a[47:]=0;im=Image.fromarray(a)
                im.alpha_composite(shift(lower,1 if f=='ne' else -1,-1));im=finalize(im)
            elif name in ('sit-down','stand-up'):im,hy=transition(f,p if name=='sit-down' else 3-p)
            else:
                im,end=gesture(f,name,p);grips.setdefault(name+'/'+f,[]).append([{'x':end[0],'y':end[1]}])
            atlas.paste(im,(p*32,r*64));offsets.append({'x':0,'y':hy})
            h=shift(HEAD[f],0,hy);mask=np.array(h)[:,:,3]>0
            assert np.array_equal(np.array(im)[mask],np.array(h)[mask]),(name,f,p,'head changed')
            headchecks+=1
        normal['headOffsetsByClip'][name+'/'+f]=offsets
        clip['sourceMaster']='runs/'+('walk' if name=='walk' else 'seat' if name in ('seated-thinking','seated-typing-front','sleep') else 'idle')+'-master-a1.png'
        if name in PROP_PATH:clip['handAnchorsByPhase']=grips[name+'/'+f]
        if name=='walk':
            clip['gaitPhases']=['contact','down','passing','up'];clip['torsoBobPx']=[0,1,0,-1];clip['fixedDrawingBodyRegistrationPx']=[-2,-1,1,2]
            if f in ('sw','nw'):clip['bodyDerivation']='anchor-preserving body-only x=32-x and upper-left relight; independently drawn '+f+' canonical head'
    atlas.save(OUT/(CID+'-iso.png'))
    doc['production']={
        'method':'built-in image generation, three serial key-pose sheets (4 turnaround, 8 gait, 4 seated drawings); one attempt each; exact palette and binary alpha; fixed parts derivation',
        'sourceIdentity':'assets/art/roster/hanbok-modern/hanbok-modern.png',
        'sourceReferences':'assets/art/roster/hanbok-modern/README.md and base.png',
        'stage':'final','standingReferenceHeight':42,'nativeCell':[32,64],'footAnchor':[16,60],
        'seatProfiles':['front'],'rejectedSeatProfiles':['end'],'light':'upper-left',
        'headOffsets':'idle/actions 0; walk 0,+1,0,-1; seated -1; transitions 0,-1,-1,-1; canonical RGBA exact',
        'headProof':'runs/derivation-proof.json','blinkMethod':'hanbok_blink_resync.py: final normal cell copy, eye-only deterministic replacement',
        'visualAcceptance':'static reviews complete; independent approval and runtime playback unverified'
    }
    for fn in (CID+'.iso.json',CID+'-iso.manifest.json'):savejson(OUT/fn,doc)
    savejson(RUN/'derivation-proof.json',dict(canonicalHeadCellsExact=headchecks,normalCells=152,generatedSheets=3,keyPoseDrawings=16,attemptsPerSheet=[1,1,1],headHeights={f:HEAD[f].getbbox()[3]-HEAD[f].getbbox()[1] for f in F},masterSHA256={n:sha(RUN/(n+'-master-a1.png')) for n in ('idle','walk','seat')}))
    print('Baked 152 cells; canonical heads exact:',headchecks)
if __name__=='__main__':build()

