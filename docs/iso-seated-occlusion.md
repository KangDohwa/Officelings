# ISO v3 착석 가림 수정 기록

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
