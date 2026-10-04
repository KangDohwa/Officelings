"""Inspect real WPF exports: native texel ownership, 4x boards and strips."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw


def review(directory,pack_directory,seated_only=False):
    records=json.loads((directory/"queue.json").read_text(encoding="utf-8"));images={};tested=0
    for record in records:
        scene=record["scene"];w,h=scene["Width"],scene["Height"]
        expected=np.zeros((h,w,4),np.uint8)
        seated=np.zeros((h,w),bool)
        frames={f["InstanceId"]:f for f in record["frames"]}
        for item in scene["Items"]:
            f=frames[item["InstanceId"]];path=pack_directory/f["file"]
            if path not in images:images[path]=Image.open(path).convert("RGBA")
            r=f["frame"];cell=images[path].crop((r["X"],r["Y"],r["X"]+r["Width"],r["Y"]+r["Height"]))
            x,y=item["X"],item["Y"];left=max(0,x);top=max(0,y);right=min(w,x+cell.width);bottom=min(h,y+cell.height)
            if right<=left or bottom<=top:continue
            pixels=np.array(cell.crop((left-x,top-y,right-x,bottom-y)))
            mask=pixels[:,:,3]==255
            expected[top:bottom,left:right][mask]=pixels[mask]
            seated[top:bottom,left:right][mask]=(item['InstanceId'] in ('actor','actor/body') or
                item['InstanceId'].startswith('chair/') or
                item['InstanceId'].startswith('desk/') and item['InstanceId'].endswith('__17'))
        actual=np.array(Image.open(directory/(record["name"]+".png")).convert("RGBA"))
        # Both ownership and every 4x nearest-neighbor block must match the queue.
        scaled=np.repeat(np.repeat(expected,4,axis=0),4,axis=1);mask=scaled[:,:,3]==255
        if seated_only:
            mask &= np.repeat(np.repeat(seated,4,axis=0),4,axis=1)
        if not np.array_equal(actual[mask],scaled[mask]):
            differences=np.argwhere(mask & np.any(actual!=scaled,axis=2))
            raise AssertionError(f"{record['name']}: WPF/queue pixel mismatch {differences[:5].tolist()}")
        tested+=int(((expected[:,:,3]==255)&(seated if seated_only else True)).sum())
    for facing in ("nw","ne","sw","se"):
        for action in ("typing","sit-down","stand-up"):
            subset=[r for r in records if f"-{facing}-{action}-" in r["name"]]
            names=sorted({r["name"].split("-"+facing)[0] for r in subset})
            board=Image.new("RGBA",(384*4,408*len(names)),"#E7D1AC");draw=ImageDraw.Draw(board)
            for row,name in enumerate(names):
                draw.text((8,row*408+4),f"{name} / {facing} / {action}",fill="#191E1E")
                for frame in range(4):
                    image=Image.open(directory/f"{name}-{facing}-{action}-{frame}-4x.png")
                    board.paste(image,(frame*384,row*408+24))
            board.save(directory/f"{facing}-{action}-board-4x.png")
    for action in ("sit-down","stand-up"):
        # Four orientations, first pilot, all transition frames, at 4x.
        name="dev-hoodie-v2";strip=Image.new("RGBA",(1536,4*408),"#E7D1AC");draw=ImageDraw.Draw(strip)
        for row,facing in enumerate(("nw","ne","sw","se")):
            draw.text((8,row*408+4),f"{facing} / {action}",fill="#191E1E")
            for frame in range(4):strip.paste(Image.open(directory/f"{name}-{facing}-{action}-{frame}-4x.png"),(frame*384,row*408+24))
        strip.save(directory/f"{action}-strip-4x.png")
    print(f"WPF/queue{' seated owners' if seated_only else ''}: {len(records)} crops, {tested} native opaque texels match; 12 boards and 2 strips written")


if __name__=="__main__":
    p=argparse.ArgumentParser(description=__doc__);p.add_argument("directory",type=Path);p.add_argument("pack",type=Path)
    p.add_argument('--seated-only',action='store_true',help='compare final opaque chair/body pixels; retain the full desk queue for occlusion')
    a=p.parse_args();review(a.directory,a.pack,a.seated_only)
