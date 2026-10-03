"""Repaint exposed desktop and CRT support from physical geometry; lane-only writes."""
from pathlib import Path
import copy, hashlib, importlib.util, json, sys
import numpy as np
from PIL import Image, ImageDraw

OUT=Path(__file__).resolve().parent; ROOT=OUT.parents[4]; OLD=OUT/'rev2-source'
sys.dont_write_bytecode=True
spec=importlib.util.spec_from_file_location('a3_geometry',ROOT/'runs/furniture-v2/geometry.py')
g=importlib.util.module_from_spec(spec);spec.loader.exec_module(g)
DELTAS={'se':(-12,-6,-.375,0),'sw':(12,-6,0,-.375),'ne':(-12,6,0,.375),'nw':(12,6,.375,0)}
FACINGS=tuple(DELTAS)
def read(name):return json.loads((OLD/name).read_text(encoding='utf-8'))
def cell(atlas,c):
    x=c.get('column',0)*128;y=c['row']*128
    return np.array(atlas.crop((x,y,x+128,y+128)))
def shifted(a,dx,dy):
    im=Image.new('RGBA',(128,128));im.alpha_composite(Image.fromarray(a),(dx,dy));return np.array(im)
def check(a,desired):
    y,x=np.where(a[:,:,3]>0);assert len(x)
    k=np.argmin((x-desired[0])**2+(y-desired[1])**2)
    return [dict(x=int(x[k]),y=int(y[k]),radius=0)]
def put(atlas,c,a):atlas.paste(Image.fromarray(a),(c.get('column',0)*128,c['row']*128))
def metadata_match(c,meta):
    return c['partId'].split('__')[1]==meta['role'] and c['depthAnchor']==meta['depthAnchor'] and bool(c.get('seatGroupId'))==bool(meta.get('seatGroupId'))
def nearest_owner(masks,active):
    yy,xx=np.indices(active.shape);dist=[]
    for mask in masks:
        y,x=np.where(mask);dd=np.full(active.shape,np.inf)
        for a,b in zip(x,y):dd=np.minimum(dd,(xx-a)**2+(yy-b)**2)
        dist.append(dd)
    return np.array(dist).argmin(axis=0)
def main():
    doc=read('desk.json');s,ss=doc['sheets'];old=Image.open(OLD/s['file']).convert('RGBA');atlas=Image.new('RGBA',(old.width,old.height+384));atlas.paste(old)
    screen_old=Image.open(OLD/ss['file']).convert('RGBA');screens=screen_old.copy();proof=[]
    donors={}
    propboxes={'se':((51,56,64,67),(40,65,59,77)),'ne':((73,52,87,64),(79,60,98,73))}
    for source_facing,boxes in propboxes.items():
        source=np.array(Image.open(OLD/f'desk-empty-{source_facing}.png').convert('RGBA'))
        donors[source_facing]=[]
        for box in boxes:
            a=source[box[1]:box[3],box[0]:box[2]].copy();colors=np.array(g.RAMPS['cream']+g.RAMPS['metal']+g.RAMPS['glass'][:1])
            keep=np.any(np.all(a[:,:,:3,None]==colors.T[None,None],axis=2),axis=2)&(a[:,:,3]>0);a[~keep]=0
            donors[source_facing].append(a)
    for fi,f in enumerate(FACINGS):
        dx,dy,di,dj=DELTAS[f];clips=[c for c in s['rows'] if c['direction']==f]
        bodyclips=[c for c in clips if '__monitor__' not in c['partId']];monitorclips=[c for c in clips if '__monitor__' in c['partId']]
        images={c['partId']:cell(old,c) for c in clips}; original_nonmonitor=np.zeros((128,128,4),np.uint8)
        for c in bodyclips:
            a=images[c['partId']];original_nonmonitor[a[:,:,3]>0]=a[a[:,:,3]>0]
        # The old authored polygons supply physical per-edge ownership underneath the CRT.
        save_monitor=g.monitor;g.monitor=lambda m:None
        bare=g.make_model('desk',f);g.monitor=save_monitor
        yy,xx=np.indices((128,128));ax,ay=bare.anchor
        top=g.polygon_mask([bare.project(*p) for p in [(-.43,-.43,18),(.625,-.43,18),(.625,1.22,18),(-.43,1.22,18)]])
        # Lift the existing cup/note as whole components before filling their old contact.
        source_facing='se' if f in ('se','sw') else 'ne';old_prop_ground=[]
        for pi,box in enumerate(propboxes[source_facing]):
            da=g.source_anchor('desk',source_facing);sx=(box[0]+box[2])/2-da[0];sy=(box[3]-1 if pi==0 else (box[1]+box[3])/2)-da[1]
            ri=sx/64+(sy+18)/32;rj=(sy+18)/32-sx/64
            ci,cj=(ri,rj) if source_facing=='se' else (-rj,ri);old_prop_ground.append((ci,cj))
            px,py=bare.project(ci,cj,18);patch=donors[source_facing][pi];h,w=patch.shape[:2]
            bx,by=(box[0],box[1]) if f==source_facing else (px-w//2,py-(h-1 if pi==0 else h//2))
            old_region=np.zeros((128,128),bool);old_region[by:by+h,bx:bx+w]=True
            for c in bodyclips:
                a=images[c['partId']];colors=np.array(g.RAMPS['cream']+g.RAMPS['metal']+g.RAMPS['glass'][:1]);keep=np.any(np.all(a[:,:,:3,None]==colors.T[None,None],axis=2),axis=2)&old_region&(a[:,:,3]>0)
                a[keep]=0
            original_nonmonitor[old_region & np.any(np.all(original_nonmonitor[:,:,:3,None]==colors.T[None,None],axis=2),axis=2)]=0
        missing=top & (original_nonmonitor[:,:,3]==0)
        # Continue sparse, ground-aligned grain instead of inventing image-space noise.
        gi=(xx+.5-ax)/64+(yy+.5-ay+18)/32;gj=(yy+.5-ay+18)/32-(xx+.5-ax)/64
        grain=np.abs(((gj+.20)% .32)-.16)<.022
        grain &= ((np.floor((gi+.43)*8).astype(int)%4)!=3)
        repaint=bare.pixels.copy();repaint[grain & top]=(*g.RAMPS['wood'][2],255)
        restored={}
        for label,meta in enumerate(bare.meta[1:],1):
            region=missing & (bare.labels==label)
            if not region.any():continue
            matches=[c for c in bodyclips if metadata_match(c,meta)]
            # Rev1 split surfaces retain the same ordinary depth but rear rank.
            if meta.get('seatGroupId'):
                matches=[c for c in bodyclips if c.get('seatGroupId') and c['isoZOrder']==(-20 if f in ('se','sw') else 10)]
            assert matches,(f,meta)
            c=matches[0];images[c['partId']][region]=repaint[region];restored[c['partId']]=int(region.sum())
        assert np.all(np.sum([images[c['partId']][:,:,3]>0 for c in bodyclips],axis=0)[top]>=1),'desktop hole'
        # Standalone CRT has a complete z18..21 foot and z21..36 housing.
        m=g.Model('desk',f,1,2);ci=-.125
        m.box((ci-.06,-.20,ci+.12,.20,18,21),'cream','monitor')
        m.box((ci-.25,-.42,ci+.18,.42,21,36),'cream','monitor');m.render_boxes()
        moved={c['partId']:shifted(images[c['partId']],dx,dy) for c in monitorclips}
        moved_union=np.zeros((128,128,4),np.uint8)
        for a in moved.values():moved_union[a[:,:,3]>0]=a[a[:,:,3]>0]
        smask=np.array(Image.open(OLD/f'desk-screen-mask-{f}.png').convert('RGBA'))
        newmask=shifted(smask,dx,dy)[:,:,3]>0
        geom=m.pixels.copy();geom[newmask]=0
        # Keep the selected master housing texture, repaint all exposed support pixels.
        crt=geom.copy();housing=m.depthmap.copy()
        srcactive=moved_union[:,:,3]>0
        crt[srcactive]=moved_union[srcactive];crt[newmask]=0
        # Base is deliberately redrawn from real projected planes, never translated cut source.
        base=g.Model('desk',f,1,2);base.box((ci-.06,-.20,ci+.12,.20,18,21),'cream','monitor');base.render_boxes()
        base_visible=(base.pixels[:,:,3]>0)&((m.pixels[:,:,3]==0)|(base.depthmap>=m.depthmap-1e-8))&~newmask
        crt[base_visible]=base.pixels[base_visible]
        active=crt[:,:,3]>0
        owner=nearest_owner([a[:,:,3]>0 for a in moved.values()],active)
        # Physical base label maps to its preserved base part; housing allocation stays ordinary.
        base_clip=monitorclips[0] if f in ('se','sw') else monitorclips[-1]
        owner[base_visible]=monitorclips.index(base_clip)
        for ni,c in enumerate(monitorclips):
            a=np.zeros_like(crt);region=active&(owner==ni);a[region]=crt[region];assert region.any(),c['partId'];images[c['partId']]=a
            c['depthAnchor']={'i':round(c['depthAnchor']['i']+di,4),'j':round(c['depthAnchor']['j']+dj,4)}
            assert c['isoZOrder']==0 and not c.get('seatGroupId')
        # Hidden desktop texels have no second owner. Mug/paper cannot sit inside the CRT.
        cover=active|newmask;removed_props=0
        cream=np.array(g.RAMPS['cream']);metal=np.array(g.RAMPS['metal'])
        for c in bodyclips:
            a=images[c['partId']]
            prop=np.any(np.all(a[:,:,:3,None]==np.vstack((cream,metal)).T[None,None],axis=2),axis=2)&(a[:,:,3]>0)
            # Keyboard lies outside the new housing; keep each contact exactly frozen.
            overlap=prop & cover
            removed_props+=int(overlap.sum())
            a[cover]=0
        # Search only legal tabletop contacts. Whole donor art moves without mirroring handles.
        occupied=cover.copy();keyboard=g.polygon_mask([bare.project(*p) for p in [(.555,-.22,18),(.695,-.22,18),(.695,.22,18),(.555,.22,18)]])
        occupied|=keyboard;prop_placement=[]
        for pi,patch in enumerate(donors[source_facing]):
            h,w=patch.shape[:2];preferred=old_prop_ground[pi];candidates=[]
            for ci in np.arange(-.30,.501,.04):
                for cj in np.arange(.46,1.121,.04):
                    px,py=bare.project(float(ci),float(cj),18);bx,by=px-w//2,py-(h-1 if pi==0 else h//2)
                    if bx<0 or by<0 or bx+w>128 or by+h>128:continue
                    layer=np.zeros((128,128,4),np.uint8);layer[by:by+h,bx:bx+w]=patch;mask=layer[:,:,3]>0
                    if pi==1:
                        if not (-.43<=ci-.11 and ci+.11<=.625 and -.43<=cj-.14 and cj+.14<=1.22):continue
                        note=Image.new('RGBA',(128,128));nd=ImageDraw.Draw(note)
                        pts=[bare.project(i,j,18) for i,j in [(ci-.11,cj-.14),(ci+.11,cj-.14),(ci+.11,cj+.14),(ci-.11,cj+.14)]]
                        nd.polygon(pts,fill=(*g.RAMPS['cream'][3],255),outline=(*g.RAMPS['cream'][2],255))
                        nd.line([bare.project(ci-.07,cj-.07,18),bare.project(ci+.06,cj-.07,18)],fill=(*g.RAMPS['cream'][4],255))
                        nd.line([bare.project(ci-.07,cj+.025,18),bare.project(ci+.03,cj+.025,18)],fill=(*g.RAMPS['cream'][1],255))
                        layer=np.array(note);mask=layer[:,:,3]>0
                    if np.any(mask&occupied):continue
                    if pi==1 and np.any(mask&~top):continue
                    # Cup ground footprint, not its elevated silhouette, must fit the surface.
                    if pi==0 and not (-.43<=ci-.10 and ci+.10<=.625 and -.43<=cj-.12 and cj+.12<=1.22):continue
                    cost=(ci-preferred[0])**2+(cj-preferred[1])**2
                    candidates.append((cost,float(ci),float(cj),layer,mask))
            assert candidates,(f,'no legal prop contact',pi)
            _,ci,cj,layer,mask=min(candidates,key=lambda t:t[:3]);occupied|=mask
            ri,rj=g.rotate(ci,cj,f,1,2)
            target=copy.deepcopy(bodyclips[0]);target.update(name=f'rear-{15+pi:02}',partId=f'desk__rear__{f}__{15+pi:02}',row=old.height//128+1+pi,column=fi,isoZOrder=0,depthAnchor=dict(i=round(ri,4),j=round(rj,4)),contactKind='desktop-mug-base' if pi==0 else 'desktop-paper-surface')
            target.pop('seatGroupId',None);s['rows'].append(target);bodyclips.append(target);images[target['partId']]=np.zeros_like(layer)
            for v in doc['furniture'][0]['variants']:
                if v['orientation']==f:v['parts'].append(dict(sheetId=s['id'],animation=target['name'],direction=f,partId=target['partId'],depthAnchor=copy.deepcopy(target['depthAnchor']),isoZOrder=0))
            for c in bodyclips:images[c['partId']][mask]=0
            images[target['partId']][mask]=layer[mask]
            prop_placement.append(dict(kind='mug' if pi==0 else 'paper',canonicalGround=[ci,cj],rotatedGround=[ri,rj],partId=target['partId'],pixels=int(mask.sum()),screenSilhouetteDisjoint=True))
        # Physical surface/fascia split, now also for NE/NW; not an actor-over-CRT bypass.
        if f in ('ne','nw'):
            c=next(c for c in bodyclips if c.get('seatGroupId'))
            a=images[c['partId']];keyboard=g.polygon_mask([bare.project(*p) for p in [(.555,-.22,18),(.695,-.22,18),(.695,.22,18),(.555,.22,18)]])
            hx,hy=bare.project(.625,0,18)
            for x in (hx-1,hx+1):keyboard[hy,x]=True
            surface=(top|keyboard)&(a[:,:,3]>0);rear=a.copy();rear[~surface]=0;fascia=a.copy();fascia[surface]=0
            assert np.array_equal(rear+fascia,a) and not np.any((rear[:,:,3]>0)&(fascia[:,:,3]>0))
            nc=copy.deepcopy(c);nc.update(name='rear-14',partId=f'desk__rear__{f}__14',row=old.height//128,column=fi,isoZOrder=-20,contactKind='horizontal-desktop-keyboard')
            s['rows'].append(nc);bodyclips.append(nc);images[nc['partId']]=rear;images[c['partId']]=fascia;c['contactKind']='physical-front-fascia'
            for v in doc['furniture'][0]['variants']:
                if v['orientation']==f:
                    p=next(p for p in v['parts'] if p['partId']==c['partId']);npart=copy.deepcopy(p);npart.update(animation=nc['name'],partId=nc['partId'],isoZOrder=-20);v['parts'].insert(v['parts'].index(p),npart)
        hidden_parts=[]
        for c in bodyclips+monitorclips:
            a=images[c['partId']];a[a[:,:,3]==0]=0
            if not np.any(a[:,:,3]):
                assert c in bodyclips and not c.get('seatGroupId'),('empty physical contact',c['partId'])
                hidden_parts.append(c['partId']);s['rows'].remove(c)
                for v in doc['furniture'][0]['variants']:
                    v['parts']=[p for p in v['parts'] if p['partId']!=c['partId']]
                put(atlas,c,a)
                continue
            desired=(c['anchorChecks'][0]['x']+(dx if c in monitorclips else 0),c['anchorChecks'][0]['y']+(dy if c in monitorclips else 0))
            c['anchorChecks']=check(a,desired);put(atlas,c,a)
        for c in ss['rows']:
            if c['direction']!=f:continue
            for phase in range(c['frames']):
                cc=dict(c,column=c.get('column',0)+phase);a=shifted(cell(screen_old,cc),dx,dy);assert np.array_equal(a[:,:,3]>0,newmask);put(screens,cc,a)
            c['depthAnchor']={'i':round(c['depthAnchor']['i']+di,4),'j':round(c['depthAnchor']['j']+dj,4)}
            c['anchorChecks']=[dict(p,x=p['x']+dx,y=p['y']+dy) for p in c['anchorChecks']]
        mask=np.zeros((128,128,4),np.uint8);mask[newmask]=(*g.RAMPS['cream'][4],255);Image.fromarray(mask).save(OUT/f'desk-screen-mask-{f}.png')
        for v in doc['furniture'][0]['variants']:
            if v['orientation']!=f:continue
            for p in v['parts']:
                if '__monitor__' in p['partId']:
                    c=next(c for c in monitorclips if c['partId']==p['partId']);p['depthAnchor']=copy.deepcopy(c['depthAnchor'])
        proof.append(dict(orientation=f,pixelDelta=[dx,dy],gridDelta=[di,dj],restoredDesktopPixels=sum(restored.values()),restoredByPart=restored,repaintedBasePixels=int(base_visible.sum()),filledMonitorPixels=int(((geom[:,:,3]>0)&~srcactive).sum()),removedOldNonWoodPixelsUnderCRT=removed_props,propPlacements=prop_placement,fullyHiddenDeskParts=hidden_parts))
    atlas.save(OUT/s['file']);screens.save(OUT/ss['file'])
    doc['provenance']['A3rev2']='CRT center A+9/8 f, integer translation with complete projected z18..21 base and repaired lower housing; exposed desktop repainted at z18. Four seat surfaces -20, fascia10, CRT ordinary ground rank0. Frozen A/C/H retained.'
    (OUT/'desk.iso.json').write_text(json.dumps(doc,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for f in FACINGS:
        full=Image.new('RGBA',(128,128))
        for c in s['rows']:
            if c['direction']==f:full.alpha_composite(Image.fromarray(cell(atlas,c)))
        c=next(c for c in ss['rows'] if c['direction']==f and c['name']=='screen-off');full.alpha_composite(Image.fromarray(cell(screens,c)));full.save(OUT/f'desk-empty-{f}.png')
    (OUT/'repaint-proof-A3-rev2.json').write_text(json.dumps(proof,indent=2)+'\n')
    board=Image.new('RGBA',(1536,575),'#E7D1AC');d=ImageDraw.Draw(board)
    for fi,f in enumerate(FACINGS):
        for ri,path in enumerate((OLD/f'desk-empty-{f}.png',OUT/f'desk-empty-{f}.png')):
            im=Image.open(path).convert('RGBA');board.alpha_composite(im,(fi*384,ri*280+20));board.alpha_composite(im.resize((384,384),Image.Resampling.NEAREST).crop((12,40,360,300)),(fi*384,ri*280+45));d.text((fi*384+4,ri*280+3),f+' '+('before' if ri==0 else 'after')+' 1x / 3x',fill='#191E1E')
    board.save(OUT/'empty-before-after-A3-rev2.png')
    print(json.dumps(proof,indent=2))
if __name__=='__main__':main()
