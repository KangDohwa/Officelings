"""Pets pack authoring. Every write is contained in this directory."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

sys.dont_write_bytecode = True
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
ART = ROOT.parent
CELL = 32
ANCHOR = (16, 29)
FACINGS = ("down", "left", "right", "up")
PETS = {
    "shiba": "A tan SHIBA INU dog, round chibi head, triangular erect ears, cream cheeks/muzzle/chest, black button nose, curled tan tail, compact quadruped body. Friendly neutral expression. No collar.",
    "tuxedo-cat": "A black-and-white TUXEDO CAT, near-black charcoal coat, ivory muzzle/bib/socks, small triangular ears with muted rose insides, long curled tail, tiny amber eyes. A true quadruped kitten, no clothes.",
    "orange-tabby": "A bright ginger ORANGE TABBY kitten, three dark ginger forehead stripes and darker stripes on body/tail, ivory muzzle and tiny white paws, upright ears, long hooked tail. Slimmer than the old reference cat; bigger round chibi head, saturated ginger, distinct upright silhouette. No clothes.",
    "corgi": "A tan-and-cream WELSH CORGI, very large upright ears, white vertical forehead blaze and cream muzzle/chest, long low loaf body, extremely SHORT legs, tiny stub tail, black button nose. Clearly different from shiba.",
    "hamster-ball": "A tiny round golden HAMSTER inside one clear rolling exercise BALL with blue-grey dark selective rim, two small blue reflection pixels, visible ivory cheeks and pink ears, black dot eyes. Ball is one connected circle, transparent-looking interior on magenta, no opaque white disk. Hamster remains upright, circle 22 logical pixels.",
    "robot-buddy": "A tiny friendly ROBOT buddy on TWO black rubber wheels, cream and muted teal body, oversize rounded-square cream head, dark teal face display with two tiny warm amber dot eyes, single thin antenna with amber tip. Chest has one red button. Compact 18x21 logical pixels. Not a round vacuum; no arms waving.",
    "parrot": "A tiny GREEN PARROT sitting on a connected walnut T-shaped PERCH, round moss-green head, curved amber beak, black dot eye, sage wing highlights, long dark-green tail, two tiny claws gripping the horizontal bar. Perch has narrow vertical post and small flat foot. Complete perch included in every view, small bird itself 12x16 pixels, whole connected object 20x24.",
    "capybara": "An extremely calm chibi CAPYBARA, warm caramel brown, tiny round ears, heavy rounded blunt muzzle, black tiny dot eyes, barrel-shaped squat quadruped with short legs, no visible tail. A small bright ORANGE fruit with one green leaf rests on its head in EVERY view. Neutral sleepy eyes. Body 22x17 logical pixels including fruit. No smile, no clothing."
}

def jwrite(relative, obj):
    p = ROOT / relative
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(obj, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

def existing(name, state=None, index=0, folder="env2/sheets"):
    d = ART / folder
    m = json.loads((d / f"{name}.manifest.json").read_text(encoding="utf-8"))
    r = m["frame_layout"]["rows"][state or next(iter(m["frame_layout"]["rows"]))][index]
    return Image.open(d / m["game_input"]).convert("RGBA").crop((r["x"], r["y"], r["x"]+r["w"], r["y"]+r["h"]))

def prepare():
    (ROOT / "references").mkdir(parents=True, exist_ok=True)
    ref = Image.new("RGBA", (320, 80), (231,209,172,255))
    samples = [existing("cat","sleep"), existing("cat","walk_right"), existing("robot-vacuum"), existing("worker","idle",0,"sheets"), existing("props","sofa",0,"sheets")]
    for i, im in enumerate(samples):
        ref.alpha_composite(im, (i*64+(64-im.width)//2,8))
    ref.resize((1280,320), Image.Resampling.NEAREST).save(ROOT / "references/style-4x.png")
    states = {}
    for pet, desc in PETS.items():
        states[pet] = {"frames":4,"fps":1,"loop":False,"action":
            f"FOUR STATIC DIRECTION VIEWS of the exact same pet: {desc} LEFT TO RIGHT STRICT ORDER: 0 faces DOWN toward viewer (front), 1 faces LEFT (nose points left), 2 faces RIGHT (nose points right), 3 faces UP away from viewer (back of head, NO eyes or face). All four complete standing idle quadruped views, identical coat markings, size, proportions, SAME eye style. NO walking, sitting or sleeping. Huge magenta gaps separate complete silhouettes. Each subject occupies about 18-22 logical pixels in a 32-pixel cell. For bird keep identical fixed perch geometry in all four views. For robot keep wheels beneath body. Body in 3/4 top-down orthographic perspective. This is ONE HORIZONTAL STRIP, never a grid."}
    states["props"] = {"frames":7,"fps":1,"loop":False,"action":
        "SEVEN DIFFERENT standalone connected objects in ONE horizontal strip, left to right exactly: 0 oval terracotta DOG BED with raised rim and ivory cushion, 44x24 logical pixels; 1 compact CAT TREE, walnut base, two sisal columns, ivory lower platform and terracotta round upper cushion, 38x48 pixels; 2 small teal WATER BOWL with blue water inside, 16x9 pixels; 3 small terracotta FOOD BOWL with three brown kibble dots, 16x9 pixels; 4 small round teal-and-ivory BALL, 9x9 pixels; 5 round brick-red YARN ball with short connected loose thread, 12x10 pixels; 6 a vertical sisal SCRATCHING POST with honey rope hatch, walnut base and top cap, 17x30 pixels. No pets, no floor or shadows. Objects are complete, separated by huge magenta gaps. Static props, not poses of one object. Match warm office reference."}
    req = {"version":1,"kind":"sprite-gen-request","engine":"component-row",
        "character":{"id":"pet-direction-seeds","description":"Each row owns a different pet identity. Reference is ONLY style/palette and size context; do not copy existing cat or vacuum identity.","base_image":"base-source.png"},
        "cell":{"shape":"square","width":32,"height":32,"safe_margin_x":4,"safe_margin_y":3},
        "chroma_key":{"name":"magenta","hex":"#FF00FF","rgb":[255,0,255],"selection":"manual"},
        "states":states,
        "style":"2D PIXEL ART chibi office pets. Orthographic 3/4 top-down game sprites, never isometric, crisp logical pixels with selective single-pixel dark charcoal outlines, warm muted tan/ivory/walnut/teal palette with moss green for parrot. Exactly two or three flat shades per coat, tiny 1-pixel eyes, compact cute quadruped animal silhouettes. No smooth digital painting, gradients, antialiasing, dithering, text, labels, ground patches, cast shadows, motion blur or guide lines. A uniform solid #FF00FF background. Render logical pixels visibly as square blocks enlarged 8x or more. Reference character is size/context ONLY; do NOT include a human. Pet rows contain independent facing choices, not generated animation. Each slot contains exactly ONE complete connected subject.",
        "layout":"taxonomy/v1",
        "fit":{"pixel_unfake":False,"resample":"kcentroid","palette_size":24,"align_x":"bbox-center","align_y":"bottom","ground_frames":True},
        "chroma":{"mode":"rgb","unmix_reach":4,"spill_max_fraction":0.005}}
    jwrite("request-seeds.json", req)
    print("Prepared pet direction rows and style reference.")

# The outline/eyes never participate in coat palette swaps.
C = {"outline":(25,30,30), "eye":(9,11,11), "cream":(250,236,201),
     "ivory":(231,209,172), "pink":(191,101,84), "gold":(240,186,57),
     "wood":(120,65,40), "honey":(181,128,67), "teal":(35,94,92),
     "blue":(100,146,163), "green":(93,143,70)}
COATS = {
    "shiba":[(114,64,41),(181,128,67),(236,166,98)],
    "corgi":[(114,64,41),(181,128,67),(236,166,98)],
    "orange-tabby":[(141,70,32),(218,128,53),(245,179,86)],
    "tuxedo-cat":[(38,45,56),(62,64,63),(92,92,90)],
    "hamster-ball":[(154,92,58),(206,137,79),(236,166,98)],
    "robot-buddy":[(29,58,57),(35,94,92),(93,177,165)],
    "parrot":[(40,72,43),(74,116,57),(125,163,80)],
    "capybara":[(108,75,47),(154,112,68),(199,155,103)]}
NAMES = {"shiba":"Shiba", "tuxedo-cat":"Tuxedo cat", "orange-tabby":"Orange tabby",
         "corgi":"Corgi", "hamster-ball":"Hamster in ball", "robot-buddy":"Robot buddy",
         "parrot":"Green parrot", "capybara":"Capybara"}

def color(c):
    return (*C.get(c,c),255) if isinstance(c,str) else (*c,255)

def blank(size=CELL):
    return Image.new("RGBA",(size,size))

def clean(im):
    a=np.array(im.convert("RGBA")); a[a[:,:,3]==0]=0
    return Image.fromarray(a)

def moved(im,dx=0,dy=0):
    result=blank(im.width); result.alpha_composite(im,(dx,dy)); return clean(result)

def extracted(state, index, run="seeds"):
    d=ROOT / "runs" / run
    m=json.loads((d/"frames/frames-manifest.json").read_text(encoding="utf-8"))
    # Paths are owned by the canonical extractor's frame manifest.
    row=next(r for r in m["rows"] if r["state"]==state)
    files=row["files"]
    item=files[index]
    path=item if isinstance(item,str) else item.get("file",item.get("path"))
    p=d/path
    if not p.exists(): p=d/"frames"/path
    return Image.open(p).convert("RGBA")

def fit(im, max_size=(22,22), size=32):
    im=im.crop(im.getbbox())
    scale=min(max_size[0]/im.width,max_size[1]/im.height)
    im=im.resize((max(1,round(im.width*scale)),max(1,round(im.height*scale))),Image.Resampling.NEAREST)
    cell=blank(size);cell.alpha_composite(im,((size-im.width)//2,size-3-im.height));return clean(cell)

def quantize(im, pet):
    a=np.array(im);mask=a[:,:,3]>=128
    extras=[C["pink"]]
    if pet in ("robot-buddy","hamster-ball","capybara","parrot"):extras += [C["gold"],C["blue"],C["green"]]
    palette=np.array([C["outline"], C["eye"], *COATS[pet], C["cream"], C["ivory"], *extras],dtype=np.int32)
    rgb=a[:,:,:3].astype(np.int32)
    ids=((rgb[:,:,None,:]-palette)**2).sum(3).argmin(2)
    a[:,:,:3]=palette[ids];a[:,:,3]=mask*255;a[~mask]=0
    return Image.fromarray(a)

def seeds_contact(states, out):
    w=4*32+80; page=Image.new("RGBA",(w,len(states)*40),(231,209,172,255));d=ImageDraw.Draw(page)
    for y,state in enumerate(states):
        d.text((2,y*40+14),state,fill=color("outline"))
        for i in range(4):page.alpha_composite(fit(extracted(state,i)),(80+i*32,y*40+4))
    (ROOT/out).parent.mkdir(parents=True,exist_ok=True)
    page.resize((w*4,len(states)*160),Image.Resampling.NEAREST).save(ROOT/out)

def head_rectangle(im, facing, pet):
    override=CALIBRATION.get(pet,{}).get(facing,{}).get("head")
    if override:return tuple(override)
    x0,y0,x1,y1=im.getbbox();w=x1-x0;h=y1-y0
    bottom=y0+round(h*(.60 if pet!="robot-buddy" else .70))
    if facing=="left":x1=x0+round(w*.58)
    if facing=="right":x0=x1-round(w*.58)
    return (x0,y0,x1,bottom)

def head_layer(im,rect):
    a=np.array(im);mask=np.zeros(a.shape[:2],bool);x0,y0,x1,y1=rect
    mask[y0:y1,x0:x1]=a[y0:y1,x0:x1,3]>0;a[~mask]=0
    return Image.fromarray(a),mask

def closure(head,eye_points):
    im=head.copy();d=ImageDraw.Draw(im)
    # Only calibrated eye interiors change. Lower pixels form a closed eyelid.
    for x,y in eye_points:
        surrounding=[head.getpixel((x+dx,y+dy)) for dx,dy in [(0,-1),(-1,0),(1,0)]
                     if head.getpixel((x+dx,y+dy))[3] and head.getpixel((x+dx,y+dy))[:3] not in (C["eye"],C["outline"])]
        fill=surrounding[0] if surrounding else color("ivory")
        d.point((x,y),fill=fill);d.point((x,y+1),fill=color("eye"))
    return clean(im)

def eye_points(im,facing,rect,pet):
    # Calibrations can override these after the direction composite review.
    override=CALIBRATION.get(pet,{}).get(facing,{}).get("eyes")
    if override is not None:return [tuple(p) for p in override]
    if facing=="up":return []
    x0,y0,x1,y1=rect
    y=y0+max(3,round((y1-y0)*.62))
    if facing=="down":return [(x0+(x1-x0)//3,y),(x1-1-(x1-x0)//3,y)]
    return [(x0+max(2,(x1-x0)//3),y)] if facing=="left" else [(x1-1-max(2,(x1-x0)//3),y)]

CALIBRATION = {
    "shiba": {
        "down":{"head":[9,10,23,22],"eyes":[[13,16],[19,16]]},
        "left":{"head":[5,8,17,22],"eyes":[[10,16]]},
        "right":{"head":[15,8,27,22],"eyes":[[22,16]]}},
    "tuxedo-cat": {
        "down":{"head":[9,13,23,24],"eyes":[[13,19],[19,19]]},
        "left":{"head":[5,11,17,23],"eyes":[[9,19]]},
        "right":{"head":[15,11,27,23],"eyes":[[22,19]]}},
    "orange-tabby": {
        "down":{"head":[9,12,23,24],"eyes":[[13,19],[19,19]]},
        "left":{"head":[5,11,17,23],"eyes":[[9,18]]},
        "right":{"head":[15,11,27,23],"eyes":[[22,18]]}},
    "corgi": {
        "down":{"head":[6,7,25,23],"eyes":[[12,18],[20,18]]},
        "left":{"head":[5,12,17,25],"eyes":[[9,20]]},
        "right":{"head":[15,12,27,25],"eyes":[[23,20]]}},
    "hamster-ball": {
        "down":{"head":[10,14,23,23],"eyes":[[13,20],[19,20]]},
        "left":{"head":[8,14,17,24],"eyes":[[11,19]]},
        "right":{"head":[15,14,24,24],"eyes":[[20,19]]},
        "up":{"head":[10,14,23,23],"eyes":[]}},
    "robot-buddy": {
        "down":{"head":[9,7,22,21],"eyes":[[13,17],[18,17]]},
        "left":{"head":[11,7,21,21],"eyes":[[12,16]]},
        "right":{"head":[11,7,21,21],"eyes":[[19,16]]},
        "up":{"head":[9,7,22,21],"eyes":[]}},
    "parrot": {
        "down":{"head":[12,7,21,15],"eyes":[[14,11],[18,11]]},
        "left":{"head":[9,7,18,15],"eyes":[[12,10]]},
        "right":{"head":[14,7,23,15],"eyes":[[19,10]]},
        "up":{"head":[12,7,21,15],"eyes":[]}},
    "capybara": {
        "down":{"head":[8,7,24,25],"eyes":[[11,17],[20,17]]},
        "left":{"head":[5,9,17,24],"eyes":[[10,17]]},
        "right":{"head":[15,9,27,24],"eyes":[[21,17]]},
        "up":{"head":[8,7,24,25],"eyes":[]}}
}

def main_component(im):
    a=np.array(im); remaining=set(zip(*np.nonzero(a[:,:,3]>0)));groups=[]
    while remaining:
        start=remaining.pop();stack=[start];group=[start]
        while stack:
            y,x=stack.pop()
            for dy in (-1,0,1):
                for dx in (-1,0,1):
                    p=(y+dy,x+dx)
                    if p in remaining:remaining.remove(p);stack.append(p);group.append(p)
        groups.append(group)
    if groups:
        for group in sorted(groups,key=len,reverse=True)[1:]:
            if len(group)<=3:
                for y,x in group:a[y,x]=0
    return Image.fromarray(a)

def final_seed(pet,index):
    im=main_component(quantize(fit(extracted(pet,index), (22,22)),pet))
    if pet=="orange-tabby" and index==3:
        # The generated up view retained a profile. Author its rear from front.
        im=main_component(quantize(fit(extracted(pet,0),(22,22)),pet))
        d=ImageDraw.Draw(im)
        d.ellipse((10,14,21,22),fill=color(COATS[pet][1]),outline=color("outline"))
        for x in (13,16,19):d.line((x,14,x,16),fill=color(COATS[pet][0]))
        d.rectangle((13,23,18,26),fill=color(COATS[pet][1]))
    if pet=="capybara":
        x,y=[(14,10),(9,12),(18,12),(14,10)][index]
        d=ImageDraw.Draw(im);d.ellipse((x,y,x+4,y+3),fill=(229,151,93,255),outline=color("honey"))
        d.line((x+3,y-1,x+4,y-2),fill=color("green"))
    if pet=="hamster-ball":
        d=ImageDraw.Draw(im);d.ellipse((5,7,26,28),outline=color("blue"))
        d.line((8,11,8,13),fill=color("cream"))
    if pet=="robot-buddy":im=rolling(im,0,pet,FACINGS[index])
    return im

def prepare_props():
    req=json.loads((ROOT/"request-seeds.json").read_text(encoding="utf-8"))
    req["states"]={"props":req["states"]["props"]}
    req["cell"].update(width=64,height=64,safe_margin_x=3,safe_margin_y=3)
    req["character"]["id"]="pet-props"
    jwrite("request-props.json",req)
    # Seed run only declares pets; props have their own 64px source run.
    run=ROOT/"runs/seeds/sprite-request.json"
    r=json.loads(run.read_text(encoding="utf-8"));r["states"].pop("props",None)
    run.write_text(json.dumps(r,indent=2)+"\n",encoding="utf-8")

def blink_base(base,head,closed):
    a=np.array(base);mask=np.array(head)[:,:,3]>0;a[mask]=np.array(closed)[mask]
    return clean(Image.fromarray(a))

def rolling(base,phase,pet,facing):
    im=base.copy();d=ImageDraw.Draw(im)
    if pet=="hamster-ball":
        # Rim remains a fixed circle; only two rim markers travel around it.
        for x,y in [(16,8),(25,18),(16,28),(6,18)]:
            d.point((x,y),fill=color("blue"))
        p=[(22,11),(25,21),(10,26)][phase%3]
        d.line((p[0],p[1],p[0]+1,p[1]),fill=color("cream"))
    else:
        for x in ((15,) if facing in ("left","right") else (11,20)):
            d.rectangle((x-1,25,x+1,28),fill=color("outline"))
            d.point((x-1+(phase%2),26+((phase//2)%2)),fill=color("ivory"))
    return clean(im)

def build_pet(pet):
    out=ROOT/pet;out.mkdir(exist_ok=True)
    bases=[final_seed(pet,i) for i in range(4)];heads=[];masks=[];closed=[];eyes=[];rects=[]
    for i,facing in enumerate(FACINGS):
        rect=head_rectangle(bases[i],facing,pet);head,mask=head_layer(bases[i],rect)
        ep=eye_points(bases[i],facing,rect,pet)
        # Single canonical set of tiny eyes for this authored facing.
        if ep:
            d=ImageDraw.Draw(head)
            for x,y in ep:d.line((x,y,x,y+1),fill=color("gold" if pet=="robot-buddy" else "eye"))
            bases[i].alpha_composite(head)
            mask=np.array(head)[:,:,3]>0
        heads.append(head);masks.append(mask);closed.append(closure(head,ep));eyes.append(ep);rects.append(rect)
        (out/"heads").mkdir(exist_ok=True);head.save(out/f"heads/{facing}.png")
    rows={"idle":(bases,1,False)}
    if pet=="parrot":
        # One perch copied to every cell, all states. Generated perches differ.
        for i in range(4):
            a=np.array(bases[i]);a[19:]=0;bases[i]=Image.fromarray(a)
            d=ImageDraw.Draw(bases[i])
            if i in (1,2):d.line((12 if i==2 else 19,17,9 if i==2 else 22,23),fill=color(COATS[pet][0]),width=2)
            d.line((14,18,14,20),fill=color("outline"));d.line((18,18,18,20),fill=color("outline"))
            d.rectangle((7,20,24,21),fill=color("wood"))
            d.rectangle((15,21,16,27),fill=color("honey"));d.line((11,28,21,28),fill=color("outline"))
        rows["idle"]=(bases,1,False)
        bob=[];flap=[]
        for phase in range(4):
            a=np.array(bases[2]);a[masks[2]]=0;f=Image.fromarray(a);f.alpha_composite(moved(heads[2],0,-(phase%2)))
            bob.append(clean(f))
            f=bases[2].copy();d=ImageDraw.Draw(f)
            wing=[(10,19,14,22),(8,16,13,21),(6,13,12,19),(8,16,13,21)][phase]
            d.ellipse(wing,fill=color(COATS[pet][2]),outline=color("outline"));f.alpha_composite(heads[2]);flap.append(clean(f))
        rows["head_bob"]=(bob,3,True);rows["wing_flap"]=(flap,6,True)
    else:
        for i,facing in enumerate(FACINGS):
            if pet in ("hamster-ball","robot-buddy"):
                frames=[rolling(bases[i],p,pet,facing) for p in range(3 if pet=="hamster-ball" else 4)]
                # Exactly reapply the accepted head, after moving rim/wheel markers.
                for f in frames:f.alpha_composite(heads[i])
                rows[f"roll_{facing}"]=(frames,6,True)
            else:
                rows[f"walk_{facing}"]=([gait(bases[i],heads[i],masks[i],p,facing,pet) for p in range(4)],5 if pet=="capybara" else 7,True)
    if pet in ("parrot","hamster-ball","robot-buddy"):
        sit=[bases[0].copy(),bases[0].copy()]
        # Rest is subtle: chest/wing status pixel, outside the head and support.
        d=ImageDraw.Draw(sit[1]);d.point((25,18) if pet=="hamster-ball" else (12,17) if pet=="parrot" else (17,23),fill=color(COATS[pet][2]))
        rows["sit"]=(sit,2,True)
        sleep=[]
        for p in range(3):
            f=blink_base(bases[2],heads[2],closed[2]);d=ImageDraw.Draw(f)
            if pet=="parrot":d.point((12,17-p%2),fill=color(COATS[pet][2]))
            elif pet=="hamster-ball":d.point((12,24-p%2),fill=color(COATS[pet][2]))
            else:d.line((13,22-p%2,16,22-p%2),fill=color(COATS[pet][2]))
            sleep.append(clean(f))
    else:
        rows["sit"]=([reposed_body(heads[0],pet,"sit",p) for p in range(2)],2,True)
        sleep=[reposed_body(closed[2],pet,"sleep",p%2) for p in range(3)]
    rows["sleep"]=(sleep,1,True)
    eat=[]
    for p in range(3):
        f=bases[2].copy();d=ImageDraw.Draw(f)
        if pet=="robot-buddy":d.point((16,23),fill=color([C["teal"],C["gold"],C["pink"]][p]))
        else:
            # Tongue/beak motion is the only designated face change here.
            x=min(29,rects[2][2]);y=min(27,rects[2][3]-2)
            d.line((x,y,x+(p%2),y+(p//2)),fill=color("gold" if pet=="parrot" else "pink"))
        eat.append(clean(f))
    rows["eat_drink"]=(eat,3,True)
    if pet=="parrot":play=rows["wing_flap"][0]
    elif pet=="hamster-ball":play=[rolling(bases[2],p%3,pet,"right") for p in range(4)]
    elif pet=="capybara":play=[gait(bases[2],heads[2],masks[2],p,"right",pet) for p in (0,1,2,0)]
    else:play=[moved(bases[2],0,y) for y in (0,-2,-4,0)]
    rows["play"]=(play,5 if pet=="capybara" else 7,False)
    react=[bases[0].copy(),bases[0].copy()]
    # Ears/antenna lift one pixel, face is untouched.
    a=np.array(react[1]);x0,y0,x1,y1=rects[0];edge=a[y0:y0+2,x0:x1].copy()
    if y0>0:a[y0-1:y0+1,x0:x1]=edge
    react[1]=clean(Image.fromarray(a));rows["react"]=(react,5,False)
    blinks=[blink_base(bases[i],heads[i],closed[i]) for i in range(4)]
    rows["blink"]=(blinks,1,False)
    if pet=="parrot":
        perch=blank();d=ImageDraw.Draw(perch)
        d.rectangle((7,20,24,21),fill=color("wood"));d.rectangle((15,21,16,27),fill=color("honey"))
        d.line((11,28,21,28),fill=color("outline"))
        for frames,_,_ in rows.values():
            for f in frames:f.alpha_composite(perch)
    m=manifest(pet,rows)
    m["idleFacings"]=list(FACINGS)
    m["pet"]={"name":NAMES[pet],"locomotion":"perched" if pet=="parrot" else "roll" if pet in ("robot-buddy","hamster-ball") else "walk",
              "actionFacings":{"sit":"down","sleep":"right","eat_drink":"right","play":"right","react":"down"},
              "sleepContact":{"x":16,"y":27},"muzzleContact":{"x":rects[2][2],"y":rects[2][3]-2},
              "headLock":{"method":"one accepted opaque head per facing, integer translations only","heads":{f:{"file":f"heads/{f}.png","rect":list(rects[i]),"eyes":[list(p) for p in eyes[i]]} for i,f in enumerate(FACINGS)},
                          "exceptions":["sleep: closed eyes","blink: closed eyes","eat_drink: tongue/beak pixels","react: ears/antenna only"]},
              "integration":"Production art only; an adapter and pet behaviour system are separate work."}
    m["blink"]={}
    for i,facing in enumerate(FACINGS):
        blinks[i].save(out/f"blink-{facing}.png")
        m["blink"][facing]={"file":f"blink-{facing}.png","source":{"state":"idle","frame":i},"durationMs":100,
                            "visibleEyes":facing!="up"}
    m["provenance"]={"provider":"codex","pipeline":"still-image component-row seeds + deterministic pixel assembly","sourceState":pet,
        "sourceFrameHashes":[hashlib.sha256(extracted(pet,i).tobytes()).hexdigest() for i in range(4)],"video":False}
    jwrite(f"{pet}/{pet}.manifest.json",m)
    animation_gif(pet,rows)
    return rows,m

def gait(base,head,headmask,phase,facing,pet):
    a=np.array(base); feet=np.zeros((32,32),bool)
    x0,y0,x1,y1=base.getbbox(); top=max(y1-4,int(np.nonzero(headmask)[0].max())+1)
    middle=(x0+x1)//2
    for leg,(lo,hi) in enumerate([(x0,middle),(middle,x1)]):
        zone=np.zeros((32,32),bool);zone[top:y1,lo:hi]=a[top:y1,lo:hi,3]>0
        feet |= zone
    body=a.copy();body[feet]=0;im=Image.fromarray(body)
    for leg,(lo,hi) in enumerate([(x0,middle),(middle,x1)]):
        patch=np.zeros_like(a);patch[top:y1,lo:hi]=a[top:y1,lo:hi]
        offset=(0,1,0,-1)[(phase+leg*2)%4]
        # Front/back paws alternately lift; profiles swing horizontally.
        dx=offset if facing in ("left","right") else 0
        dy=-1 if facing in ("down","up") and offset>0 else 0
        im.alpha_composite(moved(Image.fromarray(patch),dx,dy))
    # A tiny tail-tip wag changes no face pixels.
    if pet in ("shiba","tuxedo-cat","orange-tabby") and phase%2:
        side=x1-2 if facing in ("down","left","up") else x0+1
        for y in range(max(top-5,y0+7),top-2):
            if a[y,side,3] and not headmask[y,side]:
                im.putpixel((side,y),(0,0,0,0));im.putpixel((side+(1 if side>16 else -1),y),tuple(a[y,side]))
    # Re-apply precisely the same opaque head once, after body animation.
    im.alpha_composite(head);return clean(im)

def reposed_body(head,pet,mode,phase=0):
    im=blank();d=ImageDraw.Draw(im);dark,mid,light=COATS[pet]
    if mode=="sit":
        d.ellipse((9,19,22,27),fill=color(dark),outline=color("outline"))
        d.ellipse((10,19,21,25),fill=color(mid))
        d.rectangle((13,21,18,26),fill=color(mid if pet=="capybara" else "ivory"))
        d.rectangle((10,26,13,28),fill=color("outline"));d.rectangle((18,26,21,28),fill=color("outline"))
        d.line((11,27,13,27),fill=color(light if pet=="capybara" else "cream"));d.line((18,27,20,27),fill=color(light if pet=="capybara" else "cream"))
        if phase:d.point((23,25),fill=color(mid))
        im.alpha_composite(head)
    else:
        # Lying rump is drawn independently; the accepted profile head is copied.
        d.ellipse((8,21-phase,24,28),fill=color(dark),outline=color("outline"))
        d.ellipse((9,22-phase,22,26),fill=color(mid))
        d.line((11,27,19,27),fill=color(light if pet=="capybara" else "ivory"))
        b=head.getbbox();dx=25-b[2];dy=27-b[3]
        im.alpha_composite(moved(head,dx,dy))
    return clean(im)

def manifest(pet,rows,size=32,folder=None):
    folder=folder or pet;out=ROOT/folder;out.mkdir(parents=True,exist_ok=True)
    columns=max(len(data[0]) for data in rows.values());canvas=Image.new("RGBA",(size*columns,size*len(rows)))
    anim={};layout={}
    for y,(state,(frames,fps,loop)) in enumerate(rows.items()):
        anim[state]={"row":y,"frames":len(frames),"fps":fps,"durations_ms":[round(1000/fps)]*len(frames),"loop":loop,"frame_variant":"pixel"}
        layout[state]=[]
        for i,f in enumerate(frames):
            canvas.paste(clean(f),(i*size,y*size));layout[state].append({"x":i*size,"y":y*size,"w":size,"h":size})
    canvas.save(out/f"{pet}.png")
    m={"characterId":pet,"engine":"component-row","game_input":f"{pet}.png","degraded_static_fallback":False,
       "curation_applied":False,"frame_variant":"pixel","sprite_sheet_alpha":f"{pet}.png",
       "sprite_sheet_alpha_report":"../qa/validation.json","base_image":f"../runs/seeds/sprite-sheet-alpha.png",
       "cell":{"shape":"square","width":size,"height":size,"safe_margin_x":2,"safe_margin_y":3},
       "chroma_key":{"name":"magenta","hex":"#FF00FF","rgb":[255,0,255],"selection":"manual"},
       "anchor":{"x":size//2,"y":size-3},
       "animation":{"cellWidth":size,"cellHeight":size,"columns":columns,"rows":anim},
       "frame_layout":{"sheetWidth":canvas.width,"sheetHeight":canvas.height,"cellWidth":size,"cellHeight":size,"rows":layout}}
    jwrite(f"{folder}/{pet}.manifest.json",m);return m

def animation_gif(pet,rows):
    # All pets share a native four-phase review; perch/wheels stay supported.
    out=ROOT/"qa";out.mkdir(exist_ok=True)
    frames=[];durations=[]
    for state,data in rows.items():
        if state in ("idle","blink"):continue
        for f in data[0]:
            bg=Image.new("RGBA",(48,40),(231,209,172,255));bg.alpha_composite(f,(8,4))
            frames.append(bg.resize((192,160),Image.Resampling.NEAREST).convert("RGB"))
            durations.append(round(1000/data[1]))
    frames[0].save(out/f"{pet}-motion.gif",save_all=True,append_images=frames[1:],duration=durations,loop=0,disposal=2)

PROPS = ["dog_bed","cat_tree","water_bowl","food_bowl","ball","yarn","scratching_post"]

def build_props():
    rows={};sizes=[(44,24),(38,48),(16,9),(16,9),(9,9),(12,10),(17,30)]
    palette=np.array([*C.values(),(154,92,58),(154,66,64),(72,114,143)],dtype=np.int32)
    for i,name in enumerate(PROPS):
        f=fit(extracted("props",i,"props"),sizes[i],64);a=np.array(f);mask=a[:,:,3]>=128
        ids=((a[:,:,:3].astype(np.int32)[:,:,None,:]-palette)**2).sum(3).argmin(2)
        a[:,:,:3]=palette[ids];a[:,:,3]=mask*255;a[~mask]=0
        rows[name]=([Image.fromarray(a)],1,False)
    m=manifest("props",rows,64,"props")
    m["base_image"]="../runs/props/sprite-sheet-alpha.png"
    def point(action,target,foot,facing="right",contact="anchor",pets=None):
        return {"action":action,"target":dict(zip(("x","y"),target)),"petAnchor":dict(zip(("x","y"),foot)),
                "facing":facing,"alignPetField":contact,"pets":pets or list(PETS)}
    terrestrial=[p for p in PETS if p not in ("parrot","hamster-ball","robot-buddy")]
    m["interactions"]={
        "coordinates":"Source pixels local to the selected 64x64 prop cell; not tiles or sheet coordinates.",
        "placement":"propTopLeft=propAnchorWorld-(32,61)*scale. petTopLeft=propTopLeft+target*scale-petContact*scale. For approaching on floor, align pet anchor (16,29) to petAnchor instead. Contacts can lie outside the prop cell.",
        "points":{
            "dog_bed":[point("sleep",(32,50),(32,66),"right","pet.sleepContact",terrestrial)],
            "cat_tree":[point("sit",(32,20),(32,67),"down","anchor",["tuxedo-cat","orange-tabby"]),point("sleep",(28,39),(32,67),"right","pet.sleepContact",["tuxedo-cat","orange-tabby"])],
            "water_bowl":[point("eat_drink",(32,56),(22,65),"right","pet.muzzleContact",terrestrial)],
            "food_bowl":[point("eat_drink",(32,56),(22,65),"right","pet.muzzleContact",terrestrial)],
            "ball":[point("play",(32,57),(20,64),"right","anchor",["shiba","corgi","tuxedo-cat","orange-tabby"])],
            "yarn":[point("play",(32,56),(20,64),"right","anchor",["tuxedo-cat","orange-tabby"])],
            "scratching_post":[point("play",(32,44),(22,61),"right","anchor",["tuxedo-cat","orange-tabby"])]}}
    jwrite("props/props.manifest.json",m)
    text="# Pet props\n\n`props.png` + `props.manifest.json`: straight RGBA, 64×64 cells, anchor (32,61), seven static rows in order: dog_bed, cat_tree, water_bowl, food_bowl, ball, yarn, scratching_post. Each is frame 0, fps 1, loop false. Use nearest-neighbor.\n\nThe manifest owns interaction targets, floor approach anchors, eligible pets and the pet contact field to align. Coordinates are **source cell pixels**; apply scale once. Elevated cat-tree targets need reservation, reachability and depth/occlusion handling in an adapter. Draw pets over the bed cushion, then an optional rim foreground if desired; no separate rim layer is supplied. Hamster ball, robot and fixed-perch parrot do not eat from these floor bowls.\n\nThese are suggested placements, not installed furniture/collision definitions. Cat-tree contacts were checked on the supplied static art; no climb animation or scratching-paw row is supplied.\n"
    (ROOT/"props/README.md").write_text(text,encoding="utf-8")
    page=Image.new("RGBA",(256,160),(231,209,172,255));d=ImageDraw.Draw(page)
    for i,(name,data) in enumerate(rows.items()):
        x=(i%4)*64;y=(i//4)*80;page.alpha_composite(data[0][0],(x,y));d.text((x+1,y+64),name,fill=color("outline"))
        for p in m["interactions"]["points"][name]:
            tx=p["target"]["x"];ty=p["target"]["y"];d.line((x+tx-2,y+ty,x+tx+2,y+ty),fill=color("pink"));d.line((x+tx,y+ty-2,x+tx,y+ty+2),fill=color("pink"))
    page.resize((1024,640),Image.Resampling.NEAREST).save(ROOT/"qa/props-contacts-4x.png")

def recolor_specs():
    colors={"shiba":{"cream":[(160,138,110),(219,197,163),(250,236,201)],"sesame":[(62,64,63),(108,75,47),(181,128,67)]},
            "corgi":{"sable":[(63,46,41),(128,74,47),(199,130,81)],"cream":[(160,138,110),(219,197,163),(250,236,201)]},
            "tuxedo-cat":{"blue-grey":[(38,59,74),(72,114,143),(100,146,163)],"chocolate":[(63,46,41),(108,75,47),(154,112,68)]},
            "orange-tabby":{"silver":[(62,64,63),(137,145,143),(208,218,211)],"brown":[(63,46,41),(128,74,47),(199,155,103)]}}
    for pet,variants in colors.items():
        def hx(c):return "#"+"".join(f"{v:02x}" for v in c)
        spec={"version":1,"kind":"sprite-gen-recolor","match":"exact","tolerance":0,
              "variants":[{"name":name,"map":{hx(a):hx(b) for a,b in zip(COATS[pet],ramp)}} for name,ramp in variants.items()]}
        jwrite(f"{pet}/recolor.spec.json",spec)
    return colors

def document_pet(pet,m,variants):
    info=m["animation"]["rows"]
    lines=[f"# {NAMES[pet]}","",f"`{pet}.png` + `{pet}.manifest.json`: 32×32 cells, four columns, straight binary RGBA, anchor **(16,29)**. Pet silhouette is about 16–22 px. Use nearest-neighbor.","",
           "`idle` and `blink` are facing **choices**, in down / left / right / up order. Never cycle them as animation. The manifest's `frame_layout.rows` owns zero-based source rectangles.","",
           "| Row | State | Frames | FPS | Playback |","| ---: | --- | ---: | ---: | --- |"]
    for state,r in info.items():lines.append(f"| {r['row']} | `{state}` | {r['frames']} | {r['fps']} | {'loop' if r['loop'] else 'select facing' if state in ('idle','blink') else 'once, hold last'} |")
    lines += ["","Each facing has one accepted head in `heads/`. Walk/sit/play copy those pixels, with integer translations for jumps. `blink-*.png` closes calibrated eye pixels for 100 ms; up has no visible eyes and intentionally matches its normal idle. Schedule visible blinks during stationary idle/sit, about every 3–6 seconds. Sleeping uses closed eyes. Only tongue/beak motion and reacting ears/antenna are designated facial/head exceptions. See manifest `pet.headLock` for native rectangles and eye coordinates.","",
              "Action facings and `sleepContact` / `muzzleContact` are in the manifest. To nap on a bed/sofa, align `sleepContact` to the cushion target, keep simulation depth at the floor point and apply furniture foreground occlusion separately. Floor bowls align the muzzle, not the pet's feet. Blink PNGs match idle bodies: for sit/head-bob, transfer only the two-pixel eye delta, translated with the head; do not substitute the idle body.",""]
    if pet in variants:lines += ["Coat variants: "+", ".join(f"`variants/{name}.png`" for name in variants[pet])+". Their sibling manifests and `variants/recolor.report.json` come from **sprite-gen recolor**, with exact RGB swaps and unchanged alpha; no extra generation.",""]
    weak={"shiba":"The tiny gait is a paw/tail cycle, with no full shoulder articulation.",
          "tuxedo-cat":"Black shading is intentionally restrained; the sleep rump is simpler than the generated standing body.",
          "orange-tabby":"The generated up-facing head needed a programmatic rear-view correction. Fine tabby stripes become chunky at native scale.",
          "corgi":"Short-leg steps are especially subtle. The reposed sleeping rump loses some white chest markings.",
          "hamster-ball":"Rim markers suggest rolling; the ball is not physically rotated and the hamster stays upright. Rest/sleep are inside the ball, with no exit-ball animation.",
          "robot-buddy":"Wheel pixels suggest rolling; jumps are a stylized bounce. eat_drink means recharge/status feedback, not food.",
          "parrot":"The perch is baked in and fixed. There are no walking, climbing or flight clips; head bob and wing flap replace movement. eat_drink is a beak peck; a perch bowl is not supplied.",
          "capybara":"Very calm paw shifts replace an energetic jump. The orange stays attached to the head; sleep/sit bodies use simpler shading."}
    lines += ["Limitation: "+weak[pet],"","Generation: provider codex, still-image component-row direction seeds; all motion is deterministic pixel assembly. No video, runtime integration, application build or commit. `../qa/` contains motion GIFs and static 4× contacts. Static pixel checks establish consistency; they do not certify natural live playback.",""]
    (ROOT/pet/"README.md").write_text("\n".join(lines),encoding="utf-8")

def contacts(allrows):
    ids=list(PETS)
    for page in range(4):
        pair=ids[page*2:page*2+2];h=max(len(allrows[p]) for p in pair)*40+16
        im=Image.new("RGBA",(256,h),(231,209,172,255));d=ImageDraw.Draw(im)
        for col,pet in enumerate(pair):
            d.text((col*128+2,1),pet,fill=color("outline"))
            for y,(state,(frames,_,_)) in enumerate(allrows[pet].items()):
                d.text((col*128+1,16+y*40),state,fill=color("outline"))
                for i,f in enumerate(frames):im.alpha_composite(f,(col*128+i*32,24+y*40))
        im.resize((1024,h*4),Image.Resampling.NEAREST).save(ROOT/f"qa/pets-{page+1}-4x.png")

def preview(allrows):
    floor=existing("floor-wood-oak");w=384;h=24+48*len(PETS)
    im=Image.new("RGBA",(w,h))
    for y in range(0,h,32):
        for x in range(0,w,32):im.alpha_composite(floor,(x,y))
    d=ImageDraw.Draw(im);d.rectangle((0,0,w,17),fill=(231,209,172,255))
    d.text((3,4),"PET",fill=color("outline"));d.text((112,4),"IDLE: D L R U",fill=color("outline"))
    d.text((248,4),"MOVE RIGHT: 0 1 2 3",fill=color("outline"))
    for y,(pet,rows) in enumerate(allrows.items()):
        yy=24+y*48;d.text((4,yy+15),pet,fill=color("cream"),stroke_width=1,stroke_fill=color("wood"))
        for i,f in enumerate(rows["idle"][0]):im.alpha_composite(f,(112+i*32,yy))
        state="head_bob" if pet=="parrot" else "roll_right" if pet in ("hamster-ball","robot-buddy") else "walk_right"
        for i,f in enumerate(rows[state][0]):im.alpha_composite(f,(248+i*32,yy))
    im.resize((w*3,h*3),Image.Resampling.NEAREST).save(ROOT/"preview-pets.png")

def build():
    (ROOT/"qa").mkdir(exist_ok=True)
    allrows={};manifests={};variants=recolor_specs()
    for pet in PETS:
        allrows[pet],manifests[pet]=build_pet(pet);document_pet(pet,manifests[pet],variants)
    build_props();contacts(allrows);preview(allrows)
    print("Built eight pet sheets, props, README files and contacts. Recolor bakes run separately.")

def finish_variants():
    # Sprite-gen owns recoloring. Auxiliary files are exact crops of its result.
    summary=[]
    for pet in ("shiba","corgi","tuxedo-cat","orange-tabby"):
        base=json.loads((ROOT/pet/f"{pet}.manifest.json").read_text(encoding="utf-8"));out=ROOT/pet/"variants"
        spec=json.loads((ROOT/pet/"recolor.spec.json").read_text(encoding="utf-8"))
        base["coatVariants"]=[]
        for var in spec["variants"]:
            name=var["name"];mp=out/f"{name}.manifest.json"
            m=json.loads(mp.read_text(encoding="utf-8"));atlas=Image.open(out/f"{name}.png").convert("RGBA")
            m["characterId"]=f"{pet}-{name}";m["variantOf"]=pet;m["coatVariant"]=name
            m["sprite_sheet_alpha_report"]="../../qa/validation.json";m["base_image"]="../../runs/seeds/sprite-sheet-alpha.png"
            m["provenance"]["recolorReport"]="recolor.report.json"
            for i,facing in enumerate(FACINGS):
                b=m["frame_layout"]["rows"]["blink"][i];f=atlas.crop((b["x"],b["y"],b["x"]+32,b["y"]+32))
                filename=f"{name}-blink-{facing}.png";f.save(out/filename);m["blink"][facing]["file"]=filename
                b=m["frame_layout"]["rows"]["idle"][i];f=np.array(atlas.crop((b["x"],b["y"],b["x"]+32,b["y"]+32)))
                head=np.array(Image.open(ROOT/pet/base["pet"]["headLock"]["heads"][facing]["file"]).convert("RGBA"))
                f[head[:,:,3]==0]=0;folder=out/f"{name}-heads";folder.mkdir(exist_ok=True)
                Image.fromarray(f).save(folder/f"{facing}.png");m["pet"]["headLock"]["heads"][facing]["file"]=f"{name}-heads/{facing}.png"
            mp.write_text(json.dumps(m,indent=2)+"\n",encoding="utf-8")
            base["coatVariants"].append({"name":name,"manifest":f"variants/{name}.manifest.json"});summary.append((pet,name))
        jwrite(f"{pet}/{pet}.manifest.json",base)
    page=Image.new("RGBA",(288,4*48),(231,209,172,255));d=ImageDraw.Draw(page)
    for y,pet in enumerate(("shiba","corgi","tuxedo-cat","orange-tabby")):
        d.text((1,y*48+15),pet,fill=color("outline"));names=["base"]+[n for p,n in summary if p==pet]
        for i,name in enumerate(names):
            path=ROOT/pet/(f"{pet}.png" if name=="base" else f"variants/{name}.png")
            im=Image.open(path).convert("RGBA");page.alpha_composite(im.crop((0,0,32,32)),(96+i*64,y*48))
            d.text((96+i*64,y*48+34),name,fill=color("outline"))
    page.resize((1152,768),Image.Resampling.NEAREST).save(ROOT/"qa/coat-variants-4x.png")
    print("Published eight sprite-gen coat variants with matching head/blink crops.")

def refresh_previews():
    allrows={}
    for pet in PETS:
        m=json.loads((ROOT/pet/f"{pet}.manifest.json").read_text(encoding="utf-8"))
        im=Image.open(ROOT/pet/m["game_input"]).convert("RGBA");rows={}
        for state,r in m["animation"]["rows"].items():
            frames=[im.crop((b["x"],b["y"],b["x"]+32,b["y"]+32)) for b in m["frame_layout"]["rows"][state]]
            rows[state]=(frames,r["fps"],r["loop"])
        allrows[pet]=rows;animation_gif(pet,rows)
    preview(allrows)
    chosen=("hamster-ball","robot-buddy","parrot","capybara")
    im=Image.new("RGBA",(320,4*48),(231,209,172,255));d=ImageDraw.Draw(im)
    for y,pet in enumerate(chosen):
        rows=allrows[pet];d.text((1,y*48+16),pet,fill=color("outline"))
        state="head_bob" if pet=="parrot" else "roll_right" if pet in ("hamster-ball","robot-buddy") else "walk_right"
        frames=[rows["idle"][0][0],*rows[state][0][:3],*rows["sleep"][0][:2],rows["blink"][0][0]]
        for i,f in enumerate(frames):im.alpha_composite(f,(96+i*32,y*48))
    im.resize((1280,768),Image.Resampling.NEAREST).save(ROOT/"qa/final-details-4x.png")
    print("Refreshed preview from delivered sheets; GIF frame durations follow manifest FPS.")

def check():
    results=[];total=0;facelocks=0;actionlocks=0
    for mp in sorted(ROOT.glob("*/*.manifest.json"))+sorted(ROOT.glob("*/variants/*.manifest.json")):
        m=json.loads(mp.read_text(encoding="utf-8"));im=Image.open(mp.parent/m["game_input"]).convert("RGBA");a=np.array(im)
        cell=m["cell"]["width"];rows=m["animation"]["rows"];layout=m["frame_layout"]
        assert im.size==(layout["sheetWidth"],layout["sheetHeight"]),mp
        assert set(np.unique(a[:,:,3])).issubset({0,255}),mp
        assert not np.any(a[a[:,:,3]==0]),mp
        count=0
        for state,r in rows.items():
            refs=layout["rows"][state];assert len(refs)==r["frames"],(mp,state)
            frames=[]
            for i,b in enumerate(refs):
                assert b=={"x":i*cell,"y":r["row"]*cell,"w":cell,"h":cell},(mp,state,i)
                assert b["x"]+cell<=im.width and b["y"]+cell<=im.height
                f=np.array(im.crop((b["x"],b["y"],b["x"]+cell,b["y"]+cell)));assert np.any(f[:,:,3]),(mp,state,i)
                assert not np.any((f[:,:,0]>200)&(f[:,:,2]>200)&(f[:,:,1]<60)&(f[:,:,3]>0)),(mp,state,i)
                yy,xx=np.nonzero(f[:,:,3]);assert xx.min()>=1 and xx.max()<cell-1 and yy.min()>=1 and yy.max()<=cell-3,(mp,state,i,"margin")
                frames.append(f);count+=1
            assert not np.any(a[r["row"]*cell:(r["row"]+1)*cell,r["frames"]*cell:]),(mp,state,"trailing")
            if r["loop"] and len(frames)>1:assert len({f.tobytes() for f in frames})>1,(mp,state,"static loop")
        if m["characterId"] in PETS and mp.parent.name!="variants":
            pet=m["characterId"];hl=m["pet"]["headLock"]
            for facing,hinfo in hl["heads"].items():
                head=np.array(Image.open(mp.parent/hinfo["file"]).convert("RGBA"));mask=head[:,:,3]>0
                state=m["pet"]["locomotion"]+"_"+facing
                if state in layout["rows"]:
                    for b in layout["rows"][state]:
                        f=np.array(im.crop((b["x"],b["y"],b["x"]+cell,b["y"]+cell)))
                        assert np.array_equal(f[mask],head[mask]),(pet,state,"head changed");facelocks+=1
                i=FACINGS.index(facing);b=layout["rows"]["idle"][i];normal=np.array(im.crop((b["x"],b["y"],b["x"]+cell,b["y"]+cell)))
                blink=np.array(Image.open(mp.parent/m["blink"][facing]["file"]).convert("RGBA"))
                allowed=np.zeros((cell,cell),bool)
                for x,y in hinfo["eyes"]:allowed[y:y+2,x]=True
                assert np.array_equal(normal[~allowed],blink[~allowed]),(pet,facing,"blink changed body")
                assert np.array_equal(normal[:,:,3],blink[:,:,3]),(pet,facing,"blink changed alpha")
                if facing!="up":assert np.any(normal!=blink),(pet,facing,"blink missing")
            def verify_head(state,facing,offsets=None,asleep=False):
                nonlocal actionlocks
                hinfo=hl["heads"][facing];head=Image.open(mp.parent/hinfo["file"]).convert("RGBA")
                if asleep:head=closure(head,[tuple(p) for p in hinfo["eyes"]])
                for i,b in enumerate(layout["rows"][state]):
                    dx,dy=offsets[i] if offsets else (0,0);expected=np.array(moved(head,dx,dy));mask=expected[:,:,3]>0
                    f=np.array(im.crop((b["x"],b["y"],b["x"]+32,b["y"]+32)))
                    assert np.array_equal(f[mask],expected[mask]),(pet,state,i,"action head changed");actionlocks+=1
            verify_head("sit","down")
            verify_head("eat_drink","right")
            offsets=[(0,0)]*len(layout["rows"]["play"]) if pet in ("capybara","hamster-ball","parrot") else [(0,y) for y in (0,-2,-4,0)]
            verify_head("play","right",offsets)
            head=Image.open(mp.parent/hl["heads"]["right"]["file"]).convert("RGBA")
            offset=(25-head.getbbox()[2],27-head.getbbox()[3]) if pet not in ("hamster-ball","robot-buddy","parrot") else (0,0)
            verify_head("sleep","right",[offset]*3,asleep=True)
            if pet=="parrot":
                mask=np.zeros((32,32),bool);mask[20:22,7:25]=True;mask[21:28,15:17]=True;mask[28,11:22]=True
                perch=a[:32,:32].copy()
                for refs in layout["rows"].values():
                    for b in refs:assert np.array_equal(a[b["y"]:b["y"]+32,b["x"]:b["x"]+32][mask],perch[mask]),(pet,"perch moved")
                verify_head("head_bob","right",[(0,-(p%2)) for p in range(4)]);verify_head("wing_flap","right")
        total+=count;results.append({"manifest":str(mp.relative_to(ROOT)).replace("\\","/"),"frames":count,"rgbaSha256":hashlib.sha256(a.tobytes()).hexdigest()})
    for pet in ("shiba","corgi","tuxedo-cat","orange-tabby"):
        out=ROOT/pet/"variants"
        if not out.exists():continue
        base=np.array(Image.open(ROOT/pet/f"{pet}.png").convert("RGBA"))
        report=json.loads((out/"recolor.report.json").read_text(encoding="utf-8"))
        spec=json.loads((ROOT/pet/"recolor.spec.json").read_text(encoding="utf-8"))
        coatmask=np.zeros(base.shape[:2],bool)
        for rgb in COATS[pet]:coatmask|=np.all(base[:,:,:3]==rgb,axis=2)&(base[:,:,3]>0)
        for entry in spec["variants"]:
            vp=out/f"{entry['name']}.png"
            variant=np.array(Image.open(vp).convert("RGBA"));assert np.array_equal(base[:,:,3],variant[:,:,3]),vp
            assert np.any(base!=variant),vp
            assert np.array_equal(base[~coatmask],variant[~coatmask]),(vp,"non-coat pixel changed")
    report={"ok":True,"sheets":len(results),"declaredFrames":total,"walkingHeadComparisons":facelocks,"actionHeadComparisons":actionlocks,"checks":["declared nonempty/in-bounds frames","binary alpha and zero hidden RGB","transparent trailing cells","safe margins and no magenta","nonstatic loops","accepted heads reused in locomotion and actions","eye-only blink and preserved alpha","fixed parrot perch","recolor alpha unchanged"],"files":results,
            "limits":"Static pixel checks, not natural-motion scoring. No runtime integration or live playback certification."}
    jwrite("qa/validation.json",report);print(json.dumps({k:report[k] for k in ("ok","sheets","declaredFrames","walkingHeadComparisons")}))

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["prepare","prepare-props","build","check","finish-variants","refresh-previews","contact-a","contact-b"])
    args = parser.parse_args()
    if args.command=="prepare":prepare()
    elif args.command=="prepare-props":prepare_props()
    elif args.command=="build":build()
    elif args.command=="check":check()
    elif args.command=="finish-variants":finish_variants()
    elif args.command=="refresh-previews":refresh_previews()
    else:seeds_contact(list(PETS)[:3] if args.command=="contact-a" else list(PETS)[3:],f"qa/seeds-{args.command[-1]}-4x.png")
