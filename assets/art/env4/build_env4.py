"""Ambient office art; all writes stay in env4. Run prepare, build, or check."""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

sys.dont_write_bytecode = True
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
ART = ROOT.parent
ENV2 = ART / "env2"
sys.path.insert(0, str(REPO))
from tools.anim_normalize import normalize_frames

CELL = 64
ANCHOR = (32, 61)
PALETTE = [tuple(c) for c in json.loads((ENV2 / "palette.json").read_text(encoding="utf-8"))["colors"]]
C = {"outline": (25,30,30), "dark": (16,40,44), "navy": (38,45,56),
     "wood": (120,65,40), "wood_mid": (154,92,58), "honey": (181,128,67),
     "gold": (206,137,79), "ivory_dark": (219,197,163), "ivory": (231,209,172),
     "cream": (250,236,201), "teal_dark": (29,58,57), "teal": (35,94,92),
     "sage": (93,177,165), "bright": (78,181,169), "blue": (50,85,113),
     "blue_mid": (72,114,143), "blue_light": (100,146,163),
     "red": (154,66,64), "red_light": (191,101,84), "grey": (62,64,63)}
KEY = {"name":"magenta", "hex":"#FF00FF", "rgb":[255,0,255], "selection":"manual"}

def write_json(path, data):
    path = ROOT / path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")

def existing(name, index=0, state=None, directory=ENV2 / "sheets"):
    m = json.loads((directory / f"{name}.manifest.json").read_text(encoding="utf-8"))
    row = m["frame_layout"]["rows"][state or next(iter(m["frame_layout"]["rows"]))]
    r = row[index]
    return Image.open(directory / m["game_input"]).convert("RGBA").crop((r["x"],r["y"],r["x"]+r["w"],r["y"]+r["h"]))

def prepare():
    (ROOT / "references").mkdir(parents=True, exist_ok=True)
    ref = Image.new("RGBA", (384,128), (*C["ivory"],255))
    for i,name in enumerate(["vending-machine","fridge","bookshelf","desks","plants","mug"]):
        ref.alpha_composite(existing(name), (i*64,32))
    ref.resize((1152,384), Image.Resampling.NEAREST).save(ROOT / "references/style.png")
    actions = {
      "leisure_a": (5, "Five DIFFERENT static objects, left to right: 0 a round wall DARTBOARD, 26x26 logical pixels, ivory/dark segmented rings, red/teal bullseye, three tiny darts ATTACHED to its face. 1 compact PING-PONG TABLE, 58x34 pixels, muted teal top, straight horizontal front edge, thin cream court markings, joined net across center and four walnut/charcoal legs; no ball (added later). 2 soft brick-red BEAN BAG, 32x24 pixels, pear-shaped sagging cushion with simple seam, no floor. 3 navy/walnut ARCADE CABINET, 30x48 pixels, upright full front, sloped console with tiny red joystick and cream buttons, large EMPTY rectangular dark teal screen inset in upper front, cream marquee, no letters. 4 READING NOOK as one connected prop, 44x36 pixels: low walnut bookshelf holding cream/teal/red books, floor cushion in muted dusty blue directly against its lower front edge and one open cream book ON the cushion. Everything connected, no people or floor."),
      "leisure_b": (4, "Four DIFFERENT static objects, left to right: 0 STANDING DESK 48x44 logical pixels, honey walnut top, tall ivory adjustable legs, small navy flat monitor and keyboard on top, no chair or person. 1 FISH TANK 44x34 pixels, horizontal rectangle viewed from front/slightly above, walnut stand and charcoal top rim, EMPTY clear muted teal glass interior, few gravel pixels and a tiny plant only along bottom; no fish (added later), no scenery behind it. 2 LAVA LAMP 16x34 pixels, tall clear tapered glass between cream cap and charcoal/cream pedestal, amber empty glass, no moving blobs (added later). 3 DESK FAN 26x30 pixels, round ivory/charcoal wire grille facing front, teal internal blades, walnut/charcoal neck and oval base, full silhouette, no table. Objects separated by huge magenta gaps.")
    }
    request = {"version":1,"kind":"sprite-gen-request","engine":"component-row",
      "character":{"id":"env4-static-bodies","description":"Different office objects; reference supplies palette, logical pixel scale and front/top perspective ONLY, not identity.","base_image":"base-source.png"},
      "cell":{"shape":"square","width":64,"height":64,"safe_margin_x":2,"safe_margin_y":3},
      "chroma_key":KEY,
      "states":{k:{"frames":n,"fps":1,"loop":False,"action":a} for k,(n,a) in actions.items()},
      "style":"Match the attached env2 pixel office art: orthographic 3/4 top-down FRONT view, horizontal edges, NEVER isometric. Chunky true logical pixels, crisp selective 1px dark outlines, warm honey/walnut, ivory, muted teal, dusty blue and brick red. Two or three flat shades per material. No gradients, antialiasing, dithering, text, people, floor patches, ground shadows, motion trails or grid lines. This row is STATIC VARIANT CHOICES, NOT poses of one object. Each specified object appears exactly once as a complete connected silhouette. Huge solid magenta gaps, solid #FF00FF background.",
      "layout":"taxonomy/v1","fit":{"pixel_unfake":False,"resample":"kcentroid","palette_size":32,"align_x":"bbox-center","align_y":"bottom","ground_frames":True},
      "chroma":{"mode":"rgb","unmix_reach":4,"spill_max_fraction":0.005}}
    write_json("request-bodies.json",request)
    (ROOT / "palette.json").write_bytes((ENV2 / "palette.json").read_bytes())
    print("Prepared env4 static-body recipe and style reference.")

def rgba(color, alpha=255):
    return (*C.get(color,color),alpha) if isinstance(color,str) else (*color,alpha)

def blank(size=64):
    return Image.new("RGBA",(size,size))

def rectangle_mask(*boxes, size=64):
    im = Image.new("L",(size,size)); d = ImageDraw.Draw(im)
    for box in boxes:
        d.rectangle(box,fill=255)
    return np.array(im)>0

def clean(im):
    a = np.array(im.convert("RGBA")); a[a[:,:,3]==0]=0
    return Image.fromarray(a)

def quantize(im):
    a = np.array(im.convert("RGBA")); visible = a[:,:,3]>=128
    colors = np.array(list(C.values()),dtype=np.int32)
    rgb = a[:,:,:3].astype(np.int32)
    nearest = ((rgb[:,:,None,:]-colors)**2).sum(3).argmin(2)
    a[:,:,:3] = colors[nearest]; a[:,:,3] = visible*255; a[~visible]=0
    return Image.fromarray(a)

GENERATED = {
    "dartboard":("leisure_a",0,(26,26)), "ping-pong":("leisure_a",1,(58,34)),
    "bean-bag":("leisure_a",2,(32,24)), "arcade":("leisure_a",3,(30,48)),
    "reading-nook":("leisure_a",4,(44,36)), "standing-desk":("leisure_b",0,(48,44)),
    "fish-tank":("leisure_b",1,(44,34)), "lava-lamp":("leisure_b",2,(16,34)),
    "desk-fan":("leisure_b",3,(26,30))}

def fitted(name):
    state,index,size = GENERATED[name]
    p = ROOT / f"runs/bodies/frames/{state}/frame-{index}.png"
    im = Image.open(p).convert("RGBA"); im = im.crop(im.getbbox())
    scale = min(size[0]/im.width,size[1]/im.height)
    native = size if name in {"ping-pong","standing-desk","fish-tank","reading-nook"} else (max(1,round(im.width*scale)),max(1,round(im.height*scale)))
    im = im.resize(native,Image.Resampling.NEAREST)
    cell=blank(); cell.alpha_composite(im,((64-im.width)//2,61-im.height))
    return quantize(cell)

REGIONS = {}

def sheet(name, rows, mask=None, *, directory="sheets", size=64, body=None, note=""):
    # rows: state -> (frames, fps, loop, optional durations). Copy env2 schema.
    template=json.loads((ENV2 / "sheets/plants.manifest.json").read_text(encoding="utf-8"))
    columns=max(len(v[0]) for v in rows.values()); canvas=Image.new("RGBA",(columns*size,len(rows)*size))
    template.update(characterId=name,game_input=f"{name}.png",sprite_sheet_alpha=f"{name}.png",curation_applied=False,
                    sprite_sheet_alpha_report="../qa/export-report.json",base_image=f"../bodies/{name}.png")
    template["cell"].update(width=size,height=size,safe_margin_x=2 if size==64 else 1,safe_margin_y=3 if size==64 else 1)
    animation={}; layout={}
    for row,(state,data) in enumerate(rows.items()):
        frames,fps,loop=data[:3]; durations=data[3] if len(data)>3 else [round(1000/fps)]*len(frames)
        animation[state]={"row":row,"frames":len(frames),"fps":fps,"durations_ms":durations,"loop":loop,"frame_variant":"pixel"}
        layout[state]=[]
        for i,frame in enumerate(frames):
            assert frame.size==(size,size)
            canvas.paste(clean(frame),(i*size,row*size))
            layout[state].append({"x":i*size,"y":row*size,"w":size,"h":size})
    template["animation"]={"cellWidth":size,"cellHeight":size,"columns":columns,"rows":animation}
    template["frame_layout"]={"sheetWidth":canvas.width,"sheetHeight":canvas.height,"cellWidth":size,"cellHeight":size,"rows":layout}
    (ROOT/directory).mkdir(exist_ok=True); canvas.save(ROOT/directory/f"{name}.png")
    write_json(f"{directory}/{name}.manifest.json",template)
    (ROOT/"bodies").mkdir(exist_ok=True); clean(body or next(iter(rows.values()))[0][0]).save(ROOT/f"bodies/{name}.png")
    mask = np.zeros((size,size),bool) if mask is None else mask
    (ROOT/"qa/masks").mkdir(parents=True,exist_ok=True)
    Image.fromarray(mask.astype(np.uint8)*255).save(ROOT/f"qa/masks/{name}.png")
    REGIONS[f"{directory}/{name}"]={"mask":f"qa/masks/{name}.png","note":note,"compare_across_states":name!="icons"}

def windows():
    base=blank();d=ImageDraw.Draw(base)
    d.rectangle((6,27,57,58),fill=rgba("outline"));d.rectangle((7,28,56,57),fill=rgba("wood_mid"))
    d.rectangle((8,29,55,56),fill=rgba("wood"));d.rectangle((30,29,33,56),fill=rgba("honey"))
    d.rectangle((8,42,55,44),fill=rgba("honey"));d.rectangle((4,58,59,60),fill=rgba("outline"))
    d.rectangle((5,58,58,59),fill=rgba("gold"));d.line((8,28,54,28),fill=rgba("gold"))
    panes=rectangle_mask((9,30,29,41),(34,30,54,41),(9,45,29,55),(34,45,54,55))
    rows={}
    for state,sky in [("day","blue_light"),("sunset","red_light"),("night","dark"),("rain","blue"),("snow","blue_mid")]:
        landscape=blank();dd=ImageDraw.Draw(landscape);dd.rectangle((8,29,55,56),fill=rgba(sky))
        dd.rectangle((8,49,55,56),fill=rgba("teal_dark"))
        for x,h in [(9,7),(16,4),(24,8),(36,5),(43,9),(51,6)]:
            dd.rectangle((x,56-h,x+5,56),fill=rgba("navy" if state=="night" else "blue"))
            if state in {"night","sunset"}:
                dd.point((x+2,53-h),fill=rgba("gold"))
        if state=="sunset": dd.ellipse((44,35,51,42),fill=rgba("gold"))
        if state=="night":
            dd.ellipse((44,32,49,37),fill=rgba("cream"));dd.ellipse((46,31,50,35),fill=rgba("dark"))
            for p in [(11,33),(22,36),(37,32),(52,39),(39,40)]:dd.point(p,fill=rgba("cream"))
        frames=[]
        for i,offset in enumerate([0,1,2,3]):
            view=landscape.copy();v=ImageDraw.Draw(view)
            cloud="blue" if state=="night" else "ivory_dark"
            v.rectangle((14+offset,34,25+offset,36),fill=rgba(cloud));v.rectangle((17+offset,32,21+offset,35),fill=rgba(cloud))
            if state=="rain":
                for x,y in [(11,32),(20,38),(26,46),(37,36),(46,45),(51,32)]:
                    yy=y+(i%3)*2;v.line((x,yy,x-1,yy+4),fill=rgba("blue_light"))
            if state=="snow":
                for x,y in [(11,32),(21,38),(27,46),(37,36),(45,45),(51,32)]:
                    yy=y+(i%3)*2;xx=x+(i%2);v.point((xx,yy),fill=rgba("cream"));v.point((xx+1,yy+1),fill=rgba("ivory"))
            f=np.array(base);f[panes]=np.array(view)[panes];frames.append(Image.fromarray(f))
        rows[state]=(frames,2,True)
    sheet("window-views",rows,panes,body=base,note="Only four pane interiors; frame, mullions and sill fixed across all five weather/time states.")

def steam(base, phase, origin, height=10):
    im=base.copy();d=ImageDraw.Draw(im);x,y=origin
    for branch in [-2,2]:
        points=[(x+branch+(phase+step)%3-1,y-step*2) for step in range(height//2)]
        for a,b in zip(points,points[1:]):d.line((*a,*b),fill=rgba("ivory_dark" if branch<0 else "cream"))
    return im

def appliances():
    coffee=clean(existing("props",state="coffee_machine",directory=ART/"sheets"))
    steamframes=[steam(coffee,i,(32,15),10) for i in range(3)]
    dripframes=[];brewframes=[]
    for i in range(3):
        f=coffee.copy();d=ImageDraw.Draw(f);d.line((32,31+i*2,32,32+i*2),fill=rgba("wood_mid"))
        dripframes.append(f);brewframes.append(steam(f,i,(32,15),10))
    sheet("coffee-machine",{"steam":(steamframes,3,True),"drip":(dripframes,3,True),"brew":(brewframes,3,True)},rectangle_mask((27,7,37,15),(32,31,32,36)),body=coffee,note="Steam above reservoir and two-pixel drops in the brew opening only; reservoir, casing, cup and cabinet fixed.")
    mug=clean(existing("mug"));frames=[steam(mug,i,(30,47),8) for i in range(3)]
    sheet("mug-steam",{"steam":(frames,2,True)},rectangle_mask((25,41,35,47)),body=mug,note="Steam only above unchanged rim, handle and cup.")
    water=clean(existing("props",state="water_cooler",directory=ART/"sheets"));frames=[]
    for y in [34,29,24]:
        f=water.copy();d=ImageDraw.Draw(f);d.rectangle((30,y,32,y+2),outline=rgba("sage"));d.point((31,y+1),fill=rgba("teal"));frames.append(f)
    sheet("water-cooler",{"bubble":(frames,2,True)},rectangle_mask((30,24,32,26),(30,29,32,31),(30,34,32,36)),body=water,note="Three small bubble positions inside bottle; vessel outline and dispenser fixed.")
    clock=clean(existing("wall-clock"));frames=[];mask=Image.new("L",(64,64));md=ImageDraw.Draw(mask)
    for p in [(32,42),(39,50),(32,57),(24,50)]:
        f=clock.copy();d=ImageDraw.Draw(f);d.line((32,50,*p),fill=rgba("red"));d.point((32,50),fill=rgba("outline"));frames.append(f)
        md.line((32,50,*p),fill=255)
    sheet("wall-clock",{"tick":(frames,1,True)},np.array(mask)>0,body=clock,note="Red second-hand swept paths only; face, rim and 10:10 hour/minute hands fixed outside path. Stylized four quarter-turn ticks, not civil-time simulation.")
    light=clean(existing("ceiling-light-glow"));a=np.array(light);halo=(a[:,:,3]>0)&(a[:,:,3]<255)
    bulb=rectangle_mask((12,50,51,58)) & (a[:,:,3]==255) & (a[:,:,:3].min(2)>=190)
    warm=[];flicker=[]
    for gain in [.65,1,.8]:
        f=a.copy();f[halo,3]=np.maximum(1,np.round(a[halo,3]*gain)).astype(np.uint8);warm.append(Image.fromarray(f))
    for gain in [1,.2,.75]:
        f=a.copy();f[halo,3]=np.maximum(1,np.round(a[halo,3]*gain)).astype(np.uint8)
        f[bulb,:3]=C["cream"] if gain==1 else C["ivory_dark"] if gain<.5 else C["ivory"]
        flicker.append(Image.fromarray(f))
    sheet("ceiling-light",{"warm":(warm,1,True),"flicker":(flicker,3,True,[900,90,150])},halo|bulb,body=light,note="Existing translucent halo alpha and warm bulb fill only; metal/wood fixture silhouette fixed.")
    vending=clean(existing("vending-machine"));frames=[]
    for phase in range(3):
        f=vending.copy();d=ImageDraw.Draw(f)
        for j,y in enumerate([21,34,46]):d.line((24,y,33,y),fill=rgba("sage" if j==phase else "teal_dark"))
        d.point((41,36),fill=rgba("gold" if phase==1 else "red"));frames.append(f)
    sheet("vending-machine",{"glow":(frames,1,True)},rectangle_mask((24,21,33,21),(24,34,33,34),(24,46,33,46),(41,36,41,36)),body=vending,note="Three narrow display light strips and coin indicator; products, glass border and cabinet fixed.")
    fridge=clean(existing("fridge"));opened=fridge.copy();d=ImageDraw.Draw(opened)
    d.rectangle((21,39,42,57),fill=rgba("dark"));d.rectangle((22,40,41,56),fill=rgba("ivory_dark"))
    for y in [45,51,56]:d.line((22,y,41,y),fill=rgba("cream"))
    for x,y,color in [(25,41,"teal"),(31,41,"red"),(36,47,"blue")]:
        d.rectangle((x,y,x+3,y+3),fill=rgba(color));d.line((x+1,y-1,x+2,y-1),fill=rgba("cream"))
    d.polygon([(43,39),(55,42),(55,58),(43,57)],fill=rgba("outline"))
    d.polygon([(45,40),(53,43),(53,56),(45,55)],fill=rgba("ivory"));d.line((50,45,50,51),fill=rgba("grey"))
    sheet("fridge",{"closed":([fridge],1,False),"open":([opened],1,False)},rectangle_mask((21,39,55,58)),body=fridge,note="Lower door/interior and rightward swung leaf only. Upper freezer and outer casing outside this mask fixed; two selectable states, not a looping door.")

PLANT_BOXES=[(17,30,46,40),(19,20,44,34),(16,32,47,44),(21,40,41,49),(15,10,47,24)]

def plants():
    for i,kind in enumerate(["monstera","snake","fern","succulent","ficus"]):
        base=clean(existing("plants",i));a=np.array(base);box=PLANT_BOXES[i];x0,y0,x1,y1=box
        mask=rectangle_mask(box);frames=[base]
        for direction in [1,-1]:
            f=a.copy()
            for y in range(y0,y1+1):
                shift=direction if y<y1-2 else 0
                if shift:
                    row=a[y,x0:x1+1].copy();f[y,x0:x1+1]=0
                    if shift==1:
                        assert not row[-1,3];f[y,x0+1:x1+1]=row[:-1]
                    else:
                        assert not row[0,3];f[y,x0:x1]=row[1:]
            frames.append(Image.fromarray(f))
        sheet(f"plant-{kind}",{"sway":(frames,1,True)},mask,body=base,note=f"Env2 plants variant {i}; upper leaf tips move by one pixel, lower foliage, stem/trunk and pot untouched. Reference phase zero is byte-identical to env2.")

ICON_PATTERNS = {
 "coffee":["..C.C.....","...C.C....","..........",".OOOOOO...",".OCCCCOOO.",".OCCCO.CO.",".OCCCOOO..","..OOOO....","..........",".........."],
 "music-note":["....OOOO..","....OTTO..","....OOOO..","....O..O..","....O..O..","....O..O..","..OOO.OOO.",".OTTO.OTTO","..OO...OO.",".........."],
 "light-bulb":["...OOOO...","..OGGGGO..",".OGCCCGGO.",".OGCGCGGO.","..OGGGGO..","...OGGO...","...OOOO...","...OCCO...","....OO....",".........."],
 "sweat-drop":[".....O....","....OBO...","...OBBBO..","..OBBBBBO.",".OBCCBBBO.",".OBCCBBBO.",".OBBBBBBO.","..OBBBBO..","...OOOO...",".........."],
 "heart":["..........",".OOO.OOO..","ORRRORRRO.","ORRRRRRRO.","ORRRRRRRO.",".ORRRRRO..","..ORRRO...","...ORO....","....O.....",".........."],
 "exclamation-error":["...OOO....","...ORO....","...ORO....","...ORO....","...ORO....","...OOO....","..........","...OOO....","...ORO....","...OOO...."],
 "question":["..OOOOO...",".OGGGGGO..",".OGO.OGO..","....OGO...","...OGO....","...OO.....","..........","...OGO....","...OOO....",".........."],
 "sparkles":["....G.....","....G...C.","...GCG.CCC","GGGCCCG.C.","...GCG....","....G.....",".C..G.....","CCC.......",".C........",".........."],
 "zzz":["OOOO......","..O.......",".O........","OOOO......",".....OOO..","......O...",".....OOO..",".......OOO","........O.",".......OOO"],
 "check":["..........","........O.",".......OTO","......OTO.",".O...OTO..","OTO.OTO...",".OTOTO....","..OTO.....","...O......",".........."],
 "pizza":[".OOOOOOO..",".OGGGGGO..","..OCCCO...","..OCRCO...","...OCCO...","...ORO....","....OO....","....O.....","..........",".........."],
 "headphones":["...OOOO...","..OBBBBO..",".OBO..OBO.",".OB....BO.",".OO....OO.","OOO....OOO","OBO....OBO","OBO....OBO","OOO....OOO",".........."],
 "magnifier":["..OOOO....",".OBBBBO...","OBCCBBBO..","OBCBBBBO..","OBBBBBBO..",".OBBBBO...","..OOOOO...","......OO..",".......OO.","........O."]}

def bubbles():
    colors={"O":"outline","C":"cream","T":"bright","G":"gold","R":"red_light","B":"blue_light"}
    frames=[]
    for name,pattern in ICON_PATTERNS.items():
        icon=blank(12)
        assert len(pattern)==10 and all(len(line)==10 for line in pattern),(name,pattern)
        for y,line in enumerate(pattern):
            for x,char in enumerate(line):
                if char!=".":icon.putpixel((x+1,y+1),rgba(colors[char]))
        sheet(name,{"variants":([icon],1,False)},directory="bubbles",size=12,note="Native 12x12 glyph, one pixel transparent margin, top-left anchor (0,0).")
        frames.append(icon)
    sheet("icons",{"variants":(frames,1,False)},directory="bubbles",size=12,note="Static icon selector strip; columns are independent glyphs, not an animation.")

def activities():
    for name in ["dartboard","bean-bag","reading-nook","standing-desk"]:
        base=fitted(name);sheet(name,{"variants":([base],1,False)},body=base,note="One generated static body, native palette/size assembly only.")
    base=fitted("ping-pong");frames=[]
    ball=rectangle_mask((18,36,20,38),(43,43,45,45))
    for x,y in [(18,36),(43,43)]:
        f=base.copy();d=ImageDraw.Draw(f);d.rectangle((x,y,x+2,y+2),fill=rgba("ivory_dark"));d.rectangle((x,y,x+1,y+1),fill=rgba("cream"));frames.append(f)
    sheet("ping-pong",{"ball":(frames,2,True)},ball,body=base,note="Two discrete ball positions above fixed court; top, net, markings and legs fixed.")
    base=fitted("arcade");a=np.array(base)
    screen=rectangle_mask((24,26,38,36))
    frames=[]
    for phase in range(3):
        f=base.copy();d=ImageDraw.Draw(f);d.rectangle((24,26,38,36),fill=rgba("dark"))
        for x in [26,33]:
            y=28+phase%2;d.line((x,y,x+3,y),fill=rgba("sage"));d.line((x-1,y+1,x+4,y+1),fill=rgba("bright"));d.point((x,y+2),fill=rgba("sage"));d.point((x+3,y+2),fill=rgba("sage"))
        d.rectangle((29+phase,34,32+phase,35),fill=rgba("gold"));frames.append(f)
    sheet("arcade",{"screen":(frames,2,True)},screen,body=base,note="In-screen miniature invaders/game paddle only; cabinet, controls and marquee fixed.")
    base=fitted("fish-tank");frames=[]
    fishmask=rectangle_mask((14,34,48,44))
    for phase in range(4):
        f=base.copy();d=ImageDraw.Draw(f)
        step=[0,1,2,1][phase];facing=-1 if phase==3 else 1
        for x,y,direction,color in [(19+step*2,37,facing,"gold"),(41-step*2,39,-facing,"red_light")]:
            d.line((x-2,y,x+2,y),fill=rgba(color));d.line((x-1,y-1,x+1,y-1),fill=rgba(color));d.line((x-1,y+1,x+1,y+1),fill=rgba(color))
            tail=x-3*direction;d.line((tail,y-1,tail,y+1),fill=rgba(color));d.point((x+direction,y),fill=rgba("outline"))
        yy=42-phase*2;d.point((46,yy),fill=rgba("cream"));d.point((46,yy+1),fill=rgba("sage"));frames.append(f)
    sheet("fish-tank",{"swim":(frames,2,True)},fishmask,body=base,note="Two fish and rising bubbles within glass water only; glass rim, reflections, gravel/plant and stand fixed. Four short swim phases reset, not a long travel cycle.")
    base=fitted("lava-lamp");a=np.array(base);mask=rectangle_mask((28,35,35,48))
    frames=[]
    for phase in range(3):
        f=base.copy();d=ImageDraw.Draw(f)
        y=44-phase*3;d.ellipse((29,y,33,y+3),fill=rgba("red_light"));d.ellipse((32,36+phase*2,34,38+phase*2),fill=rgba("cream"));frames.append(f)
    sheet("lava-lamp",{"flow":(frames,1,True)},mask,body=base,note="Two blobs inside amber glass; cap, glass silhouette and pedestal fixed.")
    base=fitted("desk-fan");a=np.array(base)
    # Repaint moving dark/teal rotor pixels only; ivory wire grille stays above it.
    mask=rectangle_mask((25,36,38,47)) & np.any([np.all(a[:,:,:3]==C[c],axis=2) for c in ["teal","teal_dark","blue","blue_mid","dark","navy"]],axis=0) & (a[:,:,3]==255)
    frames=[];yy,xx=np.indices((64,64));radius=(xx-32)**2+(yy-42)**2
    for phase in range(3):
        b=a.copy();b[mask,:3]=C["dark"]
        angle=np.arctan2(yy-42,xx-32)+phase*math.pi/6
        blades=(np.cos(angle*3)>.1)&(radius>2)&(radius<45)&mask
        b[blades,:3]=C["blue_mid"];frames.append(Image.fromarray(b))
    sheet("desk-fan",{"spin":(frames,4,True)},mask,body=base,note="Interior rotor dark/blue pixels only; every ivory grille wire, hub, neck, switch and base remains fixed.")
    base=blank();d=ImageDraw.Draw(base)
    cable=[(4,35),(12,39),(22,42),(32,43),(42,41),(51,38),(59,35)]
    d.line(cable,fill=rgba("outline"));bulbs=[(10,40,"red_light"),(20,43,"gold"),(31,44,"sage"),(42,42,"red_light"),(53,39,"gold")]
    for x,y,color in bulbs:d.line((x,y-2,x,y),fill=rgba("outline"));d.rectangle((x-1,y+1,x+2,y+4),fill=rgba("outline"))
    mask=rectangle_mask(*[(x,y+2,x+1,y+3) for x,y,_ in bulbs]);frames=[]
    for phase in range(3):
        f=base.copy();d=ImageDraw.Draw(f)
        for i,(x,y,color) in enumerate(bulbs):d.rectangle((x,y+2,x+1,y+3),fill=rgba("ivory_dark" if (phase+i)%3==0 else color))
        frames.append(f)
    sheet("string-lights",{"twinkle":(frames,1,True)},mask,body=base,note="Five bulb interior fills only; drooping cable, holders and bulb outlines fixed. Seasonal red/gold/teal decor, binary alpha.")

def preview():
    width,height=448,288
    scene=Image.new("RGBA",(width,height),rgba("dark"))
    floor=existing("floor-wood-oak");wall=existing("wall-navy");wood=existing("wall-wood")
    for y in range(64,height,32):
        for x in range(0,width,32):scene.alpha_composite(floor,(x,y))
    for y in [0,32]:
        for x in range(0,width,32):scene.alpha_composite(wall if x<320 else wood,(x,y))
    # A returning paneled wall makes this a compact corner rather than a contact sheet.
    for y in range(64,224,32):scene.alpha_composite(wall,(0,y))
    placements=[("window-views","night",1,96,9),("string-lights","twinkle",1,165,-3),
      ("wall-clock","tick",0,240,-8),("dartboard","variants",0,304,-8),
      ("vending-machine","glow",1,37,58),("arcade","screen",1,105,62),
      ("fish-tank","swim",1,238,69),("plant-ficus","sway",0,354,68),
      ("reading-nook","variants",0,327,156),("bean-bag","variants",0,350,217),
      ("standing-desk","variants",0,38,190),("mug-steam","steam",1,69,161),
      ("lava-lamp","flow",1,323,120),("desk-fan","spin",1,25,163),
      ("ping-pong","ball",0,162,176),("plant-monstera","sway",1,264,213)]
    # Env2 rug is a read-only visual reference on the floor, beneath recreation props.
    rug=existing("rugs",0);scene.alpha_composite(rug,(164,173))
    for name,state,index,x,y in placements:
        m,_,frames=load_frames(ROOT/f"sheets/{name}.manifest.json")
        f=[f for s,f in frames if s==state][index];scene.alpha_composite(f,(x,y))
    # Two tiny static env2 seats give the table scale without adding characters.
    chair=existing("office-chairs",0);scene.alpha_composite(chair,(152,225));scene.alpha_composite(chair,(227,225))
    scene.convert("RGB").resize((width*2,height*2),Image.Resampling.NEAREST).save(ROOT/"preview-env4.png")
    write_json("scene.json",{"native_size":[width,height],"export_scale":2,"sampling":"nearest","anchor":[32,61],
      "placements":[{"sheet":f"sheets/{name}.png","state":state,"frame":index,"cell_top_left":[x,y]} for name,state,index,x,y in placements],
      "env2_read_only": ["floor-wood-oak","wall-navy","wall-wood","rugs","office-chairs"],"runtime_integration":False})

def build():
    REGIONS.clear()
    windows();appliances();plants();activities();bubbles()
    write_json("qa/regions.json",REGIONS)
    check();contacts();preview();final_review()
    print("Built production sheets, 3x/4x contacts and 2x night office preview inside env4.")

def final_review():
    # A single last view covers edits to fish, bulb interiors and scene placement.
    life=Image.open(ROOT/"qa/contact-small-life-3x.png").convert("RGB")
    scene=Image.open(ROOT/"preview-env4.png").convert("RGB")
    canvas=Image.new("RGB",(life.width+scene.width,max(life.height,scene.height)),C["ivory"])
    canvas.paste(life,(0,0));canvas.paste(scene,(life.width,0));canvas.save(ROOT/"qa/final-review.png")

def load_frames(path):
    m=json.loads(path.read_text(encoding="utf-8"));im=Image.open(path.parent/m["game_input"]).convert("RGBA")
    frames=[]
    for state,rects in m["frame_layout"]["rows"].items():
        for r in rects:
            frames.append((state,im.crop((r["x"],r["y"],r["x"]+r["w"],r["y"]+r["h"]))))
    return m,im,frames

def assert_fixed(frames,mask,name):
    a=np.stack([np.array(f) for f in frames])
    assert np.all(a[:,~mask]==a[0,~mask]),f"{name}: changed RGBA outside declared region"
    normalized,_,report=normalize_frames(frames,prop=name,max_shift=0,animated_mask=mask,mode="static-body-with-animated-mask")
    assert report["pixels_made_identical"]==0,report
    assert all(f.tobytes()==g.tobytes() for f,g in zip(frames,normalized))
    return report

def check():
    regions=json.loads((ROOT/"qa/regions.json").read_text(encoding="utf-8"))
    schema=json.loads((ENV2/"sheets/plants.manifest.json").read_text(encoding="utf-8"))
    assert (ROOT/"palette.json").read_bytes()==(ENV2/"palette.json").read_bytes()
    palette=set(PALETTE); report={"version":1,"sheets":[],"negative_control":False}
    counts=0;animated=0
    for key,region in regions.items():
        path=ROOT/f"{key}.manifest.json";m,im,records=load_frames(path);size=m["cell"]["width"]
        assert set(m)==set(schema),(key,"schema keys")
        for field in ["cell","chroma_key","animation","frame_layout"]:assert set(m[field])==set(schema[field]),(key,field)
        assert size==m["cell"]["height"]==m["animation"]["cellWidth"]==m["animation"]["cellHeight"]
        assert m["frame_layout"]["cellWidth"]==m["frame_layout"]["cellHeight"]==size
        assert im.size==(m["frame_layout"]["sheetWidth"],m["frame_layout"]["sheetHeight"])
        assert im.size==(m["animation"]["columns"]*size,len(m["animation"]["rows"])*size)
        assert set(m["animation"]["rows"])==set(m["frame_layout"]["rows"])
        sheetarray=np.array(im);used=np.zeros(sheetarray.shape[:2],bool)
        for row,(state,rects) in enumerate(m["frame_layout"]["rows"].items()):
            data=m["animation"]["rows"][state]
            assert set(data)==set(schema["animation"]["rows"]["variants"])
            assert data["row"]==row and data["frames"]==len(rects)==len(data["durations_ms"])
            assert data["fps"]>0 and all(x>0 for x in data["durations_ms"])
            for col,r in enumerate(rects):
                assert r=={"x":col*size,"y":row*size,"w":size,"h":size}
                used[row*size:(row+1)*size,col*size:(col+1)*size]=True
            rowframes=[f for s,f in records if s==state]
            if data["loop"] and len(rowframes)>1:
                assert len({f.tobytes() for f in rowframes})==len(rowframes),(key,state,"duplicate poses")
                animated+=1
        assert not sheetarray[~used].any(),(key,"nonzero trailing cells")
        for state,f in records:
            a=np.array(f);box=f.getbbox();assert box,(key,state,"empty frame")
            marginx=2 if size==64 else 1;marginy=3 if size==64 else 1
            assert box[0]>=marginx and box[1]>=marginy and box[2]<=size-marginx and box[3]<=size-marginy,(key,box,"margin")
            assert not a[a[:,:,3]==0].any(),(key,"hidden RGB")
            assert set(map(tuple,a[a[:,:,3]>0,:3])).issubset(palette),(key,"palette")
            if key!="sheets/ceiling-light":assert set(np.unique(a[:,:,3])).issubset({0,255}),(key,"alpha")
        mask=np.array(Image.open(ROOT/region["mask"]).convert("L"))>0
        assert mask.shape==(size,size) and not mask[0,0]
        if region["compare_across_states"]:
            norm=assert_fixed([f for _,f in records],mask,m["characterId"])
        else:norm={"status":"static-selector-choices"}
        if key.startswith("sheets/plant-"):
            idx=["monstera","snake","fern","succulent","ficus"].index(m["characterId"].removeprefix("plant-"))
            assert records[0][1].tobytes()==clean(existing("plants",idx)).tobytes()
        entry={"sheet":key,"frames":len(records),"mask":region["mask"],"normalization":norm,
               "static_rgba_sha256":hashlib.sha256(np.array(records[0][1])[~mask].tobytes()).hexdigest()}
        report["sheets"].append(entry);counts+=len(records)
    # A bad body byte must FAIL before the normalizer can conceal it.
    path=ROOT/"sheets/vending-machine.manifest.json";m,_,records=load_frames(path)
    mask=np.array(Image.open(ROOT/"qa/masks/vending-machine.png"))>0
    damaged=records[1][1].copy();damaged.putpixel((0,0),(1,0,0,255))
    try:assert_fixed([records[0][1],damaged],mask,"negative-control")
    except AssertionError:report["negative_control"]=True
    assert report["negative_control"]
    report.update(sheet_count=len(regions),declared_frames=counts,animated_rows=animated,all_checks_passed=True)
    write_json("qa/export-report.json",report)
    print(f"PASS: {len(regions)} sheets, {counts} declared frames, {animated} animated rows; fixed RGBA and negative control verified.")

def contacts():
    groups={"windows":["window-views"],"appliances":["coffee-machine","mug-steam","water-cooler","vending-machine","fridge"],
            "plants-light-clock":["plant-monstera","plant-snake","plant-fern","plant-succulent","plant-ficus","wall-clock","ceiling-light"],
            "activities":["dartboard","ping-pong","bean-bag","arcade","reading-nook","standing-desk"],
            "small-life":["fish-tank","lava-lamp","desk-fan","string-lights"]}
    for group,names in groups.items():
        rows=[]
        for name in names:
            m,_,records=load_frames(ROOT/f"sheets/{name}.manifest.json")
            for state in m["animation"]["rows"]:rows.append((f"{name}/{state}",[f for s,f in records if s==state]))
        width=max(len(f) for _,f in rows)*192;canvas=Image.new("RGB",(max(576,width),len(rows)*212),C["ivory"]);d=ImageDraw.Draw(canvas);d.fontmode="1"
        for row,(label,frames) in enumerate(rows):
            y=row*212;d.text((8,y+3),label,fill=C["outline"])
            for col,f in enumerate(frames):
                image=f.resize((192,192),Image.Resampling.NEAREST);canvas.paste(image,(col*192,y+20),image)
        canvas.save(ROOT/f"qa/contact-{group}-3x.png")
    m,im,frames=load_frames(ROOT/"bubbles/icons.manifest.json")
    canvas=Image.new("RGB",(468,156),C["ivory"]);d=ImageDraw.Draw(canvas);d.fontmode="1"
    for i,((name,_),(_,f)) in enumerate(zip(ICON_PATTERNS.items(),frames)):
        x=(i%7)*66;y=(i//7)*78;image=f.resize((48,48),Image.Resampling.NEAREST);canvas.paste(image,(x+8,y+22),image)
        d.text((x+2,y+4),name[:10],fill=C["outline"])
    canvas.save(ROOT/"qa/contact-bubbles-4x.png")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["prepare","build","check"])
    args = parser.parse_args()
    globals()[args.command]()
