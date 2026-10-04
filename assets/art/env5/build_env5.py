"""ENV5 office customization art. Every write is confined to this directory.

Use the installed sprite-gen venv; prepare / recolor / build / check.
No runtime installation, network access in build/check, or source-art mutation.
"""
from __future__ import annotations

import argparse
import copy
from collections import Counter
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
ART = ROOT.parent
REPO = ART.parents[1]
ENV2 = ART / "env2"
CLI = Path(os.environ["SPRITE_GEN_ROOT"]) / ".venv/Scripts/sprite-gen.exe" if os.environ.get("SPRITE_GEN_ROOT") else "sprite-gen"
sys.path.insert(0, str(REPO))
from tools.anim_normalize import normalize_frames

C = {
    "ink": (25,30,30), "dark": (16,40,44), "navy": (38,45,56),
    "walnut": (57,38,28), "wood": (120,65,40), "wood_mid": (154,92,58),
    "honey": (181,128,67), "gold": (206,137,79),
    "ivory_dark": (219,197,163), "ivory": (231,209,172),
    "cream": (250,236,201), "paper": (249,246,227),
    "teal_dark": (29,58,57), "teal": (35,94,92), "green": (52,136,132),
    "sage": (93,177,165), "blue_dark": (50,85,113), "blue": (72,114,143),
    "blue_light": (100,146,163), "red_dark": (97,40,43),
    "red": (154,66,64), "red_light": (191,101,84),
    "grey_dark": (62,64,63), "grey": (99,106,103), "grey_mid": (128,137,133),
    "grey_light": (139,147,144), "pink": (189,154,148),
    "lilac_dark": (66,63,126), "lilac": (100,100,184), "lilac_light": (156,163,221),
    "olive": (116,121,103), "olive_light": (137,141,123),
}
PALETTE = {tuple(c) for c in json.loads((ENV2 / "palette.json").read_text(encoding="utf-8"))["colors"]}
assert set(C.values()) <= PALETTE
KEY = {"name":"magenta","hex":"#FF00FF","rgb":[255,0,255],"selection":"manual"}
SOURCES = {}
ITEMS = []
REGIONS = {}

def output(relative):
    p = (ROOT / relative).resolve()
    if not p.is_relative_to(ROOT):
        raise ValueError(f"Write outside env5: {relative}")
    p.parent.mkdir(parents=True, exist_ok=True)
    return p

def jsave(relative, data):
    output(relative).write_text(json.dumps(data, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")

def track(p):
    SOURCES[p.relative_to(REPO).as_posix()] = hashlib.sha256(p.read_bytes()).hexdigest()

def existing(name, frame=0, state=None, env="env2"):
    directory = ART / env / "sheets"
    mp = directory / f"{name}.manifest.json"
    track(mp)
    m = json.loads(mp.read_text(encoding="utf-8"))
    p = directory / m["game_input"]
    track(p)
    r = m["frame_layout"]["rows"][state or next(iter(m["frame_layout"]["rows"]))][frame]
    with Image.open(p) as im:
        return clean(im.convert("RGBA").crop((r["x"],r["y"],r["x"]+r["w"],r["y"]+r["h"])))

def rgba(c):
    return (*C[c],255) if isinstance(c,str) else (*c,255)

def blank(size=64):
    return Image.new("RGBA",(size,size))

def clean(im):
    a = np.array(im.convert("RGBA")); a[a[:,:,3]==0] = 0
    return Image.fromarray(a)

def quantize(im):
    a = np.array(im.convert("RGBA")); visible = a[:,:,3]>=128
    colors = np.array(list(C.values()), dtype=np.int32)
    nearest = ((a[:,:,:3].astype(np.int32)[:,:,None,:]-colors)**2).sum(3).argmin(2)
    a[:,:,:3] = colors[nearest]; a[:,:,3] = visible*255; a[~visible] = 0
    return Image.fromarray(a)

def fit(im, width, height, bottom=61):
    im = im.crop(im.getbbox())
    scale = min(width/im.width,height/im.height)
    im = im.resize((max(1,round(im.width*scale)),max(1,round(im.height*scale))),Image.Resampling.NEAREST)
    cell = blank(); cell.alpha_composite(im,((64-im.width)//2,bottom-im.height))
    return clean(cell)

def prepare():
    ref = Image.new("RGBA",(512,128),rgba("cream"))
    for i,(name,f) in enumerate([("kitchen-counter-sink",0),("meeting-table-chairs",0),
          ("bookshelf",0),("whiteboard",0),("plants",4),("sofa-red",0),("desks",0),("office-chairs",0)]):
        ref.alpha_composite(existing(name,f),(i*64,32))
    ref.resize((1536,384),Image.Resampling.NEAREST).save(output("references/style.png"))
    actions = {
      "office_a": (5,"Five DIFFERENT static office objects, left to right: 0 KITCHEN ISLAND with THREE joined round stools in front, warm walnut cabinets, ivory countertop with teal kettle and tiny mug, visible flat front and tabletop; 56x36 logical pixels. 1 LARGE CONFERENCE TABLE, long horizontal walnut top viewed a little from above, THREE teal chairs along FAR long edge, THREE teal chairs along NEAR long edge, ONE chair at EACH short end (EIGHT distinct chairs total); 58x42 logical pixels; tiny paper and pitcher on top; all chairs touch table, complete connected grouping. 2 MOBILE WHITEBOARD on wheels, ivory framed horizontal white writing surface with short teal/red sketch dashes, walnut/charcoal posts, marker tray, two caster wheels, whole connected silhouette; 42x44 pixels. 3 PRINTER STATION, cream office printer on walnut two-door storage pedestal, sheets emerging at top, paper reams in lower shelf; 32x40 pixels. 4 PLANT WALL, one walnut-framed vertical living garden panel, densely layered green fern and monstera leaves, two or three flat teal-green shades; 48x42 pixels. All objects separate with huge magenta gaps."),
      "office_b": (4,"Four DIFFERENT static office props, left to right: 0 COAT RACK, dark walnut standing tree with three hooks, an ivory coat and a dusty blue scarf hanging from hooks, splayed feet, full connected silhouette; 28x48 logical pixels. 1 SHOE RACK, low walnut two-tier open bench with three pairs of cream/blue/red shoes, complete connected object, no floor; 42x24 pixels. 2 RETRO TELEVISION, complete warm walnut cabinet, cream/dark round-corner CRT glass, small knobs on right, attached short V-shaped antenna and four short feet; 36x36 pixels. 3 GAME CONSOLE CORNER, low walnut media cabinet with a charcoal flat TV on top, empty dark teal screen, tiny ivory game console and two controllers on middle shelf, everything touching cabinet as one connected unit; 48x38 pixels. Huge solid magenta gaps between objects.")
    }
    request = {"version":1,"kind":"sprite-gen-request","engine":"component-row",
      "character":{"id":"env5-office-bodies","description":"Different static props; reference gives ONLY style and perspective, not identity.","base_image":"base-source.png"},
      "cell":{"shape":"square","width":64,"height":64,"safe_margin_x":2,"safe_margin_y":3},
      "chroma_key":KEY,
      "states":{s:{"frames":n,"fps":1,"loop":False,"action":a} for s,(n,a) in actions.items()},
      "style":"Match the attached ENV2 office sprites: orthographic front view with a little top surface visible, level horizontal edges, NEVER isometric. True chunky pixel art, single logical pixel outlines, warm walnut/honey, ivory, muted teal, dusty blue and brick red. Two or three flat shades per material. No antialiasing, gradients, blurry pixels, floor patches, detached ground shadows, people, words, grid lines or borders. STATIC choices, not animation poses. Complete connected objects, visible feet, widely separated, solid #FF00FF background. Each requested item exactly once.",
      "layout":"taxonomy/v1","fit":{"pixel_unfake":False,"resample":"kcentroid","palette_size":32,"align_x":"bbox-center","align_y":"bottom","ground_frames":True},
      "chroma":{"mode":"rgb","unmix_reach":4,"spill_max_fraction":0.005}}
    jsave("request-bodies.json",request)
    output("palette.json").write_bytes((ENV2/"palette.json").read_bytes())
    print("Prepared ENV5 reference and two static component rows (9 new bodies).")

def sheet(name, rows, *, size=64, mask=None, note="", body=None):
    """Copy production schema, never insert catalog/collision fields into it."""
    template_name = "plants" if size==64 else "wall-navy"
    template = json.loads((ENV2/f"sheets/{template_name}.manifest.json").read_text(encoding="utf-8"))
    columns = max(len(v[0]) for v in rows.values())
    canvas = Image.new("RGBA",(columns*size,len(rows)*size))
    animation, layout = {}, {}
    for row,(state,data) in enumerate(rows.items()):
        frames,fps,loop = data[:3]
        animation[state] = {"row":row,"frames":len(frames),"fps":fps,
            "durations_ms":data[3] if len(data)>3 else [round(1000/fps)]*len(frames),
            "loop":loop,"frame_variant":"pixel"}
        layout[state] = []
        for col,im in enumerate(frames):
            assert im.size==(size,size)
            canvas.paste(clean(im),(col*size,row*size))
            layout[state].append({"x":col*size,"y":row*size,"w":size,"h":size})
    template.update(characterId=name, game_input=f"{name}.png", sprite_sheet_alpha=f"{name}.png",
        curation_applied=False, base_image=f"../bodies/{name}.png", sprite_sheet_alpha_report="../qa/export-report.json")
    template["cell"].update(width=size,height=size,safe_margin_x=2 if size==64 else 0,safe_margin_y=3 if size==64 else 0)
    template["animation"] = {"cellWidth":size,"cellHeight":size,"columns":columns,"rows":animation}
    template["frame_layout"] = {"sheetWidth":canvas.width,"sheetHeight":canvas.height,
        "cellWidth":size,"cellHeight":size,"rows":layout}
    canvas.save(output(f"sheets/{name}.png")); jsave(f"sheets/{name}.manifest.json",template)
    clean(body or next(iter(rows.values()))[0][0]).save(output(f"bodies/{name}.png"))
    if mask is not None:
        Image.fromarray(np.asarray(mask,dtype=np.uint8)*255).save(output(f"qa/masks/{name}.png"))
        REGIONS[name] = {"mask":f"qa/masks/{name}.png","note":note,"compare_across_states":True}

def choice(name, frames, **kw):
    sheet(name,{"variants":(frames,1,False)},**kw)

def item(ident, name, category, sheet_name, *, state="variants", frame=0, footprint=(1,1),
         blocking=True, tags=(), family=None, rotations=None, size=64, layer=1, note=""):
    source = {"sheet":f"sheets/{sheet_name}.png","manifest":f"sheets/{sheet_name}.manifest.json",
              "state":state,"frame":frame}
    rotation_list = rotations or [{"rotation":0,"source":source,"footprint":{"width":footprint[0],"height":footprint[1]}}]
    ITEMS.append({"id":ident,"name":name,"category":category,"tags":list(tags),
        "footprint":{"width":footprint[0],"height":footprint[1]},"blocking":blocking,
        "source":source,"rotationVariants":rotation_list,"recolorFamily":family,
        "cellSize":{"width":size,"height":size},"anchor":{"x":32 if size==64 else 0,"y":61 if size==64 else 0},
        "suggestedScale":1 if size==64 else .5,"layer":layer,"notes":note})

def rotated_sources(name, states, footprints):
    return [{"rotation":angle,"source":{"sheet":f"sheets/{name}.png","manifest":f"sheets/{name}.manifest.json",
            "state":state,"frame":0},"footprint":{"width":fp[0],"height":fp[1]}}
            for angle,state,fp in zip([0,90,180,270],states,footprints)]

FLOORS = [
    ("wood-maple","Maple planks","wood",("honey","ivory_dark","ivory")),
    ("wood-ash","Ash planks","wood",("grey","ivory_dark","cream")),
    ("wood-cherry","Cherry planks","wood",("wood","wood_mid","red_light")),
    ("wood-bamboo","Bamboo strips","bamboo",("honey","ivory","cream")),
    ("wood-reclaimed","Reclaimed planks","wood",("walnut","wood_mid","honey")),
    ("wood-herringbone","Herringbone oak","herringbone",("wood_mid","honey","ivory")),
    ("wood-light-oak","Light oak","wood",("honey","ivory_dark","cream")),
    ("wood-dark-walnut","Dark walnut","wood",("walnut","wood","wood_mid")),
    ("concrete","Warm concrete","concrete",("grey","grey_mid","grey_light")),
    ("polished-stone","Polished limestone","stone",("grey_mid","ivory_dark","cream")),
    ("carpet-sage","Sage carpet","carpet",("teal_dark","teal","green")),
    ("carpet-cream","Cream carpet","carpet",("ivory_dark","ivory","cream")),
    ("carpet-lilac","Lilac carpet","carpet",("lilac_dark","lilac","lilac_light")),
    ("rubber-gym","Rubber gym floor","rubber",("ink","navy","grey_dark")),
    ("tatami","Tatami mat","tatami",("olive","olive_light","ivory_dark")),
    ("checker-cream","Cream / walnut checker","checker",("wood","cream","ivory")),
    ("checker-sage","Cream / sage checker","checker",("teal","cream","sage")),
    ("checker-blue","Cream / blue checker","checker",("blue_dark","cream","blue")),
    ("grass-garden","Garden grass","grass",("teal_dark","teal","green")),
    ("bathroom-tile","Bathroom ceramic","bathroom",("grey_mid","cream","paper")),
]

def floor_tile(kind, shades, phase):
    dark,mid,light = shades
    im = Image.new("RGBA",(32,32),rgba(mid)); d=ImageDraw.Draw(im)
    if kind in {"wood","bamboo"}:
        bands=4 if kind=="wood" else 8
        height=32//bands
        for band in range(bands):
            y=band*height
            if kind=="wood" and band%3==phase: d.rectangle((0,y,31,y+height-1),fill=rgba(light))
            d.line((0,y,31,y),fill=rgba(dark));d.line((0,y+1,31,y+1),fill=rgba(mid))
            seam=(band*11+phase*7)%32
            d.line((seam,y,seam,y+height-1),fill=rgba(dark))
            for k in range(3):
                x=(band*7+k*13+phase*3)%27
                d.line((x,y+3+k%2,x+3,y+3+k%2),fill=rgba(dark if k==0 else mid))
    elif kind=="herringbone":
        # Perpendicular 2:1 planks in (x+y,y-x) coordinates, with a 32 px period.
        # The alternating runs produce parquet elbows rather than a diamond grid.
        for y in range(32):
            for x in range(32):
                u=x+y+phase*4;v=y-x;t=(u//4+v//4)%4
                if t<2:
                    along=t*4+u%4;across=v%4;color=mid
                else:
                    along=(t-2)*4+v%4;across=u%4;color=light
                if along==0 or across==0:color=dark
                elif across==1 and along in {2,3,5}:color=light if t<2 else mid
                im.putpixel((x,y),rgba(color))
    elif kind in {"concrete","rubber","grass","carpet"}:
        for y in range(32):
            for x in range(32):
                h=(x*37+y*61+x*y*7+phase*29)%127
                threshold=2 if kind=="carpet" else 4
                if h<threshold: im.putpixel((x,y),rgba(dark))
                elif h>126-threshold: im.putpixel((x,y),rgba(light))
        if kind=="grass":
            for x,y in [(4,8),(17,5),(26,19),(10,26)]:
                x=(x+phase*3)%28;d.line((x,y+2,x+1,y),fill=rgba(light));d.point((x+2,y+2),fill=rgba(dark))
    elif kind in {"stone","bathroom"}:
        step=16 if kind=="stone" else 8
        for y in range(0,32,step):
            for x in range(0,32,step):
                d.rectangle((x,y,x+step-1,y+step-1),fill=rgba(dark))
                d.rectangle((x+1,y+1,x+step-1,y+step-1),fill=rgba(mid if (x//step+y//step+phase)%3 else light))
                d.line((x+2,y+2,x+step-2,y+2),fill=rgba(light))
        if kind=="stone":
            for x,y in [(4,9),(22,25)]:d.line((x,y,x+6,y-2),fill=rgba(light))
    elif kind=="tatami":
        for y in range(1,32,2):d.line((0,y,31,y),fill=rgba(light))
        d.rectangle((0,0,2,31),fill=rgba("teal_dark"));d.rectangle((29,0,31,31),fill=rgba("teal_dark"))
        for y in range(phase,32,4):d.point((1,y),fill=rgba("olive"));d.point((30,y),fill=rgba("olive"))
    elif kind=="checker":
        step=8 if phase==0 else 16
        for y in range(0,32,step):
            for x in range(0,32,step):d.rectangle((x,y,x+step-1,y+step-1),fill=rgba(dark if (x//step+y//step)%2 else mid))
    return im

def floors():
    for suffix,label,kind,shades in FLOORS:
        name=f"floor-{suffix}";frames=[floor_tile(kind,shades,i) for i in range(2)]
        choice(name,frames,size=32)
        for i in range(2):item(f"{name}-v{i+1}",f"{label} {i+1}","floors",name,frame=i,size=32,blocking=False,layer=0,tags=(kind,suffix,"tile"),family=name)
    edge_names=["north","east","south","west","north_east","south_east","south_west","north_west"]
    for suffix,shades in [("sage",("teal_dark","teal","green")),("cream",("ivory_dark","ivory","cream")),("lilac",("lilac_dark","lilac","lilac_light"))]:
        name=f"trim-carpet-{suffix}";rows={}
        for edge in edge_names:
            im=floor_tile("carpet",shades,0);d=ImageDraw.Draw(im)
            for direction in edge.split("_"):
                box={"north":(0,0,31,2),"south":(0,29,31,31),"east":(29,0,31,31),"west":(0,0,2,31)}[direction]
                d.rectangle(box,fill=rgba(shades[0]))
                line={"north":(0,2,31,2),"south":(0,29,31,29),"east":(29,0,29,31),"west":(2,0,2,31)}[direction]
                d.line(line,fill=rgba(shades[2]))
            rows[edge]=([im],1,False)
            item(f"{name}-{edge}",f"{suffix.title()} carpet {edge} trim","floors",name,state=edge,size=32,blocking=False,layer=0,tags=("trim","carpet",edge),family=f"floor-carpet-{suffix}")
        sheet(name,rows,size=32)
    material_lookup={suffix:(kind,shades) for suffix,label,kind,shades in FLOORS}
    for name,materials in [("threshold-oak-stone",("wood-light-oak","polished-stone")),("threshold-walnut-carpet",("wood-dark-walnut","carpet-sage")),("threshold-bathroom",("bathroom-tile","concrete"))]:
        rows={}
        ka,sa=material_lookup[materials[0]];kb,sb=material_lookup[materials[1]]
        base=floor_tile(ka,sa,0);second=floor_tile(kb,sb,0)
        base.paste(second.crop((0,17,32,32)),(0,17));d=ImageDraw.Draw(base)
        d.rectangle((0,14,31,17),fill=rgba("ink"));d.line((0,15,31,15),fill=rgba("ivory"));d.line((0,16,31,16),fill=rgba("honey"))
        for angle in [0,90,180,270]:
            state=f"r{angle}";rows[state]=([base.rotate(-angle)],1,False)
            item(f"{name}-{angle}",f"{name.replace('-',' ').title()} {angle}","floors",name,state=state,size=32,blocking=False,layer=0,tags=("transition","threshold"))
        sheet(name,rows,size=32)

WALLS = {
    "brick":("Brick",("wood","red","red_light")),
    "white-plaster":("White plaster",("ivory_dark","cream","paper")),
    "wallpaper-rose":("Rose wallpaper",("red_light","pink","cream")),
    "wallpaper-mint":("Mint wallpaper",("teal","sage","cream")),
    "wallpaper-blue":("Blue wallpaper",("blue","blue_light","cream")),
    "glass":("Glass partition wall",("blue_dark","blue_light","cream")),
    "wood-dark":("Dark wood panel",("walnut","wood","wood_mid")),
    "chalkboard":("Green chalkboard",("teal_dark","teal","sage")),
    "wainscot":("Ivory wainscoting",("ivory_dark","cream","paper")),
}
WALL_STATES=["panel","top_left","top_right","inside_left","inside_right","vertical",
    "tee_left","tee_right","tee_up","tee_down","door_frame","window_classic","window_wide","window_round","archway"]

def wall_tile(material,shades,state):
    dark,mid,light=shades
    im=Image.new("RGBA",(32,32),rgba(mid));d=ImageDraw.Draw(im)
    if material=="brick":
        for y in range(0,24,6):
            d.line((0,y,31,y),fill=rgba("ivory_dark"))
            for x in range(-8+(8 if y//6%2 else 0),32,16):
                d.line((x,y,x,y+5),fill=rgba("ivory_dark"));d.line((x+2,y+1,x+13,y+1),fill=rgba(light))
    elif material.startswith("wallpaper"):
        for x in range(3,32,8):
            d.line((x,0,x,23),fill=rgba(light))
            for y in range(3,24,8):d.point((x+3,y),fill=rgba(dark));d.point((x+3,y+2),fill=rgba(dark))
    elif material=="wood-dark":
        for x in range(0,32,8):d.line((x,0,x,23),fill=rgba(dark));d.line((x+2,2,x+2,22),fill=rgba(light))
    elif material=="glass":
        for x in [0,29]:d.rectangle((x,0,x+2,23),fill=rgba("grey_dark"))
        d.line((7,6,12,1),fill=rgba(light));d.line((8,14,20,2),fill=rgba(light));d.line((24,20,29,15),fill=rgba("sage"))
    elif material=="chalkboard":
        d.rectangle((2,2,29,22),outline=rgba(dark))
        for x,y,n in [(6,6,9),(6,10,15),(17,16,8)]:d.line((x,y,x+n,y),fill=rgba("ivory_dark"))
        d.rectangle((5,15,11,19),outline=rgba("sage"))
    elif material=="wainscot":
        d.rectangle((0,13,31,23),fill=rgba("ivory_dark"));d.line((0,12,31,12),fill=rgba("honey"))
        for x in range(2,32,8):d.rectangle((x,15,x+5,22),fill=rgba("cream"),outline=rgba("ivory"))
    else:
        for x,y in [(4,5),(17,13),(27,8)]:d.point((x,y),fill=rgba("ivory"))
    d.rectangle((0,24,31,25),fill=rgba("honey"));d.line((0,24,31,24),fill=rgba("ivory_dark"))
    d.rectangle((0,26,31,30),fill=rgba("walnut"));d.line((0,31,31,31),fill=rgba("ink"))
    if state in {"top_left","inside_left","tee_left"}:
        d.rectangle((0,0,3,31),fill=rgba("walnut"));d.line((3,0,3,25),fill=rgba("honey"))
    if state in {"top_right","inside_right","tee_right"}:
        d.rectangle((28,0,31,31),fill=rgba("walnut"));d.line((28,0,28,25),fill=rgba("honey"))
    if state.startswith("inside"):
        d.line((0,16,31,16),fill=rgba("honey"))
    if state in {"vertical","tee_up","tee_down"}:
        d.rectangle((14,0 if state!="tee_down" else 24,17,31 if state!="tee_up" else 25),fill=rgba("walnut"))
        d.line((15,0 if state!="tee_down" else 24,15,31 if state!="tee_up" else 25),fill=rgba("honey"))
    if state in {"tee_left","tee_right"}:
        d.rectangle((14,0,17,31),fill=rgba("walnut"));d.line((15,0,15,31),fill=rgba("honey"))
        a,b=(0,15) if state=="tee_left" else (16,31)
        d.rectangle((a,14,b,17),fill=rgba("walnut"));d.line((a,15,b,15),fill=rgba("honey"))
    if state in {"door_frame","archway"}:
        d.rectangle((6,2,25,31),fill=rgba("walnut"));d.rectangle((8,4,23,31),fill=rgba("honey"));d.rectangle((10,6,21,31),fill=rgba("dark"))
        if state=="archway":
            d.rectangle((6,2,25,7),fill=rgba(mid));d.ellipse((6,2,25,23),fill=rgba("walnut"));d.ellipse((8,4,23,23),fill=rgba("honey"));d.ellipse((10,6,21,23),fill=rgba("dark"));d.rectangle((10,15,21,31),fill=rgba("dark"))
    if state.startswith("window"):
        if state=="window_round":
            d.ellipse((6,3,25,22),fill=rgba("walnut"));d.ellipse((8,5,23,20),fill=rgba("blue_light"));d.line((9,13,22,13),fill=rgba("ivory"));d.line((15,6,15,19),fill=rgba("ivory"))
        else:
            x0,x1=(5,26) if state=="window_classic" else (2,29)
            d.rectangle((x0,3,x1,22),fill=rgba("walnut"));d.rectangle((x0+2,5,x1-2,20),fill=rgba("blue_light"))
            d.rectangle((x0+2,16,x1-2,20),fill=rgba("sage"));d.line((x0+2,12,x1-2,12),fill=rgba("ivory"));d.line((15,5,15,20),fill=rgba("ivory"))
            d.line((x0+3,8,x0+6,5),fill=rgba("cream"));d.rectangle((x0-1,22,x1+1,23),fill=rgba("honey"))
    return im

def walls():
    for material,(label,shades) in WALLS.items():
        name=f"wall-{material}"
        sheet(name,{s:([wall_tile(material,shades,s)],1,False) for s in WALL_STATES},size=32)
        for state in WALL_STATES:
            item(f"{name}-{state}",f"{label} / {state.replace('_',' ')}","walls",name,state=state,size=32,
                 blocking=state not in {"door_frame","archway"},tags=(material,state,"architecture"),family=name,
                 note="Explicit decorative tile topology; door/arch opening is walkable. No bitmask autotile inference.")

RAMPS = {
    "wood":["walnut","wood","wood_mid","honey","gold"],
    "white":["grey_mid","ivory_dark","ivory","cream","paper"],
    "black":["ink","navy","grey_dark","grey_dark","grey"],
    "green":["teal_dark","teal","green","sage","sage"],
    "blue":["dark","blue_dark","blue","blue_light","blue_light"],
    "red":["red_dark","red_dark","red","red_light","pink"],
    "lilac":["lilac_dark","lilac_dark","lilac","lilac_light","lilac_light"],
    "oak":["wood","wood_mid","honey","ivory_dark","ivory"],
    "walnut":["walnut","walnut","wood","wood_mid","honey"],
    "birch":["wood_mid","honey","ivory_dark","ivory","cream"],
}
FABRIC_COLORS=["green","blue","red","white","black","lilac"]

RECOLOR_BASES={
    "desk-front":("desks",0,None,"env2",["wood","white","black","green","blue"],"desk"),
    "desk-right":("desk-side-wood-right",0,"on","env3",["wood","white","black","green","blue"],"desk-side"),
    "desk-left":("desk-side-wood-left",0,"on","env3",["wood","white","black","green","blue"],"desk-side"),
    "chair-front":("office-chairs",0,None,"env2",FABRIC_COLORS,"teal"),
    "chair-right":("office-chair-side-right",0,None,"env3",FABRIC_COLORS,"teal"),
    "chair-left":("office-chair-side-left",0,None,"env3",FABRIC_COLORS,"teal"),
    "sofa-down":("sofa-red",0,None,"env2",FABRIC_COLORS,"red"),
    "sofa-right":("sofa-red",1,None,"env2",FABRIC_COLORS,"red"),
    "armchair":("armchair",0,None,"env2",FABRIC_COLORS,"fabric"),
    "rug":("rugs",0,None,"env2",FABRIC_COLORS,"blue"),
    "lamp":("lamp",0,None,"env2",["white","green","red"],"cream"),
    "bookshelf-open":("bookshelf",0,None,"env2",["walnut","oak","birch"],"shelf"),
    "bookshelf-cabinet":("bookshelf",1,None,"env2",["walnut","oak","birch"],"shelf"),
}

def material_mask(base,kind):
    a=np.array(base);r,g,b=np.moveaxis(a[:,:,:3].astype(np.int32),2,0)
    yy,xx=np.indices(a.shape[:2]);visible=a[:,:,3]>0
    teal=(g>r+12)&(b>r+4)
    blue=(b>r+12)&(b>g+5)
    warm=(r>g+15)&(g>b+10)&(r>65)&(r<225)
    red=(r>g+35)&(g<b+24)&(b>38)&(r>80)&(r<210)
    cream=(r>180)&(g>160)&(b>115)
    if kind=="desk":mask=warm&(yy<50)
    elif kind=="desk-side":mask=warm&(yy>=36)&(yy<50)
    elif kind=="teal":mask=teal&(yy<54)
    elif kind=="red":mask=(red|warm)&(yy<58)
    elif kind=="blue":mask=blue|teal
    elif kind=="cream":mask=cream&(yy<43)
    elif kind=="fabric":mask=(teal|blue|cream)&(yy<57)
    elif kind=="shelf":
        # Red book spines excluded; recolor the actual walnut palette, not the books.
        mask=warm&~red
    else:raise ValueError(kind)
    return mask&visible

def recolor():
    for family,(source,index,state,env,colors,kind) in RECOLOR_BASES.items():
        base=existing(source,index,state,env);mask=material_mask(base,kind)
        if not mask.any():raise AssertionError(f"Empty material mask: {family}")
        a=np.array(base);material=np.zeros_like(a);material[mask]=a[mask]
        Image.fromarray(material).save(output(f"recolor/{family}/material.png"))
        base.save(output(f"recolor/{family}/base.png"))
        Image.fromarray(mask.astype(np.uint8)*255).save(output(f"recolor/{family}/mask.png"))
        values=np.unique(material[mask,:3],axis=0)
        luma=(values*np.array([3,6,1])).sum(1)/10
        low,high=float(luma.min()),float(luma.max())
        variants=[]
        for color in colors:
            mapping={}
            for rgb,lum in zip(values,luma):
                target=C[RAMPS[color][min(4,max(0,round(4*(lum-low)/max(1,high-low))))]]
                # Original wood front/side remains a true baseline colorway.
                if color=="wood":target=tuple(rgb)
                mapping["#"+bytes(rgb).hex()]="#"+bytes(target).hex()
            variants.append({"name":color,"map":mapping})
        jsave(f"recolor/{family}/spec.json",{"version":1,"kind":"sprite-gen-recolor","match":"exact","tolerance":0,"variants":variants})
        cmd=[str(CLI),"recolor","--base",str(ROOT/f"recolor/{family}/material.png"),
             "--spec",str(ROOT/f"recolor/{family}/spec.json"),"--out-dir",str(output(f"recolor/{family}/variants/.keep").parent)]
        result=subprocess.run(cmd,capture_output=True,text=True,encoding="utf-8",env={**os.environ,"PYTHONIOENCODING":"utf-8","PYTHONDONTWRITEBYTECODE":"1"})
        if result.returncode:raise RuntimeError(result.stderr+result.stdout)
        for color in colors:
            patch=np.array(Image.open(ROOT/f"recolor/{family}/variants/{color}.png").convert("RGBA"))
            result=a.copy();result[mask]=patch[mask]
            assert np.array_equal(result[:,:,3],a[:,:,3])
            Image.fromarray(result).save(output(f"recolor/{family}/finished-{color}.png"))
        print(f"sprite-gen recolor: {family}, {len(colors)} colorways, {int(mask.sum())} material pixels",flush=True)
    jsave("recolor/source-hashes.json",SOURCES)

def recolored(family,color):
    return clean(Image.open(ROOT/f"recolor/{family}/finished-{color}.png"))

def furniture():
    desk_colors=["wood","white","black","green","blue"]
    for family,name,colors in [("desk-front","desks-front",desk_colors),("desk-right","desks-side-right",desk_colors),("desk-left","desks-side-left",desk_colors),
                              ("chair-front","chairs-front",FABRIC_COLORS),("chair-right","chairs-side-right",FABRIC_COLORS),("chair-left","chairs-side-left",FABRIC_COLORS)]:
        choice(name,[recolored(family,c) for c in colors])
    for i,color in enumerate(desk_colors):
        rotations=[]
        for angle,name in [(0,"desks-front"),(90,"desks-side-right"),(270,"desks-side-left")]:
            rotations.append({"rotation":angle,"source":{"sheet":f"sheets/{name}.png","manifest":f"sheets/{name}.manifest.json","state":"variants","frame":i},"footprint":{"width":3,"height":2}})
        item(f"desk-{color}",f"{color.title()} desk","furniture","desks-front",frame=i,footprint=(3,2),tags=("desk",color,"work"),family="desks",rotations=rotations,
             note="Front is an empty desk; side views retain ENV3's CRT/keyboard/mug. Static on screen; chair placed separately. No work/seat interaction installed.")
    for i,color in enumerate(FABRIC_COLORS):
        rotations=[]
        for angle,name in [(0,"chairs-front"),(90,"chairs-side-right"),(270,"chairs-side-left")]:
            rotations.append({"rotation":angle,"source":{"sheet":f"sheets/{name}.png","manifest":f"sheets/{name}.manifest.json","state":"variants","frame":i},"footprint":{"width":2,"height":2}})
        item(f"chair-{color}",f"{color.title()} office chair","furniture","chairs-front",frame=i,footprint=(2,2),tags=("chair",color,"seat"),family="chairs",rotations=rotations,
             note="Complete chair sprite, not split seating layers. Blocking footprint is a suggested physical reservation; linked runtime seating needs an adapter.")
    sheet("sofas",{"down":([recolored("sofa-down",c) for c in FABRIC_COLORS],1,False),"right":([recolored("sofa-right",c) for c in FABRIC_COLORS],1,False)})
    for i,color in enumerate(FABRIC_COLORS):
        rotations=[{"rotation":angle,"source":{"sheet":"sheets/sofas.png","manifest":"sheets/sofas.manifest.json","state":state,"frame":i},"footprint":{"width":fp[0],"height":fp[1]}}
                   for angle,state,fp in [(0,"down",(3,2)),(90,"right",(2,3))]]
        item(f"sofa-{color}",f"{color.title()} sofa","furniture","sofas",state="down",frame=i,footprint=(3,2),tags=("sofa",color,"lounge"),family="sofas",rotations=rotations)
    for family,name,colors,fp,blocking in [("armchair","armchairs",FABRIC_COLORS,(2,2),True),
           ("rug","rugs",FABRIC_COLORS,(4,3),False),("lamp","lamps",["white","green","red"],(1,1),True)]:
        choice(name,[recolored(family,c) for c in colors])
        for i,color in enumerate(colors):
            item(f"{family}-{color}",f"{color.title()} {family}","furniture",name,frame=i,footprint=fp,blocking=blocking,
                 tags=(family,color,"lounge"),family=name,layer=0 if family=="rug" else 1)
    sheet("bookshelves",{style:([recolored(f"bookshelf-{style}",c) for c in ["walnut","oak","birch"]],1,False) for style in ["open","cabinet"]})
    for style in ["open","cabinet"]:
        for i,color in enumerate(["walnut","oak","birch"]):
            item(f"bookshelf-{style}-{color}",f"{color.title()} {style} bookshelf","furniture","bookshelves",state=style,frame=i,footprint=(2,1),tags=("storage","bookshelf",color,style),family="bookshelves")

def panel_prop(shape,angle=0,low=False):
    im=blank();d=ImageDraw.Draw(im)
    dark,mid,light=("wood","ivory_dark","cream") if low else ("navy","blue","blue_light")
    height=14 if low else 25
    def horizontal(x0,x1,y):
        d.rectangle((x0,y-height,x1,y),fill=rgba("ink"));d.rectangle((x0+2,y-height+2,x1-2,y-3),fill=rgba(mid))
        d.line((x0+1,y-height+1,x1-1,y-height+1),fill=rgba(light));d.line((x0+2,y-2,x1-2,y-2),fill=rgba(dark))
        for x in [x0+3,x1-4]:d.rectangle((x,y+1,x+2,min(60,y+5)),fill=rgba("grey_dark"))
    def vertical(x,y0,y1):
        d.rectangle((x,y0,x+8,y1),fill=rgba("ink"));d.rectangle((x+2,y0+2,x+6,y1-2),fill=rgba(mid));d.line((x+2,y0+2,x+2,y1-2),fill=rgba(light))
    if shape=="straight":
        if angle in {0,180}:horizontal(7,56,55)
        else:vertical(27,12,58)
    elif shape=="corner":
        if angle in {0,180}:
            horizontal(8,55,55);vertical(8 if angle==0 else 47,16,54)
        else:
            horizontal(8,55,35);vertical(47 if angle==90 else 8,35,59)
    else:
        if angle in {0,180}:
            horizontal(7,56,55 if angle==0 else 35);vertical(28,13 if angle==0 else 35,54 if angle==0 else 59)
        else:
            vertical(27,10,59);horizontal(8 if angle==90 else 27,35 if angle==90 else 56,49)
    return im

def glass_door(double=False,opened=False):
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((7,7,56,59),fill=rgba("ink"));d.rectangle((9,9,54,57),fill=rgba("grey_light"))
    if opened:
        d.rectangle((11,11,35 if not double else 45,56),fill=(0,0,0,0))
    else:
        d.rectangle((11,11,52,56),fill=rgba("blue_light"));d.rectangle((11,40,52,56),fill=rgba("sage"))
        for x,y in [(14,19),(36,25)]:d.line((x,y,x+10,y-10),fill=rgba("cream"))
    if double:
        d.rectangle((30,9,33,57),fill=rgba("grey_dark"))
        d.line((26,32,26,39),fill=rgba("cream"));d.line((37,32,37,39),fill=rgba("cream"))
    else:
        d.rectangle((37,9,40,57),fill=rgba("grey_dark"));d.line((44,31,44,39),fill=rgba("cream"))
    d.line((6,60,57,60),fill=rgba("grey_dark"))
    return im

def structure():
    for shape in ["straight","corner","tee"]:
        name=f"cubicle-{shape}";states=[f"r{a}" for a in [0,90,180,270]]
        sheet(name,{state:([panel_prop(shape,a)],1,False) for state,a in zip(states,[0,90,180,270])})
        fp=[(4,1),(1,4),(4,1),(1,4)] if shape=="straight" else [(4,3)]*4
        item(name,f"Cubicle {shape}","structure",name,state="r0",footprint=fp[0],tags=("cubicle","partition",shape),rotations=rotated_sources(name,states,fp))
    choice("low-dividers",[panel_prop("straight",a,low=True) for a in [0,90]])
    rotations=[{"rotation":a,"source":{"sheet":"sheets/low-dividers.png","manifest":"sheets/low-dividers.manifest.json","state":"variants","frame":i},"footprint":{"width":fp[0],"height":fp[1]}} for i,(a,fp) in enumerate([(0,(4,1)),(90,(1,4))])]
    item("low-divider","Low ivory divider","structure","low-dividers",footprint=(4,1),tags=("partition","low"),rotations=rotations)
    for double in [False,True]:
        name="double-door" if double else "sliding-glass-door"
        sheet(name,{state:([glass_door(double,opened)],1,False) for state,opened in [("closed",False),("open",True)]})
        for state in ["closed","open"]:
            item(f"{name}-{state}",f"{name.replace('-',' ').title()} {state}","structure",name,state=state,footprint=(3,1),blocking=state=="closed",tags=("door","glass",state),note="Static selectable state, no automatic open/close logic.")
    im=blank();d=ImageDraw.Draw(im)
    d.line((7,9,56,9),fill=rgba("walnut"),width=3)
    for x in range(9,55,6):
        d.rectangle((x,11,x+5,57),fill=rgba("red" if x%12 else "red_light"));d.line((x,12,x,56),fill=rgba("red_dark"))
    d.line((10,58,53,58),fill=rgba("red_dark"));choice("curtain",[im]);item("curtain","Brick-red curtain","structure","curtain",footprint=(3,1),blocking=False,layer=2,tags=("curtain","wall","fabric"))
    im=blank();d=ImageDraw.Draw(im)
    for i in range(5):
        x=9+i*3;y=25+i*7
        d.rectangle((x,y,53,y+6),fill=rgba("grey_dark"));d.rectangle((x,y,53,y+2),fill=rgba("ivory_dark"));d.line((x,y,53,y),fill=rgba("cream"));d.line((x,y+6,53,y+6),fill=rgba("ink"))
    choice("stairs-decor",[im]);item("stairs-decor","Decorative stairs","structure","stairs-decor",footprint=(3,3),tags=("stairs","decorative","nonfunctional"),note="Decorative silhouette only. No floor change or traversal.")
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((24,9,39,58),fill=rgba("grey_mid"));d.rectangle((25,10,29,57),fill=rgba("ivory_dark"));d.line((38,10,38,57),fill=rgba("grey_dark"))
    for box in [(21,6,42,10),(21,57,42,60)]:d.rectangle(box,fill=rgba("ink"));d.rectangle((box[0]+1,box[1]+1,box[2]-1,box[3]-1),fill=rgba("cream"))
    choice("pillar",[im]);item("pillar","Ivory pillar","structure","pillar",footprint=(2,1),tags=("pillar","architecture"))

GENERATED={
    "kitchen-island":("office_a",0,(58,39),(4,3)),
    "conference-table-eight":("office_a",1,(58,42),(4,3)),
    "whiteboard-wheels":("office_a",2,(44,46),(3,1)),
    "printer-station":("office_a",3,(34,42),(3,2)),
    "plant-wall":("office_a",4,(50,45),(4,1)),
    "coat-rack":("office_b",0,(28,49),(2,1)),
    "shoe-rack":("office_b",1,(44,26),(3,1)),
    "retro-tv":("office_b",2,(36,37),(3,2)),
    "game-console-corner":("office_b",3,(48,42),(3,2)),
}

def generated(name):
    state,index,dimensions,fp=GENERATED[name]
    p=ROOT/f"runs/bodies/frames/{state}/frame-{index}.png"
    return quantize(fit(Image.open(p).convert("RGBA"),*dimensions))

def prop(name,frames,fp=(2,1),*,blocking=True,tags=(),layer=1,labels=None,note=""):
    choice(name,frames)
    labels=labels or [name.replace("-"," ").title()+(f" {i+1}" if len(frames)>1 else "") for i in range(len(frames))]
    for i,label in enumerate(labels):
        item(name+(f"-{i+1}" if len(frames)>1 else ""),label,"props",name,frame=i,footprint=fp,blocking=blocking,tags=tags,layer=layer,note=note)

def leaves(d,x,y,kind="fern",scale=1):
    if kind=="fern":
        for dx,dy in [(-9,-8),(-5,-12),(0,-14),(5,-12),(9,-7)]:
            d.line((x,y,x+dx*scale,y+dy*scale),fill=rgba("teal_dark"))
            for t in [.35,.6,.85]:
                xx=round(x+dx*t*scale);yy=round(y+dy*t*scale)
                for side in [-1,1]:
                    d.polygon([(xx,yy),(xx+side*4*scale,yy-3*scale),(xx+side*3*scale,yy),(xx,yy+1)],fill=rgba("green"))
                    d.point((xx+side*2*scale,yy-1),fill=rgba("sage"))
    else:
        for dx,dy,w,h in [(-8,-10,9,8),(2,-15,10,9),(-1,-6,10,7),(8,-8,8,7)]:
            d.ellipse((x+dx,y+dy,x+dx+w,y+dy+h),fill=rgba("teal_dark"))
            d.ellipse((x+dx+1,y+dy,x+dx+w-1,y+dy+h-2),fill=rgba("green"))
            d.line((x+dx+3,y+dy+1,x+dx+w-2,y+dy+h-2),fill=rgba("sage"))

def hanging_plant(kind):
    im=blank();d=ImageDraw.Draw(im)
    d.line((32,9,21,43),fill=rgba("wood_mid"));d.line((32,9,43,43),fill=rgba("wood_mid"));d.point((32,8),fill=rgba("ink"))
    d.polygon([(20,43),(43,43),(39,55),(25,55)],fill=rgba("wood"));d.line((20,43,43,43),fill=rgba("honey"));d.line((26,54,38,54),fill=rgba("wood_mid"))
    leaves(d,31,44,kind)
    if kind=="vine":
        for x,y in [(23,47),(21,52),(24,57),(40,48),(42,54),(39,58)]:
            d.line((x,y-4,x,y),fill=rgba("teal_dark"));d.ellipse((x-2,y-1,x+2,y+1),fill=rgba("green"))
    return im

def art_frame(style):
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((17,28,46,58),fill=rgba("ink"));d.rectangle((18,29,45,57),fill=rgba("wood_mid"));d.rectangle((20,31,43,55),fill=rgba("ivory_dark"));d.rectangle((22,33,41,53),fill=rgba("cream"))
    if style==0:
        d.ellipse((33,35,38,40),fill=rgba("gold"));d.polygon([(22,51),(28,40),(34,49),(39,42),(41,53),(22,53)],fill=rgba("blue"));d.polygon([(22,53),(31,47),(41,51),(41,53)],fill=rgba("teal"))
    elif style==1:
        d.line((31,49,31,38),fill=rgba("teal_dark"))
        for x,y in [(27,39),(33,41),(26,45)]:d.ellipse((x,y,x+6,y+3),fill=rgba("green"))
        d.rectangle((28,49,35,52),fill=rgba("wood_mid"))
    elif style==2:
        d.ellipse((25,38,36,49),fill=rgba("red_light"));d.rectangle((29,35,38,43),fill=rgba("blue"));d.line((23,51,39,51),fill=rgba("wood"))
    elif style==3:
        d.rectangle((22,33,41,45),fill=rgba("blue_light"));d.ellipse((28,36,34,42),fill=rgba("cream"));d.rectangle((22,46,41,53),fill=rgba("teal"));d.line((23,49,39,49),fill=rgba("sage"))
    elif style==4:
        d.rectangle((23,34,40,52),fill=rgba("navy"))
        for x,y,h in [(25,42,9),(29,38,13),(34,44,7),(38,40,11)]:
            d.rectangle((x,y,x+2,51),fill=rgba("blue"));d.point((x+1,y+3),fill=rgba("gold"))
    else:
        d.polygon([(24,45),(27,39),(28,43),(34,42),(37,38),(38,45),(36,50),(27,50)],fill=rgba("honey"));d.point((29,46),fill=rgba("ink"));d.point((35,46),fill=rgba("ink"));d.line((31,48,33,48),fill=rgba("wood"))
    d.line((19,29,44,29),fill=rgba("honey"))
    return im

def poster(style):
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((20,26,44,58),fill=rgba("ink"));d.rectangle((21,27,43,57),fill=rgba("paper"));d.rectangle((23,29,41,31),fill=rgba(["red","teal","blue","wood_mid"][style]))
    if style==0:
        d.ellipse((25,34,39,48),fill=rgba("red_light"));d.ellipse((28,37,36,45),fill=rgba("cream"));d.line((24,52,40,52),fill=rgba("red"));d.line((27,54,37,54),fill=rgba("wood_mid"))
    elif style==1:
        d.line((32,48,32,35),fill=rgba("teal_dark"));d.ellipse((24,37,31,43),fill=rgba("green"));d.ellipse((33,34,39,40),fill=rgba("sage"));d.rectangle((26,50,38,54),outline=rgba("teal"))
    elif style==2:
        for x,y in [(26,37),(31,42),(36,36)]:d.rectangle((x,y,x+3,y+8),fill=rgba("blue"))
        d.line((25,53,39,53),fill=rgba("blue_dark"))
    else:
        d.ellipse((26,37,38,44),fill=rgba("wood_mid"));d.line((29,36,29,33),fill=rgba("ivory_dark"));d.line((34,36,34,33),fill=rgba("ivory_dark"));d.rectangle((25,48,39,52),fill=rgba("honey"))
    for x,y in [(22,28),(42,56)]:d.point((x,y),fill=rgba("grey_dark"))
    return im

def new_props():
    for name,(_,_,_,fp) in GENERATED.items():
        blocking=name!="plant-wall"
        note="Eight chairs at compact native scale; decorative grouped seating, no seat allocation." if name=="conference-table-eight" else ""
        prop(name,[generated(name)],fp,blocking=blocking,tags=("office",name),layer=2 if name=="plant-wall" else 1,note=note)
    # Noticeboard: hard pins and paper pieces, with no simulated text.
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((8,27,55,58),fill=rgba("ink"));d.rectangle((9,28,54,57),fill=rgba("wood"));d.rectangle((11,30,52,55),fill=rgba("honey"))
    for x,y,w,h,color in [(14,34,12,14,"cream"),(31,32,16,10,"paper"),(33,45,12,8,"blue_light")]:
        d.rectangle((x+1,y+1,x+w,y+h),fill=rgba("wood_mid"));d.rectangle((x,y,x+w-1,y+h-1),fill=rgba(color));d.point((x+w//2,y+1),fill=rgba("red"))
        for yy in range(y+5,y+h-1,3):d.line((x+3,yy,x+w-3,yy),fill=rgba("grey_mid"))
    prop("notice-board",[im],(3,1),blocking=False,layer=2,tags=("notice","board","wall","pins"))
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((6,16,57,19),fill=rgba("ink"));d.line((7,17,56,17),fill=rgba("grey_mid"));d.rectangle((9,20,54,50),fill=rgba("ivory_dark"));d.rectangle((10,20,53,48),fill=rgba("paper"));d.line((8,51,55,51),fill=rgba("ink"));d.line((32,52,32,58),fill=rgba("grey_dark"));d.point((32,59),fill=rgba("ink"))
    prop("projector-screen",[im],(4,1),blocking=False,layer=2,tags=("meeting","screen","wall"))
    prop("hanging-plants",[hanging_plant(k) for k in ["fern","monstera","vine"]],(2,1),blocking=False,layer=2,tags=("plant","hanging","green"),labels=["Hanging fern","Hanging monstera","Trailing hanging vine"])
    tall=[]
    tall.append(fit(existing("plants",4),34,52))
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((29,26,32,52),fill=rgba("wood"));leaves(d,30,28,"monstera");leaves(d,31,41,"monstera")
    d.polygon([(22,50),(42,50),(38,60),(26,60)],fill=rgba("ink"));d.polygon([(23,51),(41,51),(37,59),(27,59)],fill=rgba("ivory"));d.line((24,51,40,51),fill=rgba("cream"));tall.append(im)
    prop("tall-plants",tall,(2,1),tags=("plant","tall","green"),labels=["Tall ficus (ENV2 body)","Tall rubber plant"])
    bins=[existing("trash-bin")]
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((24,37,40,58),fill=rgba("ink"));d.rectangle((25,39,39,57),fill=rgba("teal"));d.line((26,40,26,56),fill=rgba("green"));d.rectangle((23,34,41,38),fill=rgba("teal_dark"));d.line((25,34,39,34),fill=rgba("sage"));d.rectangle((28,35,36,36),fill=rgba("ink"))
    d.line((29,45,34,45),fill=rgba("cream"));d.line((34,45,35,49),fill=rgba("cream"));d.line((35,49,30,51),fill=rgba("cream"));d.line((30,51,29,47),fill=rgba("cream"));d.point((28,46),fill=rgba("cream"));bins.append(im)
    prop("waste-bins",bins,(1,1),tags=("utility","trash","recycling"),labels=["Trash bin (ENV2 body)","Recycling bin"])
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((28,22,35,25),fill=rgba("ink"));d.rectangle((29,21,34,23),fill=rgba("blue_dark"));d.polygon([(26,27),(28,25),(35,25),(38,29),(38,55),(35,59),(27,59),(24,55),(24,30)],fill=rgba("ink"));d.polygon([(27,28),(35,27),(37,30),(37,54),(34,57),(27,57),(25,54),(25,31)],fill=rgba("blue"));d.line((27,31,27,52),fill=rgba("blue_light"));d.rectangle((25,38,37,40),fill=rgba("blue_light"));d.rectangle((25,49,37,51),fill=rgba("blue_light"));d.rectangle((28,43,34,46),fill=rgba("cream"))
    prop("water-jug",[im],(1,1),tags=("kitchen","water","jug"))
    tables=[]
    for style in range(3):
        im=existing("coffee-table");d=ImageDraw.Draw(im)
        if style==0:
            d.ellipse((35,35,42,38),fill=rgba("paper"));d.ellipse((36,34,40,38),fill=rgba("cream"));d.line((41,35,43,37),fill=rgba("ivory_dark"))
        elif style==1:
            d.rectangle((29,36,35,44),fill=rgba("ivory_dark"));leaves(d,32,36,"fern")
        else:
            d.rectangle((23,34,32,40),fill=rgba("blue"));d.rectangle((35,36,44,42),fill=rgba("red"));d.line((24,36,30,36),fill=rgba("cream"))
        tables.append(im)
    prop("coffee-table-sets",tables,(3,2),tags=("coffee-table","lounge","set"),labels=["Coffee table with cup","Coffee table with fern","Coffee table with magazines"])
    cushions=[]
    for color in ["red","blue","green","lilac"]:
        dark,mid,light=RAMPS[color][1:4];im=blank();d=ImageDraw.Draw(im)
        d.rounded_rectangle((18,40,46,59),radius=5,fill=rgba("ink"));d.rounded_rectangle((19,40,45,57),radius=5,fill=rgba(dark));d.rounded_rectangle((20,41,44,54),radius=4,fill=rgba(mid));d.line((23,43,40,43),fill=rgba(light));d.line((22,54,42,54),fill=rgba(dark));d.point((31,47),fill=rgba(dark));d.point((32,48),fill=rgba(light));cushions.append(im)
    prop("floor-cushions",cushions,(2,2),tags=("cushion","floor","lounge"),labels=[f"{c.title()} floor cushion" for c in ["red","blue","green","lilac"]])
    im=blank();d=ImageDraw.Draw(im)
    d.ellipse((22,56,36,60),fill=rgba("ink"));d.line((29,55,29,22),fill=rgba("grey_dark"),width=2);d.line((30,23,36,14),fill=rgba("grey_dark"),width=2);d.line((36,14,44,14),fill=rgba("grey_dark"),width=2);d.polygon([(39,13),(47,13),(51,23),(35,23)],fill=rgba("ink"));d.polygon([(40,14),(46,14),(49,21),(37,21)],fill=rgba("cream"));d.line((38,20,48,20),fill=rgba("ivory_dark"));d.line((24,58,33,58),fill=rgba("grey"))
    prop("standing-lamp",[im],(2,1),tags=("lighting","lamp","standing"))
    shelves=[]
    for style in range(3):
        im=blank();d=ImageDraw.Draw(im)
        if style==1:
            for x,h,col in [(13,13,"red"),(18,16,"blue"),(23,14,"cream"),(28,12,"teal"),(35,14,"ivory")]:
                d.rectangle((x,43-h,x+3,44),fill=rgba(col));d.line((x,43-h,x,43),fill=rgba("ink"));d.point((x+1,40),fill=rgba("honey"))
        if style==2:
            leaves(d,22,36,"fern");d.rectangle((17,36,26,43),fill=rgba("ivory"));d.rectangle((36,36,44,43),fill=rgba("wood_mid"));d.ellipse((34,30,46,36),fill=rgba("green"))
        d.rectangle((8,44,55,48),fill=rgba("ink"));d.rectangle((9,44,54,46),fill=rgba("honey"));d.line((10,44,53,44),fill=rgba("ivory_dark"))
        for x in [15,47]:d.polygon([(x,49),(x+4,49),(x,55)],fill=rgba("wood"))
        shelves.append(im)
    prop("wall-shelves",shelves,(3,1),blocking=False,layer=2,tags=("shelf","wall","storage"),labels=["Empty wall shelf","Wall shelf with books","Wall shelf with plants"])
    prop("framed-art",[art_frame(i) for i in range(6)],(2,1),blocking=False,layer=2,tags=("art","frame","wall"),labels=["Mountain art","Botanical art","Abstract art","Seascape art","City art","Cat art"])
    prop("wall-posters",[poster(i) for i in range(4)],(2,1),blocking=False,layer=2,tags=("poster","wall"),labels=["Circle event poster","Botanical poster","Music poster","Coffee poster"])
    im=blank();d=ImageDraw.Draw(im)
    d.rounded_rectangle((7,36,56,57),radius=3,fill=rgba("ink"));d.rounded_rectangle((9,38,54,55),radius=2,outline=rgba("teal"));d.line((13,38,17,31),fill=rgba("grey_dark"));d.line((50,38,46,31),fill=rgba("grey_dark"));d.point((31,31),fill=rgba("grey_dark"))
    prop("neon-sign",[im],(3,1),blocking=False,layer=2,tags=("sign","neon","blank"),note="Blank neon housing. OPEN lettering is a separate selectable overlay in neon-open.")
    # Hand-pixel OPEN lettering (3x5, with a 4x5 diagonal N), no antialiasing.
    glyphs={"O":["111","101","101","101","111"],"P":["110","101","110","100","100"],"E":["111","100","110","100","111"],"N":["1001","1101","1011","1001","1001"]}
    lit=im.copy();d=ImageDraw.Draw(lit)
    for index,ch in enumerate("OPEN"):
        for y,row in enumerate(glyphs[ch]):
            for x,bit in enumerate(row):
                if bit=="1":d.rectangle((17+index*8+x*2,42+y*2,18+index*8+x*2,43+y*2),fill=rgba("sage"))
    prop("neon-open",[lit],(3,1),blocking=False,layer=2,tags=("sign","neon","open"))
    im=blank();d=ImageDraw.Draw(im)
    d.rectangle((23,23,41,59),fill=rgba("ink"));d.rectangle((24,24,40,58),fill=rgba("navy"));d.ellipse((27,28,37,38),fill=rgba("grey_dark"));d.ellipse((28,29,36,37),outline=rgba("grey_mid"));d.ellipse((26,43,38,55),fill=rgba("grey_dark"));d.ellipse((28,45,36,53),outline=rgba("grey_mid"));d.ellipse((30,47,34,51),fill=rgba("ink"));d.point((38,26),fill=rgba("sage"))
    prop("speaker",[im],(2,1),tags=("speaker","music","electronics"))
    ceiling_fan()

def rectmask(boxes,size=64):
    im=Image.new("L",(size,size));d=ImageDraw.Draw(im)
    for box in boxes:d.rectangle(box,fill=255)
    return np.array(im)>0

def ceiling_fan():
    body=blank();d=ImageDraw.Draw(body)
    d.ellipse((27,9,37,14),fill=rgba("ink"));d.ellipse((28,10,36,13),fill=rgba("ivory"));d.line((32,14,32,44),fill=rgba("grey_dark"),width=2)
    d.ellipse((28,43,36,50),fill=rgba("ink"));d.ellipse((30,44,34,48),fill=rgba("honey"))
    frames=[];m=Image.new("L",(64,64));md=ImageDraw.Draw(m)
    for phase in [0,math.pi/6,math.pi/3]:
        blades=blank();bd=ImageDraw.Draw(blades)
        for blade in range(4):
            theta=phase+blade*math.pi/2;points=[]
            for rad,ang in [(5,-.4),(22,-.14),(22,.14),(5,.4)]:
                points.append((round(32+rad*math.cos(theta+ang)),round(46+rad*.43*math.sin(theta+ang))))
            bd.polygon(points,fill=rgba("ink"));md.polygon(points,fill=255)
            inner=[(round(32+rad*math.cos(theta+ang)),round(46+rad*.43*math.sin(theta+ang))) for rad,ang in [(6,-.25),(20,-.08),(20,.08),(6,.25)]]
            bd.polygon(inner,fill=rgba("wood_mid" if blade%2 else "honey"))
        blades.alpha_composite(body);frames.append(clean(blades))
    mask=(np.array(m)>0)&(np.array(body)[:,:,3]==0)
    sheet("ceiling-fan",{"spin":(frames,3,True)},mask=mask,body=body,note="Only four blade silhouettes; mount, drop rod and hub fixed.")
    item("ceiling-fan","Ceiling fan","props","ceiling-fan",state="spin",footprint=(3,1),blocking=False,layer=2,tags=("ceiling","fan","animated"),note="Three stylized rotor poses. Mount/hub/drop rod are byte-identical; no air or cooling simulation.")

def seasonal():
    tree=blank();d=ImageDraw.Draw(tree)
    d.rectangle((29,49,34,56),fill=rgba("wood"));d.rectangle((25,55,39,60),fill=rgba("ink"));d.rectangle((26,56,38,59),fill=rgba("wood_mid"))
    for top,left,right,bottom in [(15,22,42,32),(23,18,46,43),(33,14,50,54)]:
        d.polygon([(32,top),(right,bottom),(left,bottom)],fill=rgba("teal_dark"));d.polygon([(32,top+2),(right-3,bottom-2),(left+3,bottom-2)],fill=rgba("green"));d.line((left+3,bottom-2,right-3,bottom-2),fill=rgba("teal"))
    d.polygon([(32,7),(34,12),(39,12),(35,15),(36,20),(32,17),(28,20),(29,15),(25,12),(30,12)],fill=rgba("gold"));d.point((32,12),fill=rgba("cream"))
    for y,x0,x1 in [(29,25,39),(40,22,43),(49,19,46)]:d.line((x0,y,x1,y+3),fill=rgba("honey"))
    bulbs=[(30,26),(36,32),(25,38),(38,41),(23,48),(32,49),(42,50)]
    for x,y in bulbs:d.ellipse((x-1,y-1,x+1,y+1),fill=rgba("red_dark"))
    frames=[]
    for phase in range(3):
        f=tree.copy();dd=ImageDraw.Draw(f)
        for i,p in enumerate(bulbs):dd.point(p,fill=rgba(["cream","red_light","blue_light"][(i+phase)%3]))
        frames.append(f)
    mask=rectmask([(x,y,x,y) for x,y in bulbs])
    sheet("winter-tree",{"twinkle":(frames,2,True)},mask=mask,body=tree,note="Seven one-pixel ornament centers only; star, tree and pot fixed.")
    item("winter-tree","Winter tree","seasonal","winter-tree",state="twinkle",footprint=(3,2),tags=("winter","tree","animated"))
    garland=blank();d=ImageDraw.Draw(garland)
    for x in range(7,57):
        y=38+round(4*math.sin((x-7)/49*math.pi))
        d.line((x,y-2,x,y+3),fill=rgba("teal_dark"));d.point((x,y-1),fill=rgba("green"));d.point((x,y+1),fill=rgba("sage"))
    bulbs=[(15,45),(27,49),(40,49),(51,45)]
    for x,y in bulbs:d.line((x,y-5,x,y-2),fill=rgba("wood"));d.ellipse((x-2,y-2,x+2,y+2),fill=rgba("red_dark"))
    frames=[]
    for phase in range(3):
        f=garland.copy();dd=ImageDraw.Draw(f)
        for i,(x,y) in enumerate(bulbs):dd.rectangle((x-1,y-1,x+1,y+1),fill=rgba(["cream","red","gold"][(i+phase)%3]))
        frames.append(f)
    sheet("winter-garland",{"twinkle":(frames,2,True)},mask=rectmask([(x-1,y-1,x+1,y+1) for x,y in bulbs]),body=garland,note="Four bulb interiors only; green branch and sockets fixed.")
    item("winter-garland","Winter garland","seasonal","winter-garland",state="twinkle",footprint=(3,1),blocking=False,layer=2,tags=("winter","garland","animated"))
    frames=[existing("window-views",i,"snow","env4") for i in range(4)]
    mask=rectmask([(9,30,29,41),(34,30,54,41),(9,45,29,55),(34,45,54,55)])
    sheet("winter-snow-window",{"snow":(frames,2,True)},mask=mask,body=frames[0],note="Reuse ENV4 snow window; only pane interiors can change.")
    item("winter-snow-window","Snow window (ENV4 loop)","seasonal","winter-snow-window",state="snow",footprint=(4,1),blocking=False,layer=2,tags=("winter","snow","window","animated"))
    im=blank();d=ImageDraw.Draw(im)
    for line in [(9,54,50,30),(21,47,18,31),(30,42,37,24),(39,36,52,39)]:d.line(line,fill=rgba("wood"),width=2)
    for x,y in [(16,45),(19,35),(24,42),(31,36),(35,29),(41,32),(49,32),(47,38)]:
        for dx,dy in [(-2,0),(2,0),(0,-2),(0,2)]:d.ellipse((x+dx-1,y+dy-1,x+dx+1,y+dy+1),fill=rgba("pink"))
        d.point((x,y),fill=rgba("cream"));d.point((x+1,y+1),fill=rgba("red_light"))
    choice("spring-cherry-branch",[im]);item("spring-cherry-branch","Cherry blossom branch","seasonal","spring-cherry-branch",footprint=(3,1),blocking=False,layer=2,tags=("spring","blossom","branch"))
    flowers=[]
    for style in range(3):
        im=blank();d=ImageDraw.Draw(im)
        for x,y in [(23,37),(31,31),(40,38),(27,42),(36,43)]:
            d.line((32,52,x,y),fill=rgba("teal_dark"));d.line((x,y+7,x+4,y+5),fill=rgba("green"))
            color=["pink","red","cream"][style]
            if style==1:d.polygon([(x-3,y-3),(x,y-1),(x+3,y-3),(x+2,y+3),(x-2,y+3)],fill=rgba(color))
            else:
                for dx,dy in [(-2,0),(2,0),(0,-2),(0,2)]:d.ellipse((x+dx-1,y+dy-1,x+dx+1,y+dy+1),fill=rgba(color))
                d.point((x,y),fill=rgba("gold"))
        d.polygon([(25,50),(39,50),(36,60),(28,60)],fill=rgba("ink"));d.polygon([(26,51),(38,51),(35,59),(29,59)],fill=rgba("ivory"));d.line((27,52,37,52),fill=rgba("cream"));flowers.append(im)
    choice("spring-flowers",flowers)
    for i,label in enumerate(["Pink spring bouquet","Red tulips","White spring flowers"]):item(f"spring-flowers-{i+1}",label,"seasonal","spring-flowers",frame=i,footprint=(2,1),tags=("spring","flowers","plant"))
    pumpkins=[]
    for style in range(3):
        im=blank();d=ImageDraw.Draw(im)
        pumpkins_xy=[(32,48,13)] if style<2 else [(25,49,10),(40,50,8)]
        for x,y,r in pumpkins_xy:
            d.ellipse((x-r,y-9,x+r,y+10),fill=rgba("wood"));d.ellipse((x-r+1,y-9,x+r-1,y+8),fill=rgba("gold"));d.ellipse((x-6,y-8,x+6,y+8),outline=rgba("wood_mid"));d.line((x,y-7,x,y+7),fill=rgba("honey"));d.rectangle((x-1,y-12,x+1,y-8),fill=rgba("teal_dark"))
            if style==1:
                for xx in [x-5,x+4]:d.polygon([(xx,y-4),(xx+3,y),(xx-2,y)],fill=rgba("ink"))
                d.line((x-6,y+5,x+6,y+5),fill=rgba("ink"));d.point((x-3,y+6),fill=rgba("ink"));d.point((x+3,y+4),fill=rgba("ink"))
        pumpkins.append(im)
    choice("halloween-pumpkins",pumpkins)
    for i,label in enumerate(["Harvest pumpkin","Jack-o-lantern","Pumpkin pair"]):item(f"halloween-pumpkin-{i+1}",label,"seasonal","halloween-pumpkins",frame=i,footprint=(2,1),tags=("halloween","pumpkin","autumn"))
    webs=[]
    for side in [0,1]:
        im=blank();d=ImageDraw.Draw(im)
        origin=(12,18)
        for p in [(12,53),(22,51),(37,43),(48,28),(49,18)]:d.line((*origin,*p),fill=rgba("ivory_dark"))
        for radius in [10,20,30]:
            pts=[(12+round(radius*math.cos(a)),18+round(radius*math.sin(a))) for a in np.linspace(0,math.pi/2,7)]
            d.line(pts,fill=rgba("cream"))
        if side:im=im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        webs.append(im)
    choice("halloween-cobwebs",webs)
    for i in range(2):item(f"halloween-cobweb-{i+1}",f"Cobweb {'left' if i==0 else 'right'}","seasonal","halloween-cobwebs",frame=i,footprint=(3,1),blocking=False,layer=2,tags=("halloween","cobweb","corner"))
    autumn=[]
    for phase in range(2):
        im=blank();d=ImageDraw.Draw(im)
        for j,(x,y) in enumerate([(15,45),(27,55),(40,47),(49,56),(23,38),(36,40)]):
            x=x+(2 if phase else 0);color=["gold","red","honey","red_light"][j%4]
            d.polygon([(x,y-3),(x+4,y),(x+1,y+3),(x-3,y+1)],fill=rgba(color));d.line((x-1,y+1,x+3,y-1),fill=rgba("wood"));d.point((x-2,y+2),fill=rgba("wood"))
        autumn.append(im)
    choice("autumn-leaves",autumn)
    for i in range(2):item(f"autumn-leaves-{i+1}",f"Autumn leaf scatter {i+1}","seasonal","autumn-leaves",frame=i,footprint=(3,2),blocking=False,layer=0,tags=("autumn","leaves","floor"))

def font(size=13):
    for name in ["C:/Windows/Fonts/consola.ttf","C:/Windows/Fonts/arial.ttf"]:
        if Path(name).exists():return ImageFont.truetype(name,size)
    return ImageFont.load_default(size=size)

def wrap_label(d,text,width,f):
    lines=[];line=""
    for word in text.replace("-"," ").replace("_"," ").split():
        candidate=(line+" "+word).strip()
        if d.textlength(candidate,font=f)>width and line:lines.append(line);line=word
        else:line=candidate
    lines.append(line)
    return lines[:3]

def load_frame(source):
    m=json.loads((ROOT/source["manifest"]).read_text(encoding="utf-8"))
    r=m["frame_layout"]["rows"][source["state"]][source["frame"]]
    return Image.open(ROOT/source["sheet"]).convert("RGBA").crop((r["x"],r["y"],r["x"]+r["w"],r["y"]+r["h"]))

CATEGORY_NAMES={"floors":"FLOORS & TRIMS","walls":"WALLS & OPENINGS","structure":"PARTITIONS & STRUCTURE",
    "furniture":"FURNITURE COLOR FAMILIES","props":"OFFICE & LOUNGE PROPS","seasonal":"SEASONAL DECOR"}

def contact_pages():
    for category,title in CATEGORY_NAMES.items():
        sheets=sorted({x["source"]["sheet"] for x in ITEMS if x["category"]==category}|
            {r["source"]["sheet"] for x in ITEMS if x["category"]==category for r in x["rotationVariants"]})
        frames=[]
        for path in sheets:
            name=Path(path).stem;m=json.loads((ROOT/f"sheets/{name}.manifest.json").read_text(encoding="utf-8"))
            for state,rects in m["frame_layout"]["rows"].items():
                for index,r in enumerate(rects):
                    frames.append((f"{name} / {state} / {index}",load_frame({"sheet":path,"manifest":f"sheets/{name}.manifest.json","state":state,"frame":index})))
        columns=8;cw=164;ch=178 if category not in {"floors","walls"} else 116
        canvas=Image.new("RGBA",(columns*cw,48+math.ceil(len(frames)/columns)*ch),rgba("cream"));d=ImageDraw.Draw(canvas)
        d.text((12,12),f"ENV5 / {title} / every declared frame / 2x",font=font(19),fill=rgba("ink"))
        for index,(label,im) in enumerate(frames):
            x=(index%columns)*cw;y=48+(index//columns)*ch
            pw,ph=im.width*2,im.height*2;px=x+(cw-pw)//2
            d.rectangle((x+3,y,x+cw-4,y+ph+3),fill=rgba("ivory"))
            for yy in range(y,y+ph,16):
                for xx in range(x+3,x+cw-4,16):
                    if (xx//16+yy//16)%2:d.rectangle((xx,yy,min(xx+15,x+cw-4),min(yy+15,y+ph)),fill=rgba("ivory_dark"))
            canvas.alpha_composite(im.resize((pw,ph),Image.Resampling.NEAREST),(px,y))
            for j,line in enumerate(wrap_label(d,label,cw-12,font(12))):d.text((x+6,y+ph+6+j*13),line,font=font(12),fill=rgba("ink"))
        canvas.convert("RGB").save(output(f"qa/contact-{category}-2x.png"))

def example_offices():
    scenes=[]
    def room(title,floor_name,wall_name):
        im=Image.new("RGBA",(480,352),rgba("dark"));placements=[]
        def put(pack,name,x,y,state=None,frame=0,role="prop"):
            if pack=="env5":
                m=json.loads((ROOT/f"sheets/{name}.manifest.json").read_text(encoding="utf-8"))
                state=state or next(iter(m["frame_layout"]["rows"]))
                source={"sheet":f"sheets/{name}.png","manifest":f"sheets/{name}.manifest.json","state":state,"frame":frame}
                sprite=load_frame(source)
            else:
                sprite=existing(name,frame,state,pack)
                m=json.loads((ART/pack/f"sheets/{name}.manifest.json").read_text(encoding="utf-8"))
                state=state or next(iter(m["frame_layout"]["rows"]))
            im.alpha_composite(sprite,(x,y))
            placements.append({"pack":pack,"sheet":f"{pack}/sheets/{name}.png","manifest":f"{pack}/sheets/{name}.manifest.json","state":state,"frame":frame,"x":x,"y":y,"role":role})
        for y in range(64,352,32):
            for x in range(0,480,32):put("env5",floor_name,x,y,frame=(x//32+y//32)%2,role="floor")
        for y in [0,32]:
            for x in range(0,480,32):put("env5",wall_name,x,y,state="panel",role="wall")
        for x in [64,128]:put("env5",wall_name,x,32,state="window_classic",role="wall")
        scenes.append({"title":title,"nativeWidth":480,"nativeHeight":352,"scale":2,"placements":placements})
        return im,put
    cozy,p=room("Cozy studio","floor-wood-light-oak","wall-wallpaper-rose")
    for y in range(192,352,32):
        for x in range(0,224,32):p("env5","floor-carpet-sage",x,y,frame=(x//32+y//32)%2,role="floor")
    # Floor overlays precede all furniture.
    p("env5","rugs",63,221,frame=3,role="floor-overlay")
    p("env5","sofas",36,166,state="down",frame=5)
    p("env5","armchairs",170,178,frame=3)
    p("env5","coffee-table-sets",82,207,frame=0)
    p("env5","standing-lamp",1,183)
    p("env5","floor-cushions",175,253,frame=1)
    p("env5","desks-front",261,100,frame=0)
    p("env2","monitors",260,75,frame=1)
    p("env5","chairs-front",261,131,frame=0)
    p("env5","bookshelves",361,80,state="open",frame=2)
    p("env5","tall-plants",416,86,frame=1)
    p("env2","fridge",412,157)
    p("env2","kitchen-counter-sink",337,156)
    p("env4","coffee-machine",297,156,state="brew",frame=0)
    p("env5","coat-rack",413,259)
    p("env5","shoe-rack",344,256)
    p("env5","spring-cherry-branch",190,-3)
    p("env5","framed-art",283,-2,frame=1)
    p("env5","wall-shelves",368,-3,frame=2)
    p("env5","hanging-plants",2,26,frame=2)
    p("env5","spring-flowers",54,71,frame=2)
    bright,p=room("Bright coworking space","floor-polished-stone","wall-white-plaster")
    for y in range(64,256,32):
        for x in range(32,320,32):p("env5","floor-wood-maple",x,y,frame=(x//32+y//32)%2,role="floor")
    for y in range(256,352,32):
        for x in range(320,480,32):p("env5","floor-checker-sage",x,y,role="floor")
    p("env5","rugs",334,189,frame=0,role="floor-overlay")
    p("env5","plant-wall",2,55)
    for x in [67,159,251]:
        p("env5","desks-front",x,94,frame=1)
        p("env2","monitors",x,69,frame=1)
        p("env5","chairs-front",x,125,frame=1)
    p("env5","conference-table-eight",354,92)
    p("env5","projector-screen",357,-4)
    p("env5","notice-board",275,-3)
    p("env5","cubicle-straight",110,151,state="r0")
    p("env3","laptop-table-side-right",75,223)
    p("env5","chairs-side-right",49,223,frame=0)
    p("env3","laptop-table-side-left",180,223)
    p("env5","chairs-side-left",206,223,frame=0)
    p("env5","whiteboard-wheels",264,220)
    p("env5","sofas",342,157,state="down",frame=1)
    p("env5","coffee-table-sets",347,202,frame=1)
    p("env5","kitchen-island",346,258)
    p("env2","fridge",417,247)
    p("env5","printer-station",269,288)
    p("env5","bookshelves",7,270,state="cabinet",frame=1)
    p("env5","tall-plants",416,72,frame=0)
    p("env5","hanging-plants",190,-3,frame=0)
    p("env5","neon-open",415,-3)
    for title,im in [("cozy-studio",cozy),("bright-coworking",bright)]:
        im.resize((960,704),Image.Resampling.NEAREST).convert("RGB").save(output(f"examples/{title}-2x.png"))
    jsave("example-scenes.json",{"version":1,"kind":"art-composition-evidence","notes":"Production-pixel compositions, not installed or validated runtime layouts. All drawn art comes from env2..env5; labels are outside scenes.","scenes":scenes})
    return cozy,bright

def preview(scenes):
    width=1984;cols=13;cw=150;ch=153
    groups=[]
    for category,title in CATEGORY_NAMES.items():
        representatives={}
        for it in ITEMS:
            # Main overview shows material/family representatives; QA pages contain all frames.
            key=it["source"]["sheet"]
            if category=="walls" and "white-plaster" in key:key=it["id"]
            if category=="furniture":key=it["id"]
            if category=="seasonal":key=it["id"]
            if it["category"]==category and key not in representatives:representatives[key]=it
        groups.append((title,list(representatives.values())))
    height=76+sum(40+math.ceil(len(items)/cols)*ch for title,items in groups)+798
    canvas=Image.new("RGBA",(width,height),rgba("cream"));d=ImageDraw.Draw(canvas)
    d.text((18,13),"ENV5  |  BUILD YOUR OWN OFFICE",font=font(28),fill=rgba("ink"))
    d.text((18,49),"ENV2 palette / 32px tiles / 64px props / nearest-neighbor 2x / full frame inventory in qa/",font=font(15),fill=rgba("wood"))
    y=76
    for title,items in groups:
        d.rectangle((16,y,width-17,y+29),fill=rgba("teal_dark"));d.text((24,y+5),title,font=font(18),fill=rgba("cream"));y+=40
        for index,it in enumerate(items):
            x=16+(index%cols)*cw;yy=y+(index//cols)*ch
            im=load_frame(it["source"]);im=im.resize((im.width*2,im.height*2),Image.Resampling.NEAREST)
            d.rectangle((x+1,yy,x+cw-7,yy+127),fill=rgba("ivory"));canvas.alpha_composite(im,(x+(cw-im.width)//2,yy+(128-im.height)//2))
            for j,line in enumerate(wrap_label(d,it["name"],cw-12,font(11))[:2]):d.text((x+4,yy+130+j*12),line,font=font(11),fill=rgba("ink"))
        y+=math.ceil(len(items)/cols)*ch
    d.text((18,y+12),"TWO PLAYER-MADE OFFICES  /  production art only  /  2x",font=font(21),fill=rgba("ink"))
    for x,title,scene in [(16,"COZY STUDIO",scenes[0]),(1008,"BRIGHT COWORKING SPACE",scenes[1])]:
        d.text((x,y+48),title,font=font(19),fill=rgba("teal_dark"));canvas.alpha_composite(scene.resize((960,704),Image.Resampling.NEAREST),(x,y+78))
    canvas.convert("RGB").save(output("preview-env5.png"))

def catalog():
    jsave("catalog.json",{"version":1,"packId":"env5","title":"Office customization pack",
        "kind":"production-editor-catalog","footprintUnit":"16px runtime tile at suggestedScale",
        "notes":"Catalog metadata is an adapter input, not assets.json. Only explicitly listed rotations exist. Source cells/anchors follow ENV2; collision rectangles are recommendations. No runtime definitions or editor code were changed.",
        "categories":[{"id":k,"name":v} for k,v in CATEGORY_NAMES.items()],
        "palette":"palette.json","items":ITEMS})

def build():
    ITEMS.clear();REGIONS.clear();SOURCES.clear()
    output("palette.json").write_bytes((ENV2/"palette.json").read_bytes())
    floors();walls();structure();furniture();new_props();seasonal();catalog()
    jsave("qa/animation-regions.json",REGIONS)
    contact_pages();scenes=example_offices();preview(scenes)
    SOURCES.update(json.loads((ROOT/"recolor/source-hashes.json").read_text(encoding="utf-8")))
    track(ENV2/"palette.json");track(REPO/"tools/anim_normalize.py")
    jsave("qa/source-hashes.json",SOURCES)
    check()

def assert_fixed(frames,mask):
    arrays=np.stack([np.array(f.convert("RGBA")) for f in frames])
    if not np.all(arrays[:,~mask]==arrays[0,~mask]):
        raise AssertionError("Non-animated RGBA bytes changed")
    return hashlib.sha256(arrays[0,~mask].tobytes()).hexdigest()

def check():
    report={"version":1,"passed":False,"sheets":[],"animations":[],"counts":{},"errors":[]}
    def require(condition,message):
        if not condition:report["errors"].append(message)
    cat=json.loads((ROOT/"catalog.json").read_text(encoding="utf-8"));items=cat["items"]
    require((ROOT/"palette.json").read_bytes()==(ENV2/"palette.json").read_bytes(),"ENV2 palette bytes changed")
    require(len({it["id"] for it in items})==len(items),"Duplicate catalog IDs")
    templates={s:json.loads((ENV2/f"sheets/{n}.manifest.json").read_text(encoding="utf-8")) for s,n in [(32,"wall-navy"),(64,"plants")]}
    regions=json.loads((ROOT/"qa/animation-regions.json").read_text(encoding="utf-8"))
    declared=set();covered=set();manifests={};all_frame_count=0;loop_rows=0
    files=sorted((ROOT/"sheets").glob("*.png"))
    require(len(files)==len(list((ROOT/"sheets").glob("*.manifest.json"))),"Orphan sheet or manifest")
    for png in files:
        name=png.stem;mp=png.with_suffix(".manifest.json")
        if not mp.exists():report["errors"].append(f"Missing manifest: {name}");continue
        m=json.loads(mp.read_text(encoding="utf-8"));manifests[name]=m
        im=Image.open(png);a=np.array(im);size=m["cell"]["width"]
        require(size in {32,64} and m["cell"]["height"]==size,f"{name}: wrong cell")
        require(im.mode=="RGBA",f"{name}: non-RGBA PNG")
        require(set(m)==set(templates[size]),f"{name}: top-level schema mismatch")
        require(set(m["cell"])==set(templates[size]["cell"]),f"{name}: cell schema mismatch")
        require(set(m["animation"])==set(templates[size]["animation"]),f"{name}: animation schema mismatch")
        require(set(m["frame_layout"])==set(templates[size]["frame_layout"]),f"{name}: frame layout schema mismatch")
        require(m["characterId"]==name and m["game_input"]==png.name and m["sprite_sheet_alpha"]==png.name,f"{name}: filename mismatch")
        require((ROOT/"sheets"/m["base_image"]).is_file(),f"{name}: missing base image")
        require(im.size==(m["frame_layout"]["sheetWidth"],m["frame_layout"]["sheetHeight"]),f"{name}: sheet dimensions")
        require(im.width%size==0 and im.height%size==0,f"{name}: partial cells")
        require(m["animation"]["cellWidth"]==size==m["frame_layout"]["cellWidth"] and m["animation"]["cellHeight"]==size==m["frame_layout"]["cellHeight"],f"{name}: inconsistent cell sizes")
        require(im.width==m["animation"]["columns"]*size and im.height==len(m["animation"]["rows"])*size,f"{name}: row/column dimensions")
        require(set(m["animation"]["rows"])==set(m["frame_layout"]["rows"]),f"{name}: row keys")
        require(set(np.unique(a[:,:,3])).issubset({0,255}),f"{name}: non-binary alpha")
        require(np.all(a[a[:,:,3]==0]==0),f"{name}: nonzero hidden RGB")
        colors={tuple(c) for c in np.unique(a[a[:,:,3]>0,:3],axis=0)}
        unknown=colors-PALETTE;require(not unknown,f"{name}: colors outside palette {sorted(unknown)}")
        used=np.zeros(a.shape[:2],bool);sheet_frames=[];bounds=[]
        for row,(state,rects) in enumerate(m["frame_layout"]["rows"].items()):
            animation=m["animation"]["rows"][state]
            expected_keys=set(next(iter(templates[size]["animation"]["rows"].values())))
            require(set(animation)==expected_keys,f"{name}/{state}: row schema mismatch")
            require(animation["row"]==row and animation["frames"]==len(rects),f"{name}/{state}: count/index")
            require(len(animation["durations_ms"])==len(rects) and all(x>0 for x in animation["durations_ms"]),f"{name}/{state}: timing")
            row_frames=[]
            for i,r in enumerate(rects):
                require(r=={"x":i*size,"y":row*size,"w":size,"h":size},f"{name}/{state}/{i}: rectangle")
                f=im.crop((r["x"],r["y"],r["x"]+size,r["y"]+size));fa=np.array(f);bbox=f.getbbox()
                require(bbox is not None,f"{name}/{state}/{i}: empty frame")
                if size==32:require(np.all(fa[:,:,3]==255),f"{name}/{state}/{i}: tile not opaque")
                elif bbox:require(bbox[0]>=2 and bbox[1]>=3 and bbox[2]<=62 and bbox[3]<=61,f"{name}/{state}/{i}: safe margin {bbox}")
                used[r["y"]:r["y"]+size,r["x"]:r["x"]+size]=True
                declared.add((name,state,i));row_frames.append(f);sheet_frames.append(f);bounds.append(bbox)
                all_frame_count+=1
            if animation["loop"]:
                loop_rows+=1
                require(name in regions,f"{name}: loop without declared mask")
                require(len({f.tobytes() for f in row_frames})==len(row_frames),f"{name}: duplicate loop phases")
        require(np.all(a[~used]==0),f"{name}: trailing cells not byte zero")
        animation_check=None
        if name in regions:
            mask=np.array(Image.open(ROOT/regions[name]["mask"]).convert("L"))>0
            require(mask.shape==(size,size) and mask.any() and not mask.all(),f"{name}: invalid/broad animation mask")
            try:
                static_hash=assert_fixed(sheet_frames,mask)
                normalized,returned,api=normalize_frames(sheet_frames,name,max_shift=0,animated_mask=mask,mode="static-body-with-animated-mask")
                require(api["pixels_made_identical"]==0,f"{name}: API would repair a defect")
                require(all(f.tobytes()==n.tobytes() for f,n in zip(sheet_frames,normalized)),f"{name}: normalization modified final bytes")
                require(np.array_equal(returned,mask),f"{name}: API mask changed")
                animation_check={"sheet":name,"fixedRgbaSha256":static_hash,"api":api}
                report["animations"].append(animation_check)
            except AssertionError as e:report["errors"].append(f"{name}: {e}")
        report["sheets"].append({"sheet":f"sheets/{png.name}","cellSize":size,"dimensions":list(im.size),"frames":len(sheet_frames),"colors":len(colors),"alpha":"binary","unknownColors":len(unknown),"frameBounds":bounds,"sha256":hashlib.sha256(png.read_bytes()).hexdigest()})
    for it in items:
        require(it["category"] in CATEGORY_NAMES,f"{it['id']}: category")
        require(type(it["blocking"]) is bool and bool(it["tags"]),f"{it['id']}: metadata")
        require("recolorFamily" in it,f"{it['id']}: missing recolor family")
        fp=it["footprint"];require(set(fp)=={"width","height"} and all(isinstance(v,int) and v>0 for v in fp.values()),f"{it['id']}: footprint")
        require(len({r["rotation"] for r in it["rotationVariants"]})==len(it["rotationVariants"]),f"{it['id']}: duplicate rotations")
        require(any(r["source"]==it["source"] and r["footprint"]==fp for r in it["rotationVariants"]),f"{it['id']}: default rotation missing")
        for src in [it["source"]]+[r["source"] for r in it["rotationVariants"]]:
            name=Path(src["sheet"]).stem
            require(src["sheet"]==f"sheets/{name}.png" and src["manifest"]==f"sheets/{name}.manifest.json",f"{it['id']}: source filename")
            require((name,src["state"],src["frame"]) in declared,f"{it['id']}: missing source frame")
            covered.add((name,src["state"],src["frame"]))
            if name in manifests and manifests[name]["animation"]["rows"][src["state"]]["loop"]:
                covered.update((name,src["state"],i) for i in range(manifests[name]["animation"]["rows"][src["state"]]["frames"]))
        for r in it["rotationVariants"]:require(r["rotation"] in {0,90,180,270},f"{it['id']}: invalid angle")
    require(declared==covered,f"Catalog does not cover exported frames: {sorted(declared-covered)}")
    recolor_checks=[]
    for family,(_,_,_,_,colors,_) in RECOLOR_BASES.items():
        base=np.array(Image.open(ROOT/f"recolor/{family}/base.png").convert("RGBA"))
        mask=np.array(Image.open(ROOT/f"recolor/{family}/mask.png").convert("L"))>0
        proof=json.loads((ROOT/f"recolor/{family}/variants/recolor.report.json").read_text(encoding="utf-8"))
        require(proof["kind"]=="sprite-gen-recolor-report",f"{family}: recolor proof")
        for color in colors:
            result=np.array(recolored(family,color))
            require(np.array_equal(base[:,:,3],result[:,:,3]),f"{family}/{color}: alpha changed")
            require(np.array_equal(base[~mask],result[~mask]),f"{family}/{color}: non-material pixels changed")
        recolor_checks.append({"family":family,"colorways":colors,"alphaIdentical":True,"nonMaterialBytesIdentical":True,"report":f"recolor/{family}/variants/recolor.report.json"})
    # Independent negative control: a body defect must fail BEFORE normalization.
    negative=False
    body=Image.open(ROOT/"sheets/winter-tree.png").convert("RGBA")
    frames=[body.crop((i*64,0,i*64+64,64)) for i in range(3)]
    mask=np.array(Image.open(ROOT/"qa/masks/winter-tree.png"))>0
    corrupted=frames[1].copy();corrupted.putpixel((30,57),rgba("blue"))
    try:assert_fixed([frames[0],corrupted],mask)
    except AssertionError:negative=True
    require(negative,"Negative control failed to reject changed tree pot")
    source_hashes=json.loads((ROOT/"qa/source-hashes.json").read_text(encoding="utf-8"))
    for path,digest in source_hashes.items():require(hashlib.sha256((REPO/path).read_bytes()).hexdigest()==digest,f"Read-only source changed during build: {path}")
    scenes=json.loads((ROOT/"example-scenes.json").read_text(encoding="utf-8"))
    for scene in scenes["scenes"]:
        require((scene["nativeWidth"],scene["nativeHeight"],scene["scale"])==(480,352,2),"Example scene dimensions")
        for p in scene["placements"]:require(p["pack"] in {"env2","env3","env4","env5"},"Foreign art in example")
    report.update(passed=not report["errors"],counts={"sheets":len(files),"manifests":len(files),"declaredFrames":all_frame_count,"catalogItems":len(items),"rotationVariants":sum(len(it["rotationVariants"]) for it in items),"itemsByCategory":dict(Counter(it["category"] for it in items)),"loopRows":loop_rows,"newGenerationCalls":2,"generatedBodies":len(GENERATED),"recolorBakes":len(RECOLOR_BASES)},recolorChecks=recolor_checks,negativeControlRejected=negative,sourceFilesVerified=len(source_hashes))
    jsave("qa/export-report.json",report)
    print(json.dumps({"passed":report["passed"],"counts":report["counts"],"errors":report["errors"]},indent=2),flush=True)
    if report["errors"]:raise SystemExit(1)

if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command",choices=["prepare","recolor","build","check"])
    args=parser.parse_args()
    globals()[args.command]()
