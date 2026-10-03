"""Final scoped handoff and exact S0 proposal; does not change shared documents."""
from pathlib import Path
from datetime import datetime, timezone
import difflib,hashlib,json
OUT=Path(__file__).resolve().parent;ROOT=OUT.parents[4]
doc=(ROOT/'docs/iso-slice-s0.md').read_text(encoding='utf-8')
proposed=doc.replace('CRT ground center is `A+3/4 f`; its projected base is `P(A+3/4 f,18)`','CRT ground center is `A+9/8 f`; its projected base is `P(A+9/8 f,18)`')
proposed=proposed.replace('For SE/SW desks with NW/NE seated workers, split the keyboard-bearing seat strip','For all four desk orientations, split the keyboard-bearing seat strip')
proposed=proposed.replace(' Known gap (2026-10-04): the SE/SW hand-contact ROI is 0% visible behind the desk in the current pilots; it has no gate yet.',' With A3 rev2 furniture, all 48 pilot typing phases expose the documented hand-contact skin ROI fully. Forearm/sleeve corridor visibility for SE/SW workers is 86.5–95.9%. These are measured final-art results with fixed actor snapshots, not a new anatomical hand gate or runtime acceptance.')
marker='## Exact contact reference\n'
extra='Relative to the former CRT center, desk SE/SW/NE/NW projected deltas are (-12,-6), (+12,-6), (-12,+6), (+12,+6), with corresponding ground deltas (-3/8,0), (0,-3/8), (0,+3/8), (+3/8,0). Apply these deltas to all CRT housing/base and screen/LED ordinary depthAnchor values. Preserve monitor isoZOrder 0 and existing overlay rank 1; do not attach CRT parts to seat-ground depth. Keep A/C/H, keyboard contact pairs, source anchors, desk height 18 and monitor top 36. Rebuild exposed desktop and cut CRT supports as physical art. Relocate desktop props on legal surface contacts consistently across monitor states when needed; publish their actual ground keys.\n\n'
proposed=proposed.replace(marker,extra+marker)
diff=''.join(difflib.unified_diff(doc.splitlines(True),proposed.splitlines(True),fromfile='docs/iso-slice-s0.md',tofile='docs/iso-slice-s0.md (proposed)',n=2))
proof=json.loads((OUT/'export-overlap-proof-A3-rev2.json').read_text(encoding='utf-8'));rep=json.loads((OUT/'repaint-proof-A3-rev2.json').read_text(encoding='utf-8'));meas=json.loads((OUT/'measurements-A3-rev2.json').read_text(encoding='utf-8'))
headmin=min(r['head']['fraction'] for r in meas['records'] if r['stage']=='after' and r['worker'] in ('ne','nw'))
changes='''# A3 rev2 — 책상·CRT 실제 재제작

요청된 desk+monitor 수정 완료. 쓰기는 이 workstations 폴더 안에 한정했다. 시각 승인은 조건부이며 WPF 실행 승인과 구분한다.

- `desk-parts.png`, `desk.iso.json`: CRT 중심 A+3/4 f → A+9/8 f. 네 방향 픽셀/ground delta를 body·screen·LED에 적용했다. 기존 source anchor, monitor rank0, screen/LED rank1 및 일반 ground sorting을 보존했다.
- 새로 드러나는 z18 상판을 기존 투영 모델과 고정 wood ramp로 복원했다. CRT z18..21 받침 및 잘린 하우징 하부는 실제 면으로 재그렸다. 기존 상부 마스터를 재사용했으며 단순 PNG 이동으로 끝내지 않았다. 받침은 SE/SW에서 12px씩 보이고 NE/NW에서는 하우징에 물리적으로 가려진다.
- NE/NW의 키보드·수평 seat strip을 새 rear14(−20)로 분리하고 fascia10을 남겼다. 기존 SE/SW rev1 split은 유지했다. CRT를 seat rank로 우회하거나 손/팔을 덧그리지 않았다.
- 머그는 선택된 원본 컴포넌트를 핸들 반전 없이 옮겼다. 종이는 z18의 작은 실제 평면으로 다시 그렸다. 네 방향 rear15/16은 실제 소품 ground를 사용하고 모든 상태에서 동일하다. CRT·키보드·머그·종이의 silhouette 겹침을 제거했다. 이동 후 완전히 숨은 기존 desktop strip 5개(SE rear07/08, SW rear10/11, NE rear07)는 활성 parts/clip에서 제외했다.
- `desk-screen.png`, 네 `desk-screen-mask-*.png`, 네 `desk-empty-*.png`를 재출력했다. default=off, screensaver 정지, working 4phase/3fps 및 128px cell·A/C/H·높이18/36 등 다른 S0 값은 그대로다. 의자와 footprint 기반 ground shadow는 byte-identical이며 별도 CRT cast shadow는 기존 계약에 없다.

[전후 접점 1×](contact-before-after-A3-rev2-1x.png) / [3×](contact-before-after-A3-rev2-3x.png), [빈 책상 전후](empty-before-after-A3-rev2.png), [native+hero](native-and-hero-A3-rev2.png), [보행·전환 시각 검수](walkers-transitions-review-A3-rev2.png), 캐릭터별 `quality-*-A3-rev2.png`에 1×/3×와 hero 원본 크롭이 있다.

[모든 phase 실측](measurements-A3-rev2.md) / [ROI·큐·해시·전후 JSON](measurements-A3-rev2.json), [재구성·상태·가림 검사](export-overlap-proof-A3-rev2.json), [재그리기·소품 접점](repaint-proof-A3-rev2.json).

세 캐릭터 × 네 방향 × 네 typing phase: 손 피부 ROI 100%, 전면(SE/SW) 머리 100%, 후면(NE/NW) 머리 88.1–91.2%. 전후 머리 노출 감소 없음. SE/SW 전완/소매 ROI는 86.5–95.9%; 몸통까지 phase별 분자/분모를 표에 기록했다. 리뷰의 고정 피부/관절 corridor ROI를 그대로 사용했으며 해부학 전체 손/전완 마스크로 과장하지 않는다. source actor snapshot과 실제 current furniture+screen/LED queue를 사용했다. 독립 전체 소유권 raster와 이후 alpha 합집합 수를 교차 확인했다. 캐릭터 원본 drift 0개.

`tools/iso_check.py …/workstations --tolerance 0 --check-reference`: **PASS 29 / SKIP 12 / FAIL 0**, exit0. [실제 출력](validation-A3-rev2.txt). 비적용 floor/character 검사는 SKIP이다. 자체 검사: **28개 state frame**, disjoint body/overlay·빈 책상 정확 재구성·snapped surface hole 0·CRT 연결 1개·소품 접점과 silhouette 검사; **1,536 보행 경우**(3캐릭터×desk4×8 near/far/end 위치×walk4방향×4phase), walker 자체 ground depth와 전후 alpha ownership 일치; **96 sit/stand frame**, contact/ground sole·정확 reversal·머리 기준/전후 비감소. 걷는 사람을 seat depth로 대입하지 않았다. 그림자·의자·lounge/utility **186개 파일 SHA-256 불변**.

sprite-gen 지침의 기존 마스터·binary alpha·NEAREST·고정 ramp와 해당 venv Python을 사용했다. 이미지 생성 0회, 이미지 열람 14회. 보행/전환의 작은 초기 검수 보드는 일부 crop이 잘못돼 수정한 `walkers-transitions-review-A3-rev2.png`로 최종 열람했다. 재현: `revise-A3-rev2.py` → `measure-A3-rev2.py` → `verify-A3-rev2.py` → `final-review-A3-rev2.py`. `rev2-source`는 수정 전 파츠/캐릭터 snapshot이며 별도 `.iso.json`을 만들지 않아 checker 발견 대상과 섞이지 않는다.

남은 약점: hero보다 CRT가 직각 상자로 읽히고, 새 받침·복원한 상판 음영이 단순하다. NW 잔여 목재 grain은 여전히 번잡하며 소품은 작아졌다. 손 피부는 드러나지만 SE/SW 팔·손목 연결은 여전히 딱딱하고, 양손 해부학/타이핑 리듬이나 기존 보행의 기계적인 느낌을 이번 가구 수정으로 해결했다고 주장하지 않는다.

미검증: WPF 실제 queue/instance ID 동점 순서·DPI·picking·충돌/항법·연속 애니메이션·runtime 전환 스케줄 및 인간의 최종 미술 승인. 정적 모든 저장 phase 검사와 일부 1×/3× 시각 검수만 수행했다. 상위 furniture fixture PNG는 이전 배치를 보여 주므로 새로운 fixture로 갱신하지 않았다. 공유 reference/checker는 이전 CRT 3/4 값을 유지한다. reference PASS는 기존 frozen C/H fixture 검증이며 9/8 CRT fixture의 동기화 증거는 아니다. 통합 담당자가 checker reference 생성의 두 .75 계수를 1.125로 바꾸고 ref PNG를 재생성해야 한다. 공유 파일을 수정하지 않았고 git 쓰기·전체 dotnet 테스트는 실행하지 않았다.

## 정확한 S0 제안 — docs 미수정

현재 S0의 줄 단위 unified diff. 관련 geometry/queue 명시와 최종 실측으로 기존 Known gap을 갱신한다. 기존 머리 gate와 손 허용오차는 유지한다.

```diff
'''+diff+'''```

실측 작업 시간은 `lane-time-A3-rev2.json`에 기록한다. 최초 clock부터 보고서 작성까지 한 lane의 경과를 계산하며 생성 대기 0초다. 이 작업의 속도를 다른 캐릭터 생산 처리량으로 일반화하지 않는다.
'''
(OUT/'CHANGES-A3-rev2.md').write_text(changes,encoding='utf-8')
# Record only this lane's production write set and immutable snapshots, no shared filesystem scan.
oldnames={p.name for p in (OUT/'rev2-source').glob('*') if p.is_file()}
updated=[]
for p in OUT.iterdir():
    if not p.is_file():continue
    if p.name.startswith('desk-') or p.name=='desk.iso.json':
        snapshot=OUT/'rev2-source'/p.name.replace('.iso.json','.json')
        if snapshot.exists() and hashlib.sha256(p.read_bytes()).digest()!=hashlib.sha256(snapshot.read_bytes()).digest():updated.append(p.name)
(OUT/'changed-production-A3-rev2.json').write_text(json.dumps(dict(updated=updated,checker=json.loads((OUT/'checker-result-A3-rev2.json').read_text()),imageGenerationCalls=0,imageViews=14,protectedFiles=proof['checks']['protectedFurnitureFiles'],headMinimumBack=headmin),indent=2)+'\n')
print(json.dumps(dict(updated=updated,checks=proof['checks']),indent=2))
