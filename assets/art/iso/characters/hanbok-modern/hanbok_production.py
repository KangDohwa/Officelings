"""Generated drawing normalization and fixed-parts production; lane writes only."""
from hanbok_lane import *
import copy, subprocess
PALETTE_HEX=['100804','25160F','432F26','623A2A','835A47','C9896A','FBD3A8','FBDDBC','CC684C','E57053','F07B5F','F9A17E','574A3F','9F8372','AD8870','B9957C','EADCCA','FFFFFF','B96537','ECA662']
PAL=np.array([tuple(bytes.fromhex(s)) for s in PALETTE_HEX],dtype=np.int16)
C=lambda s:tuple(bytes.fromhex(s))+(255,)
O=C('25160F');SK=C('FBD3A8');SL=C('C9896A');SH=C('FBDDBC')
def snap(im, outline=False):
    a=np.array(im);mask=a[:,:,3]>=160
    colors=a[:,:,:3].astype(np.int16)
    nearest=((colors[:,:,None,:]-PAL[None,None,:,:]).astype(np.int32)**2).sum(axis=3).argmin(axis=2)
    a[:,:,:3]=PAL[nearest];a[:,:,3]=mask.astype(np.uint8)*255;a[~mask]=0
    if outline:
        inner=mask.copy()
        for dy,dx in [(0,1),(0,-1),(1,0),(-1,0)]:
            m=np.zeros_like(mask);ys=slice(max(0,dy),min(64,64+dy));xs=slice(max(0,dx),min(32,32+dx))
            m[ys,xs]=mask[slice(max(0,-dy),min(64,64-dy)),slice(max(0,-dx),min(32,32-dx))]
            inner &= m
        # Keep gold at the boundary; a one-native-pixel edge repairs lost dark source ink.
        boundary=mask & ~inner
        bright_gold=np.all(a[:,:,:3]==tuple(bytes.fromhex('ECA662')),axis=2)
        a[boundary & ~bright_gold,:3]=tuple(bytes.fromhex('25160F'))
    a[:,0]=0;a[61:]=0
    return Image.fromarray(a)
def components(mask):
    seen=np.zeros_like(mask);groups=[]
    for y,x in zip(*np.where(mask)):
        if seen[y,x]:continue
        stack=[(y,x)];seen[y,x]=1;group=[]
        while stack:
            yy,xx=stack.pop();group.append((yy,xx))
            for dy in (-1,0,1):
                for dx in (-1,0,1):
                    ay,ax=yy+dy,xx+dx
                    if 0<=ay<mask.shape[0] and 0<=ax<mask.shape[1] and mask[ay,ax] and not seen[ay,ax]:
                        seen[ay,ax]=1;stack.append((ay,ax))
        groups.append(group)
    return sorted(groups,key=len,reverse=True)
def cutgrid(name,cols,rows):
    im=load(RUN/(name+'-master-a1.png'));a=np.array(im);result=[]
    for r in range(rows):
        for c in range(cols):
            box=(round(c*im.width/cols),round(r*im.height/rows),round((c+1)*im.width/cols),round((r+1)*im.height/rows))
            tile=im.crop(box);ta=np.array(tile);mask=ta[:,:,3]>=160;groups=components(mask)
            # Connected figures, not fixed-box opaque/background assumptions.
            main=np.zeros_like(mask)
            for y,x in groups[0]:main[y,x]=1
            ta[~main]=0;tile=Image.fromarray(ta)
            result.append(dict(tile=tile,box=tile.getbbox(),sourceBox=box))
    return result
def normalize():
    proofs={}
    for name,target,ground in [('idle',42,60),('seat',39,56)]:
        src=cutgrid(name,2,2);scale=target/max(s['box'][3]-s['box'][1] for s in src)
        proofs[name]=dict(scale=scale,ground=ground,normalization='one shared isotropic scale for four authored facings; fixed native master thereafter',sourceBoxes=[s['sourceBox'] for s in src],alphaBoxes=[s['box'] for s in src])
        for f,s in zip(F,src):
            crop=s['tile'].crop(s['box']);crop=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.NEAREST)
            im=blank();im.alpha_composite(crop,(16-crop.width//2,ground-crop.height+1));im=snap(im,True)
            im.save(RUN/(f+'-'+name+'-native.png'))
    src=cutgrid('walk',4,2)
    for ri,f in enumerate(('se','ne')):
        row=src[ri*4:ri*4+4];maxheight=max(s['box'][3]-s['box'][1] for s in row);scale=42/maxheight
        # A single row origin: no per-phase bbox fitting or foot recentering.
        baseline=max(s['box'][3] for s in row)-1;cx=sum((s['box'][0]+s['box'][2])/2 for s in row)/4
        proofs['walk-'+f]=dict(scale=scale,sourceBaseline=baseline,sourceCenterX=cx,normalization='shared row scale and fixed origin; source head replaced by direction-locked canonical layer',sourceBoxes=[s['sourceBox'] for s in row],alphaBoxes=[s['box'] for s in row])
        for p,s in enumerate(row):
            tile=s['tile'];tile=tile.resize((round(tile.width*scale),round(tile.height*scale)),Image.Resampling.NEAREST)
            im=blank();im.alpha_composite(tile,(16-round(cx*scale),60-round(baseline*scale)));im=snap(im,True)
            im.save(RUN/(f+'-walk-'+str(p)+'-native.png'))
    savejson(RUN/'normalization-proof.json',proofs)
    review=Image.new('RGBA',(1000,550),BG);d=ImageDraw.Draw(review)
    for k,f in enumerate(F):
        d.text((k*240+10,5),f+' native 1x / exact 3x',fill=O)
        for j,n in enumerate(('idle','seat')):
            im=load(RUN/(f+'-'+n+'-native.png'))
            review.alpha_composite(im,(k*240+10,j*205+30));review.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(k*240+60,j*205+30))
    for p in range(4):
        for j,f in enumerate(('se','ne')):
            im=load(RUN/(f+'-walk-'+str(p)+'-native.png'))
            review.alpha_composite(im,(440+p*128,425+j*64));review.alpha_composite(im.resize((64,128),Image.Resampling.NEAREST),(15+p*100,420))
    review.save(RUN/'native-masters-review.png')
    template=read(ROOT/'assets/art/iso/characters/dev-hoodie-v2/dev-hoodie.iso.json')
    s=template['sheets'][0]
    print('Normal sheet keys',list(s))
    print('Sample rows',s['rows'][0],s['rows'][8],s['rows'][16],s['rows'][24])
    print('Template root keys',list(template))
    print('Normalization',proofs)



def cutgrid(name,cols,rows):
    im=load(RUN/(name+'-master-a1.png'));a=np.array(im);groups=components(a[:,:,3]>=160)
    groups=groups[:cols*rows]
    groups.sort(key=lambda g:sum(y for y,x in g)/len(g))
    result=[]
    for r in range(rows):
        row=groups[r*cols:r*cols+cols];row.sort(key=lambda g:sum(x for y,x in g)/len(g))
        for c,g in enumerate(row):
            ys=[y for y,x in g];xs=[x for y,x in g];box=(min(xs),min(ys),max(xs)+1,max(ys)+1)
            # Initial connected-component extraction for standalone masters. No clipping at grid seams.
            cut=np.zeros((box[3]-box[1],box[2]-box[0],4),np.uint8)
            for y,x in g:cut[y-box[1],x-box[0]]=a[y,x]
            tile=Image.fromarray(cut)
            if name=='walk':
                # Preserve fixed grid coordinates for the animated strip and its common origin.
                canvas=Image.new('RGBA',(round(im.width/cols),round(im.height/rows)))
                canvas.alpha_composite(tile,(box[0]-round(c*im.width/cols),box[1]-round(r*im.height/rows)))
                tile=canvas
            result.append(dict(tile=tile,box=tile.getbbox(),sourceBox=box))
    return result

def heads_preview():
    b=Image.new('RGBA',(1000,400),BG);d=ImageDraw.Draw(b)
    for k,f in enumerate(F):
        im=load(RUN/(f+'-idle-native.png'));b.alpha_composite(im.crop((0,18,32,44)).resize((256,208),Image.Resampling.NEAREST),(k*248,15))
        d.text((k*248+5,240),f+' head source y18..43',fill=O)
    b.save(RUN/'heads-before-repair.png')
if __name__=='__main__':globals()[sys.argv[1]]()
