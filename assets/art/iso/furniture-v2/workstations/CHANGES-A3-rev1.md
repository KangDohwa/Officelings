# A3 rev1 — 후면 착석 가구 수정

조건부 인계. 모든 쓰기는 이 workstations 디렉터리 안에서만 수행했다.

- `desk-parts.png` / `desk.iso.json`: `desk__front__se__00`, `desk__front__sw__02`를 물리적 표면·키보드와 fascia로 분리. 새 `desk__rear__se__14`, `desk__rear__sw__14`는 seat rear −20, 원래 fascia는 10. disjoint RGBA union은 원본과 정확히 같다. 기존 anchor/depthAnchor와 다른 93개 desk 셀은 보존했다.
- `chair-parts.png` / `chair.iso.json` / `chair-empty-{se,sw,ne,nw}.png`: 네 방향 등받이를 실제 투영한 z12..21 패널로 다시 그렸다. 원래 상부를 통째로 이동하지 않았다. 쿠션은 z12, 골반 contact texel 및 (16,44) rig/C/H는 유지했다. 팔걸이는 짧은 z12..13 cap과 z10..12 지지대로 재제작했다. 팔걸이 높이는 S0 frozen 값이 아니며, 머리 가림을 줄이기 위한 이번 물리 형상 선택이다.
- 14개 monitor 셀의 RGBA, screen atlas/4개 mask, desk empty PNG, desk/chair shadow 파일은 byte-identical. 빈 의자는 새 파츠로 정확히 재구성되며 모든 body part mask가 disjoint다. 화면 상태별 기존 데이터도 보존했다.

[전후 접점 1×](contact-before-after-A3-rev1-1x.png) / [3×](contact-before-after-A3-rev1-3x.png), [실제 최종 1×/3× + 올바른 hero 크롭](final-quality-A3-rev1.png), [빈 의자](empty-review-A3-rev1.png), [실측 전체 표](measurements-A3-rev1.md), [phase별 수치·입력 hash·깊이 큐](measurements-A3-rev1.json), [재구성/보존 증거](export-proof-A3-rev1.json).

| 캐릭터 | NW 머리 전→후 | NE 머리 전→후 | 후면 손 ROI |
|---|---:|---:|---:|
| dev | 44.8→89.6% | 25.4→88.1% | 100% |
| punk | 45.4→91.2% | 27.3→88.8% | 100% |
| professor | 55.5→91.2% | 29.0→88.7% | 100% |

48 typing phase의 머리·몸통·손을 실제 export로 측정했고, 동일한 actor snapshot으로 전후 분모를 보존했다. 독립 Pillow layer-ownership raster로 세 ROI의 visible count를 교차 확인했다. 후면 per-phase 최저 머리 88.06%, 전면 최저 98.24%로 제안된 85%/90% gate를 통과한다. 몸통/손은 리뷰의 고정 ROI이며 전체 해부학 부품 노출률은 아니다.

측정 중 professor manifest/atlas가 다른 작업에서 변경됐다. `rev1-source/actor-inputs/`에 읽기 복사본을 고정했으며 캐릭터 원본은 수정하지 않았다. 기존 리뷰와 달라진 30개 ROI/phase 기록은 JSON의 `referenceDrift`, 원본 hash 차이는 `actor-input-drift-A3-rev1.json`에 기록했다. 머리 분모/수치는 동일하지만 professor 몸통·손 분모는 새 snapshot 기준이다. 이후 캐릭터 변경에는 이 수치를 그대로 적용할 수 없다.

`tools/iso_check.py …/workstations --tolerance 0 --check-reference`: **PASS 29 / SKIP 12 / FAIL 0**, exit 0. [실제 출력](validation-A3-rev1.txt). SKIP은 floor/character 전용 검사 등 비적용 항목이다. sprite-gen 지침의 native/binary-alpha/고정 팔레트 규칙과 venv를 사용했고 기존 master 기반 수작업·코드 재제작만 수행했다. 이미지 생성 0회, 이미지 열람 총 14회. [재제작 스크립트](revise-A3-rev1.py), [리뷰에서 복사·수정한 측정 스크립트](measure-A3-rev1.py).

남은 약점: hero보다 등받이 쿠션감과 곡면 음영이 단순하고, 매우 낮고 짧은 팔걸이가 다소 가볍게 읽힌다. 기존 NW desk grain/소품의 복잡함은 남는다. **worker SE/SW 손 ROI는 세 캐릭터 모두 여전히 0%**다. 전면 typing의 물리적인 손 읽힘까지 해결했다고 주장하지 않는다.

SE/SW 손 가림은 NE/NW desk의 seat-front strip 2개와 monitor housing part 2개가 원인이다. phase0에서 front strip이 2px, monitor가 7–12px를 가린다. 해결 비용의 최소 범위는 이 두 방향의 추가 surface/fascia split + monitor 주변의 물리 mask/depth 재검토, 3캐릭터×2방향×4phase 접점 재측정과 empty/state/walker 검수다. strip split만으로 monitor 가림은 해결되지 않는다. 손을 CRT 위에 띄우는 단순 rank 우회는 물리적 해결로 승인할 수 없다. 필요하면 monitor 실루엣/배치 재제작과 연동 계약 승인이 추가된다. 이 추가 작업의 시간은 측정하지 않았다.

[보행자 정적 3×](walkers-A3-rev1-3x.png)에서 네 방향의 가까운/먼 끝 16개 정적 경우를 확인했다. walker는 자신의 ground key를 유지하며 seat key를 대입하지 않았다. 일부 위치는 동일 좌표의 다른 facing이고, 연속 보행/충돌/전체 depth sweep 검증은 아니다. **WPF/runtime 순서, DPI, picking, sit/stand 전환과 animated playback은 검증하지 않았다.** 기존 상위 v2 리뷰/NOTES는 이전 chair를 보여 준다.

## 정확한 S0 수정 제안 — docs는 수정하지 않음

아래 geometry/seat 규칙은 채택된 option 4를 명시한다. 마지막 visual gate 문구는 기존 S0에 없었던 기준으로 integrator의 최종 반영 대상이다.

```diff
--- docs/iso-slice-s0.md
+++ docs/iso-slice-s0.md (proposed)
@@ Frozen geometry and field names
-| `deskHeight`, `seatHeight` | Desktop 18, cushion 12. Monitor top 36; chair back top 28. |
+| `deskHeight`, `seatHeight` | Desktop 18, cushion 12. Monitor top 36; chair back top 21. |
@@ Naming and manifest conventions
-At a seat, chair rear is −20, actor 0, desk front 10, chair front 20. The backrest covering a back-facing torso belongs to the front occluding part, regardless of its physical name. Split long front faces into per-edge strips as needed. Parts reconstruct the complete empty object without duplicated opaque pixels; seat-specific anchors/ranks affect only the attached assembly. Walkers use their own ground depth. Furniture definitions use the existing `parts` hook; runtime IDs and group ownership are supplied by packaging/Core.
+At a seat, chair rear is −20, actor 0, desk front 10, chair front 20. For SE/SW desks with NW/NE seated workers, split the keyboard-bearing seat strip into the horizontal desktop/keyboard surface and the physical front fascia. The horizontal surface belongs to a seat-group rear part at −20; the fascia stays at 10. Preserve the exact source anchors, ordinary ground-cell depthAnchor values and frozen C/H contacts. Use disjoint physical masks whose RGBA union equals the complete source strip.
+
+The backrest covering a back-facing torso belongs to the front occluding part, regardless of its physical name. Rebuild the physical backrest in all four orientations at top height 21, retaining cushion height 12 and the frozen pelvis/hand contacts. For SE/SW sitters the backrest is rear; for NE/NW sitters it is front. Do not move the entire backrest behind a back-facing torso or globally raise actor/head depth.
+
+Split long front faces into per-edge strips as needed. Parts reconstruct the complete empty object without duplicated opaque pixels; seat-specific anchors/ranks affect only the attached assembly. Walkers use their own ground depth. Furniture definitions use the existing `parts` hook; runtime IDs and group ownership are supplied by packaging/Core.
+
+For each of the four phases of seated-typing-front and each pilot, use the documented canonical head alpha at its clip offset and the actual furniture queue. Require at least 85% head visibility for workers NW/NE and at least 90% for workers SE/SW. Preserve head denominators, and publish visible/total head, precisely defined torso ROI and hand-contact skin ROI for all four directions. A checker pass alone is not visual acceptance. Review empty reconstruction, C/H, walkers, transitions and WPF ordering separately.
```

그 밖의 projection, tile/cell, foot, standing height, pelvis, desk/monitor/cushion, A/C/H, hand tolerance, phaseContacts/timings, 38-row/24-blink 계약, 팔레트와 일반 ground sorting은 바꾸지 않았다.

실측 시간은 `lane-time-A3-rev1.json`에 기록한다. 첫 clock 기록 전의 초기 지침 읽기는 미계측이며, 측정 구간은 도구 대기·재그리기·검수·보고 작성을 포함한다. 생성 대기 시간은 0이다.
