# Outline rev2 적용 결과

감독자가 외곽선을 포함한 grounded standing/idle 높이 38–43px를 승인했다. dev-hoodie-v2, punk-girl, professor, glasses-bun, hanbok-modern에 rev2를 적용했다. rev2-max는 비교 이미지에만 사용했다. 현재 normal/blink 10개 백업과 SHA-256은 runs/outline-rev1-backup/<character>/ 및 preflight.json에 보존했다.

## 규칙

- 본체를 줄이거나 옮기지 않고 외부에 연결된 투명 영역에 8-neighbor Chebyshev 거리 1의 바깥 외곽선 #111924를 추가했다. 닫힌 내부 구멍은 확장하지 않았다. 허용 영역의 누락된 outline texel은 0이다.
- x=0, y=61..63 투명도를 유지했다. standing sole y=60, transition의 phaseContacts.footY, seated의 기존 최하단 발 행을 RGBA/alpha 그대로 보존하며 그 행 아래로 확장하지 않는다. foot/pelvis/hand contacts, cell size, head offsets와 manifest는 변경하지 않았다.
- Y<0.18의 편집 가능한 의복 경계에 기존 identityPalette 색의 내부 rim을 넣었다. 조명 쪽만 칠한 시안은 어두운 배경 목표에 못 미쳐 적용판은 의복 경계 전체에 rim을 연결했다. --rim-sides upper-left로 조명 쪽 제한도 가능하다. 머리·얼굴·눈·손에는 내부 rim을 넣지 않는다.
- arm/torso, collar/cuff, hem/belt, sole, 보행 다리 경계의 contour를 더 짙게 하고 재질 ramp에서 명암을 한 단계 분리했다. 피부에 인접한 의복을 편집할 수 있도록 rev1의 피부 collar만 해제했다. 원래 head/hand/contact RGBA는 그대로 잠갔다.
- max는 같은 1px 확장에 더 밝은 rim, 2px 내부 contour, 재질 두 단계 분리를 사용한다. 생산 atlas에는 적용하지 않았다. 모든 색은 frozen ramp 또는 해당 identityPalette에 있다.

| 캐릭터 | rev2 rim | max rim | 외곽선 포함 idle 높이 SE/SW/NE/NW |
| --- | --- | --- | --- |
| dev-hoodie-v2 | #DCBE89 | #F7ECD3 | 41/41/41/41 |
| punk-girl | #F19188 | #FADFD1 | 43/43/43/43 |
| professor | #CEB59B | #F4E1C2 | 43/43/43/43 |
| glasses-bun | #F39D58 | #FCEAC7 | 43/43/43/43 |
| hanbok-modern | #EADCCA | #FFFFFF | 43/43/42/42 |

머리 내부 hair/face overlap은 더 칠하지 않고 기존 head RGBA 보존을 우선했다.

## 감독자 적용용 공유 패치

공유 tools/iso_check.py와 docs/iso-slice-s0.md는 수정하지 않았다. [S0-outline-rev2-proposed.patch](S0-outline-rev2-proposed.patch)는 UTF-8/LF unified diff이며 git apply --check를 통과했다. 커밋은 만들지 않았다.

checker의 정확한 변경:

```diff
-                assert 38 <= yy.max()-yy.min()+1 <= 42, 'standing height outside 38–42'
+                assert 38 <= yy.max()-yy.min()+1 <= 43, 'standing height outside 38–43'
```

S0의 정확한 본문 셀 변경:

```diff
-| `characterCell`, `footAnchor`, sheet `anchor` | 32×64, `(16,60)`, `(16,60)`; `scale:1`. Standing reference height 40, allowed 38–42. |
+| `characterCell`, `footAnchor`, sheet `anchor` | 32×64, `(16,60)`, `(16,60)`; `scale:1`. Standing reference height 40; grounded standing/idle height including the exterior outline allowed 38–43. The character body, foot/pelvis/hand contacts, head offsets, cell size, and x=0 / y=61..63 transparency remain unchanged. |
```

C1 작업 표의 Grounded 38–42px idle silhouette,도 Grounded 38–43px idle silhouette including the exterior outline; unchanged character body and contacts,로 바꾸는 정확한 전체 행 diff가 같은 패치에 있다.

[로컬 iso_check.py](iso_check.py)는 위 허용치와 이동에 필요한 ROOT 계산 parents[1] → parents[5]만 다르다. ROOT 변경은 공유 패치에 포함하지 않는다.

## 대비 측정

상하좌우 불투명/투명 pixel pair에서 본체 RGB와 해당 렌더의 실제 배경 RGB를 사용했다. sRGB를 선형화하고 상대 휘도 비율 (max(Y)+0.05)/(min(Y)+0.05)을 계산했다. 낮은 대비는 <3:1, 어두운 배경은 Y<0.1, 밝은 배경은 Y≥0.25다.

두 지표를 구분했다. 윤곽 띠 지표는 동일한 원래 perimeter pair에서 본체 경계 색과 새 외곽선 색 각각을 원래 실제 배경과 비교한 최대값이다. 원본/rev1에는 추가 불투명 층이 없어 기존 가장자리 지표와 같다. 물리적 바깥선 지표는 별도 파일에 기록했다. rim 개선으로 검은 바깥선 자체의 개선을 주장하지 않는다.

목표: 기존 실제 actor 15곳/2,262쌍에서 윤곽 띠 ≥3:1 비율 전체 ≥75%, 어두운 배경 ≥45%, 허용 영역의 외곽선 누락 0. 이 목표는 통과했다. 15곳은 rev1과 같은 seated 12곳 + walking 3곳이다. 5명 전체의 추가 wall/checker probe 집계는 별도 측정이며 75% 목표 달성으로 보고하지 않는다.

| 측정 집합 | pair 수 | 원본 ≥3:1 | rev1 ≥3:1 | rev2 ≥3:1 | max ≥3:1 |
| --- | --- | --- | --- | --- | --- |
| 실제 actor 15곳, 전체 | 2,262 | 51.459% | 55.570% | 78.249% | 79.841% |
| 같은 위치, 어두운 배경 | 697 | 25.968% | 23.529% | 51.220% | 51.220% |
| 같은 위치, 밝은 배경 | 800 | 64.125% | 71.750% | 90.375% | 90.375% |
| 5명 전체, 각 9개 실제 배경 합성 probe | 7,479 | 46.490% | 49.512% | 73.125% | 74.154% |
| 5명 합성 probe, 어두운 배경 | 3,413 | 21.770% | 16.965% | 51.362% | 51.362% |
| source 일치 post-occlusion 156곳 | 24,109 | 51.898% | 54.025% | 82.961% | 84.251% |
| 같은 post-occlusion, 어두운 배경 | 6,559 | 24.302% | 21.894% | 60.710% | 60.710% |

기존 실제 위치의 낮은 대비 비율은 48.541% → 44.430% → 21.751%다. 같은 원래 경계점에서 가장 바깥 색만 비교하면 어두운 배경의 ≥3:1은 rev2 10.330%로 rev1 23.529%보다 낮다. 5명 합성의 팽창 후 실제 perimeter 자체를 비교한 바깥선 지표도 어두운 배경에서 rev2 4.309%다. 보존한 어두운 머리의 dark-on-dark 291쌍은 계속 3:1 미만이다.

[contrast.json](contrast.json), [after-per-edge.jsonl](after-per-edge.jsonl), [all-five-dual-layer-contrast.json](all-five-dual-layer-contrast.json), [all-five-exterior-contrast.json](all-five-exterior-contrast.json), [post-occlusion-proof.json](post-occlusion-proof.json)에 근거가 있다. glasses-bun/hanbok-modern의 rev1은 기존 pilot 규칙을 같은 원본에 재현한 비교용 시뮬레이션이다.

## 검사

- 로컬 checker로 각 폴더 --stage final --tolerance 0: 각 12 PASS / 0 FAIL / 5 SKIP, 합계 60 PASS / 0 FAIL / 25 SKIP.
- 미수정 공유 checker: 56 PASS / 4 FAIL / 25 SKIP. 4 FAIL은 승인된 43px를 아직 허용하지 않는 standing height outside 38–42뿐이다. 다른 기존 규칙은 FAIL 0이다. 공유 패치 전 결과를 전체 통과로 보고하지 않는다.
- normal 760셀의 canonical head와 잠긴 hand/contact RGBA SHA-256 일치. 원래 불투명 본체 픽셀과 발 접촉 행 보존. manifest/eye-mask/원래 head-source 파일 SHA-256 불변.
- 5개 기존 blink_resync helper를 실행해 신규 normal에서 blink 480셀을 재생성했다. 닫힌 눈은 그대로, 눈 밖은 해당 phase normal과 정확히 같다. 얼굴/눈 highlight도 원래 head RGBA 그대로다.
- v3 desk/chair 직렬화 파츠/ground queue로 각 캐릭터 typing 48건, walking 1,536건, 총 240/7,680건을 검사했다. 네 desk 방향 × 실제 working-screen 0/1/2 phase × 네 actor phase이며 보행은 추가 8 probe × 네 facing이다. 원본/새 sitter 가림을 따로 계산했다. 실제 head/hand 가시성 악화 0, typing 손 100% 노출이다.
- 보행 손은 원래 제작 palm 좌표 반경 2의 피부 픽셀이다. professor walk-points.json, glasses-bun motion-joints.json, 나머지 원래 제작 스크립트의 palm 좌표를 사용했다. 처음의 넓은 피부색 ROI에는 professor의 바지 y51도 포함되어 있었다. 이를 숨기지 않고 legacySkinColorROI로 보존했다. 옛 색상 ROI에서는 24건(화면 phase 중복 포함)에 의복 1–3px가 더 가려진다. 실제 palm ROI와 canonical head의 분자/분모는 악화되지 않았다. 옛 색상 ROI까지 통과한 것으로 주장하지 않는다.

[visibility.json](visibility.json)에 전체 분자/분모가 있다. characters/<id>/proof.json에는 head/hand 전후 SHA-256, eye-mask/manifest hash, 적용된 normal/blink hash와 높이가 있다. 각 iso-check-final.txt/blink-resync.txt, [verification-summary.json](verification-summary.json)도 확인했다. 최종 normal/blink 10개 파일의 적용 hash를 다시 확인했다.

## 실행과 PNG

```text
python -B assets/art/iso/outline-pass/outline_pass.py --all --comparisons --apply
python -B assets/art/iso/outline-pass/outline_pass.py <owned-folder> --comparisons
python -B assets/art/iso/outline-pass/outline_pass.py <owned-folder> --revision rev1
```

첫 명령은 rev2만 적용한다. --apply를 빼면 preview다. --revision rev2-max --apply는 거부된다. --rim-sides/--rim-below/--contour-width로 조정할 수 있다. 저장 백업을 기준으로 하므로 재실행 팽창/색칠은 누적되지 않는다.

| 전체 5명 PNG | 1x | 2x | 3x |
| --- | --- | --- | --- |
| 원본 | [PNG](all-five-original-1x.png) | [PNG](all-five-original-2x.png) | [PNG](all-five-original-3x.png) |
| rev1 | [PNG](all-five-rev1-1x.png) | [PNG](all-five-rev1-2x.png) | [PNG](all-five-rev1-3x.png) |
| rev2 적용판 | [PNG](all-five-rev2-1x.png) | [PNG](all-five-rev2-2x.png) | [PNG](all-five-rev2-3x.png) |
| max | [PNG](all-five-rev2-max-1x.png) | [PNG](all-five-rev2-max-2x.png) | [PNG](all-five-rev2-max-3x.png) |
| 4열 비교 | [PNG](owner-comparison-1x.png) | [PNG](owner-comparison-2x.png) | [PNG](owner-comparison-3x.png) |

전체 4방향/walking/wall/checker 비교: comparison-all-five-{1,2,3}x.png. 실제 actor 위치 비교: comparison-actual-{seated,walking}-{1,2,3}x.png. post-occlusion 비교: comparison-post-occlusion-{1,2,3}x.png. 원래 실제 scene 전체 각 변형: integrated-{seated,walking}-<revision>-{1,2,3}x.png. 확대는 NEAREST다.

## 미검증과 제한

새 pack 빌드와 WPF runtime 재렌더는 하지 않았다. 기존 실제 scene에서 원래 source와 RGBA가 불일치한 기존 픽셀은 보존하고 수를 따로 기록했다. post-occlusion typing/walk 후보 240건 중 exposed head source 95% 이상 일치한 156건만 사용했다. 불일치한 84건은 적용/대비 통계에서 제외했고 원인은 추정하지 않았다. 5명 전체 sheet는 실제 빈 v3 scene과 실제 desk/chair 위치의 정적 합성이다. glasses-bun/hanbok-modern의 새 runtime 통합까지 검증한 것으로 주장하지 않는다.

다른 character lane, furniture, pack, docs/src/tests/tools는 변경하지 않았다. 공유 패치 적용과 새 runtime 통합은 감독자의 다음 작업이다.
