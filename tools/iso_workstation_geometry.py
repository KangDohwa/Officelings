"""Art-derived workstation proxy and disjoint, frame-baked desk partitions.

The retained desktop polygon and input-device boxes come from
furniture-v3/workstations/build-desk-rig-v3-lane.py. Lower support surfaces
are vertical native columns recovered from the committed structural alpha:
grounded leg/pedestal contours use z0, short fascia columns join the z18 rim.
Only authored opaque samples participate; no rectangle fills art holes.
"""
from io import BytesIO
import hashlib
import math
import numpy as np
from PIL import Image
import iso_seated_geometry as g

DESK_FACING = dict(nw="se", ne="sw", sw="ne", se="nw")


def mask_bits(mask):
    return g.bits(mask)


def desk_rotate(i, j, facing):
    return dict(se=(i,j), sw=(16-j,i), ne=(j,-i), nw=(-i,16-j))[facing]


def desk_surface(facing, anchor, bounds):
    i0,j0,i1,j1,z0,z1=bounds
    mesh=g.box(i0*16,j0*16,i1*16,j1*16,z0,z1)
    # Reuse the native rasterizer, translating its fixed origin into desk space.
    translated=[]
    for face,material in mesh:
        points=[]
        for i,j,z in face:
            i,j=desk_rotate(i,j,facing)
            di=(anchor["x"]-64)/4+(anchor["y"]-80)/2
            dj=(anchor["y"]-80)/2-(anchor["x"]-64)/4
            points.append((i+di,j+dj,z))
        translated.append((points,material))
    d=g.raster(translated,"se")[0]
    # Remove the artificial origin translation from physical camera depth.
    return d-(anchor["y"]-80)


def proxy(facing, anchor, art, surface):
    yy,xx=np.indices((128,128));solid=art[:,:,3]>0
    top=desk_surface(facing,anchor,(-.43,-.43,.625,1.22,18,18))
    topmask=np.isfinite(top)
    depth=np.full((128,128),-np.inf)
    depth[topmask & solid]=top[topmask & solid]
    # The support silhouette is the union of the real apron, drawer pedestal
    # and legs. Native bottom contours, rather than guessed support boxes,
    # determine each vertical ray's ground contact. Slab-only columns use the
    # known tabletop edge as their contact instead of extending to the floor.
    for x in np.flatnonzero(solid.any(axis=0)):
        edge=np.flatnonzero(topmask[:,x]);ys=np.flatnonzero(solid[:,x])
        if not len(edge):continue
        rim=edge.max()+.5;bottom=ys.max()+.5
        ground=bottom if bottom-rim>6 else rim+18
        select=solid[:,x] & ~topmask[:,x]
        depth[select,x]=3*ground-2*(yy[select,x]+.5)-anchor["y"]
    # Authored rear-14 carries tabletop and contact ink. Its frozen rim texels
    # must remain on z18 even just outside the mathematical top polygon.
    depth[surface]=yy[surface]+.5-anchor["y"]+3*18
    # Keyboard/mouse are real z18..19 volumes and retain source case pixels.
    for bounds in ((.4375,0,.6875,.5,18,19),(.5625,-.1875,.6875,0,18,19)):
        d=desk_surface(facing,anchor,bounds)
        active=solid & np.isfinite(d)
        depth[active]=np.maximum(depth[active],d[active])
    # Sparse native rim/ink texels may lie one pixel beyond the polygon; the
    # nearest physical surface gives them depth without expanding coverage.
    missing=solid & ~np.isfinite(depth)
    sy,sx=np.where(np.isfinite(depth));my,mx=np.where(missing)
    if len(mx):
        nearest=((mx[:,None]-sx)**2+(my[:,None]-sy)**2).argmin(axis=1)
        depth[my,mx]=depth[sy[nearest],sx[nearest]]
    return depth


class DeskBake:
    def __init__(self,manifest,files):
        self.cells=[];self.rows=[];self.cache={};self.desks={}
        sheet=next((s for s in manifest["sheets"] if s["id"]=="iso-desk-parts"),None)
        if sheet is None:return
        pixels=Image.open(BytesIO(files[sheet["file"]]))
        desk=next(f for f in manifest["furniture"] if f["id"]=="iso/desk")
        for actor_facing,facing in DESK_FACING.items():
            variant=next(v for v in desk["variants"] if v["orientation"]==facing)
            profile=variant["seatProfiles"][0];seat=profile["seatGround"]
            parts=[];art=np.zeros((128,128,4),np.uint8);surface=np.zeros((128,128),bool)
            for part in variant["parts"]:
                name=part.get("animation",part.get("clip"))
                if part["sheetId"]!=sheet["id"] or name.startswith("monitor-") or name in ("rear-15","rear-16"):continue
                c=next(c for c in sheet["clips"] if c["name"]==name and c["direction"]==facing)
                a=g.source_cell(sheet,pixels,c,0)
                if np.any((art[:,:,3]>0)&(a[:,:,3]>0)):raise ValueError("duplicate desk source pixels")
                art+=a;parts.append((c,a))
                if name=="rear-14":surface |= a[:,:,3]>0
            anchor=parts[0][0].get("anchor",sheet["anchor"])
            shift=(int(64-32*(seat["i"]-seat["j"])-anchor["x"]),
                   int(80-16*(seat["i"]+seat["j"])-anchor["y"]))
            depth=proxy(facing,anchor,art,surface)-16*(seat["i"]+seat["j"])
            self.desks[actor_facing]=(parts,art,depth,shift,anchor,seat,facing)

    def chair_depth(self,facing):
        if facing not in self.desks:return None
        _,_,depth,(dx,dy),_,_,_=self.desks[facing]
        aligned=np.full((128,128),-np.inf)
        y,x=np.where(np.isfinite(depth));valid=(x+dx>=0)&(x+dx<128)&(y+dy>=0)&(y+dy<128)
        aligned[y[valid]+dy,x[valid]+dx]=depth[y[valid],x[valid]]
        return aligned

    def cell(self,template,pixels):
        key=(template["partId"],template["isoZOrder"],pixels.tobytes())
        if key not in self.cache:
            n=len(self.cells);name=f"desk-mask-{n:03d}";y,x=np.where(pixels[:,:,3]>0)
            checks=[dict(x=int(x[0]),y=int(y[0]),radius=0)] if len(x) else []
            row={**template,"name":name,"row":n//16,"column":n%16,"frames":1,"fps":1,"loop":False,"anchorChecks":checks}
            self.rows.append(row);self.cells.append(Image.fromarray(pixels));self.cache[key]=name+"/"+row["direction"]
        return self.cache[key]

    def frame(self,facing,aligned,dx,dy,pixels):
        if facing not in self.desks:return {}
        parts,art,depth,(sx,sy),anchor,seat,desk_facing=self.desks[facing]
        ay,ax=np.where(pixels[:,:,3]>0);wx=ax+dx;wy=ay+dy;tx=wx-sx;ty=wy-sy
        valid=(tx>=0)&(tx<128)&(ty>=0)&(ty<128)
        overlap=np.zeros((64,32),bool);hidden=np.zeros_like(overlap)
        ay,ax,wx,wy,tx,ty=[v[valid] for v in (ay,ax,wx,wy,tx,ty)]
        overlap[ay,ax]=art[ty,tx,3]>0
        # Desk rank10 wins exact depth ties against actor rank0.
        hidden[ay,ax]=overlap[ay,ax] & (depth[ty,tx]>=aligned[wy,wx]-1e-7)
        front=np.zeros((128,128),bool);front[ty,tx]=hidden[ay,ax]
        front_art=np.zeros_like(art);front_art[front]=art[front]
        replacements=[]
        for c,a in parts:
            if not np.any(front & (a[:,:,3]>0)):continue
            remainder=a.copy();remainder[front]=0
            replacements.extend((c["name"]+"/"+c["direction"],self.cell(c,remainder)))
        template=dict(direction=desk_facing,partId=f"desk__front__{desk_facing}__17",anchor=anchor,
                      depthAnchor=seat,isoZOrder=10,seatGroupId="seat")
        return dict(deskClips=replacements,deskFront=self.cell(template,front_art),
                    deskHidden=mask_bits(hidden),deskOverlap=mask_bits(overlap))

    def finish(self,manifest,files):
        if not self.cells:return
        atlas=Image.new("RGBA",(2048,128*math.ceil(len(self.cells)/16)))
        if atlas.height>8192:raise ValueError("desk sandwich atlas exceeds 8192px")
        for n,cell in enumerate(self.cells):atlas.paste(cell,((n%16)*128,(n//16)*128))
        stream=BytesIO();atlas.save(stream,format="PNG",compress_level=9);data=stream.getvalue()
        sha=hashlib.sha256(data).hexdigest();file=f"sheets/pack-{sha}.png";files[file]=data
        manifest["sheets"].append(dict(id="iso-desk-sandwich",kind="furniture",file=file,cellW=128,cellH=128,
            anchor=dict(x=64,y=80),scale=1,projection="iso2to1",rows=self.rows,clips=self.rows.copy(),
            width=atlas.width,height=atlas.height,sha256=sha))
