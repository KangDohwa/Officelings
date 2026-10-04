# ISO wall/decor v3

출력은 이 폴더에만 있다. 기존 ENV polish를 v2 비교 기준으로 사용했다. 별도 env-v2 폴더는 없다. 기존 벽·바닥·팩은 수정하지 않았다.

- `walls.iso.json`: 원래 15개 논리 모듈, 32×64 L/R 셀 및 64×64 코너, clip/edgeAnchor/door hinge/열린 문 동작 메타데이터 유지. 새 ID에 `-v3`만 추가했다. 새 평벽과 하단 웨인스콧·몰딩 픽셀을 제공한다. trim은 기존 trim 픽셀을 대체하는 방식이다.
- `wall-decor.iso.json`: 낮/밤 큰 창, 블라인드 창, 화이트보드·스티키 노트·다이어그램, 시계, 추상화, 도시 그림, 코르크 보드, 화분 벽선반, 벽등, 스트링 라이트, LAB 문패의 L/R 쌍. 환경 sheet에 identityPalette를 추가하지 않았다.
- 큰 창은 **명시적 spanEdges:2**이다. 기존 32×64 셀 두 개를 32px/16px 간격으로 조립한다. 한 셀의 크기나 앵커를 늘리지 않는다. `decorAssemblies`에 조립 순서와 half별 edgeIndex가 있다. 두 평벽 위에 backdrop → frame → optional glow를 올린다.
- 기존 picture/clock 슬롯 (t16,v9), string 슬롯 (t16,v6) 및 hooks(6,8)/(25,8)을 유지했다. 장식은 host ground depth를 상속한다. `anchorChecks`는 상단 테두리·브래킷 나사·훅·창틀 또는 발광부의 실제 texel이다.
- `wall-glow.png`는 별도 선택 레이어다. binary alpha/정확한 팔레트라 부드러운 광원 확산을 표현하지 않는다. 문패는 닫힌 leaf에만 올리고, 열린 leaf/threshold의 기존 앵커는 그대로 둔다.
- 기본 방은 왼쪽 문 (0,4), 오른쪽 창 (4,0)을 유지한다. daylight/night/counterpart 각 1x/2x/3x 및 body-only 검수 PNG가 있다. counterpart는 양쪽 장식 방향 확인용이며 runtime layout 변경이 아니다.

도시 그림에 built-in imagegen을 1회 사용했다. 나머지 직선형 장식은 native hand-pixel masks로 충분했다. 원본 master는 production sheet가 아니다. 20×20 NEAREST 축소 후 frozen ramp로 snap하여 벽면으로 투영했다. 어떤 alpha bbox fit도 구조 기울기 계산에 사용하지 않았다.

재현: installed Pillow/numpy Python으로 `-B build_measure_wall_decor_v3_20261004.py`를 실행한다. helper는 자신의 폴더에만 쓴다. 검사: `-B tools/iso_check.py assets/art/iso/env-v3 --tolerance 0`. geometry-endpoints.json과 raster-edge-measurements.json은 endpoint 검사와 saved-alpha OLS 측정을 따로 기록한다. 곡선 시계 테두리·잎·전구 케이블·그림 내용은 바닥 평면 구조선이 아니다.

남은 약점: 48px 벽 높이 때문에 보드 글자는 기호 수준이고 도시는 작게 압축된다. 우측 면의 세부 대비는 낮다. 모든 장식을 넣은 방은 hero보다 촘촘하고, 원래 코너/열린 문 잎의 작은 형태를 유지한다. 광원은 작고 그래픽하다. hero의 회화적 주변광, 가구와의 가림, 실제 앱 배치·문 통과는 검증하지 않았다. 새 런타임 통합은 별도 선택이 필요하다.
