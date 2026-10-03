# 후면 착석 디자인 리뷰

**원인은 (a) 의자 상부 형상과 (c) 책상 표면의 전면 분류가 겹친 것**이다. NE에서는 책상 영향이 더 크다. C/H 배치 오류는 없으며, 캐릭터 머리를 1px 올리는 것만으로 해결되지 않는다. **권고: 책상 표면/키보드와 전면 테두리를 분리하고 의자 등받이를 7px 낮추는 가구 수정안.** 아래 수치는 최종 에셋의 합격 선언이 아닌 결정론적 수정 시뮬레이션이다.

[전체 수치](measurements.md) · [프레임별 수치/깊이 큐/입력 SHA-256](measurements.json) · [합성 1×](contact-1x.png) / [3×](contact-3x.png) · [수정안 3×](recommended-3x.png) · [hero 크롭](hero-crops-3x.png) · [S0 문구 제안](s0-proposed-wording.txt)

## 측정 기준

- `measure.py`: 세 캐릭터의 `seated-typing-front` 4방향 × 4phase = 48프레임. v2 `default` desk/chair와 실제 PNG part를 사용한다. chair 방향은 worker 방향이며, desk 방향과 반대다. 표의 픽셀 수는 4phase 합산이다.
- 머리는 제작자가 고정한 **canonical head alpha**에 manifest의 `headOffsetsByClip`을 적용한다. dev는 native idle의 SE/SW `y<41`, NE/NW `y<39` 및 원래 눈/헤드폰/목 보정, seated −3; punk는 각 native idle `y<38`, seated −1; professor는 저장된 `head-<facing>.png`, seated −3이다. dev SW/NW는 원래 `x′=32−x`를 굽는다. 머리에는 원래 head 부품에 포함된 목·수염·헤드폰도 포함된다. 새 해부학적 경계로 재해석하지 않았다.
- 몸통 ROI = actor alpha255 중 `11≤x≤22`, `y<44`, 머리/손 제외. 손 ROI = 각 `handAnchors`의 Chebyshev 반경 2 안에 있는 해당 캐릭터의 정확한 피부 ramp 픽셀, 머리 제외. 몸통/손은 이 **고정 ROI의 노출률**이며 수동으로 분리한 전체 해부학적 마스크가 아니다. [마스크 확인](regions-3x.png): 분홍 머리, 파랑 몸통, 노랑 손.
- 좌표는 manifest의 명시적 anchor와 `P(i,j,z)`로 배치한다. `seatGroupId`가 있는 part에만 seat-ground depth를 대입하고 `(depth, isoZOrder, instanceId, partId)`로 정렬한다. rear −20 / actor 0 / desk-front 10 / chair-front 20을 지킨다. 높이나 alpha bbox로 정렬하지 않는다. 그림자·바닥은 몸을 가리는 part가 아니므로 생략했다.
- visible = region alpha 중 **actor 이후에 그려지는 가구 alpha255와 겹치지 않는 픽셀**. part별 단독 overlap은 중복될 수 있어 원인 표에는 마지막 가림 part의 배타적 픽셀 수를 쓴다.

## 원인 판정

| 항목 | 판정과 근거 |
|---|---|
| (a) 등받이 높이/형상 | 실제 기여. 후면 머리 41–73px를 의자가 가린다. 의자만 있을 때 노출은 72.7–82.8%. 의자 상부만 8px 내려도 전체 NE 노출은 45.4–50.9%라 단독 해결은 불가하다. |
| (b) 착석 head/pelvis 오프셋 | C/H metadata 오차 0px, 96개의 typing 손 접점에서 피부 거리 ≤1px. 허용 접점 안의 정렬 실패는 발견하지 않았다. 머리 −1px 실험의 개선은 0.7–4.6%p뿐이다. −8px도 NE 52.4–68.1%이고 목 연결/전환을 검증하지 않아 채택할 수 없다. |
| (c) front/rear 분류 | **등받이를 front로 둔 것은 S0에 맞다. 문제는 `desk__front__se__00` / `desk__front__sw__02` 안에 표면·키보드까지 묶여 있다는 점이다.** 해당 strip은 후면 머리를 각각 65–76 / 121–129px 가린다. 이번 default 큐에서 직접 머리를 가린 monitor part는 0px였다. [분리된 part](source-parts-3x.png)로 확인했다. |
| (d) hero와의 차이 | hero의 dev/professor/punk 크롭에서는 머리 대부분이 등받이 위에 있고, 키보드 위 손과 어깨가 읽힌다. 현재 합성은 머리가 책상 테두리·등받이 사이에 묻힌다. hero는 의도 확인용이며 숨은 머리의 정답 마스크·S0 단위 높이를 제공하지 않는다. hero 노출률이나 정확한 px 높이를 추정하지 않았다. |

punk/professor의 기존 수치를 네 방향 모두 정확히 재현했다. dev의 기존 약 42%는 `runs/v2/review_contacts.py`가 **v1 가구**와 `y<43` 머리/상체 영역을 사용한 값이다. v2와 canonical head로 다시 측정한 값은 NW 44.8%, NE 25.4%다.

## 수정 비용 순 제안

비용은 수정 대상/계약 부담의 상대 순위이며 시간 측정값이 아니다. 범위는 세 캐릭터의 후면 NW/NE, 모든 typing phase다.

| 순위 / 안 | 측정 효과: NW / NE 머리 | 변경 상수·재출력 대상 |
|---|---|---|
| 1. 책상 표면/키보드만 rear로 분리 | 65.3–73.9% / 63.7–71.4%; 후면 손 ROI 100% | frozen 수치 유지. seated surface에 rear −20 적용 규칙 추가, fascia는 10 유지. **가구 desk만** part PNG/manifest 재출력. |
| 2. seated head −1px | 48.5–58.4% / 26.1–30.7%; 손/몸통 개선 없음 | C/H·셀·발·골반 상수 유지. **캐릭터만** 관련 normal/blink·전환 재출력. ≤1px phase lock과 action 간 누적 변위 해석/목 연결은 미검증이며, C/H 허용오차가 머리 이동 허가를 뜻하지는 않는다. 효과가 작다. |
| 3. chair 상부 −높이 4 / 8px | 4px: 58.4–69.3% / 39.9–42.0%; 8px: 66.0–72.7% / 45.4–50.9% | **가구 chair만**. 전 방향 높이를 일관되게 바꾸려면 frozen chair back top **28→24 / 20**. 다른 상수 유지. |
| **4. 표면 분리 + chair 7px 축소 (권고)** | **85.8–91.2%**, 아래 캐릭터별 수치 | **가구 desk+chair만**, 캐릭터 재생성 불필요. frozen chair back top **28→21** 제안. surface rear 규칙 추가. |
| 5. 머리만 chair 가림을 우회하는 별도 layer | 68.1–72.7% / 45.4–53.9%; desk 가림 잔존 | **캐릭터 head 분리와 가구/합성 계약 모두** 필요. 단일 rank만으로 이번 per-pixel 우회를 표현할 수 없다. 가장 비싸며 권고하지 않는다. |

`chair-rear`는 몸통까지 의자 위로 뜨므로 S0 위반 대조군이다. `desk-seat-rear`로 strip 전체를 보내면 표면과 함께 fascia도 사라져 좋아 보이는 수치가 나온다. 둘 모두 최종 수정안으로 쓰지 않는다. 이 대조군 및 head −4/−8, 축소 4–8px sweep도 전체 수치에 남겼다.

| 캐릭터 / worker | 현재 머리 | 권고 머리 | 권고 몸통 ROI | 권고 손 ROI |
|---|---:|---:|---:|---:|
| dev NW | 44.8% | 89.2% | 46.9% | 100% |
| dev NE | 25.4% | 87.7% | 51.1% | 100% |
| punk NW | 45.4% | 87.8% | 39.8% | 100% |
| punk NE | 27.3% | 85.8% | 51.1% | 100% |
| professor NW | 55.5% | 91.2% | 51.8% | 100% |
| professor NE | 29.0% | 88.7% | 56.4% | 100% |

후면 머리 ≥85%를 **새 시각 gate 제안**으로 잡으면, 시험한 정수 축소 4–8px 중 7px가 최소다(6px에서 punk NE 83.5%). 이 85%는 기존 S0나 hero에서 나온 기준이 아니다. 실제 승인 전에는 아래 근사 마스크를 물리적 가구 형상으로 다듬어 같은 측정을 다시 해야 한다.

## 시뮬레이션의 정확한 수정 범위와 한계

표면 split은 empty desk에서 고정한 2:1 테두리 경계와 키보드 ROI다. SE는 `y<min(0.5x+53,111−0.5x)` 또는 `x=80..103,y=55..70`; SW는 `y<min(0.5x+46,127−0.5x)` 또는 `x=48..82,y=73..85`. **해당 seat-front part의 원래 alpha에만** 적용한다. 선택 픽셀은 seat depth의 rear −20, 나머지는 기존 front 10이며 disjoint union의 RGBA가 원본과 같다. actor 머리에 맞춰 가구를 지우는 마스크가 아니다.

chair는 NE/NW `chair__front__*__00`의 source `y<68` 상부를 Npx 아래로 옮기고 `y<68`에서 자른다. `y≥68`의 seat/base, 위치·anchor·C/H는 유지한다. 이 **상부 축소 근사**는 쿠션 근처 armrest도 건드릴 수 있다. 7px 이동의 효과가 실제 3D back top 21의 최종 래스터와 같다고 검증한 것은 아니다. 제작 시 등받이/팔걸이를 실제 부품으로 나누고, SE/SW에서는 rear에 속한 등받이도 같은 높이로 다시 그려야 한다.

권고 재출력: 두 desk seat strip의 표면/키보드·fascia part와 해당 `desk-parts.png`/`desk.iso.json`; 네 방향 chair body part·`chair-parts.png`/`chair.iso.json`·empty reference, 필요 시 chair shadow. monitor/screen art와 캐릭터 body는 재생성할 필요가 없다. 화면·렌더 pack 수정은 이 리뷰에서 하지 않았다. 실제 export에서는 표면의 ground-cell별 depthAnchor와 보행자 overlap을 재검수해야 한다. 이번 후보는 원래 strip의 ground key를 유지한 자리 합성이다.

baseline의 worker SE/SW는 머리 93.8–98.6%지만 손 ROI가 모두 0%다. 후면에만 적용한 후보도 이 방향은 그대로이므로, **네 방향 손 가림까지 해결한 안이라고 주장하지 않는다.** 키보드 접점/레이어에 대한 별도 가구 검수가 필요하다.

## 실행 및 검증

```powershell
& 'C:/Users/myori/.codex/skills/sprite-gen/.venv/Scripts/python.exe' -B assets/art/iso/design-review/measure.py
```

816개 scenario/phase record를 생성했다. 48 baseline은 별도의 Pillow 레이어 소유권 래스터로도 머리 노출 수를 확인했다. punk/professor의 기존 접점 증거 8방향 수치 재현, C/H·실제 피부 접점, part/clip depth 일치, surface split 원본 재구성, 가림 수의 배타적 합계, 입력 SHA-256 유지 assertion이 통과했다. 1×/3× PNG와 hero 크롭을 열람했다.

**WPF 실행, runtime 깊이 정렬·보행자 overlap·DPI·picking·동작 전환은 검증하지 않았다.** 그림의 자연스러움과 최종 가구 제작의 승인은 별도다. `iso_check.py`/dotnet 전체 테스트는 실행하지 않았다. 신규 생성 이미지는 필요하지 않아 생성하지 않았으며, 모든 쓰기는 이 design-review 폴더에 한정했다.
