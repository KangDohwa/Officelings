# P1 공용 소품 — 수치 통과, 세 rig 전체 접점 승인은 미완료

파일: `carried-props.png` 128×128, 32×32 셀; `carried-props.iso.json`. cup/water-cup/phone/report-paper 네 종류 × SE/SW/NE/NW 네 방향이다. source grip (16,16); actor grips SE (24,44), SW (8,44), NE (24,40), NW (8,40). 실제 잡는 rim/edge는 anchorChecks radius0로 확인했다. 소품만 있고 팔·손·몸체·그림자는 없다. 기존 몸체 픽셀도 수정하지 않았다.

제작: GPT component row 한 번을 sprite-gen 성분 추출했다. 각 다른 물체의 master를 최초 한 번만 6×6, 5×6, 4×8, 6×10으로 정리한 뒤 고정 셀/그립에 배치했다. S0 재료 ramp의 정확한 12색, 잡는 쪽별 손잡이 변형, 상단 왼쪽 빛 재칠, phone rear panel과 무인쇄 report rear를 베이크했다. 읽을 수 있는 글자는 없다. runtime flip/회전이 필요 없다. actionBindings와 handAnchorsByPhase 사용 규칙은 adapter 선언이며 실행 코드가 아니다.

검토: `carried-props-review-1x3x.png`, `carried-props-rig-review-1x3x.png`. dev-hoodie v2와 현재 punk-girl의 idle phase0, 네 방향 × 네 물체(각 16조합)를 별도 48px 검토 캔버스에 붙여 셀 밖 소품을 자르지 않았다. 지정 손 위치 1px 안에 기존 몸체 alpha가 있다. `runs/p1-props-20261004/rig-contact-proof.json`에 source hash와 범위를 남겼다. alpha 존재 검사만으로 피부/손 모양의 정확한 grip을 증명하지 않는다.

**professor SKIP**: 검토 시 완성 iso manifest/body가 없어 합성할 수 없었다. 다른 캐릭터로 대체 승인하지 않았다. 세 rig 전체 수용 조건은 아직 완료되지 않았다. pickup/coffee/phone/handover 전 phase별 손 경로, 움직이는 손의 grip, 원본 body의 baked prop 중복 여부, runtime draw order/clip bounds와 앱은 미검증이다.

checker: `tools/iso_check.py assets/art/iso/props --tolerance 0` — **PASS 5 / SKIP 3 / FAIL 0**, `validation.txt`. floor/furniture/blink 적용 제외 SKIP과 professor rig SKIP은 별개다. 고양이와 checker 합계는 **PASS 15 / SKIP 9 / FAIL 0**이며 시각·접점 전체 승인은 아니다.

약점: 6px 컵 손잡이와 커피/물 차이는 1×에서 미묘하고, 휴대폰 뒤쪽은 어두운 작은 판으로 보인다. 보고서의 모서리/ink는 최소 표시 수준이다. dynamic hand가 검증되기 전 coffee/phone 동작 품질을 주장하지 않는다.

시간은 단일 P1 lane **0.3788 lane-hours / 22.73분**에 포함되며 두 deliverable에 중복 가산하지 않았다. 직접 저장소 쓰기는 pets/props/만; 다른 lane/src/tests/docs/pack/git 쓰기, dotnet 테스트 및 앱 빌드는 없음. 재현은 sprite-gen venv Python으로 `runs/p1-props-20261004/build_props.py`를 실행한다. 원본·frames·request·최초 크기 기록은 runs에 있다.
