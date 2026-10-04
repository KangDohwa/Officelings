"""Read-only rev1 weakness measurements on supplied native production rasters.

Run with Python -B; all output is confined to this file's directory.
Each perimeter sample is a 4-connected opaque/transparent pixel-pair. Linear
sRGB relative luminance ratio is (max(Y)+.05)/(min(Y)+.05); low contrast <3.
Actual actor samples must match source RGBA exactly and survive the complete
v3 desk/chair queue. Additional all-five probes are explicitly counterfactual
composites at real seat/walking coordinates over iso-empty-1x.png.
"""
from pathlib import Path
import hashlib
import importlib.util
import json
import os
import subprocess
import sys

sys.dont_write_bytecode = True
import numpy as np
from PIL import Image

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[4]
ART = ROOT / 'assets/art/iso'
SCENES = Path('C:/Codex-Workspace/vd-lanes/packv3/artifacts/iso-packv3-final')
IDS = ('dev-hoodie-v2', 'punk-girl', 'professor', 'glasses-bun', 'hanbok-modern')
THRESHOLD = 3.0


def read(p):
    return json.loads(p.read_text(encoding='utf-8'))


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def luminance(rgb):
    c = np.asarray(rgb, dtype=float)[..., :3] / 255
    linear = np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)
    return np.dot(linear, [.2126, .7152, .0722])


def ratio(a, b):
    a, b = luminance(a), luminance(b)
    return (np.maximum(a, b) + .05) / (np.minimum(a, b) + .05)


def summary(rows, field='ratio'):
    values = np.array([r[field] for r in rows], float)
    return dict(edgePairs=len(rows), atLeast3Percent=round(float((values >= 3).mean()*100), 3),
                lowContrastPercent=round(float((values < 3).mean()*100), 3),
                medianRatio=round(float(np.median(values)), 4)) if len(values) else dict(edgePairs=0)


def perimeter(src, bg, pos, owned, tag, alternative=None, head=None, require_source=False):
    mask = src[:, :, 3] > 0
    ax, ay = pos
    result = []
    for y, x in zip(*np.where(mask & owned)):
        if require_source and not np.array_equal(bg[ay+y, ax+x], src[y, x]):
            continue
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            nx, ny = int(x+dx), int(y+dy)
            if 0 <= nx < 32 and 0 <= ny < 64 and mask[ny, nx]:
                continue
            gx, gy = ax+nx, ay+ny
            if not (0 <= gx < bg.shape[1] and 0 <= gy < bg.shape[0]):
                continue
            background = bg[gy, gx, :3]
            record = dict(**tag, x=int(x), y=int(y), outside=[nx, ny],
                          region='head' if head is not None and head[y, x] else 'body',
                          sourceRGB=src[y, x, :3].tolist(), backgroundRGB=background.tolist(),
                          ratio=float(ratio(src[y, x, :3], background)),
                          backgroundY=float(luminance(background)))
            if alternative is not None:
                record['rev1Ratio'] = float(ratio(alternative[y, x, :3], background))
            result.append(record)
    return result


def main():
    op = load('outline_rev1_readonly', OUT.parent/'outline_pass.py')
    verifier = load('outline_v3_readonly', ART/'furniture-v3/workstations/verify-desk-rig-v3-lane.py')
    m = verifier.m
    desk, ds = verifier.furniture(ART/'furniture-v3/workstations', 'desk')
    chair, cs = verifier.furniture(ART/'furniture-v3/chair', 'chair')
    items, seats = op.room_items(m, desk, ds, chair, cs)
    seated_png = op.rgba(SCENES/'iso-twelve-working-1x.png')
    walking_png = op.rgba(SCENES/'iso-arrival-02.0s-1x.png')
    actual = []
    manifests = {}
    atlases = {}
    for ident in IDS:
        folder = ART/'characters'/ident
        cid = 'dev-hoodie' if ident == 'dev-hoodie-v2' else ident
        sheet = next(s for s in read(folder/(cid+'.iso.json'))['sheets'] if s['kind']=='character')
        manifests[ident] = sheet
        current = op.rgba(OUT/'runs/outline-rev1-backup'/ident/sheet['file'])
        original = OUT.parent/'evidence'/ident/'before'/sheet['file']
        atlases[ident] = (op.rgba(original) if original.exists() else current.copy(), current)
    seated = read(OUT.parent/'measurements.json')['integrated']
    walking = read(OUT.parent/'walking-integrated-measurements.json')['cases']
    for state, records, image in [('typing', seated, seated_png), ('walk', walking, walking_png)]:
        for record in records:
            ident = record['character']; sheet = manifests[ident]
            action = 'seated-typing-front' if state == 'typing' else 'walk'
            facing = record['facing']; phase = record['phase']; pos = tuple(record['position'])
            clip = next(c for c in sheet['rows'] if c['name']==action and c['direction']==facing)
            old, current = [op.cell(a, clip['row'], phase) for a in atlases[ident]]
            key = seats[pos]['key'] if state == 'typing' else (record['groundDepth'],0,'walker',ident)
            owned = op.owned_in_room(items, old, pos, key)
            hm = op.shifted(op.heads(ART/'characters'/ident, sheet, op.PROFILES[ident])[facing],
                            **dict(dx=sheet['headOffsetsByClip'][action+'/'+facing][phase]['x'],
                                   dy=sheet['headOffsetsByClip'][action+'/'+facing][phase]['y']))
            tag = dict(character=ident, state=state, facing=facing, phase=phase,
                       actorPosition=list(pos), source='actual-render-exact-source')
            actual.extend(perimeter(old, image, pos, owned, tag, current, hm, True))
    report = dict(metric='linear-sRGB luminance ratio on exposed 4-connected perimeter pairs',
                  lowContrastBelow=THRESHOLD, actualMatchedActorPositions=len(seated)+len(walking),
                  actualOriginal=summary(actual), actualRev1=summary(actual, 'rev1Ratio'),
                  perCharacter={}, perRegion={}, sources=[str(SCENES/p) for p in
                  ['iso-twelve-working-1x.png','iso-arrival-02.0s-1x.png']])
    for ident in op.PROFILES:
        group = [r for r in actual if r['character']==ident]
        report['perCharacter'][ident] = dict(original=summary(group), rev1=summary(group,'rev1Ratio'))
    for region in ('head', 'body'):
        group = [r for r in actual if r['region']==region]
        report['perRegion'][region] = dict(original=summary(group),rev1=summary(group,'rev1Ratio'))
    report['backgroundBuckets']={}
    for label, keep in [('dark-background', lambda r:r['backgroundY']<.1),
                        ('light-background', lambda r:r['backgroundY']>=.25),
                        ('dark-on-dark',lambda r:r['backgroundY']<.1 and float(luminance(r['sourceRGB']))<.1),
                        ('light-on-light',lambda r:r['backgroundY']>=.25 and float(luminance(r['sourceRGB']))>=.25)]:
        group=[r for r in actual if keep(r)]
        report['backgroundBuckets'][label]=dict(original=summary(group),rev1=summary(group,'rev1Ratio'))
    (OUT/'baseline-per-edge.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in actual),encoding='utf-8')
    (OUT/'baseline-contrast.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    checks=[]
    for ident in IDS:
        folder=ART/'characters'/ident
        command=[sys.executable,'-B',str(ROOT/'tools/iso_check.py'),str(folder),'--stage','final','--tolerance','0']
        result=subprocess.run(command,capture_output=True,encoding='utf-8',env={**os.environ,'PYTHONIOENCODING':'utf-8'})
        (OUT/(ident+'-baseline-check.txt')).write_text(result.stdout+result.stderr,encoding='utf-8')
        checks.append(dict(character=ident,command=command,exitCode=result.returncode,
                           PASS=result.stdout.count('| PASS |'),FAIL=result.stdout.count('| FAIL |'),
                           SKIP=result.stdout.count('| SKIP |')))
    (OUT/'baseline-checks.json').write_text(json.dumps(checks,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2)); print(json.dumps(checks,indent=2))


if __name__ == '__main__':
    main()
