# P1 고양이 — 수치 통과, 시각 승인은 조건부

파일: `cat-normal.png` 128×288, 32×32 셀, 9행/36셀; `cat.iso.json`; `cat-shadow.png` 512×1152, 128×128 셀. idle/walk SE·SW·NE·NW와 curled sleep any, 각 네 칸이다. 몸체 앵커 (16,28), 서 있는 실루엣 18px, 접지 y28과 아래 투명 여백을 유지했다. 정확한 9색은 flat 원본에서 추출한 identityPalette에 속한다. 비차단 `blockedCells:[]`, `blocking:false`, 자기 바닥 i+j 깊이, seatGroup 없음. 별도 그림자는 #111924, 알파 48/96이며 몸체에는 없다.

검토: `cat-review-1x3x.png`, `cat-vs-identity-1x3x.png`, `cat-workstation-fixture.png`, `cat-workstation-review-1x3x.png`, `cat-motion-3x.gif`. GIF는 4초/32프레임이고 120/130ms를 교차하여 보행 평균 8fps, 수면 1fps를 유지한다. v2 워크스테이션의 실제 파트·앵커·깊이 키로 네 방향 fixture를 합성했다. 전경 고양이는 네 방향 모두 몸체 100% 보인다. rear 위치는 방향에 따라 0–84.2% 보이며 책상 뒤쪽의 가림을 보여 준다. 의자 깊이를 고양이에 빌려 주지 않았다. 정적 소스 파트 합성이며 Core/WPF의 깊이 승인 결과가 아니다.

정체성: `assets/art/env2/normalized/cat.png`의 주황 줄무늬 tabby와 hero 왼쪽 소파 고양이를 나란히 봤다. hero를 생산 스프라이트로 잘라 쓰지 않았다. GPT로 SE 보행·NE 보행·curled sleep의 세 component-row 원화를 한 번씩 순차 생성했다. sprite-gen prepare 및 성분 추출/배경 제거를 사용했다. 고정 슬롯 자르기로 대체하지 않았다. 자동 키가 blue를 골라 첫 추출이 실패했고, magenta를 명시한 최종 kcentroid 추출은 오류/경고 없이 성공했다. 고정 팔레트와 이진 알파로 마무리했다.

파생: 생성된 머리·몸통을 고정하고 생성된 아래쪽 발 자세와 1px 꼬리 끝 흔들기를 조립했다. SW/NW는 x′=32−x를 베이크한 후 상단 왼쪽 털 가장자리를 다시 칠했다. 비대칭 액세서리는 없다. idle은 네 정지 칸을 반복하고 sleep은 한 원화를 공통 크기로 줄인 뒤 등 위쪽만 숨쉬는 두 자세를 반복한다. 보행은 행마다 서로 다른 네 자세이다. NE/NW는 귀 뒷면과 등으로 읽히며 정면 눈을 붙이지 않았다.

checker: `tools/iso_check.py assets/art/iso/pets --tolerance 0` — **PASS 10 / SKIP 6 / FAIL 0**, `validation.txt`. SKIP은 바닥 seam/가구 이름/캐릭터 blink의 적용 제외다. 추가 검사는 36셀 접지·패딩·연결성, 원본 색 provenance, 보행 행마다 네 자세, 안정된 머리·몸통 ROI를 확인했다. `additional-checks.json`, `contact-proof.json`은 runs에 있다. sprite-gen inspect-motion 네 방향은 exact/silhouette/near duplicate 0이지만 모두 `needs-review`, `stride_verified:false`다. 같은 발의 접지 구간과 고립 ROI를 지정하지 않았으므로 속도/stride를 만들거나 추정하지 않았다. GIF 수정 전후 생산 PNG/manifest의 SHA256이 같아 통과한 checker를 반복하지 않았다.

실측: 2026-10-03 20:57:53 UTC부터 2026-10-03 21:20:36 UTC까지 **0.3788 lane-hours / 22.73분**. 읽기·생성·추출 대기·보정·fixture·검증·NOTES까지 단일 lane 점유 시간이다. 첫 UTC 관측 전 최초 스킬 읽기는 제외했다. 고양이 3회와 소품 1회, 생성 동시성 1, 각 master 1회/최대 3회, 본 이미지 13개. 두 deliverable에 시간을 중복 가산하지 않았다. `lane-receipt.json`은 측정 원본이다. 승인된 생산 처리량으로 사용하지 않는다.

약점: golden tabby 정체성은 읽히지만 hero의 강한 주황색과 풍성한 털 볼륨에 못 미친다. SE/SW의 높은 카메라 각도는 약하고, 18px에서 발 구분이 작으며 고정 몸통·짧은 네 발 사이클은 다소 기계적으로 보일 수 있다. 캐릭터의 뒤쪽 머리 가림 문제가 개선됐다는 주장은 하지 않는다. 정적 합성과 네 자세를 봤고 독립적인 자연스러운 보행/루프 재생 승인은 받지 않았다.

미검증: 실제 앱, DPI, 이동 충돌·좌석 예약, alpha picking, shadow floor clipping, pack/tracker 소비자. CLI read-only guide는 홈 오류로 login probe에 실패해 현재 세션의 GPT 도구를 사용했다. 계정·설정·기본값은 바꾸지 않았다. 생성 도구 자체 관리 캐시는 기본 Codex generated_images에 생겼고 원본을 lane runs에 복사했다. 직접 저장소 쓰기는 pets/props/뿐이며 src/tests/docs, 다른 lane/v1/v2 가구, pack/build_pack, git 쓰기 및 dotnet 테스트는 하지 않았다.

재현: workspace root에서 sprite-gen venv Python으로 `runs/p1-cat-20261004/build_cat.py`, `fixture_cat.py`를 실행한다. raw 원화·canonical frames·request·추출 manifest는 runs에 있다. 재현 스크립트는 생성/업로드를 호출하지 않는다.
