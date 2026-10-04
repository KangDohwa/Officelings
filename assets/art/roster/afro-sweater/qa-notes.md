## Current cleanup delivery ? 2026-10-04

**PASS: facelock, blink, seated pixel checks, and cyclic frame/seam review.** This does not certify real-time playback or polished anatomy in every cell.

Recomputed facelock from the unlocked run sheet with the existing reviewed ten-row profile, one-lens right-idle restriction and reviewed offsets. Added tight down/left/right blink interiors and a silhouette-specific seated calibration. No new art generation or face redraw.

Load `afro-sweater.png` + `afro-sweater.manifest.json` for the flat 13-row delivery. The root PNG equals the currently committed locked PNG. Follow `locked/afro-sweater.manifest.json` for generation paths; older root files in `locked/` are historical. The immutable corrected source is `cleanup/source/`, its local reviewed calibration is `cleanup/profile.json`, and the pre-cleanup delivery is `cleanup/before.*`. The original `run/` is preserved.

New seated frames are committed through **`seated/afro-sweater.manifest.json`** inside this character folder: 12 normal frames in three appended rows, plus eight phase-matched side blink frames. Follow that manifest to its `.generations/` sheet and blink files. The top 42 cells are byte-identical to the current locked sheet; this local output has not been copied to shared `assets/art/seated/` or adopted by the pack.

QA: `cleanup/facelock-qa/report.json` has 13 groups, after=0, failures=0. `cleanup/validation.json` records independent reload checks: 42 nonempty flat cells, 12 seated cells, 32 opaque colors, binary alpha, unchanged silhouette/layout/timing, zero transparent RGB, empty trailing cells, safe margins, baseline y=60, source freshness and eye-only blink changes. Seated heads are preserved through the recorded one-pixel nod and lower bodies are identical across phases. Shared facelock/QA/seated code and QA thresholds were not edited.

Walks: all four full cycles and the explicit **2?3?0?1** seam strips were viewed in `cleanup/walk-seams.png`. No extra wrap jump or baseline reset was identified; native changed-pixel counts at 3?0 are down: 270, left: 261, right: 354, up: 425. These counts supplement visual inspection and do not score gait quality. `cleanup/walk-metrics.json` binds the results to frame hashes, while the four `cleanup/walk_*.gif` files preserve order and timing. **Real-time/browser/runtime playback remains UNVERIFIED:** browser inventory returned apps=[], browsers=[], and the in-app browser was unavailable. A self-contained replay page for all 16 loops is retained at `../chef-sous/cleanup/loops.html`.

Remaining weak: Small side lenses and the report far-eye sparkle remain weak. Back-walk hair volume changes between phases, and finger taps are tiny. Seated arm/shoe shading is simplified. Native eye pixels being consistent does not certify the full original face-quality bar in every pose.

Reproduce with the existing sprite-gen Python environment and the character-local driver `assets/art/roster/chef-sous/cleanup/cleanup.py afro-sweater <stage>`; stages are `source`, `lock`, `qa`, `seated`, `review`, `deliver`. It invokes the existing tools' processing/check functions with local calibrations in memory and redirects seated publication here. QA invokes `facelock_qa.check` for this committed report because the stock CLI scanned an unrelated locked report before filtering and hit PermissionError; the original checker and its assertions are unchanged. Corruption-detecting self-check also passed.

No build_pack, pack mutation, runtime change, commit or push. Image generation was serial (chef face, then gardener face); generated references and prompts are local to those two cleanup folders.

---

**Historical generation/extraction notes below describe the pre-cleanup snapshot and are superseded for current face-lock, blink, seated and seam status.**

# afro-sweater redo QA

새 베이스 + 13개 새 행 + 좌우 걷기/뒤 타이핑/보고서 보정 4개를 provider codex로 **하나씩** 생성했습니다. 옛 얼굴은 재사용하지 않았습니다. 총 **12번의 이미지 보기**로 두 기존 베이스를 각각 한 번, 새 베이스, 모든 고유 생성/보정 원본, 폐기한 셀 처리, 모든 최종 셀, walk_down/walk_left/idle 전후 스트립, 3× contact를 확인했습니다. `.raw.png` 복제본은 같은 이미지입니다.

## 수정과 판단

- 베이스: 머리 경계를 눈썹 위로 올려 연속된 이마 띠를 확보했습니다. 적갈색 머리 음영, 밝은 피부, 검은 둥근 안경, 흰 눈 하이라이트를 사용했습니다. 머리는 전체 높이의 1/3 이상입니다.
- 좌우 걷기: 첫 생성의 0/2 접지는 같은 앞쪽 다리를 반복했습니다. 2번 포즈의 밝은 앞쪽 다리와 어두운 뒤쪽 다리를 반대로 바꾼 보정본을 채택했습니다. 다리·신발 교대는 정적으로 읽힙니다.
- 뒤 타이핑: 어깨 옆의 주먹 같은 손을 허리 앞쪽으로 내린 보정본을 채택했습니다. 손 동작은 작습니다.
- 보고서: 긴 팔/종이가 2번 포즈의 축소를 유발했습니다. 머리 폭 안의 작은 카드와 짧은 전달 동작으로 행 전체를 재생성했습니다. 최종 높이는 48/48/47px이며 카드가 손에 닿습니다.
- `pixel_unfake`는 흰 눈과 하이라이트를 검은 픽셀로 뭉개어 폐기했습니다. `redo/qa/all-cells-before-lock.png`는 이 **폐기된 처리**이며 최종이 아닙니다.
- 최종은 스킬의 kcentroid component extraction과 `build_shared_palette`/`apply_palette`입니다. 공유 32색·이진 알파를 적용하고 curation에는 정수 이동만 기록했습니다. 최종 추출 errors/warnings는 비어 있습니다. 최종 셀을 모두 직접 확인했습니다.

## 얼굴 잠금과 검증

`tools/facelock.py`에 전용 10개 얼굴 행 프로필을 추가했습니다. 눈·안경·눈썹과 선택적인 입 영역을 잠급니다. idle 세 방향은 걷기 기준 얼굴을 공유합니다. 오른쪽 idle은 보이는 렌즈 하나만 복사합니다. back 행과 up idle은 그대로이며, talk 입·coffee 입/컵·phone 입은 원본입니다.

```powershell
& C:\Users\<user>\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock.py --manifest assets/art/roster/afro-sweater/run/manifest.json --sheet assets/art/roster/afro-sweater/run/sprite-sheet-alpha.png
& C:\Users\<user>\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock_qa.py --all --self-check --out assets/art/roster/afro-sweater/redo/facelock-qa-all
```

보고서 재생성 후 다시 실행했습니다. **52개 캐릭터 / 실패 0개**, corruption-detecting self-check PASS. afro-sweater의 10개 얼굴 행 + 3개 idle/walk 그룹은 모두 after=0, 최대 before=224입니다. QA는 원본/출력의 공통 불투명 eye box를 다시 계산하고 알파, 패치 밖 픽셀, back 행, layout/timing, exports와 source hash를 검증했습니다.

`redo/qa/facelock-before-after-and-final.png`에서 요청된 세 전후 스트립과 전체 셀을 보았습니다. 이 보드의 report는 마지막 보정 이전입니다. 최신 보고서 원본/최종과 전체 contact는 `redo/qa/last-review.png`에서 보았습니다. 현재 개별 전후 스트립·수치는 `redo/facelock-qa-all/`이 최신입니다. 최종 구조 검증은 `redo/qa/validation.json`입니다.

## 한계

정면 이마·안경·눈 하이라이트는 읽힙니다. **측면 작은 렌즈와 보고서의 먼 눈 반짝임은 dev-hoodie보다 약합니다.** phone/typing 손가락 변화는 작고, 행 사이 머리 크기·기울기 차이가 조금 남습니다. 점수 0은 지정된 눈 영역의 일치이며 전체 디자인 품질을 인증하지 않습니다.

최종 네 방향 걷기 GIF는 생성했으나 재생을 관찰하지 않았습니다. 네 걷기 모두 **experimental / playback unverified**입니다. 영상 생성, 앱/팩 수정, 커밋은 하지 않았습니다. finish guide는 호출했고, 기본값 저장은 선택하지 않았습니다.
