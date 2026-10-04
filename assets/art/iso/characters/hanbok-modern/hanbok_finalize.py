"""Final logging and serialized export checks; no shared writes."""
from hanbok_review_verify import *
def final_board():
    doc=read(OUT/(CID+'.iso.json'));s=doc['sheets'][0];a=load(OUT/s['file']);bl=load(OUT/doc['sheets'][1]['file'])
    board=Image.new('RGBA',(1800,3020),BG);draw=ImageDraw.Draw(board)
    board.alpha_composite(load(OUT/'iso-vs-flat.png'),(0,0))
    for n,r in enumerate(CHECK.BLINK_ROWS):
        x=(n%3)*600;y=750+(n//3)*250;c=s['rows'][r]
        draw.text((x+6,y+4),'blink %02d normal %02d %s / %s'%(n,r,c['name'],c['direction']),fill=O)
        for p in range(4):
            im=cell(bl,n,p);board.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+6+p*96,y+30));board.alpha_composite(im,(x+420+p*32,y+50))
    for k,r in enumerate((8,9)):
        x=k*600;y=2770;draw.text((x+5,y+4),'FINAL typing '+s['rows'][r]['direction'],fill=O)
        for p in range(4):
            im=cell(a,r,p);board.alpha_composite(im.resize((96,192),Image.Resampling.NEAREST),(x+6+p*96,y+30));board.alpha_composite(im,(x+420+p*32,y+50))
    board.save(RUN/'final-identity-blink-review.png')
def checks():
    doc=read(OUT/(CID+'.iso.json'));s=doc['sheets'][0];a=load(OUT/s['file'])
    assert (OUT/(CID+'.iso.json')).read_bytes()==(OUT/(CID+'-iso.manifest.json')).read_bytes()
    proofs={}
    for k,f in enumerate(F):
        path=OUT/('walk-'+f+'-3x.gif');im=Image.open(path);count=im.n_frames;dur=[];equal=[]
        assert count==4 and im.size==(96,192) and im.info.get('loop')==0
        for p in range(4):
            im.seek(p);dur.append(im.info['duration'])
            expected=Image.new('RGBA',(32,64),BG);expected.alpha_composite(cell(a,4+k,p));expected=expected.resize((96,192),Image.Resampling.NEAREST).convert('RGB')
            equal.append(np.array_equal(np.array(im.convert('RGB')),np.array(expected)))
        assert all(equal) and dur==[120,130,120,130]
        proofs[f]=dict(frames=count,size=[96,192],durationsMs=dur,loopMs=sum(dur),nativeNearestPixelsExact=equal)
    savejson(RUN/'walk-gif-proof.json',proofs)
    vis=read(OUT/'seated-visibility.json')
    for path,digest in vis['inputSHA256'].items():assert sha(ROOT/path)==digest,('v3 source changed',path)
    assert all(r['allPhaseHeadGate'] and r['allPhaseHands100'] for r in vis['summaries'])
    cmd=[sys.executable,'-B',str(ROOT/'tools/iso_check.py'),str(OUT),'--stage','final','--tolerance','0']
    r=subprocess.run(cmd,capture_output=True,encoding='utf-8',env={**os.environ,'PYTHONIOENCODING':'utf-8','PYTHONDONTWRITEBYTECODE':'1'})
    (OUT/'validation.txt').write_text(r.stdout+r.stderr,encoding='utf-8')
    result=dict(command=cmd,exitCode=r.returncode,PASS=r.stdout.count('| PASS |'),SKIP=r.stdout.count('| SKIP |'),FAIL=r.stdout.count('| FAIL |'),normalRows=38,blinkRows=24,normalCells=152,blinkCells=96,gifSerializedProof='runs/walk-gif-proof.json',visibility='seated-visibility.json')
    savejson(OUT/'validation-summary.json',result)
    assert r.returncode==0,r.stdout+r.stderr
    print('Final checker:',result['PASS'],'PASS /',result['SKIP'],'SKIP /',result['FAIL'],'FAIL')
    print('Four exported GIFs exactly match native x3 frames, 120/130/120/130ms, 500ms loop.')
if __name__=='__main__':
    for arg in sys.argv[1:]:globals()[arg]()

