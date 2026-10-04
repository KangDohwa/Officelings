"""Re-derive all blink cells after motion: only SE/SW eye mask bytes may change."""
from hanbok_lane import *
from hanbok_export import EYES,C,SK,O
def main():
    doc=read(OUT/(CID+'.iso.json'));normal,blink=doc['sheets']
    atlas=load(OUT/normal['file']);src=np.array(atlas);closed=Image.new('RGBA',(128,1536))
    masks={f:Image.new('RGBA',atlas.size) for f in EYES};count=0
    for b,r in enumerate(CHECK.BLINK_ROWS):
        clip=normal['rows'][r];f=clip['direction']
        for p in range(4):
            im=atlas.crop((p*32,r*64,(p+1)*32,(r+1)*64));result=im.copy()
            off=normal['headOffsetsByClip'][clip['name']+'/'+f][p]
            dx,dy=off['x'],off['y'];mask=np.zeros((64,32),bool)
            for x,y in EYES[f]:
                for yy in range(y+dy,y+dy+3):
                    for xx in range(x+dx,x+dx+2):
                        assert im.getpixel((xx,yy))[3]==255,(r,p,xx,yy)
                        mask[yy,xx]=True;result.putpixel((xx,yy),SK)
                        masks[f].putpixel((p*32+xx,r*64+yy),(255,255,255,255))
                for xx in range(x+dx,x+dx+2):result.putpixel((xx,y+dy+1),O)
            assert np.array_equal(np.array(im)[~mask],np.array(result)[~mask])
            assert np.any(np.array(im)[mask]!=np.array(result)[mask]);count+=1
            closed.paste(result,(p*32,b*64))
    closed.save(OUT/blink['file'])
    for f,im in masks.items():im.save(OUT/normal['faceLock'][f]['eyeMask'])
    savejson(OUT/'blink-resync-proof.json',dict(nonEyeCellsExact=count,eyeOnlyReplacement=True,normalSHA256=sha(OUT/normal['file']),blinkSHA256=sha(OUT/blink['file']),eyeMaskSHA256={f:sha(OUT/normal['faceLock'][f]['eyeMask']) for f in EYES},normalRows=CHECK.BLINK_ROWS))
    print('Eye-only deterministic blink:',count,'cells')
if __name__=='__main__':main()

