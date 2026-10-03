# 타이핑 손 가림 디자인 리뷰

**SE/SW 손 0%는 재현됐다. 주범은 상판이 아니라 CRT 하우징이다.** 현재 CRT 배치에서는 물리적으로 가능한 가림이지만, hero처럼 팔·손목으로 타이핑을 읽히게 하려면 개선할 가치가 있다. **권고는 가구만 수정: 키보드·수평 표면을 rear로 분리하고 CRT 중심을 `A+3/4 f → A+9/8 f`로 옮기는 후보.** 아래 100%는 PNG 이동 시뮬레이션이며 최종 에셋 합격이 아니다.

[개별 phase 측정](measurements.md) · [ROI 좌표·깊이 큐·해시·816개 기록](measurements.json) · [접촉 1×](contact-1x.png) / [3×](contact-3x.png) · [ROI](regions-3x.png) · [후보 비교](proposals-3x.png) · [권고 후보 네 방향](inward12-all-directions-3x.png) · [hero 카메라 쪽 좌석](hero-camera-crops-3x.png) · [S0 문구 제안](s0-proposed-wording.txt)

## 현재 측정

최신 캐릭터 원본과 `furniture-v2/workstations`의 실제 default part를 사용했다. **worker 방향** 기준이며 desk는 반대 방향이다. 4phase 합산:

| 캐릭터 | SE 손 / 전완·소매 | SW 손 / 전완·소매 | NE 손 / 전완·소매 | NW 손 / 전완·소매 |
|---|---:|---:|---:|---:|
| dev-hoodie-v2 | 0% / 50.5% | 0% / 69.6% | 100% / 100% | 100% / 100% |
| punk-girl | 0% / 41.9% | 0% / 60.6% | 100% / 100% | 100% / 100% |
| professor | 0% / 53.0% | 0% / 72.3% | 100% / 95.2% | 100% / 89.1% |

SE/SW 손 `0/358`, 전완·소매 `1064/1824=58.3%`; 개별 phase 전완 최저–최고 41.4–72.3%. 최신 SE/SW 머리는 98.2–100%다. 과거 A3 snapshot과 **32개 손 ROI/phase 분모가 다르다**. dev/punk 모션 변경분이므로 과거 분모를 현재 값으로 재사용하지 않았다. professor 손 분모는 동일하다.

`measure.py`는 기존 `design-review/measure.py`를 복사·수정했다. 손 ROI는 기존과 같은 접점 Chebyshev 반경2 안의 정확한 피부 ramp, canonical head 제외. 전완·소매 ROI는 각 최신 모션 제작 스크립트의 **어깨→팔꿈치→손목 경로**에 상완 폭4px(dev 5px), 말단 폭3px(punk 4px)를 적용하고 actor alpha·머리·손과 교차한 고정 corridor다. 어깨에 붙은 소매를 포함하며, 독립 분리된 전체 해부학 부품이라고 주장하지 않는다. 분홍=canonical head, 파랑=전완·소매, 노랑=접점 피부. ROI는 가림 결과를 보고 선택하지 않았고 두 ROI의 분모는 모든 후보에서 보존했다.

좌표는 frozen C/H와 실제 source anchor로 배치한다. `seatGroupId` part에만 seat-ground depth를 대입하고 `(ground i+j, isoZOrder, instanceId, partId)`로 정렬한다. instanceId는 이전 측정과 같은 리뷰용 `actor/desk/chair`이며 runtime ID·동점 순서는 미확인이다. 가림은 actor 이후 opaque texel의 합집합이며, 기여는 마지막 part만 배타적으로 센다. 별도 전체 장면 Pillow 소유권 래스터로 모든 후보의 ROI 수를 교차 확인했다. 이는 같은 입력·계약을 재현한 검사이며 **ROI의 해부학적 정확성이나 runtime 순서의 독립 정답은 아니다**.

## 무엇이 가리는가

| SE/SW 손의 최종 가림 | 24phase 합산 | 판정 |
|---|---:|---|
| CRT 옆면/하우징 | 312px, 87.2% | worker SE: `desk__monitor__nw__01`; SW: `desk__monitor__ne__00` |
| 키보드 keycap | 46px, 12.8% | `desk__front__nw__04` / `desk__front__ne__06`에 속한 접점 texel |
| 수평 상판 / fascia·trim / monitor base | 각각 0px | 손의 topmost 원인은 아님. 전완의 760px 가림도 전부 CRT housing |

base/housing은 원 제작 모델의 base z18..21, housing z21..36 투영 마스크로 구분했다. 원 PNG는 두 물리 부품을 별도 이름으로 저장하지 않으므로 경계는 모델 기반 분류다. 현재 손 주변은 base 경계보다 하우징 안쪽이다. [실제 blocking part](blocking-parts-2x.png)와 JSON 좌표로 확인할 수 있다. NE/NW에서 professor 전완 일부는 의자 front에 가린다.

키보드에는 실제로 cream 접점 두 texel이 남아 있다(NE source `(71,44)/(73,44)`, NW `(23,60)/(25,60)`). 주변 CRT part의 같은 texel은 비어 있다. 제작 코드 `runs/furniture-v2/geometry.py`는 CRT를 만든 뒤 이 keycap texel을 강제로 keyboard label에 넣는다. **접점 texel이 존재한다는 검사만으로 손바닥이나 키보드 평면이 읽힌다고 볼 수 없다.** 따라서 strip rear 변경은 이 작은 구멍을 통해 손 일부만 드러낸다.

S0의 `H=P(A+3/8 f,18)`는 손을 **상판과 같은 z18**에 놓는다. CRT base `M=P(A+3/4 f,18)`는 SE/SW에서 H보다 `(±12,+6)`이고 top은 base보다 18px 위다. 같은 x 영역의 CRT는 손 높이를 지나므로, 올바른 2:1 투영에서도 손이 CRT 뒤에 숨을 수 있다. 손이 상판 아래에 있어서 반드시 0%인 것은 아니다. 현재 0%는 CRT 실루엣과 keycap rank가 합쳐진 결과이며 C/H 좌표 불일치는 발견하지 않았다.

hero의 얼굴이 읽히는 BVT·MINT 카메라 쪽 크롭에는 키보드로 이어지는 전완과 밝은 손목·손바닥 같은 덩어리가 보인다. 어깨만 보이는 그림은 아니지만 두 손·손가락의 구분은 불명확하다. hero는 큰 개념 이미지여서 이 ROI의 정답 분모나 native px 높이를 제공하지 않는다. 손 100%를 hero에서 나온 기존 합격 기준으로 만들지 않았다. **현 배치를 유지하면 손 0%를 물리적 가림으로 허용할 수 있으나, 타이핑 읽힘은 팔이 occluder로 이어지는지 별도 시각 판단이 필요하다.**

## 순위와 변경 범위

효과는 SE/SW 24phase 합산의 손 / 전완·소매다. 순위는 효과와 계약 변경량의 판단이며 시간 측정값이 아니다.

| 순위 / 안 | 시뮬레이션 | 정확한 S0·픽셀 변경 / 재출력 |
|---|---|---|
| **1. 표면 rear + CRT 안쪽 3/8타일 (권고)** | **100% / 92.1%**; 손 모든 phase 100%, 전완 86.5–95.9% | **가구 desk만**. CRT 중심 `A+3/4 f → A+9/8 f`; 원 모델 `ci .25→−.125, cj=0`. 높이18/36·C/H 유지. NE/NW desk의 키보드·수평 표면은 seat rank **10→−20**, fascia 10. 네 방향 CRT·screen/LED/mask·빈 책상·숨은 상판·manifest/필요 shadow 재출력. |
| 2. 표면 rear + CRT 안쪽 1/4타일 | 82.1% / 91.9%; 손 개별 phase 54.5–100% | **가구 desk만**. CRT `A+3/4 f → A+1 f`, `ci .25→0`. 다른 조건은 1과 같다. 덜 이동하지만 모든 손은 회복하지 못한다. |
| 3. 키보드·표면만 rear, 팔이 CRT로 들어가는 기존 포즈 유지 | 12.8% / 58.3%; 전완 개선 없음 | **가구 desk만**. geometry·H 상수 변경 없음. NE/NW seat-front의 물리 표면/키보드만 −20, fascia 10. 몸통·팔을 CRT 위에 덧그리지 않는다. 0%를 깨는 최소 수정이지만 손 가독성 해결로 권고하지 않는다. |
| 4. 손·소매를 1px 위로 | 손 0%; 손만 이동 시 전완58.3%, 팔도 이동 시59.2% | **캐릭터만**. typing SE/SW 손/팔 픽셀 y−1, `handAnchors y44`와 C/H 유지; 실제 피부까지 거리0..1. normal typing rows8/9와 연결 blink 몸체 재출력. 머리·골반·셀 상수 변경 없음. 재연결·전환은 미검증이며 효과 작음. |
| 5. 키보드/손을 화면 아래로 | +1px: 12.6% / 58.3%; +8px: 7.3% / 58.3%, 일부 phase0% | **둘 다**. +1px는 z17로 낮추거나 ground `(1/32,1/32)`를 더하는 서로 다른 설계가 가능해 proxy만 측정했다. +8px 안은 z18 유지, H에 grid `(+1/4,+1/4)` 추가, source SE `(27,52)/(29,52)`, SW `(3,52)/(5,52)`. desk contacts NW y−12→−4, NE y−28→−20. 기존 H 허용오차를 벗어나며 팔은 재연결해야 한다. |
| 6. 팔 별도 layer를 desk-front 앞에 | seat depth, arms rank15: 12.8% / 58.3%; 모든 가구 뒤에 재합성하면100% / 100% | **둘 다 + 합성 계약**. 새 팔 part rank15는 CRT ground depth를 넘지 못한다. 모든 가구 앞의 `max depth+.001` 우회는 S0 ground sorting 위반 대조군이며 팔이 CRT에 그려진다. 채택하지 않는다. |

횡방향 이동 대조군도 JSON/PNG에 있다. `(±8,−4)`+표면 rear는82.1%, `(±12,−6)`는100% 손을 보이게 하지만 **NE desk에서 모델 j 최소가 −.42→−.795로 상판 최소−.43을 벗어난다**. 이를 대칭 회전 가능한 완성 설계로 권고하지 않는다. monitor base만 rear는 효과0%; monitor 제거는 손87.2%로 keycap 가림을 분리해 보여 주는 대조군이다.

키보드를 CRT보다 카메라 쪽으로 f 방향 1/2타일 옮기는 다른 해석은 `H-step 3/8→7/8`이다. SE source 손 x43/45, SW x−13/−11로 **32px cell을 벗어나므로**, C/H·cell을 유지하는 작은 변경이 아니다. 현재 손 +8y 후보와 혼동하지 않는다.

권고 CRT 이동의 projected delta는 desk SE `(-12,-6)`, SW `(12,-6)`, NE `(-12,6)`, NW `(12,6)`. monitor/screen ordinary `depthAnchor`에도 각각 grid `(-3/8,0)`, `(0,-3/8)`, `(0,+3/8)`, `(+3/8,0)`를 적용한다. 기존 monitor rank0을 보존한다. **SE/SW 머리100%, NE/NW 머리88.1–91.2%, 네 방향 손100%**로 이전 후면 개선을 유지하는 정적 후보였다. 모델 housing의 i 범위 `[-.375,.055]`, j `[-.42,.42]`는 상판 범위 i `[-.43,.625]`, j `[-.43,1.22]` 안이다. 이는 footprint 가능성 확인이며 래스터의 자연스러움 검증은 아니다.

## 한계와 권고 조건

권고 후보는 **현재 저장된 CRT/LED PNG를 정수 이동**했다. 원래 CRT 아래 숨었던 상판과 source에서 이미 잘린 CRT 하부는 복원하지 않았다. 그림에는 빈 자리·잘린 지지부·소품 겹침이 남을 수 있다. 표면 분리는 원 모델의 z18 top/keyboard polygon 및 snapped contact에만 적용했고 disjoint RGBA union을 확인했다. 원본 occlusion mask에 맞춰 가구를 지운 실험은 아니다. 키보드 이동은 접점 피부만 옮겨 팔이 끊길 수 있는 진단용이며, 새 포즈 품질을 검증하지 않았다.

따라서 **최종 승인 전에는 가구의 물리 형상으로 다시 제작한 뒤 같은 측정, 빈 책상 재구성·모든 screen 상태·소품/보행자/전환을 확인**해야 한다. 캐릭터 재생성은 권고안에 필요하지 않는다. 현재 CRT 배치를 유지하기로 결정하면 손 0%를 허용하고 전완/소매의 타이핑 읽힘을 평가하는 문구를 S0에 명시하는 것이 정직하다.

실행: `& 'C:/Users/myori/.codex/skills/sprite-gen/.venv/Scripts/python.exe' -B assets/art/iso/design-review-hands/measure.py`. 48 기본 프레임·17안=816개 기록, C/H 정확 일치, 기본 피부 접점≤1px, 두 ROI 분모 보존, 표면 원본 재구성, 레이어 소유권 교차 확인, 입력 33개 SHA-256 불변 검사가 통과했다. PNG 21쌍의 3×가 정확한 NEAREST 확대인지 확인하고 합성과 hero 크롭을 열람했다. **WPF 실행·runtime depth·DPI/picking·연속 동작·모든 가구 상태 검증은 하지 않았다.** docs·원본 에셋·기존 design-review는 수정하지 않았고 모든 쓰기는 이 폴더에 한정했다. 이미지 생성과 git 쓰기는 하지 않았다.
