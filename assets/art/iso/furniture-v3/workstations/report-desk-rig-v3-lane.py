"""Write readable final lane evidence and exact current-S0 proposal diff, locally."""
from pathlib import Path
import difflib, hashlib, importlib.util, json, os, subprocess, sys
import numpy as np
from PIL import Image
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent; ROOT=OUT.parents[4]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    # Capture UTF-8 directly: Windows PowerShell's byte decoding mangles × otherwise.
    cmd=[sys.executable,'-B',str(ROOT/'tools/iso_check.py'),str(OUT),'--tolerance','0']
    r=subprocess.run(cmd,capture_output=True,encoding='utf-8',env={**os.environ,'PYTHONIOENCODING':'utf-8'})
    (OUT/'validation-iso-check-desk-rig-v3.txt').write_text(r.stdout+r.stderr,encoding='utf-8')
    assert r.returncode==0,r.stdout+r.stderr
    passn=r.stdout.count('| PASS |');skipn=r.stdout.count('| SKIP |');failn=r.stdout.count('| FAIL |')
    proof=json.loads((OUT/'geometry-proof-desk-rig-v3.json').read_text());measure=json.loads((OUT/'measurements-desk-rig-v3.json').read_text())
    for e in proof['edges']:assert abs(e['dx'])<1e-8 or abs(abs(e['dx'])-2*abs(e['dy']))<1e-8
    spec=importlib.util.spec_from_file_location('ramp_check_v3',ROOT/'tools/iso_check.py');c=importlib.util.module_from_spec(spec);spec.loader.exec_module(c)
    pal={tuple(c.rgb(x)) for s in c.RAMPS.values() for x in s.split()}; colors={}
    for name in ['desk-parts.png','desk-screen.png']:
        a=np.array(Image.open(OUT/name).convert('RGBA'));cs={tuple(x) for x in np.unique(a[:,:,:3][a[:,:,3]>0],axis=0)}
        assert cs<=pal and set(np.unique(a[:,:,3]))<={0,255}
        assert np.all(a[a[:,:,3]==0]==0)
        colors[name]=len(cs)
    s0path=ROOT/'docs/iso-slice-s0.md';s0=s0path.read_text(encoding='utf-8')
    new=s0.replace('Desktop 18, cushion 12. Monitor top 36; chair back top 21.','Desktop 18, cushion 12. Monitor top 36 (v2), 40 (desk rig v3); chair back top 21.')
    target='Keep A/C/H, keyboard contact pairs, source anchors, desk height 18 and monitor top 36.'
    replacement='Keep A/C/H, keyboard contact pairs, source anchors and desk height 18. Monitor top remains 36 for v2; desk rig v3 uses top 40 to fit its 18px-high 16:9 flat panel above a slim neck and 1px-high base.'
    assert target in new and new!=s0
    new=new.replace(target,replacement)
    addition='\nDesk rig v3 replaces the CRT with a flat monitor: canonical ground center (-7/32,0), equivalent to A+39/32 f, panel footprint i=[-1/4,-3/16], j=[-1/2,1/2], panel z22..40, base footprint i=[-3/8,-1/16], j=[-3/16,3/16], base z18..19. Its ordinary depthAnchor values follow the actual base, neck, panel and screen contacts; monitor rank 0 and overlay rank 1 stay fixed. The v2 contact fixture below remains unchanged.\n'
    new=new.replace('\n## Exact contact reference\n',addition+'\n## Exact contact reference\n',1)
    diff=''.join(difflib.unified_diff(s0.splitlines(keepends=True),new.splitlines(keepends=True),fromfile='docs/iso-slice-s0.md',tofile='docs/iso-slice-s0.md (desk-rig-v3 proposal)',n=2))
    rows=[]
    for o in proof['objects']:
        gd=o['center'];rows.append(f"| {o['orientation'].upper()} | ({gd['i']},{gd['j']}) | {o['screenPixels']}+{o['ledPixels']} | {o['keyboard']['hiddenByPanelPixels']} | {o['keyboard']['mugPixelsHiddenByPanel']} |")
    text=f'''# DESK RIG v3 변경·검증

**S0 예외: 이 작업대의 모니터 상단만 36→40px, 중심·발자국·ordinary depthAnchor를 얇은 패널에 맞춰 변경했다. 책상 높이 18, 셀·source anchor·1×2/2×1 책상 footprint·A/C/H·pelvis (16,44)·좌석 레이어 규약과 모든 기존 part ID/아틀라스 위치는 유지했다.** 아래 diff는 제안이며 공유 S0 문서는 수정하지 않았다.

앞면은 현대적인 27인치 모니터 형태다. 16:9 외곽 32×18px 패널, 2px 깊이, 가는 목, 1px 높이의 낮은 받침을 사용했다. CRT와 PC 타워는 없다. 앞면 실제 화면은 468px, 별도 상태 LED는 1px으로 기존 242px 마스크보다 합계 93.8% 넓다. off / working 4phase·3fps / screensaver는 화면·LED 마스크 안에서만 바뀌며 본체 RGBA는 바이트 단위로 동일하다. rear view는 패널 화면 없이 LED만 바뀐다.

키보드는 분리된 키캡, 4개 행의 기능키 힌트와 스페이스바, 1px 케이스 단차를 추가했다. 모든 방향에서 마우스를 앉은 사용자 오른쪽(원래 모델의 negative j)에 놓았다. frozen H 두 점을 키보드 립과 마우스 립에 맞추고 상판/입력 장치를 기존 seat rear(-20)에 유지했다. 물리 전면 fascia(10), monitor ordinary(0), screen ordinary(1)은 그대로다. 마우스를 독립 새 part ID로 추가하지 않고 기존 `desk__rear__<facing>__14`에 포함했다. `inputDevices`에는 각각의 실제 중심·footprint를 기록했다.

서랍·기존 목재 구조는 보존했다. CRT 아래 가려진 상판을 z18에서 복원하고, 머그/종이를 네 방향의 동일한 모델 좌표로 배치했다. 머그 기준 (.5625,.8125), 종이 기준 (.0625,1.0625), 모니터 중심 (-.21875,0)이다. 마스터는 built-in image generation 1회로 만들었으며 [프롬프트](master-prompt-desk-rig-v3.txt), [마스터](master-desk-rig-v3.png)를 남겼다. 생성된 화면 quad를 역투영·최근접 샘플링하고 고정 ramp로 스냅했다. 조형은 정확한 2:1 면으로 재구성했다. 화면 master 재생성은 하지 않았다.

[v2·hero·최근 통합 렌더 비교, 1×/3×](comparison-v2-hero-integrated-desk-rig-v3.png) · [캐릭터 전후 1×](contact-before-after-desk-rig-v3-1x.png) / [3×](contact-before-after-desk-rig-v3-3x.png) · [보행자 1×](walkers-desk-rig-v3-1x.png) / [3×](walkers-desk-rig-v3-3x.png).

## 검증 결과

- `tools/iso_check.py <이 폴더> --tolerance 0`: **PASS {passn} / SKIP {skipn} / FAIL {failn}**, exit0. [실제 UTF-8 출력](validation-iso-check-desk-rig-v3.txt). SKIP은 floor seam, shadow part naming, character blink/face-lock처럼 이 폴더에 해당하지 않는 검사다.
- [측정](measurements-desk-rig-v3.txt) / [원시 JSON](measurements-desk-rig-v3.json): 3 pilot × 4 orientation × 4 phase, **48 typing frame 손 ROI 100%**. front worker(SE/SW) 머리 100%, back worker(NE/NW) 89.6–93.7%. 기존 head gate(85%/90%) 및 모든 phase의 v2 대비 비감소, 기존 ROI 분모를 유지했다. torso·forearm/sleeve의 분자/분모도 모두 공개했다. 현재 character 원본과 고정 snapshot의 drift는 {len(measure['liveActorDrift'])}개다.
- 보행자 **1,536건**(3 pilot × 4 desk × 8 near/far/end 지점 × 4 walk 방향 × 4 phase)은 자기 ground depth를 사용했다. 전체 픽셀 ownership과 나중 레이어 alpha 합집합을 교차 확인했다. 정면 가장 앞에 놓인 walker는 전신 100% 표시를 확인했다.
- 앉기/일어서기 **96 frame**은 head gate·v2 대비 비감소, 바닥 발 접점과 정확한 역순을 통과했다. 키보드·마우스의 frozen contact texel은 두 점 모두 실제 alpha255다.
- 28개 state frame: 본체·화면 mask의 disjoint 재구성, body SHA 동일, empty PNG와 정확한 일치, desktop hole 0, panel/neck/base 1개 연결 성분. [투영 근거](geometry-proof-desk-rig-v3.json)의 **{len(proof['edges'])}개 새 면 edge**가 정확히 2:1 또는 수직이다. 생산 body는 {colors['desk-parts.png']}색, screen은 {colors['desk-screen.png']}색이며 전부 frozen ramps 안에 있다. alpha는 0/255, 투명 RGB는 0이다.
- v2 작업대·의자 입력 해시를 검증 전후 확인했다. 의자는 read-only v2를 합성에 사용했고 이 폴더에 chair 파일을 출력하지 않았다. shared docs/src/tests/tools, 다른 lane, packs, git은 변경하지 않았다. shadow는 기존 책상 바닥 footprint PNG/manifest를 그대로 복사했다.

| desk | monitor ground center | 화면+LED px | 패널 뒤 입력 texel | 패널 뒤 머그 texel |
|---|---|---:|---:|---:|
{chr(10).join(rows)}

## 남은 약점·범위

NW rear view에서는 넓은 패널이 입력 장치 23texel과 머그 2texel을 가린다. 고정 H와 손 ROI는 모두 노출된다. 1× 키캡은 작은 크기 때문에 일부가 체크 무늬처럼 합쳐 보이며, working 화면의 작은 문자는 읽을 수 있는 실제 텍스트가 아니다. 기존 목재의 점 형태 grain이 복원한 상판보다 일부 거칠다. chair v2의 납작한 쿠션/몸통 접촉 문제는 별도 lane 범위다.

정적 PNG·직렬화 queue 검증과 1×/3× 시각 비교만 완료했다. WPF 실제 queue/instance tie·DPI·picking·nav·연속 재생, 새로운 chair v3와의 조합은 검증하지 않았다. pack 업로드·런타임 통합은 수행하지 않았다.

## 정확한 S0 텍스트 diff — 공유 문서 미수정

현재 S0 SHA-256: `{sha(s0path)}`. v2의 CRT contact fixture는 그대로 두고 이 lane의 예외만 추가한다.

```diff
{diff.rstrip()}
```
'''
    (OUT/'CHANGES-DESK-RIG-v3.md').write_text(text,encoding='utf-8')
    result=dict(isoCheck=dict(passCount=passn,skipCount=skipn,failCount=failn,exitCode=r.returncode,command=cmd),strictNewEdges=len(proof['edges']),nativeRampColors=colors,productionSHA256={p.name:sha(p) for p in OUT.glob('desk*') if p.is_file()},scopeOutputOnly=str(OUT),s0ReadOnlySHA256=sha(s0path),masterGenerations=1,visualImagesViewed=9,remainingWeaknesses=['NW input 23 texels and mug 2 texels hidden by rear panel; H/hand ROI intact','native keycaps can read as checker clusters','wood grain still coarse in retained v2 texture','runtime/new chair v3 integration untested'])
    (OUT/'validation-summary-desk-rig-v3.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'iso_check PASS {passn} / SKIP {skipn} / FAIL {failn}; {len(proof["edges"])} strict projected edges; reports written in lane.')
if __name__=='__main__':main()
