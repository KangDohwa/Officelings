# ISO v3 착석 가림 수정 기록

## 의자 축소와 허벅지 깊이 수정 (lane/chairslim)

기준은 `beb346a`다. 기존 프레임별 z-buffer 분할에서도 큰 방석이 몸 밖으로 튀어나왔고, 골반 z12에서 무릎 z8로 내려가는 허벅지 프록시가 방석 아래를 통과했다. 실제 캐릭터 픽셀을 바꾸지 않고 의자와 착석 하체 프록시를 함께 수정했다.

방석의 i/j 범위는 각각 -2.1..2.1이다. 두 축의 길이가 7에서 4.2로 **40%** 줄었고, 네 모서리를 깊게 깎았다. 두께는 z10.5..12의 1.5px이다. 등판은 i=-4..-3.5, j=-2.75..2.75로 폭을 8에서 5.5로 줄였다. 등판을 몸 쪽으로 옮기면 NE/NW의 머리 경계가 새로 가려져 기존 앞뒤 위치를 유지하고 얇은 금속 연결대를 넣었다. 팔걸이 패드는 i=-1.5..1.5, j=±2.9±.3, 지지대는 i=-1.25..-.75, j=±2.9±.25다. z13 패드와 기존 5개 캐스터·베이스·그림자는 유지한다.

착석 골반 타원체는 중심 (0,0,14), 반경 (2.4,2.5,2)이고, 허벅지 중심선은 hip→knee 모두 z13.2다. 허벅지 아래쪽은 z12.1, 골반 아래쪽은 z12에 닿는다. 서 있는 프록시는 종전 값이다. **S0 방석 12 / 등판 상단 21, (16,44) 골반 접점, phaseContacts, 손 앵커, 의자 (64,80), 1×1 footprint, 방향당 세 파트와 -20/20 rank는 그대로다.**

### 측정 정의와 전후 수치

`tools/check_iso_chair_fit.py`는 실제 알파 실루엣과 저장된 세 파트를 읽는다. lap/thigh ROI는 원본 골반 접점의 y-2..y+6(양끝 포함)이며 정강이·발을 제외한다. 이 행마다 몸의 최소/최대 x 밖에 있는 **보이는 방석** 픽셀의 수와 최대 좌우 돌출 길이를 측정한다. 방석은 front-01뿐 아니라 rear에 있는 동일한 물리 방석 표면도 포함한다. 별도로 front-01이 ROI의 몸 픽셀을 덮는 수를 센다. 전환 위상별 수치는 JSON에 보존하며 높이 z12인 typing/thinking/sleep에 합격 기준을 적용한다.

| 방향 | 착석 프레임 | 좌우 최대 돌출 전→후 (1x px) | 몸 밖 방석 픽셀 전→후 | 앞 립이 덮는 lap/thigh 픽셀 전→후 |
| --- | ---: | --- | ---: | ---: |
| SE | 54 | (9,9) → (2,1) | 3,458 → 34 | 2,749 → 0 |
| SW | 54 | (10,9) → (2,1) | 3,467 → 34 | 2,604 → 0 |
| NE | 72 | (1,10) → (0,1) | 1,494 → 12 | 2,471 → 0 |
| NW | 72 | (11,0) → (2,0) | 2,092 → 24 | 1,969 → 0 |

6인 typing/thinking/sleep 252프레임의 앞 립 가림 합계는 **9,793→0px**다. 방석과 겹치는 lap/thigh 픽셀을 실제 화면 ray로 역투영한 z가 12 미만인 샘플은 **8,823→0개**다. 같은 ROI의 최대 프록시 외삽은 SE 2.2361→1px, SW 5→1px, NE/NW 4→2px다. 가구와 무관한 머리·옷까지 포함한 배포 팩 전체 최대 외삽 13.8924px는 그대로다. 머리 상위 18 source rows와 타이핑 손 앵커 ±1 ROI에서 새로 숨긴 픽셀은 **0개**다. 전후 ROI bit mask를 비교하므로 같은 개수의 다른 픽셀이 숨는 경우도 거부한다.

캐릭터는 배포 팩의 dev-hoodie-v2(검은 곱슬머리·노란 상의), punk-girl, professor와 기존 소스 glasses-bun, hanbok-modern, red-braid를 사용했다. 별도 afro-sweater ISO 소스는 이 브랜치에 없다. 배포 캐릭터/블링크 PNG 바이트와 다른 가구·소파·TV·펫, layout은 보존한다. 추가 세 디자인은 작업 트리의 합성 fixture에만 들어간다.

방석이 몸 뒤로 완전히 가려지면 front-01 셀은 투명하다. `iso_check`는 생성된 `iso-chair-sandwich`의 모든 참조에 대해 세 파트의 ID/rank와 원본 의자 RGBA 합집합을 먼저 검사한다. 정확한 재구성이 증명된 투명 파트만 빈 셀/물리 접점 검사의 예외다. 원본 아트의 빈 셀이나 합집합의 누락·중복·색 변경은 계속 실패한다.

### 재생성 순서

완성된 팩의 manifest와 그 manifest가 참조하는 모든 파일을 먼저 준비한다. 이 레인은 기준 배포 팩 전체를 `artifacts/chairslim/before/pack`에 복사했다. 캐릭터 변경을 원하지 않는 의자 수정에서는 기본 full builder를 배포 디렉터리에 실행하지 않는다.

```powershell
python -B tools/check_iso_chair_fit.py assets/art/iso/pack artifacts/chairslim/before/fit --fixture artifacts/chairslim/before/six-pilots
python -B tools/iso_seated_geometry.py
python -B tools/build_iso_pack.py --seated-only
python -B tools/check_iso_chair_fit.py assets/art/iso/pack artifacts/chairslim/after/fit --fixture artifacts/chairslim/after/six-pilots --before artifacts/chairslim/before/fit/metrics.json --enforce --details
```

`--details`는 각 디자인/방향/자세/위상에 대해 left, right, overhangPixels, lipCoveredLap, cushionCoveredLap, lapProxyBelowTop, hiddenHead, hiddenHands 순서로 전후 수치를 출력한다. `build_iso_pack.py`는 수정하지 않았다. packv4를 병합할 때 그 레인의 완성 팩을 먼저 준비한 뒤 `--seated-only`를 다시 실행하면 새 캐릭터·장식·펫 픽셀을 보존한다. 이 레인의 옛 `pack/assets.json`을 그 위에 덮어쓰지 않는다.

전후 PNG는 `artifacts/chairslim/<before|after>/smoke/iso-demo-24.0s-3x.png`, `fit/<facing>-six-designs-2x.png`, `wpf-six/`에 있다. 빈 의자 비교는 `artifacts/chairslim/empty-before-after-6x.png`다. 모두 작업 트리의 검수 자료이며 커밋하지 않는다. 아래 seatfix의 숫자와 기존 furniture-v3 제작 PNG는 이전 형상의 기록이다.

최종 Python 전체 unittest는 **82/82**, 별도 simulator는 **9 PASS / 1 expected failure**다. 소스 ISO 검사 **509 PASS / 227 SKIP / 0 FAIL**, 배포 팩 검사 **356개 / 0 FAIL**, 오프라인 엔진 검사 **13/13**을 통과했다. 전체 솔루션 빌드는 경고·오류 0개이고 .NET 전체 테스트는 **556/556**이다. 테스트 임시 경로는 작업 트리 `artifacts/chairslim/test-temp`로 지정했다. 빈 의자의 검토된 새 RGBA 해시 네 개를 갱신했으며 파트 합집합과 머리·손 검사는 유지했다.

6인 실제 엔진은 blink 포함 **792프레임 / 의자 교차 143,758px / 책상 교차 11,556px**를 검사했다. 머리 상위 18 source rows의 최소 노출은 뒤쪽 **90.83%**, 앞쪽 **100%**이고 타이핑 손은 **100%**다. 양쪽 보행 진입·이탈은 **768프레임 / 63,291 의자 교차 픽셀**에서 일반 지면 깊이 규약을 통과했다. 이 숫자는 별도 six-pilots fixture의 결과다.

앱의 offscreen demo는 빈 방·3인·12인·24초 demo를 포함한 16시점에서 1x/2x/3x **48캡처**, 추가 보기 5개를 저장했다. 요청한 네 장면 × 세 배율의 소파·TV 영역(1x x20..184, y160..264)은 전후 RGBA가 정확히 같다. 의자 두 시트를 제외한 **42개 시트**의 PNG 바이트와 비좌석 메타데이터, 다른 가구 정의와 layout도 보존됐다. 네 방향 확대 시트와 전후 장면 PNG를 열어 확인했다.

전후 실제 WPF 확대 렌더는 각각 **576크롭**이다. 최종 의자·인물 소유 픽셀을 전체 책상 큐의 가림 순서까지 반영해 비교한 결과, 기준 **212,396px**, 수정본 **153,488px**가 정확히 일치했다. thinking/sleep을 추가한 전체 가구 픽셀 비교는 기준/수정본 공통으로 `dev-hoodie-v2-ne-thinking-0-4x`의 책상 영역 1x (66,25)에서 불일치했다. 기대 RGBA (199,130,81,255), 실제 (25,30,30,255)이며 원인은 확인하지 않았다. `review_iso_seated_renders.py --seated-only`는 의자·인물이 최종 소유하는 픽셀을 검사하고 기존 전체 비교 모드는 유지한다. 이 결과를 전체 가구 픽셀 검사의 통과로 보고하지 않는다.

남은 항목은 P2 기준/수정본 공통 책상 영역의 큐 비교 불일치, P3 작은 방석에 비해 넓게 보이는 기존 캐스터 베이스다. 의자 관련 합격 기준은 충족했다. 2D 옷의 실제 3D 두께, 임의의 연속 보행 위치에 대한 물리 z-buffer, live 사용자 세션, 다른 DPI, packv4 병합 후 전체 장면은 검증 범위 밖이다.

## 이전 구현: 기하 기반 좌석 조립 (lane/seatfix)

기준은 `39184c7`이다. 아래의 단일 front 마스크 수정 기록은 이전 구현이다. 새 구현은 의자의 각 픽셀과 인물의 대응 표면 깊이를 비교하고, 인물 프레임마다 rear / cushion / front 조합을 팩에 저장한다. 런타임에서는 레이아웃 생성 시 만든 항목 세 개를 선택한다. 프레임 중에 마스크를 만들거나 이미지 픽셀을 수정하지 않는다.

원인은 두 가지였다. 5px 두께의 큰 방석이 낮은 z21 등판을 가렸고, 방석·리프트·베이스 전체를 rank 20으로 옮겨 인물보다 뒤에 있어야 할 면까지 몸통을 덮었다. 앞뒤 교차를 모두 검사해야 한다. `y>=64` 또는 “방석 위 인물은 모두 숨김”으로 판정하면 서는 중의 몸통과 가까운 손·발도 가린다.

### 프록시와 투영

`tools/iso_seated_geometry.py`가 모델과 소프트웨어 z-buffer의 원본이다. 수평 단위는 1/16 tile, 높이는 native pixel이다. 의자 셀에서는 `P(i,j,z)=(64+2(i-j),80+i+j-z)`이며 원래 S0 투영과 같다. 카메라 깊이는 `D=i+j+2z`, 큰 값이 앞이다. `(i,j,z)+(t,t,2t)`는 같은 화면 좌표로 투영되고 D는 증가한다. 각 면을 삼각형으로 나누어 native 픽셀 중심 `(x+.5,y+.5)`에서 barycentric 깊이를 계산한다. 가장 큰 D만 남기며 동률은 인물 쪽에 둔다.

| 요소 | 모델 값 |
| --- | --- |
| 방석 | i=-3..4, j=-3.5..3.5, z10..12. 앞 모서리를 계단식으로 둥글게 한 2px 박스 |
| 등판 | i=-4..-3.5, j=-4..4, z12..21의 얇은 메쉬 슬래브 |
| 팔걸이 | i=-2..2, j=±4.5, 패드 z12..13, 지지대 z8..12 |
| 하부 | 가스 리프트 z2..10, 5개 스포크와 z0..2 캐스터. 네 축 방향 반경 6, 다섯째 끝 (4,4) |
| 골반 | 착석 z12. lower는 z20,18,15,12; raise는 역순. 원본 (16,44) 및 phaseContacts 유지 |
| 하체 | 골반 타원체, 양쪽 허벅지·정강이의 팔각 단면 캡슐, 지면 발 박스 |
| 상체 | 몸통·머리 타원체, 어깨→팔꿈치→손 캡슐. resting의 몸통 기울기 포함 |
| 타이핑 손 | (6,±.25,18), 즉 S0의 A+3/8 f와 z18. 손 볼륨 반경 (1.6,1.6,1.8) |

네 방향은 모델을 90도씩 회전한다. 의자 아트도 같은 모델의 z-buffer로 그리므로 실루엣과 깊이 표면이 일치한다. 동결 Teal/Metal ramp만 사용한다. 골반·키보드·발 접점과 의자 (64,80) 앵커를 이동시키지 않았다. **S0 방석 12 / 등판 상단 21은 그대로다.**

인물의 실제 알파가 최종 검사 실루엣이다. 원본은 2D이므로 프록시 밖의 옷·머리·손 픽셀은 화면에서 가장 가까운 프록시 표면의 깊이에 대응한다. 머리 크기와 팔다리 폭은 32×64 리그, 실제 실루엣, 고정 C/H에 맞췄다. 팔목 캡슐만으로 손을 표현하면 끝 픽셀이 무릎에 대응되므로 별도 손 볼륨을 사용한다. 모든 파일럿/프레임에서 최대 외삽 거리는 13.8924px이다. 이 숫자는 가구와 겹치지 않는 픽셀도 포함하며 물리적으로 정확한 3D 인물 복원을 뜻하지 않는다. SE/SW sleep은 기존 held-thinking 1프레임 alias이고 그 원본 자세를 사용한다. NE/NW에는 작성된 sleep 4프레임이 있다.

### 파트와 팩

방향마다 **3개**, 종전 대비 draw item **+1**이다. 그림자 수는 같다.

| partId 역할 | rank | 실제 소유 픽셀 |
| --- | ---: | --- |
| `chair__rear__<facing>__00` | -20 | 인물보다 먼 의자 표면 |
| `chair__front__<facing>__01` | 20 | 인물을 덮어야 하는 방석 몸체/앞 립 (mid 의미) |
| `chair__front__<facing>__00` | 20 | 앞쪽 팔걸이·등판·하부 등 나머지 가까운 표면 |

mid는 새 role을 추가하지 않고 계약의 `front__01`을 쓴다. 의자 128×128 원본과 모든 프레임 파트의 RGBA 합집합이 정확히 같다. 불투명 중복 픽셀은 없다. 빈 의자와 독립 보행자는 기본 세 파트를 사용한다. 보행 인물은 자신의 ordinary ground depth를 유지한다. SE/SW 책상 전면 rank 10, rear keyboard -20, 모니터 ordinary depth도 유지했다.

`iso-chair-sandwich` 시트는 중복 마스크를 재사용한다(배포 3인 팩: 222 셀). 작성 원본 `rows`는 유지하고 재생 `clips.chairFrames`에 파트 키 3개와 hidden/overlap 32×64 bit mask를 넣는다. `IsoPack`은 이 필드만 원래 해석된 클립에 붙인다. `IsoSeatChairs.cs`는 레이아웃 로드 때 destination/depth/ID/hit target을 보존한 항목과 슬롯을 캐시한다. 선택 키는 원본 row/column이며 blink도 같은 조합을 쓴다. 빈 좌석으로 돌아가면 static 기본 파트가 다시 쓰인다.

정렬에서 늘어난 비교 횟수가 enum boxing 비용을 키웠다. 새 좌석 팩에서는 이미 저장된 `IsoDrawItem.Depth` 정수 키를 비교하고 기존 ordinal owner/part 동률 규칙을 사용한다. pass/12자리 ground/rank 순서는 동일하다. 기존 소스/v2 팩은 종전 비교기를 쓴다.

### 재생성·검증

현재 소스 캐릭터 6개 normal/blink PNG는 배포 팩과 다르다. 좌석 수정 중 캐릭터까지 교체하지 않도록 다음 명령을 사용한다. 기본 full build는 소스 전체를 새로 통합할 때 사용한다.

```powershell
python -B tools/iso_seated_geometry.py
python -B tools/build_iso_pack.py --seated-only
python -B tools/test_iso_seated_geometry.py
python -B -m unittest discover -s tools -p test_build_iso_pack.py
dotnet run --project tests/Vibedotcom.IsoChecks -- --seated-pack artifacts/seatfix/five-pilots
dotnet run --project tests/Vibedotcom.IsoChecks -- --render-seated assets/art/iso/pack artifacts/seatfix/review-after
python -B tools/review_iso_seated_renders.py artifacts/seatfix/review-after assets/art/iso/pack
```

Python geometry 4/4, 기존 pack 25/25, offline ISO 12/12, 관련 xUnit 36/36 통과. 소스 전체 checker는 234 PASS / 105 SKIP / 0 FAIL이다. 테스트는 합성 데이터만 사용했다. 5인 fixture는 배포 3인의 픽셀과 glasses-bun / hanbok-modern 소스 픽셀을 사용한다. glasses-bun은 원본에 catalog record가 없어 fixture에서만 명시적으로 연결하며 배포 roster는 3인 그대로다.

5인 × 4방향 × 6자세의 실제 저장 프레임 450개에서 새 z-buffer와 팩 마스크가 일치한다(105,818 교차 픽셀, 이 중 63,426 must-show). 실제 엔진은 blink까지 **660프레임**, 의자 교차 **162,866px**, 책상 교차 **9,628px**를 검사했다. 인물/의자 전체를 한쪽으로 그리는 두 mutation을 모두 거부한다. 독립 보행 진입/이탈 **640프레임 / 74,159 교차 픽셀**은 지면 깊이 규약으로 검사했다. 보행 검사는 일반 깊이 규약 검사이며 임의의 연속 보행 위치에 대한 3D z-buffer 증명은 아니다.

머리 상위 18 source rows의 보수적 ROI에서 최소 노출 NE/NW **90.83%**, SE/SW **100%**다. 기존 3인 idle-head ROI 검사는 NE/NW **91.18%**, SE/SW **100%**다. 실제 타이핑 손 픽셀의 노출은 **100%**다. 각 ROI의 정의는 테스트 코드에 있으며 두 머리 분모를 혼동하지 않는다. lower/raise의 지면 발 접점 및 착석 끝점의 연속성 검사도 통과했다.

12인 동일 typing 프레임의 composition 500회 × 15회 중앙값: 전 **488.833ms / 137,328,000B / 576 items**, 후 **473.192ms / 8,592,000B / 588 items**. 이는 Stopwatch 경과 시간과 스레드 할당량이며 CPU 사용률 측정은 아니다. event-driven idle 10,000회는 **0B**, 기존 WPF 무변경 revision 검사도 **0B / 0 redraw**다. 새 atlas의 RGBA decode 공간은 2048×1792×4=14MiB이며 팩 로드 때 한 번 필요하다. 장시간 메모리/RSS는 측정하지 않았다.

전체 ISO xUnit 중 v2 소스 12인 golden 12개는 기준 상태에서도 실패했다. 중간 전체 실행은 112 PASS / 16 FAIL이었으며 그중 새 binding 관련 4개는 수정 후 관련 36개 재실행에서 모두 통과했다. 전체를 최종 재실행한 결과로 합산하지 않는다. v2 golden의 기대 hash는 바꾸지 않았다.

### PNG와 병합 주의

`artifacts/seatfix/review-before/`와 `review-after/`에는 각 방향의 typing/sit-down/stand-up 4× board 12개, transition strip 2개, `twelve-agents-2x.png`가 있다. 각각 144개 개별 크롭이다. `review-five/`에는 5인 240크롭과 같은 board/strip이 있다. 실제 WPF 큐와 최종 불투명 픽셀 **528크롭 / 1,533,571 native texels**가 일치했다. `comparisons/<nw|ne|sw|se>-before-after-4x.png`는 행 typing/sit/stand, 열 before 0/3 → after 0/3이다. PNG는 렌더 후 열어 확인했으며 artifacts는 커밋하지 않는다.

공유 엔진 수정은 `IsoAssets.cs`의 선택적 ChairFrames 필드, `IsoPack.cs`의 해당 필드 연결, `IsoSceneBuilder.cs`의 partial/cache/선택/정렬 호출뿐이다. 일반 가구 순서 로직은 새로 정의하지 않았다. depthsort 레인은 Build의 정렬 호출을 병합할 때 이 정수 비교기의 기존 tie 규칙을 보존해야 한다. source chair 6파일, 팩 manifest와 두 새 hash PNG를 함께 반영한다. 다른 레인의 팩을 먼저 병합한 뒤 `--seated-only`로 다시 만들면 그 팩의 캐릭터/다른 가구 픽셀을 보존한다.

S0가 낮은 등판을 요구하므로 헤드레스트 같은 높은 실루엣은 없다. 2D 옷/머리의 실제 3D 두께, arbitrary walker per-pixel 물리 깊이, live 사용자 세션, 다른 DPI/native 소비자, depthsort와 병합 후의 전체 장면은 검증하지 않았다. 최초 v3의 `geometry-chair-v3.json`, measurements/review PNG와 helper 기록은 이전 5px 방석의 자료이며 현재 oracle 원본이 아니다.

## 이전 단일 front 마스크 수정 기록

기준 커밋은 `4a958e0`, 작업 브랜치는 `lane/occlusion`이다. 실제 `IsoPack.LoadDefault` → `IsoSceneBuilder.Build` → `IsoView` → WPF offscreen 경로에서 합성 데이터를 사용했다. 책상 (3,3), 계약상의 의자 위치와 방향, 세 파일럿을 조합했다. 결과 PNG와 큐 덤프는 작업 트리의 `artifacts/occlusion/`에만 보존하며 커밋하지 않는다.

A는 재현됐다. NE/NW 의자의 방석 전체와 리프트·베이스가 `chair__rear__<facing>__00`에 들어 있어 착석 골반·허벅지·바지/부츠·신발 밑창이 그 위에 그려졌다. 등판 자체는 이미 `chair__front__<facing>__00`이며 정상적으로 몸통을 덮었다. 방석과 등판의 접합부를 rear에 넣은 분할이 하체 관통을 허용했다.

## 실제 순서와 픽셀

책상 (3,3)의 좌석 ground depth는 인물 NW=7, NE=8, SW=5, SE=6이다. `AddFurniturePart`는 의자의 `DeskId`로 책상 좌석을 찾고, 파트의 `seatGroupId=seat`가 있으면 그 ground depth를 쓴다. `AddActor`도 seated/lower/raise에서 같은 값을 쓴다. 이후 `IsoDepthKey`는 pass → ground depth → rank → instance ID → part ID 순서로 정렬한다. rear=-20, actor=0, desk front=10, chair front=20이다. WPF는 이 혼합 큐 순서를 그대로 유지한다. 이번 오류는 rank가 다른 rear와 actor 사이에서 발생하므로 ID 동률 정렬이나 캐릭터 셀 앵커 오류가 아니다.

아래는 타이핑 각 위상 0~3에서 동일하게 발생한 픽셀 수다. 좌표는 **캐릭터 32×64 셀의 로컬 좌표**이며 하체 ROI는 y≥43이다. 방석에는 골반/허벅지와 바지·부츠가, 베이스에는 신발 밑창이 겹쳤다. 개별 좌표 전부는 `before/measurements.json`의 `actorAboveFurniture` 배열에 기록했다.

| 인물 | 방향 | 방석 위 인물 픽셀 | 베이스 위 인물 픽셀 | 좌표 범위(양끝 포함) | 수정 후 |
| --- | --- | ---: | ---: | --- | ---: |
| dev-hoodie-v2 | NW | 147 | 3 | x4..23, y43..56 | 0 |
| punk-girl | NW | 127 | 5 | x6..22, y43..56 | 0 |
| professor | NW | 125 | 4 | x8..22, y43..56 | 0 |
| dev-hoodie-v2 | NE | 151 | 3 | x9..28, y43..56 | 0 |
| punk-girl | NE | 131 | 5 | x11..25, y43..56 | 0 |
| professor | NE | 127 | 5 | x10..25, y43..56 | 0 |

예를 들어 NW hoodie의 (9,43)은 의자 셀 (57,67)에 겹친다. 원래 해당 의자 픽셀의 rank는 -20이므로 actor/body가 최종 소유자였고, 수정 후에는 rank 20인 chair front가 소유한다. 이미 front였던 하체 겹침은 NW 3px, NE 1px이며 원래도 정상적으로 가려졌다.

B의 책상 전면·다리 관통은 재현되지 않았다. SE 인물은 `desk__front__nw__04`의 좌석 fascia가 하체 19/17/12px(hoodie/punk/professor)을 덮고, ordinary front `__05`가 추가 1px을 덮는다. SW 인물은 ordinary depth=8인 `desk__rear__ne__04`가 depth=5인 인물의 하체 10/10/11px을 덮는다. 물리 부품의 이름이 rear여도 ordinary 지면 깊이가 더 크면 앞에 그려진다. 수평 상판·키보드 `desk__rear__ne/nw__14`의 인물 겹침은 -20 rear 계약에 따른다. 해당 수평면까지 앞에 올리면 손을 가리게 된다. SE/SW의 전체 144개 렌더는 수정 전후 RGBA가 동일하다.

## 이전 정적 측정과의 차이

`chair-v3-build-review.py`는 v2 책상과 고정 actor snapshot을 사용한다. `measure-A3-rev2.py`의 `parts`는 seat depth/rank와 owner/part ID로 정렬하므로 핵심 좌석 순서는 실제 큐와 같다. 그러나 의자 검사의 조건은 `cushionOverPelvisThighs == 0`, 즉 **방석이 인물 하체를 가리지 않아야 한다**였다. 이번 신고에 필요한 반대 조건을 검사하지 않았다. 머리·손 노출만 통과해도 하체 관통은 남았다. 책상 v3 측정 역시 v2 의자를 사용해 이 조합을 검사하지 않았다.

현재 체크아웃에는 이전 측정의 `runs/.../head-*`, `idle-native` 입력이 없어서 기존 head ROI의 분모까지 재실행할 수 없다. 새 검사는 커밋된 idle 시트의 머리 영역과 작성된 머리 이동량을 사용한다. 이 ROI에서 타이핑·전환 144프레임의 머리 노출 최솟값은 NE/NW 91.18%, SE/SW 100%; 타이핑 피부색 손 ROI는 48프레임 모두 100%다. 동일 ROI의 수정 전후 visible/total이 모두 같음을 별도로 확인했다. 기존 정적 측정의 머리 분모와 같다고 주장하지 않는다.

## 수정과 재생성

NE/NW의 작성된 물리 방석·가스 리프트·바퀴 베이스 전체 마스크를 기존 rear-00에서 front-00으로 옮겼다(NE 549px, NW 547px). far arm은 rear에 남겼다. 인물 모양의 구멍이나 임의의 y 띠를 만들지 않았고 새 파트 ID도 필요하지 않았다. 빈 의자 RGBA 합집합과 네 방향의 외형, 앵커, 지면 키, rank, 좌석 소유권, 그림자는 동일하다. rear의 기존 cushion 접점이 이동했으므로 물리 anchorChecks만 far arm 접점 NE(59,62), NW(69,62)로 갱신했다.

`tools/split_iso_chair_occlusion.py`는 기존 제작 helper의 기하 마스크를 메모리에서 만들고 PNG/manifest 두 파일만 수정한다. 이미 수정된 입력에서는 이동이 0px이며 결과가 같다. 원래 미술 helper로 재생성할 때에는 다음 순서를 지켜야 한다.

```powershell
python -B tools/split_iso_chair_occlusion.py
python -B tools/build_iso_pack.py
```

`build_iso_pack.py`와 엔진 변경은 필요하지 않았다. 기존 assembler가 수정된 source sheet와 접점을 그대로 팩에 반영한다. source chair PNG/manifest와 `pack/assets.json`, 새 hash sheet를 함께 병합해야 한다. 다른 레인의 팩 변경과 충돌하면 수정된 source들을 먼저 병합한 뒤 팩을 재생성한다.

## 검증과 크롭 위치

- 수정 전 새 하체·전환 회귀 검사 2개가 실제 관통 좌표에서 실패했고, 수정 후 통과했다.
- offline ISO 검사 8개 통과. 새 검사는 타이핑 48프레임/3,720 교차 픽셀, sit/stand 96프레임 및 보행 깊이 36사례, 네 빈 의자의 원본 RGBA SHA, 머리·손 144프레임을 확인한다.
- Python pack 검사 25개 통과. 팩 제작 검사 234 PASS / 105 SKIP / 0 FAIL.
- 수정 전후 각각 288개 WPF 4× 렌더. topmost opaque 인물/가구 픽셀을 실제 큐 합성과 대조해 모두 일치했다. 실제 navigation 마지막 cardinal 구간의 보행 진입·이탈 합계 96개 렌더를 전후에 각각 추가했다.
- 기존 전체 xUnit 실행은 495개 중 479 PASS / 16 FAIL. Tracker 미빌드에 따른 15개는 빌드 후 재실행에서 통과했고, 새 회귀 검사 3개도 최종 재실행에서 통과했다. 합산 최종 확인은 494 PASS / 1 FAIL이다(전체를 다시 실행한 결과가 아님). 남은 `IsoPresentationModesTests.MovingBodyAndLabelArePickedAgainAtTheStationaryPointer`는 v2 source fixture의 `AnimationTickCount > 0`(159행)에서 두 번 실패했다. 범위를 넓혀 타이머 코드를 수정하지 않았다.

`artifacts/occlusion/before/`와 `after/`의 `<nw|ne|sw|se>-typing-board.png`, `-sit-down-board.png`, `-stand-up-board.png`는 세 인물 × 네 위상을 4×로 모은 크롭이다. 원본은 `<character>-<facing>-<action>-<phase>-4x.png`다. 해당 폴더의 `queue.json`은 실제 정렬된 항목, destination, depth, source frame을 기록하고 `measurements.json`은 모든 하체 교차 좌표를 기록한다. navigation 보행 자료는 `walk-before/`, `walk-after/`의 `-walk-in-board.png`, `-walk-out-board.png`다. 착석·기립에서 발의 바닥 접점과 최종/초기 seated 픽셀의 연속성도 검사했다.

소유자의 실제 세션·설정, live 창의 장시간 재생, 다른 DPI 모니터, native 소비자는 검증하지 않았다. 의자 외형 밖에 남는 신발 픽셀과 전환 중 far arm 뒤/앞의 정상적인 가림은 그대로다. flat 코드·기존 테스트는 수정하지 않았다.
