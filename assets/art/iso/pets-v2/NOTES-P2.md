# ISO PETS v2 — 시바와 황금색 햄스터

`dog-normal.png`과 `hamster-normal.png`은 각각 128×288, 32×32 셀의 9행·36칸이다. 행은 idle SE/SW/NE/NW, walk SE/SW/NE/NW, curled sleep any이며 행마다 네 위상이다. idle·sleep은 1fps, walk는 8fps, 모두 loop다. 몸체 앵커는 고양이와 같은 (16,28). 서 있는 시바는 17–18px, 햄스터는 15–16px이고, 모든 칸의 접지는 y28, 아래 y29–31은 투명하다.

각 동물의 `.iso.json`은 고양이의 시트·펫 구조를 유지한다. `identityPalette`, 행별 실제 발/수면 접지 `anchorChecks`, `blockedCells:[]`, `blocking:false`, 독립적인 바닥 i+j 깊이와 별도 shadows 패스를 포함한다. `*-shadow.png`은 512×1152, 128×128 셀의 같은 9행·36위상이며 앵커는 (64,80)이다. 그림자 색은 고정 navy #111924, 알파는 0/48/96이고 몸체 알파는 0/255다. 그림자의 마름모 대각선은 2:1, cast 이동은 고양이와 같은 (+6,+3)이다.

정체성은 `assets/art/pets/shiba/shiba.png`과 `assets/art/pets/hamster-ball/hamster-ball.png`에서 확인했다. 시바의 황갈색 털, 크림색 주둥이와 발, 선 귀, 말린 꼬리와 햄스터의 황금색 등, 크림색 볼, 둥근 귀를 유지했다. 사용한 identity 7색/8색은 해당 평면 원본의 정확한 RGB다. 햄스터는 요청된 네 다리 보행을 보여 주기 위해 공 밖에 있다. 평면 명단의 유리 공은 포함하지 않으며, 공에서 나오는 전환도 없다.

내장 `image_gen`으로 동물당 원화 한 장씩 순차 생성했다. 두 원화 모두 첫 시도이며 생성 동시성은 1이다. `dog-master.png`, `hamster-master.png`과 실제 프롬프트 `master-prompts.txt`를 보존했다. 원화는 SE 보행 네 자세, NE 보행 네 자세, 수면 네 자세의 4×3 구성이다. 알파 성분을 각 셀에서 확인해 경계를 추출하고 native 격자로 축소한 뒤 정확한 팔레트와 이진 알파로 정리했다. 추출 좌표는 `*-extraction.json`에 있다. 생성 도구가 자체 관리하는 Codex generated_images 캐시는 기본 위치에 생겼으며, 에이전트가 직접 쓴 프로젝트 파일은 이 새 출력 디렉터리 안에만 있다.

최종 보행은 선택한 생성 머리·몸통에 국소 픽셀 관절을 조립했다. 생성 원화의 네 발 자세를 그대로 채택했다는 뜻은 아니다. A 대각선 발 쌍의 신장→통과→B 쌍의 신장→통과로 네 위상을 만들고, 무릎·발끝 위치를 개별 변경했다. 몸통 bob은 0/−1/0/−1px이다. 시바는 생성된 말린 꼬리 성분을 0/+1/0/−1px 움직인다. SW/NW는 x′=32−x를 정수로 베이크하고 화면 왼쪽 위 광원을 다시 칠했다. 수면은 한 curled 원화의 등 위쪽만 들썩이며 턱·발 접지는 유지한다. 걷기 관절과 접지 기록은 `*-gait-contacts.json`, `*-contact-proof.json`에 있다.

`*-review-1x.png`, `*-review-3x.png`, `*-review-1x3x.png`은 고양이와 새 펫, 평면 정체성을 나란히 놓았다. `*-vs-identity-1x3x.png`은 hero 전체와 고양이·시바·햄스터 보행을 함께 보여 준다. `*-workstation-fixture.png`, `*-workstation-review-1x3x.png`은 기존 v2 책상·의자 파트의 네 방향에 독립 바닥 깊이로 배치한 정적 합성이다. `*-motion-3x.gif`은 4초 루프이며 120/130ms를 번갈아 써 보행 평균 8fps, idle·수면 1fps를 유지한다. `walk-loop-review-1x8x.png`은 2→3→0→1 연결, `shadow-review-2x.png`은 몸체와 그림자 정렬을 보여 준다. 열람한 이미지 출력은 총 11개였다.

검증 명령: `tools/iso_check.py assets/art/iso/pets-v2 --tolerance 0`. 결과는 **PASS 20 / SKIP 12 / FAIL 0**, 원문은 `validation.txt`다. SKIP은 이 펫·그림자에 적용되지 않는 floor seams, furniture part names, character blink 검사다. 추가 검사로 72칸 접지·여백·연결성과 서 있는 64칸의 높이, 평면 원본 색 provenance, 여덟 보행 행의 서로 다른 네 다리 위상과 3→0 픽셀 연속성을 확인했다. 보행 32위상 모두 실제 y28 sole를 확인했다. `*-additional-checks.json`과 `*-contact-proof.json`이 증거다. 보호된 고양이·평면 원본·hero·규격·checker의 해시는 `source-hashes.json`과 같았다.

작업 시간: 단일 lane의 첫 UTC 관측 2026-10-04 07:43:48부터 최종 기록까지 측정했다. 실제 종료 시각·초·lane-hours는 `lane-receipt.json`과 아래 기록에 있다. 조사, 순차 생성 대기, native 정리, 보행 수정, 검수와 NOTES 작성까지 포함하며 동물별 시간을 임의로 나누지 않았다.

약점: 32×32에서 원화의 부드러운 털과 귀 안쪽은 거친 클러스터로 줄었다. 시바 꼬리의 말림은 1×에서 몇 픽셀만 보이고, 햄스터의 발은 작아서 상하 운동이 다리 관절보다 먼저 보일 수 있다. 고정된 몸통에 작은 관절을 붙인 짧은 보행이라 어깨·척추의 회전은 제한된다. 수면은 두 상태의 호흡을 네 칸에 저장했다. NE/NW는 후면 사선이지만 머리 옆면 일부가 보인다. 픽셀·접지·루프 검사를 통과했으나 자연스러운 실시간 이동 속도나 WPF 렌더러의 재생·가림을 인증하는 결과는 아니다. pack 및 runtime 통합은 하지 않았다.

재현: 기존 sprite-gen venv의 Python으로 새 helper `pets_v2_native_20261004.py`를 `-B` 옵션과 함께 실행한다. 모든 쓰기는 helper와 같은 이 디렉터리로 제한되며 이미지 생성·업로드·pack·git 작업은 호출하지 않는다. docs/src/tests/tools/기존 art/pack 수정, git add/commit/stash/checkout은 수행하지 않았다.

실측: 2026-10-04 07:43:48 UTC → 2026-10-04 08:00:50 UTC; **0.283889 lane-hours / 17.03분**. 단일 lane이므로 같은 구간의 경과 시간과 같다. 종료는 최종 기록 파일 저장 직전 UTC 관측이며 기록 자체의 수 ms는 별도 계측하지 않았다.
