# A3 rev2 — 책상·CRT 실제 재제작

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
--- docs/iso-slice-s0.md
+++ docs/iso-slice-s0.md (proposed)
@@ -97,9 +97,11 @@
 Blue rug uses navy with cloth clusters. Checker uses metal darkest `#191E1E` and cream light `#E7D1AC`. Shadows use navy darkest `#111924`, with the fixed alpha values above. Screen/lamp glow stays on a small separate overlay. Character/pet `identityPalette` is an explicit list of `#RRGGBB` from that identity reference, shared unchanged by its normal/blink sheets. Environment/prop sheets cannot expand the palette with arbitrary identity colors. Checker membership tolerance is RGB max-channel difference ≤3, not a license to add new ramps. The reference PNG includes the swatches; a second palette PNG is unnecessary.
 
+Relative to the former CRT center, desk SE/SW/NE/NW projected deltas are (-12,-6), (+12,-6), (-12,+6), (+12,+6), with corresponding ground deltas (-3/8,0), (0,-3/8), (0,+3/8), (+3/8,0). Apply these deltas to all CRT housing/base and screen/LED ordinary depthAnchor values. Preserve monitor isoZOrder 0 and existing overlay rank 1; do not attach CRT parts to seat-ground depth. Keep A/C/H, keyboard contact pairs, source anchors, desk height 18 and monitor top 36. Rebuild exposed desktop and cut CRT supports as physical art. Relocate desktop props on legal surface contacts consistently across monitor states when needed; publish their actual ground keys.
+
 ## Exact contact reference
 
 Reference: [iso-contact-v1.png](../assets/art/iso/ref/iso-contact-v1.png), **768×320 RGBA**, native 1×. Generate deterministically with `tools/iso_check.py --reference`. No smoothing, resizing, external image generation or production-sheet cutting is involved.
 
-There are four 192×160 panels per row. Top row desk orientations are SE, SW, NE, NW. Each panel has local desk-origin D=`(96,80)`, the exact floor diamonds under the desk and chair, a tabletop at z18, cushion at z12, upright monitor from z18 to z36, and a simple seated rig. CRT ground center is `A+3/4 f`; its projected base is `P(A+3/4 f,18)`, with an 18px screen height. This is a contact fixture, not an approved silhouette or depth golden. Cross centers are the authoritative contacts: cream `#F2EBD3` = ground foot reference, teal `#5DB1A5` = pelvis, red `#BF6554` = hands. Each cross extends two pixels in x/y; paired hand crosses overlap.
+There are four 192×160 panels per row. Top row desk orientations are SE, SW, NE, NW. Each panel has local desk-origin D=`(96,80)`, the exact floor diamonds under the desk and chair, a tabletop at z18, cushion at z12, upright monitor from z18 to z36, and a simple seated rig. CRT ground center is `A+9/8 f`; its projected base is `P(A+9/8 f,18)`, with an 18px screen height. This is a contact fixture, not an approved silhouette or depth golden. Cross centers are the authoritative contacts: cream `#F2EBD3` = ground foot reference, teal `#5DB1A5` = pelvis, red `#BF6554` = hands. Each cross extends two pixels in x/y; paired hand crosses overlap.
 
 | Top panel | Global desk origin | Chair ground / pelvis | Character cell top-left | Global two hands |
@@ -176,5 +178,5 @@
 Blink clips have names `blink-<normal-name>`, the matching direction/fps/loop, four phases, and rows 0..23 in the normalRows order. All non-eye RGBA bytes must match the corresponding normal phase exactly; each marked eye region is at most 32 texels, within 12×4 pixels, and lies on the opaque head. Keep the head silhouette/landmarks stable, allowing only documented integer translation of at most 1px per phase; masks follow that translation. The checker proves mask presence and normal/blink equality outside it. Human review still proves identity and cross-phase face stability.
 
-At a seat, chair rear is −20, actor 0, desk front 10, chair front 20. For SE/SW desks with NW/NE seated workers, split the keyboard-bearing seat strip into the horizontal desktop/keyboard surface and the physical front fascia. The horizontal surface belongs to a seat-group rear part at −20; the fascia stays at 10. Preserve the exact source anchors, ordinary ground-cell depthAnchor values and frozen C/H contacts. Use disjoint physical masks whose RGBA union equals the complete source strip.
+At a seat, chair rear is −20, actor 0, desk front 10, chair front 20. For all four desk orientations, split the keyboard-bearing seat strip into the horizontal desktop/keyboard surface and the physical front fascia. The horizontal surface belongs to a seat-group rear part at −20; the fascia stays at 10. Preserve the exact source anchors, ordinary ground-cell depthAnchor values and frozen C/H contacts. Use disjoint physical masks whose RGBA union equals the complete source strip.
 
 The backrest covering a back-facing torso belongs to the front occluding part, regardless of its physical name. Rebuild the physical backrest in all four orientations at top height 21, retaining cushion height 12 and the frozen pelvis/hand contacts. For SE/SW sitters the backrest is rear; for NE/NW sitters it is front. Do not move the entire backrest behind a back-facing torso or globally raise actor/head depth.
@@ -182,5 +184,5 @@
 Split long front faces into per-edge strips as needed. Parts reconstruct the complete empty object without duplicated opaque pixels; seat-specific anchors/ranks affect only the attached assembly. Walkers use their own ground depth. Furniture definitions use the existing `parts` hook; runtime IDs and group ownership are supplied by packaging/Core.
 
-For each of the four phases of seated-typing-front and each pilot, use the documented canonical head alpha at its clip offset and the actual furniture queue. Require at least 85% head visibility for workers NW/NE and at least 90% for workers SE/SW. Preserve head denominators, and publish visible/total head, precisely defined torso ROI and hand-contact skin ROI for all four directions. A checker pass alone is not visual acceptance. Review empty reconstruction, C/H, walkers, transitions and WPF ordering separately. Known gap (2026-10-04): the SE/SW hand-contact ROI is 0% visible behind the desk in the current pilots; it has no gate yet.
+For each of the four phases of seated-typing-front and each pilot, use the documented canonical head alpha at its clip offset and the actual furniture queue. Require at least 85% head visibility for workers NW/NE and at least 90% for workers SE/SW. Preserve head denominators, and publish visible/total head, precisely defined torso ROI and hand-contact skin ROI for all four directions. A checker pass alone is not visual acceptance. Review empty reconstruction, C/H, walkers, transitions and WPF ordering separately. With A3 rev2 furniture, all 48 pilot typing phases expose the documented hand-contact skin ROI fully. Forearm/sleeve corridor visibility for SE/SW workers is 86.5–95.9%. These are measured final-art results with fixed actor snapshots, not a new anatomical hand gate or runtime acceptance.
 
 ## Exclusive output ownership
```

실측 **0.3547 lane-hours / 21분 17초**(2026-10-03 22:40:13–23:01:30 UTC). `lane-time-A3-rev2.json`에 기록한다. 최초 clock부터 최종 검증·보고서까지 한 lane의 경과이며 생성 대기 0초다. 이 작업의 속도를 다른 캐릭터 생산 처리량으로 일반화하지 않는다.
