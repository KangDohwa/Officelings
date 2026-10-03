"""Deterministic, integer-pixel face locking. Dependencies: Pillow and numpy.

Only publishes to locked/. Production manifests/frame_layout remain authoritative.
Reviewed coordinates are half-open native-pixel boxes, not scaled screenshots.
"""
import argparse
import copy
import hashlib
import io
import json
import os
import re
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "assets/art"
VERSION = 1
BACK = {"walk_up", "typing_back"}
STATES = ("idle", "walk_down", "walk_left", "walk_right", "walk_up",
          "typing_back", "typing_side", "report", "sleep", "coffee", "phone",
          "talk", "surprised")
COUNTS = (4, 4, 4, 4, 4, 3, 3, 3, 2, 3, 3, 3, 2)
FACE_ROWS = tuple(s for s in STATES if s != "idle" and s not in BACK)

# Small, reviewed calibrations keep glasses, white eyebrows, and dark skin out of
# color-heuristic ambiguity. Unlisted characters use skin/hole detection below.
# Each entry: eye/brow patches, optional mouth patch. Reference is frame zero.
PROFILES = {
    "dev-hoodie": [
        ([(8,20,15,27),(19,20,25,27)], (15,27,21,29)),
        ([(8,20,15,26)], (8,26,13,28)),
        ([(16,20,23,26)], (22,27,26,29)),
        ([(13,24,21,30)], (18,30,23,32)),
        ([(10,26,15,32)], (16,31,21,33)),
        ([(9,26,16,32),(20,26,26,32)], (16,32,20,34)),
        ([(9,20,16,27),(20,20,26,26)], None),
        ([(9,20,16,26),(21,20,26,26)], (15,26,21,28)),
        ([(10,22,17,28),(22,21,27,27)], None),
        ([(9,19,15,25),(20,19,25,25)], (15,25,19,28)),
    ],
    "glasses-bun": [
        ([(8,20,26,29)], (15,29,20,31)),
        ([(6,22,18,31)], (9,31,14,33)),
        ([(18,22,29,31)], (22,30,26,32)),
        ([(12,19,27,27)], (19,27,24,29)),
        ([(11,22,23,29)], (16,29,21,31)),
        ([(8,24,24,33)], (15,32,20,34)),
        ([(10,18,26,27)], None),
        ([(10,18,26,26)], (16,26,21,28)),
        ([(8,18,27,27)], None),
        ([(8,20,25,28)], (15,27,20,30)),
    ],
    "professor": [
        ([(8,27,14,33),(18,27,24,33)], (15,34,19,36)),
        ([(8,24,14,31)], (6,30,12,33)),
        ([(17,23,24,30)], (23,30,27,33)),
        ([(12,20,18,26),(21,20,27,26)], (17,27,22,29)),
        ([(20,21,27,29)], (22,29,28,32)),
        ([(7,25,15,30),(18,24,25,29)], (16,31,20,33)),
        ([(10,24,16,30),(20,24,26,30)], None),
        ([(10,21,16,27),(21,21,27,27)], (16,28,21,30)),
        ([(11,22,17,29),(21,22,26,29)], None),
        ([(8,20,14,29),(19,20,25,29)], (15,29,19,31)),
    ],
    "punk-girl": [
        ([(10,17,16,25),(19,16,26,25)], (15,25,20,28)),
        ([(11,20,18,27)], (12,27,17,29)),
        ([(14,19,20,25),(22,19,27,25)], (19,26,24,28)),
        ([(12,18,18,25),(23,18,28,25)], (17,25,22,28)),
        ([(18,22,25,30)], (20,29,25,31)),
        ([(9,28,15,34),(21,28,25,34)], (16,33,20,35)),
        ([(10,20,17,28),(20,20,27,28)], None),
        ([(11,19,17,27),(21,19,26,27)], (16,27,21,29)),
        ([(11,17,18,24),(21,17,27,24)], None),
        ([(9,15,16,25),(19,15,25,25)], (15,26,19,29)),
    ],
    "p5": [
        ([(9,33,12,38),(18,33,21,38)], None),
        ([(12,35,16,41)], None),
        ([(19,34,22,40)], None),
        ([(13,31,16,36)], None),
        ([(16,32,20,36)], None),
        ([(10,35,15,39),(18,35,23,39)], None),
        ([(10,31,15,36),(19,31,23,36)], None),
        ([(12,31,16,36),(19,31,22,36)], None),
        ([(13,31,16,36),(20,31,23,36)], None),
        ([(11,32,14,37),(19,32,22,37)], (15,36,18,38)),
    ],
    "beanie-scarf": [
        ([(8,27,14,33),(18,27,24,33)], (12,33,20,36)),
        ([(9,30,16,36)], (10,35,16,37)),
        ([(14,30,19,35),(21,29,25,35)], (17,35,23,38)),
        ([(12,29,17,35),(21,29,25,35)], (16,35,22,38)),
        ([(18,28,23,34)], (18,33,23,35)),
        ([(9,32,15,37),(19,32,25,37)], (16,36,20,38)),
        ([(13,27,18,34),(21,27,25,34)], None),
        ([(12,26,17,33),(21,26,25,33)], (17,32,21,35)),
        ([(11,27,17,34),(21,27,25,34)], None),
        ([(10,29,16,35),(20,29,25,35)], (16,34,20,36)),
    ],
    "labcoat-tall": [
        ([(10,25,15,30),(18,24,23,30)], (15,30,20,33)),
        ([(12,25,17,31)], (11,30,16,32)),
        ([(16,26,21,31)], (18,30,23,33)),
        ([(11,29,16,34),(20,29,24,34)], (16,33,21,36)),
        ([(17,25,20,28)], (15,28,19,30)),
        ([(10,30,15,33),(19,30,23,33)], (14,33,19,35)),
        ([(11,25,16,30),(20,25,24,30)], None),
        ([(11,26,17,30),(20,26,24,30)], (16,30,21,33)),
        ([(11,25,16,30),(20,25,24,30)], None),
        ([(11,26,16,31),(20,26,24,31)], (16,31,20,33)),
    ],
    "cap-teen": [
        ([(7,21,13,27),(16,21,23,27)], (13,27,17,29)),
        ([(11,26,17,31)], (8,30,13,32)),
        ([(16,24,22,30)], (21,29,25,32)),
        ([(13,23,19,30),(23,23,27,28)], (19,30,23,32)),
        ([(17,18,23,25)], None),
        ([(8,28,13,33),(15,28,20,33)], (13,34,18,36)),
        ([(7,18,13,26),(16,18,23,26)], None),
        ([(7,21,13,27),(16,21,22,27)], (12,27,18,29)),
        ([(7,19,13,26),(16,19,22,26)], None),
        ([(9,19,15,26),(17,19,23,26)], (13,27,17,30)),
    ],
    "cat-hood": [
        ([(9,28,15,35),(18,28,24,35)], (14,34,19,36)),
        ([(8,29,14,35)], (7,34,12,37)),
        ([(17,28,22,35),(23,29,26,34)], (21,34,26,37)),
        ([(18,32,23,38)], (19,38,23,40)),
        ([(15,27,20,33)], (17,33,20,35)),
        ([(9,37,14,40),(17,35,22,39)], (14,40,19,42)),
        ([(17,27,23,33)], None),
        ([(16,30,21,35)], None),
        ([(17,27,23,33)], None),
        ([(11,28,16,34),(19,28,24,34)], (14,33,19,35)),
    ],
    "manager-suit": [
        ([(9,26,15,33),(18,26,24,33)], (14,33,19,36)),
        ([(10,26,17,34)], (11,34,16,36)),
        ([(15,26,22,33)], (18,34,23,36)),
        ([(11,28,17,34),(19,28,22,34)], (13,34,18,36)),
        ([(10,26,15,32),(18,26,21,32)], (13,33,18,35)),
        ([(10,30,15,35),(17,30,23,35)], (14,37,19,39)),
        ([(11,27,17,33),(19,26,24,33)], None),
        ([(12,26,17,32),(20,26,24,32)], (15,32,20,34)),
        ([(9,24,15,32),(17,24,22,32)], None),
        ([(9,25,15,33),(18,25,23,33)], (14,33,19,37)),
    ],
}
BLINK_EYES = {
    "dev-hoodie": [[(9,22,14,27),(20,22,24,27)],[(9,22,15,26)],[(17,22,22,26)]],
    "glasses-bun": [[(10,23,14,27),(19,23,23,27)],[(11,25,15,29)],[(20,25,25,29)]],
    "professor": [[(9,28,14,33),(18,28,24,33)],[(9,26,14,31)],[(18,25,24,30)]],
    "punk-girl": [[(10,19,16,25),(19,19,25,25)],[(11,22,18,27)],[(14,20,21,26),(22,20,27,26)]],
    "p5": [[(9,34,12,38),(18,34,21,38)],[(12,36,16,41)],[(19,35,22,40)]],
    "cap-teen": [[(8,23,13,27),(17,23,22,27)],[(11,27,17,30)],[(17,25,22,30)]],
}
BLINK_SKIN_POINTS = {"professor": [(16,30),(14,29),(24,28)]}
# Idle anchors were authored with different head pitch/scale. These placements
# were reviewed against actual eye positions; hair cross-correlation alone is
# insufficient across those pose changes. No scaling of facial pixels occurs.
IDLE_OFFSETS = {
    "dev-hoodie": [[0,-2],[-2,-1],[3,-1]],
    "glasses-bun": [[0,-2],[-1,-2],[1,-4]],
    "professor": [[0,-7],[1,-2],[2,-2]],
    "punk-girl": [[0,-1],[-2,-3],[4,-1]],
    "p5": [[0,-1],[-3,-4],[-1,-4]],
    "cat-hood": [[0,-1],[0,-1],[2,-1]],
    "manager-suit": [[0,-1],[-1,-2],[2,-2]],
}
# The cat's right idle hides the far eye visible in its three-quarter walk.
IDLE_EYE_BOXES = {"cat-hood": {"right": [(17,28,22,35)]}}
ROW_OFFSETS = {"p5": {"sleep": [[0,0],[0,0]]},
               "cap-teen": {"report": [[0,0],[-2,5],[-4,9]]},
               "cat-hood": {"sleep": [[0,0],[0,-1]]},
               "manager-suit": {"sleep": [[0,0],[1,-1]]}}


class Incomplete(ValueError):
    """Input is not a complete, stable publication."""


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def rgba(path):
    with Image.open(path) as im:
        return np.array(im.convert("RGBA"))


def shift(a, dx, dy):
    """Zero padded translation; no wrap, interpolation, or alpha compositing."""
    out = np.zeros_like(a)
    h, w = a.shape[:2]
    if abs(dx) < w and abs(dy) < h:
        x0, x1 = max(0, dx), min(w, w + dx)
        y0, y1 = max(0, dy), min(h, h + dy)
        out[y0:y1, x0:x1] = a[y0-dy:y1-dy, x0-dx:x1-dx]
    return out


def box_mask(shape, boxes):
    mask = np.zeros(shape[:2], dtype=bool)
    for x0, y0, x1, y1 in boxes:
        if not (0 <= x0 < x1 <= shape[1] and 0 <= y0 < y1 <= shape[0]):
            raise ValueError(f"face box outside cell: {(x0,y0,x1,y1)}")
        mask[y0:y1, x0:x1] = True
    return mask


def dilate(mask, radius=1):
    return np.logical_or.reduce([shift(mask, x, y) for x in range(-radius,radius+1)
                                 for y in range(-radius,radius+1)])


def alignment(reference, frame, boxes):
    """Cross-correlate alpha/outline and hair above the face, never the eyes."""
    alpha = reference[:,:,3] > 0
    top = int(np.where(alpha)[0].min())
    bottom = min(b[1] for b in boxes)
    region = np.zeros(alpha.shape, dtype=bool)
    region[max(0, top-2):bottom] = True
    if region.sum() == 0:
        raise ValueError("no head silhouette above eyes")
    edge = alpha & ~np.logical_and.reduce([shift(alpha,1,0),shift(alpha,-1,0),
                                           shift(alpha,0,1),shift(alpha,0,-1)])
    weights = np.where(edge, 4.0, 1.0) * region
    # Candidate is translated into the reference space. Result is ref -> frame.
    best = None
    for dy in range(-7,8):
        for dx in range(-7,8):
            a = shift(frame, -dx, -dy)
            occupied = a[:,:,3] > 0
            alpha_cost = np.sum(weights * (occupied != alpha)) / weights.sum()
            both = occupied & alpha & region
            color_cost = (np.abs(a[:,:,:3].astype(float)-reference[:,:,:3])[both].mean()/255
                          if both.any() else 1)
            score = (alpha_cost + .3 * color_cost, abs(dx)+abs(dy), abs(dy), dy, dx)
            if best is None or score < best[0]:
                best = (score, dx, dy)
    return [best[1], best[2]]


def skin_pixels(a):
    r,g,b = np.moveaxis(a[:,:,:3].astype(int),2,0)
    return (a[:,:,3]>0)&(r>95)&(g>65)&(r-g>20)&(g-b>18)&(g>r*.4)


def auto_profile(frame):
    """Conservative skin-enclosed dark-hole detection for newly finished art."""
    alpha = frame[:,:,3]>0
    top = int(np.where(alpha)[0].min())
    skin = skin_pixels(frame)
    # Ignore the body. Hair highlights are rejected by a bright skin mode.
    skin[:top+10] = False
    skin[min(frame.shape[0],top+34):] = False
    colors, counts = np.unique(frame[skin,:3],axis=0,return_counts=True)
    if not len(colors):
        raise ValueError("cannot detect skin; supply --profile with reviewed boxes")
    bright = colors[:,0] >= max(110, int(np.percentile(colors[:,0],65)))
    color = colors[bright][np.argmax(counts[bright])].astype(float)
    skin &= np.linalg.norm(frame[:,:,:3].astype(float)-color,axis=2)<65
    # Fill skin row spans, finding eyes/eyebrows inside those spans.
    hull = np.zeros_like(skin)
    for y in range(top+10,min(top+32,frame.shape[0])):
        xs = np.where(skin[y])[0]
        if len(xs)>=3:
            hull[y,xs.min():xs.max()+1] = True
    dark = (frame[:,:,:3].mean(axis=2)<color.mean()*.58)&hull
    ys,xs = np.where(dark)
    if len(xs)<2:
        raise ValueError("no skin-enclosed eyes; supply --profile")
    # ponytail: native 32x64 sprites only; reviewed boxes handle unusual hairstyles.
    y0 = int(np.percentile(ys,15))
    lower=ys>y0+7
    dark[ys[lower],xs[lower]] = False
    ys,xs=np.where(dark)
    rgb=frame[:,:,:3].astype(int)
    white=hull & (rgb[:,:,1]>205) & (rgb[:,:,2]>140) & (rgb[:,:,0]-rgb[:,:,1]<45)
    wy,wx=np.where(white)
    if len(wy):
        # Highlights identify the eye band independently of lower mouth dots.
        # Leave up to three pixels above for brows and two below for the iris.
        start=int(wy.min())-3
        end=min(int(wy.max())+2,start+9)
        keep=(ys>=start)&(ys<=end)
    else:
        keep=ys<=y0+5  # closed lids; do not absorb a lower mouth into talk eyes.
    if keep.any():
        ys,xs=ys[keep],xs[keep]
    box=(max(0,int(xs.min())-1),max(0,int(ys.min())-1),
         min(frame.shape[1],int(xs.max())+2),min(frame.shape[0],int(ys.max())+2))
    return [box], None


def discover(art=ART):
    for mp in sorted((art/"sheets").glob("*.manifest.json")):
        m=read_json(mp)
        if "walk_down" in m.get("frame_layout",{}).get("rows",{}):
            yield mp.stem.removesuffix(".manifest"), mp, mp.with_name(mp.name.replace(".manifest.json",".png")), None
    for d in sorted((art/"roster").glob("*")):
        if not d.is_dir():
            continue
        mp=d/f"{d.name}.manifest.json"
        if mp.exists() and (d/f"{d.name}.png").exists():
            yield d.name,mp,d/f"{d.name}.png",d
        else:
            yield d.name,d/"run/manifest.json",d/"run/sprite-sheet-alpha.png",d


def load_source(name, manifest_path, sheet_path, roster=None, frames_dir=None):
    """Snapshot all declared input bytes; reject partial/mismatched publications."""
    blobs={}
    def read(p):
        p=Path(p).resolve()
        if not p.is_file():
            raise Incomplete(f"missing {p.name}")
        b=p.read_bytes();blobs[p]=b
        return b
    try:
        m=json.loads(read(manifest_path).decode("utf-8-sig"))
    except json.JSONDecodeError as e:
        raise Incomplete("manifest is not yet valid JSON") from e
    layout=m.get("frame_layout",{})
    rows=layout.get("rows",{})
    if set(rows)!=set(STATES) or any(len(rows[s])!=n for s,n in zip(STATES,COUNTS)):
        raise Incomplete("expected all 13 rows / 42 declared frames")
    if any(m.get("animation",{}).get("rows",{}).get(s,{}).get("frames")!=n for s,n in zip(STATES,COUNTS)):
        raise Incomplete("animation counts disagree with final frame layout")
    if (layout.get("cellWidth"),layout.get("cellHeight"))!=(32,64):
        raise ValueError("only native 32x64 character cells are supported")
    if sheet_path and Path(sheet_path).exists():
        with Image.open(io.BytesIO(read(sheet_path))) as im:
            sheet=np.array(im.convert("RGBA"))
    else:
        if frames_dir is None:
            raise Incomplete("missing final sheet")
        sheet=np.zeros((layout["sheetHeight"],layout["sheetWidth"],4),dtype=np.uint8)
    if sheet.shape[:2]!=(layout["sheetHeight"],layout["sheetWidth"]):
        raise Incomplete("sheet dimensions disagree with manifest")
    if frames_dir is None and roster and (roster/"run/curated").is_dir():
        frames_dir=roster/"run/curated"
    frames={}
    for state,rects in rows.items():
        frames[state]=[]
        for i,r in enumerate(rects):
            x,y,w,h=(r[k] for k in ("x","y","w","h"))
            if w!=32 or h!=64 or x<0 or y<0 or x+w>sheet.shape[1] or y+h>sheet.shape[0]:
                raise Incomplete(f"invalid cell {state}/{i}")
            a=sheet[y:y+h,x:x+w].copy()
            if frames_dir:
                with Image.open(io.BytesIO(read(Path(frames_dir)/f"{state}-frame-{i}.png"))) as im:
                    exported=np.array(im.convert("RGBA"))
                if sheet_path and Path(sheet_path).exists() and not np.array_equal(exported,a):
                    raise Incomplete(f"curated/sheet mismatch {state}/{i}; producer may be publishing")
                a=exported
                sheet[y:y+h,x:x+w]=a
            if a.shape!=(64,32,4) or not a[:,:,3].any():
                raise Incomplete(f"missing/empty frame {state}/{i}")
            frames[state].append(a)
    return m,sheet,frames,blobs


def stable(blobs):
    return all(p.is_file() and p.read_bytes()==b for p,b in blobs.items())


def write_bytes(path, data):
    """No change on an identical rerun; each published file is atomic."""
    if path.exists() and path.read_bytes()==data:
        return
    path.parent.mkdir(parents=True,exist_ok=True)
    temp=path.with_name(path.name+f".{os.getpid()}.tmp")
    temp.write_bytes(data)
    temp.replace(path)


def png_bytes(a):
    stream=io.BytesIO();Image.fromarray(a).save(stream,format="PNG")
    return stream.getvalue()


def json_bytes(m):
    return (json.dumps(m,ensure_ascii=False,indent=2)+"\n").encode("utf-8")


def lock_row(frames, boxes, mouth, reference=None, ref_source=None, offsets=None):
    reference=frames[0] if reference is None else reference
    reviewed_offsets=offsets is not None
    offsets=offsets or [alignment(reference,a,boxes) for a in frames]
    eye=box_mask(reference.shape,boxes)
    patch=eye.copy()
    if mouth:
        patch |= box_mask(reference.shape,[mouth])
    # Copy only common opaque support: alpha/silhouette can never change.
    support=reference[:,:,3]>0
    for a,(dx,dy) in zip(frames,offsets):
        support &= shift(a,-dx,-dy)[:,:,3]>0
    # Box corners may include transparent cheek/outline pixels. Those are not
    # facial features and must not change the animation's alpha silhouette.
    eye &= support
    patch &= support
    locked=[]
    for a,(dx,dy) in zip(frames,offsets):
        mask=shift(patch,dx,dy)
        source=shift(reference,dx,dy)
        b=a.copy();b[mask,:3]=source[mask,:3]
        locked.append(b)
    record={"reference":ref_source or {"state":"self","frame":0},
            "eyeBoxes":[list(b) for b in boxes], "mouthBox":list(mouth) if mouth else None,
            "offsets":offsets,"patchPixels":int(patch.sum()),
            "alignment":"reviewed integer placement" if reviewed_offsets else "head alpha/outline + hair cross-correlation",
            "eyePixels":[[int(y),int(x)] for y,x in zip(*np.where(eye))],
            "silhouetteExcludedPixels":int((box_mask(reference.shape,boxes)&~support).sum()),
            "patchRuns":[[int(y),int(x)] for y,x in zip(*np.where(patch))]}
    return locked,record


def blink_frame(frame, eye_boxes, skin_point=None):
    result=frame.copy()
    for x0,y0,x1,y1 in eye_boxes:
        area=box_mask(frame.shape,[(x0,y0,x1,y1)])
        rgb=frame[:,:,:3].astype(float)
        warm=skin_pixels(frame)&(rgb[:,:,1]<=rgb[:,:,0]*.85)
        nearby=area&warm
        if not nearby.any():
            nearby=dilate(area,3)&~area&warm
        if not nearby.any():
            raise ValueError("blink has no nearby skin")
        colors,counts=np.unique(frame[nearby,:3],axis=0,return_counts=True)
        skin=(frame[skin_point[1],skin_point[0],:3] if skin_point else colors[counts.argmax()])
        # A single native-pixel lid, using the darkest original eye color.
        pixels=frame[area,:3];ink=pixels[np.argmin(pixels.mean(axis=1))]
        area &= frame[:,:,3]>0
        result[area,:3]=skin
        y=y0+(y1-y0)//2
        lid=np.zeros_like(area);lid[y,x0:x1]=True;lid &= area
        result[lid,:3]=ink
    return result


def process(name, mp, sp, roster=None, make_blink=False, frames_dir=None, profile_path=None):
    m,sheet,frames,blobs=load_source(name,mp,sp,roster,frames_dir)
    p5=re.fullmatch(r"(?:codex|claude|antigravity)-(?:worker|supervisor|subagent)-0[1-4]|worker(?:-(?:subagent|supervisor))?",name)
    profile_key=name if name in PROFILES else ("p5" if p5 else None)
    profile=PROFILES.get(profile_key)
    if profile_path:
        # Same ordering as FACE_ROWS; JSON permits explicit per-character review.
        profile=read_json(profile_path)["rows"]
        profile_key="custom"
    results={s:[a.copy() for a in aa] for s,aa in frames.items()}
    records={}
    for row_index,state in enumerate(FACE_ROWS):
        boxes,mouth=profile[row_index] if profile else auto_profile(frames[state][0])
        if state in ("talk","coffee"):
            mouth=None  # talk articulates; cup occludes the coffee mouth.
        reference=frames[state][0]
        if profile_key=="p5" and state=="sleep":
            # The tilted second source puts its lids below the hair-based bob.
            # Erase the union of both old lid footprints and author thin closed
            # lids once, avoiding a leftover second eye below the locked patch.
            reference=blink_frame(reference,boxes)
        results[state],records[state]=lock_row(frames[state],boxes,mouth,
                                               reference=reference,
                                               ref_source={"state":state,"frame":0},
                                               offsets=ROW_OFFSETS.get(profile_key,{}).get(state))
    # Idle slots share their walking-facing face. Do not animate idle directions.
    for i,direction in enumerate(("down","left","right")):
        state=f"walk_{direction}";r=records[state]
        boxes=r["eyeBoxes"]
        # Her right idle is a true profile; its far eye is hidden behind hair.
        if profile_key=="punk-girl" and direction=="right":
            boxes=boxes[:1]
        boxes=IDLE_EYE_BOXES.get(profile_key,{}).get(direction,boxes)
        out,record=lock_row([frames["idle"][i]],boxes,None,
                            reference=frames[state][0],ref_source={"state":state,"frame":0},
                            offsets=[IDLE_OFFSETS[profile_key][i]] if profile_key in IDLE_OFFSETS else None)
        results["idle"][i]=out[0];record["idleColumn"]=i
        records[f"idle_{direction}"]=record
    outdir=(roster if roster else Path(mp).parent)/"locked"
    sheet_name=Path(sp).name if sp else "sprite-sheet-alpha.png"
    manifest_name=Path(mp).name
    files={}
    for state,rects in m["frame_layout"]["rows"].items():
        for i,(r,a) in enumerate(zip(rects,results[state])):
            x,y,w,h=(r[k] for k in ("x","y","w","h"));sheet[y:y+h,x:x+w]=a
            if roster or frames_dir:
                files[f"curated/{state}-frame-{i}.png"]=png_bytes(a)
    files[sheet_name]=png_bytes(sheet)
    m=copy.deepcopy(m)
    m["game_input"]=sheet_name;m["sprite_sheet_alpha"]=sheet_name
    m.pop("blink",None)
    blink={}
    if make_blink:
        for i,direction in enumerate(("down","left","right")):
            state=f"walk_{direction}"
            boxes=BLINK_EYES.get(profile_key,[None]*3)[i]
            if boxes is None:
                boxes=[(b[0],min(b[3]-1,b[1]+2),b[2],b[3]) for b in records[state]["eyeBoxes"]]
            file=(f"{name}-blink-{direction}.png" if roster is None else f"blink-{direction}.png")
            point=BLINK_SKIN_POINTS.get(profile_key,[None]*3)[i]
            files[file]=png_bytes(blink_frame(results[state][0],boxes,point))
            blink[direction]={"file":file,"source":{"state":state,"frame":0},"durationMs":100}
        m["blink"]=blink
    input_hash=hashlib.sha256(b"".join(b for _,b in sorted(blobs.items(),key=lambda p:str(p[0])))).hexdigest()
    report={"version":VERSION,"character":name,"profile":profile_key or "automatic-unreviewed",
            "sourceManifest":os.path.relpath(Path(mp).resolve(),outdir.resolve()).replace("\\","/"),
            "sourceSheet":os.path.relpath(Path(sp).resolve(),outdir.resolve()).replace("\\","/") if sp else None,
            "sourceFrames":os.path.relpath(Path(frames_dir).resolve(),outdir.resolve()).replace("\\","/") if frames_dir else None,
            "inputSha256":input_hash,"sourceHashes":{str(p.relative_to(ROOT)) if p.is_relative_to(ROOT) else str(p):hashlib.sha256(b).hexdigest() for p,b in blobs.items()},
            "outputSheet":sheet_name,"outputManifest":manifest_name,"rows":records,
            "skippedRows":sorted(BACK),"blink":blink}
    m["facelock"]={"version":VERSION,"report":f"{name}.facelock.json","inputSha256":input_hash}
    files[manifest_name]=json_bytes(m)
    if roster:
        # Stable filenames for downstream adapters irrespective of team layout.
        alias=copy.deepcopy(m);alias["game_input"]=f"{name}.png";alias["sprite_sheet_alpha"]=f"{name}.png"
        files[f"{name}.png"]=files[sheet_name]
        files[f"{name}.manifest.json"]=json_bytes(alias)
    files[f"{name}.facelock.json"]=json_bytes(report)
    if not stable(blobs):
        raise Incomplete("input changed during read; rerun after producer finishes")
    # Never resolve an output into a source/run directory, including symlinks.
    if outdir.is_symlink() or outdir.resolve()==Path(mp).parent.resolve():
        raise ValueError("unsafe locked output directory")
    for relative,data in files.items():
        target=outdir/relative
        if not target.resolve().is_relative_to(outdir.resolve()) or target.is_symlink():
            raise ValueError("output escapes locked directory")
        write_bytes(target,data)
    return report,outdir


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    choice=ap.add_mutually_exclusive_group(required=True)
    choice.add_argument("--all",action="store_true")
    choice.add_argument("--manifest",type=Path)
    ap.add_argument("--sheet",type=Path)
    ap.add_argument("--frames",type=Path,help="final flat curated PNG directory instead of a sheet")
    ap.add_argument("--profile",type=Path,help="reviewed rows: [[eyeBoxes,mouthBox], ...] in FACE_ROWS order")
    ap.add_argument("--blink",action="store_true",help="also export closed-eye single frames")
    args=ap.parse_args()
    if args.all and any((args.sheet,args.frames,args.profile)):
        ap.error("--sheet/--frames/--profile require --manifest")
    if args.all:
        jobs=list(discover())
    else:
        roster=(args.manifest.parent.parent if args.manifest.parent.name=="run" else
                args.manifest.parent if args.manifest.parent.parent.name=="roster" else None)
        name=roster.name if roster else args.manifest.stem.removesuffix(".manifest")
        sheet=args.sheet or (args.manifest.parent/read_json(args.manifest)["sprite_sheet_alpha"] if not args.frames else None)
        jobs=[(name,args.manifest,sheet,roster)]
    done=skipped=failed=0
    for name,mp,sp,roster in jobs:
        try:
            report,outdir=process(name,mp,sp,roster,args.blink,args.frames,args.profile)
            print(f"LOCK {name}: {len(report['rows'])} face groups -> {outdir.relative_to(ROOT) if outdir.is_relative_to(ROOT) else outdir} [{report['profile']}]",flush=True)
            done+=1
        except Incomplete as e:
            print(f"SKIP {name}: {e}",flush=True);skipped+=1
        except (ValueError,OSError,KeyError) as e:
            print(f"ERROR {name}: {e}",flush=True);failed+=1
    print(f"locked={done} incomplete={skipped} errors={failed}")
    return 1 if failed else 0


if __name__=="__main__":
    raise SystemExit(main())
