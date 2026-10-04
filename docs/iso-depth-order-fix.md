# ISO 가구 깊이 순서 수정

통합 기본 배치의 알파 겹침 334쌍에서 85건, 소스 기본 배치의 50쌍에서 1건을 재현했다. 책상 파트의 단일 `i+j` 키가 다른 책상·의자의 footprint 선후 관계를 뒤집었다. 일부 구조 파트의 앵커는 1×2 또는 2×1 본체 밖으로 한 타일 더 나갔다. 같은 책상의 지지면과 모니터/소품도 독립 ground 키만으로는 지지 관계가 보장되지 않았다.

`IsoFurnitureOrder`는 알파가 겹치는 가구의 footprint와 z 범위에서 분리 평면 제약을 만들고 안정적인 위상 순서로 그린다. 제약 없는 선택은 기존 `IsoDepthKey`를 따른다. 보행자와 고양이는 각자의 ground point를 사용한다. 정적 footprint 관계, 애니메이션 전체 프레임의 알파 마스크와 정적 겹침을 캐시하며 작업 배열을 재사용한다. `IsoOffice`의 정지 경로는 그대로이고, 합성 데이터로 1,000회 업데이트의 추가 할당 0바이트를 확인했다.

`tools/iso_depth_metadata.py`를 팩 어댑터에서 호출한다. 책상 **구조 파트만** footprint 경계로 보정하며 좌석·모니터·소품 접점을 이동시키지 않는다. 로컬 `supportRank`는 다른 가구의 순서를 바꾸지 않는다. 키보드와 마우스는 기존 `rear-14` 수평면 마스크에 들어 있다. 사이드 테이블의 컵처럼 이미 지지면과 합성된 소품은 같은 파트로 이동·정렬된다. PNG, 프레임, 이미지 해시는 변경하지 않았다.

좌석 그룹과 착석 캐릭터의 마스크·접점·isoZOrder는 수정하지 않았다. 점유된 좌석 파트는 본체 밖 접근 접점에서 기존 S0 평면을 사용한다. 연결된 책상/의자는 별도의 독립 사각형으로 좌석 내부 순서를 덮어쓰지 않는다. 이 부분을 바꾸는 `lane/occlusion`과 병합할 때에는 `IsoSceneBuilder.Build`의 마지막 정렬 호출과 점유 좌석 평면 처리를 함께 확인해야 한다.

## 재현과 검증

모든 명령은 이 worktree에서 실행한다. 기본 팩, 고정 시드 74129, 합성 캐릭터만 사용한다.

```powershell
dotnet run --project tests/Vibedotcom.DepthChecks -- artifacts/depthsort-after --compare-audit artifacts/depthsort-before/audit.json
dotnet run --project tests/Vibedotcom.IsoChecks
dotnet test tests/Vibedotcom.Tests/Vibedotcom.Tests.csproj --filter "FullyQualifiedName~Iso"
python -B tools/test_iso_depth_metadata.py
dotnet src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.dll --view iso --demo --smoke-test artifacts/depthsort-smoke-after --home artifacts/depthsort-synthetic-home
```

독립 오라클은 엔진의 정렬 함수나 숫자 Depth를 기대값에 사용하지 않는다. 실제 알파 겹침을 전수 조사하고 tile-center footprint와 S0 z 범위의 분리 평면을 비교한다. `audit.json`에 ID, 위치/회전, footprint, 키, 기대/실제 최상단과 크롭 범위를 기록한다. 각 위반은 3배 PNG로 저장한다. 이전 보고서를 지정하면 같은 영역의 수정 후 크롭도 저장한다. `--pack DIR --legacy-order`는 지정 팩에 대해 기존 단일 키 순서를 재현한다.

수정 후 두 기본 배치의 위반은 0건이다. 독립 검사 8,804건에는 합법적인 가구 조합 배치 540개, 네 책상 방향/세 모니터 상태/네 프레임의 지지 관계 7,368쌍, 가구 양쪽 및 양 끝의 보행자·고양이 알파 겹침 745건, 무작위 합법 배치 100개, 입력 순서를 뒤집은 결정론 검사가 포함된다. 원본 팩과 기존 단일 키 순서로 동일 검사를 실행하면 5,933개 실패 기록이 생긴다. 기본 배치 외의 실패도 `fixture-violations.json`과 `failures.txt`에 기록한다.

기존 오프라인 ISO 검사 5개와 메타데이터 Python 검사 3개가 통과했다. 관련 xUnit 127개는 병렬 실행에서 126개 통과, UI 타이머 검사 `MovingBodyAndLabelArePickedAgainAtTheStationaryPointer` 1개 실패였다. 이 검사는 정렬 엔진을 쓰지 않는 `FakeIsoOffice`를 사용하며 단독 실행에서는 통과했다. 통합 smoke는 합성 홈 데이터로 1×/2×/3× 렌더 48개를 생성했다.

## S0 변경과 한계

S0 표의 “Depth is ground i+j, not image bottom or z.” 문장은 유지하고, footprint와 지지 관계가 fallback key를 보완한다는 문장을 덧붙였다. split-part 설명 뒤에 한 단락을 추가해 분리 평면, `heightPx`, `supportRank`, 숫자 depth의 비단조성, 점유 좌석 보존과 앵커 보정을 명시했다. `iso-spec.md`의 깊이 절도 같은 내용으로 수정했다.

알파가 겹쳐도 양쪽 분리 평면이 서로 반대 방향이면 단일 앞뒤 관계를 단정하지 않고 기존 키를 사용한다. navigation footprint 밖의 큰 돌출이나 서로 꿰뚫는 새 아트에는 더 정확한 물리 범위 또는 파트 분할이 필요하다. 순환 제약은 관련 파트 경로를 포함한 오류로 보고하며 잘못된 순서를 조용히 선택하지 않는다. 최대 크기 사용자 레이아웃의 성능과 내부 벽/문은 검증하지 않았다. Pillow가 없는 환경이어서 전체 이미지 팩 재빌드와 기존 이미지 어댑터 Python 테스트는 실행하지 않았다. 메타데이터를 별도로 재생성하고 원본 43개 sheet의 PNG 해시와 file index 보존을 확인했다. 전체 픽셀 golden은 자동 갱신하지 않았다.
