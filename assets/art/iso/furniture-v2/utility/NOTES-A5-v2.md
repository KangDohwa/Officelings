# A5 UTILITY FURNITURE v2 — exports 완료, 시각 수용은 조건부

식물·선반·커피 머신·냉장고·정수기·스탠드 **6종 × SE/SW/NE/NW = 24 named variants**를 만들었다. 이 디렉터리의 `*-parts.png`, `*-shadow.png`, `*.iso.json` 6개와 `*-empty-{se,sw,ne,nw}.png` 24개가 production/private export다. PNG는 검수 보드 포함 **38개**, manifest는 **6개**다. [1×/3× 및 hero 비교](utility-review.png), [v2 workstation fixture](utility-fixture-room.png), [checker](validation.txt). A5 v1 `assets/art/iso/furniture/utility/`가 없어 v1-v2 비교는 SKIP이다.

## 생성과 파생

준비된 `runs/a5-v2/`의 가이드·hero crop·env2/env3/env5 참조·프롬프트를 재사용했으며 prep은 재실행하지 않았다. **내장 image_gen 호출 10회가 완료**됐다: 앞면 6개 + 실제 가려진 선반/기기 뒷면 4개. 각 master는 **1 attempt**, concurrency **1**. CLI 생성이나 다른 provider로 전환하지 않았다. 내장 도구 원본은 1254×1254로 반환되어 `*-master.raw.png`에 복사했다. sprite-gen의 기존 `cutout --key magenta`로 배경을 제거하고, 준비된 빌더에서 한 번 native guide 영역에 맞춘 뒤 nearest sampling, frozen palette snap, binary alpha, 면/접점 보정을 적용했다. 가이드는 생성물 대용으로 쓰지 않았다.

`*-master.png`는 크로마 제거본, `*-native.png`는 네이티브 정리본이다. 프롬프트·가이드·raw·reports·builder·verify·review는 모두 `runs/a5-v2/`에 있다. 제공된 생성 helper는 CLI 방식이어서 이번에는 실행하지 않았다. sprite-gen의 시작 가이드는 로컬 `codex login status` 확인 실패를 보고했지만, 요청된 내장 image_gen은 정상 작동했다. 생성 승인 차단은 없었다. 선반 첫 요청은 PowerShell 문자열이 객체로 직렬화되어 인수 타입 검사에서 실패했고, 문자열 값으로 바로잡은 뒤 실제 생성 1회를 완료했다. 그 실패는 이미지 생성에 도달하지 않았다.

생성 결과의 앞면이 반대 가시 면에 있던 냉장고/정수기는 native fit 이전에 반사했고, 선반/커피/냉장고 rear도 NE 가이드에 맞춰 반사·조명 보정했다. SW와 NW 기기/선반은 canonical object face sampling으로 재구성해 책 순서·hinge/handle·controls/spigots가 object 면을 따라가게 했다. 식물/스탠드 SW와 NW는 `x'=128-x`의 anchor 보존 반사와 shade repair, NE는 radial 재사용이다. 정수기 병은 네 방향 모두 동일한 화면 기준 highlight를 유지한다. 커피 표시등은 추출구와 같은 사용 면으로 옮겼다.

## 고정 계약과 검사

128×128 셀, source scale 1, SE/SW/NE/NW 순서, upper-left light, 별도 shadow(+6,+3; alpha48/96)를 지켰다. 선반은 SE/NW 1×2, SW/NE 2×1/top44이며 source anchor는 (80,72)/(48,72)다. 나머지는 1×1, anchor(64,80): 식물 top34/pot12, 커피 top28, 냉장고 top44, 정수기 top40/chassis26, 스탠드 top40. variant parts와 clip anchors/depthAnchor/isoZOrder/physical anchorChecks를 명시했다. 선반 긴 면은 ground-cell별 strip이다. body alpha0 아래 RGB는 0이다.

준비 코드의 rear 정수기 chassis가 병을 포함한 높이40으로 잡힌 점을 고쳤다. 실제 몸통은26, 병 포함 top40이며 front guide 범위를 사용했다. 갈색으로 snap된 정수기 plastic은 cream ramp와 넓은 면 cluster로 정리했다. 기존 준비 코드의 냉장고 pickup interaction도 제거했다: S0는 새 냉장고 상호작용을 요구하지 않는다.

`tools/iso_check.py assets/art/iso/furniture-v2/utility --tolerance 0`: **PASS 66 / SKIP 30 / FAIL 0**, 96 entries. SKIP은 floor seam/character face-lock/shadow depth-part 등 이 lane에 해당하지 않는 검사다. 별도 `verify.py`는 **PASS 6 / FAIL 0**: 24 variants의 disjoint empty reconstruction, 실제 contact 주변 alpha, footprint/height metadata, part linkage, approach tile과 재계산한 reach≤20px를 검사했다. 높이는 빌더 상수를 되읽는 대신 S0의 별도 값으로 비교한다. 이는 모든 시각적 높이나 사람의 reach pose를 독립 승인한 결과가 아니다.

Fixture는 실제 v2 workstation manifest의 source clips/anchors/part ground depth를 읽어 조립했다. native720×430 → exact3×2160×1290 PNG다. 바닥은 위치 확인용 단순 geometry guide다. coffee NW at(9,1), water NW at(9,5)의 접근 (8,1)/(8,5)는 blocked cells를 피해 입구(0,4)에서 grid flood-fill로 **도달 가능**했다. 이 검사는 WPF/Core runtime A* 검증을 대신하지 않는다.

## 측정 시간과 남은 약점

이번 재시도 측정: **2026-10-03T21:22:24Z–2026-10-03T21:38:05Z, 941초 / 0.261 lane-hours**. 읽기·생성 대기·크로마 제거·픽셀/manifest 보정·검수·검사를 포함한다. 이전 prep **672초 / 0.187 lane-hours**와 합치면 **0.448 lane-hours**다. 마지막 NOTES/timing 기록과 최종 응답 몇 초는 측정에서 제외했다. 성공한 생성 10회는 전체 lane 시간 안에 포함되며 중복 가산하지 않았다. 이미지 열람은 raw 생성 10개 + reference2개 + review/fixture composite2개 = **14개**다.

Hero와 1×/3×로 나란히 봤다. 선반 책 배열/띠와 rear wood grain이 반복적이며, 정수기 cream 면은 보정 후 조금 평평하다. 작은 기기의 spout/spigot/service grille은 1×에서 읽기 어렵고, rear coffee의 돌출 형태는 front와 완전히 자연스럽게 이어지지 않는다. Hero의 더 풍부한 공간감/매력과 같다고 주장하지 않는다.

Fixture에는 seated character가 없다. 따라서 dev-hoodie pilot의 back-head ~42% visibility, 실제 손/컵이 추출구·꼭지에 닿는 동작, 기계적인 보행을 개선하거나 검증했다고 주장하지 않는다. 실제 actor/walker occlusion, 모든 shelf 끝 통과, WPF integer-DPI/alpha picking/animation, runtime pack 소비와 사람의 시각 승인도 **미검증**이다. 이 lane은 캐릭터·다른 가구의 수용을 대신 승인하지 않는다.

프로젝트 쓰기는 이 utility 디렉터리 안에 한정했다. src/tests/docs/다른 lane/flat/v1/pack/build_pack을 바꾸지 않았고, git add/commit/stash/checkout, 앱 build, full dotnet test는 실행하지 않았다. 기본 설정 저장과 curation UI도 하지 않았다.

