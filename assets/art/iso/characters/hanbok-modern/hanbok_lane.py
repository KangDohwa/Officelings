"""Hanbok-modern lane: all writes are confined to this directory."""
from pathlib import Path
import os, sys, json, hashlib, importlib.util, math
from datetime import datetime, timezone
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.dont_write_bytecode = True
OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[4]
RUN = OUT / 'runs'
RUN.mkdir(exist_ok=True)
PY = sys.executable
CID = 'hanbok-modern'
FLAT = ROOT / 'assets/art/roster/hanbok-modern'
F = ('se','sw','ne','nw')
BG = '#E7D1AC'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def savejson(p,d): p.write_text(json.dumps(d,ensure_ascii=False,indent=2,default=lambda o:o.item())+'\n',encoding='utf-8')
def load(p): return Image.open(p).convert('RGBA')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def blank(): return Image.new('RGBA',(32,64))
def shift(im,x=0,y=0):
    dst=Image.new('RGBA',im.size);dst.alpha_composite(im,(x,y));return dst
def module(name,path):
    s=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
CHECK = module('hanbok_iso_check_readonly',ROOT/'tools/iso_check.py')
def refs():
    board=Image.new('RGBA',(1160,660),BG);d=ImageDraw.Draw(board)
    sheet=load(FLAT/'hanbok-modern.png')
    d.text((12,8),'LOCKED FLAT IDENTITY: coral jeogori, tie, half-up knot, gold pin',fill='#191E1E')
    board.alpha_composite(sheet.crop((0,0,128,64)).resize((512,256),Image.Resampling.NEAREST),(12,30))
    base=load(FLAT/'base.png');base.thumbnail((360,300));board.alpha_composite(base,(600,25))
    hero=load(ROOT/'assets/art/iso-concept/iso-office-hero.png')
    hero=hero.crop((685,350,990,610));hero.thumbnail((420,320));board.alpha_composite(hero,(710,330))
    for i,(folder,name) in enumerate([('dev-hoodie-v2','dev-hoodie'),('punk-girl','punk-girl'),('professor','professor')]):
        a=load(ROOT/'assets/art/iso/characters'/folder/(name+'-iso.png'))
        d.text((12+i*225,318),folder+' 1x / 3x',fill='#191E1E')
        strips=Image.new('RGBA',(128,128))
        for j in range(4):
            strips.alpha_composite(a.crop((0,j*64,32,(j+1)*64)),(j*32,0))
            strips.alpha_composite(a.crop((0,(j+8)*64,32,(j+9)*64)),(j*32,64))
        board.alpha_composite(strips,(12+i*225,340))
        board.alpha_composite(strips.resize((192,192),Image.Resampling.NEAREST),(12+i*225,465))
    board.save(RUN/'references.png')
    colors=np.unique(np.array(sheet)[:,:,:3][np.array(sheet)[:,:,3]>0],axis=0)
    savejson(RUN/'flat-palette.json',['#'+bytes(c).hex().upper() for c in colors])
    print('Flat palette:',read(RUN/'flat-palette.json'))
    print('Reference:',RUN/'references.png')
if __name__=='__main__':
    globals()[sys.argv[1]]()
