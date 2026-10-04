"""Offline, deterministic native-resolution seated assembly z-buffer.

World units are 1/16 tile horizontally and native pixels vertically.
P=(64+2*(i-j), 80+i+j-z); camera depth is i+j+2*z (larger is nearer).
No runtime rasterization, actor-shaped holes in art, or source sprite edits.
"""
from __future__ import annotations

import base64
from functools import lru_cache
from io import BytesIO
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image

FACINGS = ("se", "sw", "ne", "nw")
TEAL = ("10282C", "235E5C", "20746C", "3C9F96", "5DB1A5")
METAL = ("191E1E", "262D38", "565958", "808985", "89918F")
POSES = {"seated-typing-front": "typing", "seated-thinking": "waiting", "sleep": "resting",
         "sit-down": "sit-down", "stand-up": "stand-up", "idle": "turn"}
CUSHION_SIDE = BACKREST_WIDTH = 5.5
CUSHION_RADIUS = .5


def cushion_outline():
    h = CUSHION_SIDE / 2; r = CUSHION_RADIUS
    return [(i + r*math.cos(t) + 1, j + r*math.sin(t))
            for i,j,start in ((h-r,h-r,0),(-h+r,h-r,90),(-h+r,-h+r,180),(h-r,-h+r,270))
            for t in np.deg2rad(np.linspace(start,start+90,4))]


def cushion_mesh():
    poly=cushion_outline();factor=(CUSHION_SIDE/2-.25)/(CUSHION_SIDE/2)
    high=[(i,j,12) for i,j in poly]
    low=[(1+(i-1)*factor,j*factor,10.5) for i,j in poly]
    faces=[high,low[::-1]]+[[low[n],low[(n+1)%len(poly)],high[(n+1)%len(poly)],high[n]] for n in range(len(poly))]
    return [(face,2) for face in faces]


def rotate(p, facing):
    i, j, z = p
    return {"se": (i, j, z), "sw": (-j, i, z), "ne": (j, -i, z), "nw": (-i, -j, z)}[facing]


def project(p):
    i, j, z = np.asarray(p).T
    return np.array((64 + 2 * (i - j), 80 + i + j - z, i + j + 2 * z)).T


def prism(poly, z0, z1, material=0):
    low = [(*p, z0) for p in poly]; high = [(*p, z1) for p in poly]
    faces = [high, low[::-1]]
    faces += [[low[n], low[(n+1) % len(poly)], high[(n+1) % len(poly)], high[n]] for n in range(len(poly))]
    return [(face, material) for face in faces]


def box(i0, j0, i1, j1, z0, z1, material=0):
    return prism([(i0,j0), (i1,j0), (i1,j1), (i0,j1)], z0, z1, material)


def limb(a, b, radius, material=0):
    a, b = np.array(a, float), np.array(b, float)
    axis = b - a; axis /= np.linalg.norm(axis)
    u = np.cross(axis, (0, 0, 1) if abs(axis[2]) < .9 else (1, 0, 0)); u /= np.linalg.norm(u)
    v = np.cross(axis, u)
    rings = [[p + radius * (u * math.cos(t) + v * math.sin(t)) for t in np.arange(8)*math.pi/4] for p in (a,b)]
    faces = [rings[0][::-1], rings[1]]
    faces += [[rings[0][n], rings[0][(n+1)%8], rings[1][(n+1)%8], rings[1][n]] for n in range(8)]
    return [(face, material) for face in faces]


def ellipsoid(center, radii):
    # Latitude rings avoid degenerate pole triangles; caps are flat tiny facets.
    rings = [[np.asarray(center) + np.asarray(radii)*np.array((math.cos(t)*math.cos(p), math.sin(t)*math.cos(p), math.sin(p)))
              for t in np.arange(12)*math.pi/6] for p in np.linspace(-1.50,1.50,7)]
    faces = [rings[0][::-1], rings[-1]]
    for a,b in zip(rings,rings[1:]):
        faces += [[a[n],a[(n+1)%12],b[(n+1)%12],b[n]] for n in range(12)]
    return [(face, 0) for face in faces]


def raster(mesh, facing, gradients=False):
    depth = np.full((128,128), -np.inf); labels = np.zeros((128,128), np.uint8)
    shades = np.zeros((128,128), np.uint8)
    gx=np.zeros_like(depth);gy=np.zeros_like(depth)
    for face, material in mesh:
        world = np.array([rotate(p,facing) for p in face]); pp = project(world)
        normal = np.cross(world[1]-world[0],world[2]-world[0])
        # Geometric shading uses only frozen palette ramps.
        shade = 3 if abs(normal[2]) > abs(normal[0])+abs(normal[1]) else (2 if normal[0] > normal[1] else 1)
        for n in range(1,len(pp)-1):
            a,b,c = pp[[0,n,n+1]]
            x0,y0 = np.maximum(np.floor(np.min((a[:2],b[:2],c[:2]),axis=0)).astype(int),0)
            x1,y1 = np.minimum(np.ceil(np.max((a[:2],b[:2],c[:2]),axis=0)).astype(int),128)
            if x1<=x0 or y1<=y0: continue
            yy,xx = np.mgrid[y0:y1,x0:x1]; xx=xx+.5; yy=yy+.5
            den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(den)<1e-9: continue
            wa=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
            wb=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den; wc=1-wa-wb
            d=wa*a[2]+wb*b[2]+wc*c[2]
            hit=(wa>=-1e-9)&(wb>=-1e-9)&(wc>=-1e-9)&(d>depth[y0:y1,x0:x1]+1e-8)
            depth[y0:y1,x0:x1][hit]=d[hit]; labels[y0:y1,x0:x1][hit]=material
            shades[y0:y1,x0:x1][hit]=shade
            if gradients:
                gradient=np.linalg.solve(np.array([[b[0]-a[0],b[1]-a[1]],[c[0]-a[0],c[1]-a[1]]]),[b[2]-a[2],c[2]-a[2]])
                gx[y0:y1,x0:x1][hit]=gradient[0];gy[y0:y1,x0:x1][hit]=gradient[1]
    return (depth,labels,shades,gx,gy) if gradients else (depth,labels,shades)


@lru_cache(maxsize=4)
def chair(facing):
    mesh=[]
    # Four cardinal spokes and the fifth diagonal spoke; all five have ground casters.
    for i,j in ((3.8,0),(0,3.8),(-3.8,0),(0,-3.8),(2.7,2.7)):
        mesh += limb((0,0,1.5),(i,j,1.5),.32,1) + box(i-.5,j-.5,i+.5,j+.5,0,1.5,1)
    mesh += box(-.45,-.45,.45,.45,1.5,10.5,1)
    # Square footprint matching the back width; only the native corner is rounded.
    mesh += cushion_mesh()
    # Separate thin back slab: top remains z21, lower joint at cushion height.
    mesh += box(-4,-2.75,-3.5,2.75,12,21,3)
    mesh += box(-3.75,-.4,-.8,.4,10,12,1)
    for j in (-2.9,2.9):
        mesh += box(-1.5,j-.3,1.5,j+.3,12,13,4)
        mesh += box(-1.25,j-.25,-.75,j+.25,8,12,4)
    d,label,shade=raster(mesh,facing)
    art=np.zeros((128,128,4),np.uint8)
    for m in range(1,5):
        ramp = METAL if m in (1,4) else TEAL
        for s in (1,2,3): art[(label==m)&(shade==s)] = (*bytes.fromhex(ramp[s]),255)
    yy,xx=np.indices(d.shape)
    # Mesh detail is ink on the same physical slab, not a taller replacement back.
    back=label==3
    art[back&((xx+2*yy)%4==0)] = (*bytes.fromhex(TEAL[0]),255)
    art[back&((xx+2*yy)%4==2)] = (*bytes.fromhex(TEAL[3]),255)
    # Highlight true object boundaries without expanding its silhouette/depth map.
    solid=np.isfinite(d)
    edge=solid & ~np.roll(solid,1,axis=0)
    art[edge&(label==2)] = (*bytes.fromhex(TEAL[4]),255)
    return art,d,label


def actor_mesh(facing, pose, frame, height):
    pelvis=height
    standing=pose=="turn" or height>=19
    knee=(1.5,9) if standing else (5,pelvis+1.2)
    mesh=ellipsoid((0,0,pelvis+(1 if standing else 2)),(1.6,2,2) if standing else (2.4,2.5,2))
    if not standing:
        # The clothing lap spans the seat, including rear-view skirt hems.
        # The old narrow ellipsoid mapped these authored samples onto shins
        # below the cushion. Its supported underside stays above the z12 seat.
        mesh += box(-3.5,-3.5,3.5,3.5,pelvis+.1,pelvis+3,2)
    for side in (-1,1):
        # The thigh underside rests on z12 instead of descending through the
        # cushion to the old z8 knee. Contact/phase metadata and art stay fixed.
        hip=(0,side*1.5,pelvis if standing else pelvis+1.2); k=(knee[0],side*1.5,knee[1]); foot=(knee[0]+1,side*1.5,1)
        mesh += limb(hip,k,1.1)+limb(k,foot,.85)+box(foot[0]-.5,foot[1]-.8,foot[0]+1.6,foot[1]+.8,0,2)
    lean=1.1 if pose=="resting" else .3
    mesh += ellipsoid((lean,0,pelvis+7),(1.9,2.2,6.5))
    mesh += ellipsoid((lean,0,pelvis+18),(2.8,2.8,6.5))
    for side in (-1,1):
        shoulder=(lean,side*2,pelvis+11)
        # Keyboard hands preserve frozen H=P(A+3/8 f,18).
        hand=(6,side*.25,18) if pose=="typing" else (2.5,side*2,pelvis+4)
        elbow=((shoulder[0]+hand[0])/2,side*2.5,(shoulder[2]+hand[2])/2-2)
        mesh += limb(shoulder,elbow,.65)+limb(elbow,hand,.6)
        # Authored palms span ~5 native pixels around H, larger than the wrist
        # cylinder. Without a hand volume nearest-surface calibration snaps the
        # outer fingertips to the projected knee/cushion instead of z18.
        mesh += ellipsoid(hand,(1.6,1.6,1.8))
        if pose=="typing":
            mesh += box(hand[0]-2.5,hand[1]-2.5,hand[0]+2.5,hand[1]+2.5,18.1,20.4,4)
    return mesh


def source_cell(sheet, pixels, clip, frame):
    x=(clip.get("column",0)+frame)*sheet["cellW"]; y=clip["row"]*sheet["cellH"]
    return np.array(pixels.crop((x,y,x+sheet["cellW"],y+sheet["cellH"])))


def contact(clip,frame):
    if clip.get("phaseContacts"):
        c=clip["phaseContacts"][frame]; return (64-int(c["seatAnchor"]["x"])+int(c["contactOffset"]["x"]),
            80+int(c["contactOffset"]["y"])-int(c["seatAnchor"]["y"]), -c["contactOffset"]["y"])
    if clip.get("seatAnchor"): return 48,24,12
    return 48,20,20


@lru_cache(maxsize=128)
def actor_surface(facing,pose,height):
    return raster(actor_mesh(facing,pose,0,height),facing,gradients=True)


def actor_depth(facing,pose,height):
    return actor_surface(facing,pose,height)[0]


@lru_cache(maxsize=128)
def head_actor_depth(facing,pose,height):
    lean=1.1 if pose=="resting" else .3
    return raster(ellipsoid((lean,0,height+18),(2.8,2.8,6.5)),facing)[0]


def calibrated_actor_depth(facing,pose,height,dx,dy,pixels):
    """Map authored alpha to proxy depth; expose diagnostics without masking art."""
    ad,materials,_,gx,gy=actor_surface(facing,pose,height)
    actor=pixels[:,:,3]>0
    # Calibration: map every real sprite sample to the closest projected proxy
    # surface. Coverage is actual authored alpha, never the primitive silhouette.
    ay,ax=np.where(actor); worldx=ax+dx; worldy=ay+dy
    sy,sx=np.where(np.isfinite(ad))
    distances=(worldx[:,None]-sx)**2+(worldy[:,None]-sy)**2
    nearest=distances.argmin(axis=1)
    ny,nx=sy[nearest],sx[nearest]
    supported=np.isin(materials[ny,nx],(2,4))
    values=ad[ny,nx]+supported*(gx[ny,nx]*(worldx-nx)+gy[ny,nx]*(worldy-ny))
    # Calibrate source head/hair against the head volume, independently of the
    # wider lap and lower arms. A long braid must not snap to a low seat/desk
    # surface simply because that surface is closer in the projected image.
    upper=ay<ay.min()+18
    if pose=="typing":
        hx,hy={"se":(28,44),"sw":(4,44),"ne":(28,32),"nw":(4,32)}[facing]
        upper &= ~((abs(ax-hx)<=3)&(abs(ay-hy)<=2))
    if upper.any():
        ud=head_actor_depth(facing,pose,height);uy,ux=np.where(np.isfinite(ud))
        nearest_upper=((worldx[upper,None]-ux)**2+(worldy[upper,None]-uy)**2).argmin(axis=1)
        values[upper]=ud[uy[nearest_upper],ux[nearest_upper]]
    aligned=np.full((128,128),-np.inf); aligned[worldy,worldx]=values
    return aligned, float(np.sqrt(distances.min(axis=1)).max())


def oracle(facing,pose,frame,clip,pixels,desk_depth=None):
    art,cd,labels=chair(facing); dx,dy,height=contact(clip,frame)
    aligned,distance=calibrated_actor_depth(facing,pose,height,dx,dy,pixels)
    actor=pixels[:,:,3]>0; ay,ax=np.where(actor); worldx=ax+dx; worldy=ay+dy
    values=aligned[worldy,worldx]
    overlaps=np.zeros_like(actor)
    overlaps[ay,ax]=art[worldy,worldx,3]>0
    hidden=np.zeros_like(actor); hidden[ay,ax]=overlaps[ay,ax] & (cd[worldy,worldx]>values+1e-7)
    # Empty regions use the pelvis plane; overlaps use the exact depth comparison.
    front=(art[:,:,3]>0)&(cd>height*2)
    front[worldy,worldx]=hidden[ay,ax] & (art[worldy,worldx,3]>0)
    if desk_depth is not None:
        # A nearer desktop hides chair surfaces too. Put those samples behind
        # the sitter/desk instead of letting chair rank20 cover desk rank10.
        front &= cd > desk_depth+1e-7
        hidden[ay,ax] = front[worldy,worldx] & overlaps[ay,ax]
    parts=[np.zeros_like(art) for _ in range(3)]
    parts[0][~front]=art[~front]
    parts[1][front&(labels==2)]=art[front&(labels==2)]
    parts[2][front&(labels!=2)]=art[front&(labels!=2)]
    return parts,hidden,overlaps,distance


def bits(mask):
    import zlib
    return "z:"+base64.b64encode(zlib.compress(np.packbits(mask.ravel(),bitorder="little").tobytes(),9)).decode("ascii")


def write_chair(directory):
    path=directory/"chair.iso.json"; manifest=json.loads(path.read_text(encoding="utf-8"))
    sheet=manifest["sheets"][0]; sheet["rows"]=[]
    atlas=Image.new("RGBA",(384,512))
    for n,f in enumerate(FACINGS):
        art,d,label=chair(f); front=(art[:,:,3]>0)&(d>24)
        masks=(~front,front&(label==2),front&(label!=2))
        rows=[]
        for k,(role,nn,rank) in enumerate((("rear","00",-20),("front","01",20),("front","00",20))):
            pixels=np.zeros_like(art); pixels[masks[k]]=art[masks[k]]; ys,xs=np.where(pixels[:,:,3]>0)
            # Contact closest to the physical cushion/back joint.
            t=((xs-64)**2+(ys-68)**2).argmin()
            row=dict(name=f"{role}-{nn}",direction=f,row=n,column=k,frames=1,fps=1,loop=False,
                     partId=f"chair__{role}__{f}__{nn}",anchor=dict(x=64,y=80),depthAnchor=dict(i=0,j=0),
                     isoZOrder=rank,seatGroupId="seat",anchorChecks=[dict(x=int(xs[t]),y=int(ys[t]),radius=0)])
            rows.append(row); atlas.paste(Image.fromarray(pixels),(k*128,n*128))
        sheet["rows"]+=rows
        v=next(v for v in manifest["furniture"][0]["variants"] if v["orientation"]==f)
        v["parts"]=[dict(sheetId=sheet["id"],animation=r["name"],direction=f,partId=r["partId"],
                         depthAnchor=r["depthAnchor"],isoZOrder=r["isoZOrder"],seatGroupId="seat") for r in rows]
        Image.fromarray(art).save(directory/f"chair-empty-{f}.png")
    atlas.save(directory/sheet["file"])
    manifest["provenance"]["chairV3"]="Deterministic native 3D raster: rounded square 5.5x5.5 cushion, center (1,0), radius .5, z10.5..12, lower waterfall inset .25; 5.5-wide mesh back slab z12..21; spokes radius 3.8, thickness .64, casters 1x1x1.5, lift .9x.9; frozen anchors/palette."
    manifest["provenance"]["seatedOcclusion"]="tools/iso_seated_geometry.py native 3D z-buffer; thigh underside z12.1 and pelvis underside z12 rest above the cushion; three disjoint parts. Pack bakes per-source-frame sandwich masks."
    manifest["provenance"]["limitations"]="Actor art is 2D: nearest projected proxy surface extrapolates clothing/hair. No physical claim for arbitrary independent walkers."
    path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")


def bake(manifest,files):
    """Append content-addressed chair cells and per-actor source frame bindings."""
    chair_sheet=next((s for s in manifest["sheets"] if s["id"]=="iso-chair-parts"),None)
    if chair_sheet is None or len(chair_sheet["rows"])!=12: return
    from iso_workstation_geometry import DeskBake
    desk=DeskBake(manifest,files)
    cells=[]; rows=[]; cache={}; max_distance=0.; count=0
    for sheet in manifest["sheets"]:
        if sheet["kind"]!="character": continue
        # Authored rows are source provenance, while clips are resolved playback.
        # Keep new assembly metadata exclusively on clips.
        for row in sheet["rows"]: row.pop("chairFrames",None)
        pixels=Image.open(BytesIO(files[sheet["file"]]))
        for c in sheet["clips"]:
            source=c["sourceClip"]["name"]
            if source not in POSES: continue
            facing=c["direction"]; bindings=[]
            for frame in range(c["frames"]):
                sprite=source_cell(sheet,pixels,c,frame)
                parts,hidden,overlaps,distance=oracle(facing,POSES[source],frame,c,sprite,desk.chair_depth(facing))
                dx,dy,height=contact(c,frame)
                aligned,_=calibrated_actor_depth(facing,POSES[source],height,dx,dy,sprite)
                keys=[]
                for k,part in enumerate(parts):
                    key=(facing,k,part.tobytes())
                    if key not in cache:
                        n=len(cells); name=f"sandwich-{n:03d}"
                        template=next(r for r in chair_sheet["rows"] if r["direction"]==facing and r["column"]==k)
                        ys,xs=np.where(part[:,:,3]>0)
                        checks=[]
                        if len(xs):
                            t=((xs-64)**2+(ys-68)**2).argmin()
                            checks=[dict(x=int(xs[t]),y=int(ys[t]),radius=0)]
                        r={**template,"name":name,"row":n//16,"column":n%16,"anchorChecks":checks}
                        rows.append(r); cells.append(Image.fromarray(part)); cache[key]=name+"/"+facing
                    keys.append(cache[key])
                bindings.append(dict(clips=keys,hidden=bits(hidden),overlap=bits(overlaps),
                                     **desk.frame(facing,aligned,dx,dy,sprite)))
                count+=1; max_distance=max(max_distance,distance)
            c["chairFrames"]=bindings
    atlas=Image.new("RGBA",(2048,128*math.ceil(len(cells)/16)))
    if atlas.height>8192: raise ValueError("chair sandwich atlas exceeds 8192px")
    for n,cell in enumerate(cells): atlas.paste(cell,((n%16)*128,(n//16)*128))
    stream=BytesIO(); atlas.save(stream,format="PNG",compress_level=9); data=stream.getvalue()
    import hashlib
    sha=hashlib.sha256(data).hexdigest(); file=f"sheets/pack-{sha}.png"
    manifest["sheets"].append(dict(id="iso-chair-sandwich",kind="furniture",file=file,cellW=128,cellH=128,
         anchor=dict(x=64,y=80),scale=1,projection="iso2to1",rows=rows,clips=rows.copy(),
         width=atlas.width,height=atlas.height,sha256=sha))
    files[file]=data
    desk.finish(manifest,files)
    manifest["seatedOracle"]=dict(version=2,projection="P=(2(i-j),i+j-z)",nearDepth="i+j+2z",maskEncoding="z:base64-zlib-little-bitset",
         frames=count,cells=len(cells),deskCells=len(desk.cells),maxExtrapolationPixels=round(max_distance,4),partsPerChair=3)


if __name__=="__main__":
    write_chair(Path(__file__).resolve().parents[1]/"assets/art/iso/furniture-v3/chair")
