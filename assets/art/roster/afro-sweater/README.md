# afro-sweater · team C · redo

**Current cleanup (2026-10-04): PASS** for facelock/blink/seated pixel checks and cyclic 3?0 frame review. Flat delivery remains `afro-sweater.png` + `afro-sweater.manifest.json` (32?64 cells, 42 frames). New seated delivery is `seated/afro-sweater.manifest.json` (12 added normal + eight side-blink frames). Latest evidence: `cleanup/validation.json`, `cleanup/facelock-qa/`, `cleanup/final-cells.png`, `cleanup/seated-review.png`, `cleanup/walk-seams.png` and four walk GIFs. Real-time playback is unverified because no browser was available. See the current section of `qa-notes.md` for changes, reproduction and remaining weak details. Original source notes below are historical; their deferred/no-facelock statements predate this cleanup.


큰 주황색 아프로, 드러난 이마, 검은 둥근 안경, 초록 조끼와 아이보리 칼라 셔츠, 구리색 바지와 로퍼. 새 베이스에서 13개 행을 모두 다시 생성했습니다. 이전 얼굴은 최종 프레임에 재사용하지 않았습니다.

최종은 `afro-sweater.png`와 `afro-sweater.manifest.json`입니다. **32×64 셀 / 4×13 그리드 / 128×832 시트 / 42프레임**. 기존 파일명, 행 순서, 프레임 수, FPS, 반복값과 좌표를 유지합니다.

| 행 | 상태 | 프레임 | FPS / 반복 |
|---|---|---|---|
| 0 | idle | 4: down, left, right, up | 0 / 정지 방향 선택 |
| 1 | walk_down | 4 | 7 / 반복 |
| 2 | walk_left | 4 | 7 / 반복 |
| 3 | walk_right | 4 | 7 / 반복 |
| 4 | walk_up | 4 | 7 / 반복 |
| 5 | typing_back | 3 | 5 / 반복 |
| 6 | typing_side | 3 | 5 / 반복 |
| 7 | report | 3 | 5 / 1회 |
| 8 | sleep | 2 | 2 / 반복 |
| 9 | coffee | 3 | 3 / 반복 |
| 10 | phone | 3 | 3 / 반복 |
| 11 | talk | 3 | 4 / 반복 |
| 12 | surprised | 2 | 5 / 1회 |

RGBA straight alpha, 이진 알파 0/255, 투명 RGB=0, 공유 32색 팔레트입니다. 원본 행의 크로마는 **#FF00FF**, 최종 빈 10칸은 투명합니다. 기준은 `(16,60)`, 하단 여백 4px입니다. 확대는 nearest-neighbor를 사용합니다. 타이핑 가구와 놀람 `!`는 worker처럼 별도 레이어입니다.

sprite-gen 2.20.0, provider **codex**, 정지 이미지 component-row 파이프라인입니다. 영상 생성은 하지 않았습니다. 눈을 손상시킨 `pixel_unfake` 처리는 폐기했습니다. 최종은 **기존 kcentroid 추출 → sprite-gen 공유 팔레트/알파 처리 → 정수 위치 보정 → facelock**이며 추가 축소는 없습니다.

`run/sprite-sheet-alpha.png`와 `run/curated/`는 잠금 전 원본입니다. 최종 PNG는 `locked/afro-sweater.png`와 동일합니다. 전용 프로필은 `tools/facelock.py`, 설명은 `docs/facelock.md`에 있습니다.

`C-contact.png`는 idle과 네 방향 걷기의 정확한 **3× nearest-neighbor** 검수판입니다. `redo/qa/last-review.png`는 새 보고서 원본/최종과 dev-hoodie 눈 비교입니다. 전후 스트립·전체 QA는 `redo/facelock-qa-all/`, 최종 검증은 `redo/qa/validation.json`, 잠금 후 걷기 GIF는 `redo/previews/`에 있습니다. 옛 런은 `redo/previous-run/`, 이번 폐기/보정은 `redo/rejected/`와 `redo/corrections/`에 보존했습니다.

기존 layout/timing, 42프레임, 빈 10칸, 팔레트·알파·여백·기준선·최종/locked 일치를 검증했습니다. 전체 facelock QA와 self-check는 **52개 캐릭터, 실패 0개**입니다.

품질 자기평가: 이마·정면 안경·하이라이트와 좌우 다리 구분은 개선됐습니다. **측면 안경과 보고서의 먼 눈 디테일은 dev-hoodie보다 약합니다.** 손가락 동작도 미세합니다. 모든 방향이 같은 품질 기준에 도달했다고 단정하지 않습니다. GIF 재생은 관찰하지 않아 보행은 experimental입니다.
