# Iso 편집기 독립 검토 수정

`lane/edfix`, base `32900b7`(edwire 병합) 기준이다. 공개 서명과 JSON 버전, shipped art/pack, flat editor, Models/Office/OfficeView/Settings/Tracker 소스는 바꾸지 않았다. 모든 입력과 TEMP/TMP는 이 worktree의 합성 데이터다.

## 항목별 수정과 회귀 검사

`IsoEditorReviewCases`의 동일한 assertion body를 xUnit Theory와 NuGet 없이 실행하는 `Officelings.EditorChecks`가 호출한다. 아래 이름은 runner 인수이기도 하다.

| 항목 | 수정 | 검사 |
| --- | --- | --- |
| P0 | 좌석 프레임을 요청 시 바인딩한다. 병합 시 decoded mask/image/compiled clip과 파생 cache를 보존한다. 보수적인 조립체 관계 그래프의 SCC별로 모든 80개 점유 조합을 검사하고, 변하지 않은 조립체와 프레임을 재사용한다. | `P0`, `P0-cache`, `P0-equivalence`, 활성화한 U008 |
| 1 | 잘못된 명시 frame 뒤에는 픽셀 접근을 중단한다. | `R01`: -1/16 → O007, 예외 없음 |
| 2 | anchor 예산, checked 좌표, 장면과 WPF bitmap의 8192px/16,777,216px 예산을 승인·생성 전에 검사한다. | `R02`: int.MinValue/1,000,000, 개별 안전 anchor의 합산 면적, oversized static bitmap |
| 3 | 개수·4 MiB 제한을 충돌/접점/장면 검사보다 먼저 검사한다. | `R03`: 50,000개가 1초 안에 L017 하나, 1024개의 큰 ID가 byte 제한으로 L017 하나. `R03-count`: 1025개 |
| 4, 7, 12, 15 | base의 직접 생성, 실제 CommitPack, revision ack, 취소 전달을 재검증했다. | `edwire`: 실제 commands/objects/runtime/U001–U007 |
| 5 | CLI는 파일 접근 전에 Windows 이외 실행을 exit 2로 거부한다. | [비 Windows 확인 절차](iso-custom-objects.md#플랫폼-확인): 이 Windows 환경에서는 실행하지 못함 |
| 6 | linked contact 투영 전에 long으로 footprint 경계를 검사한다. | `R06`: Move/Preview의 극단 좌표 → L005, history/revision 유지 |
| 8 | 일반 part와 shadow의 Frame/Opacity를 보존하고, frame 미지정 animation의 변경 시각을 예약한다. | `R08`: 유효한 합성 2-frame body/shadow, frame 1, alpha 26, 유한한 다음 변경 시각 |
| 9 | fixed host의 위치에 해당하는 bundled module/state와 대조한다. | `R09`: plain host를 두 번째 door 또는 다른 state로 교체 → L016 |
| 10 | 숨긴 pet도 pending retreat가 끝날 때까지 다음 갱신 시각을 유지한다. | `R10`: 직원 없는 정적 방에서 숨긴 pet이 유한 deadline으로 swap 완료 |
| 11 | 새 revision의 승인된 즉시 적용을 한 번 advance하고 기존 timer를 예약한다. | `R11`: timer가 멈춘 빈 방에 plant 배치 후 즉시 장면 변경, selection은 advance 없음 |
| 13 | committed invalid draft wall을 LastValidLayout과 비교한다. | `R13`: invalid draft에 추가한 wall decor의 실제 image ghost 유지 |
| 14 | 알려진 rear face의 수평 투영으로 높이와 무관한 host edge를 선택한다. | `R14`: Project(-0.5,5,height)의 height 0/16/40/48 → edge 4.5 |
| 16 | 진단 ID에는 bounded `user/<slug>[/sheet]` 문법만 허용한다. | `R16`: 절대 경로 숨김, 정상 namespace ID 유지 |
| 17 | 검증된 catalog displayName을 palette Title/search에 사용한다. | `R17`: 실제 import 후 “Blue filing cabinet”을 `filing`으로 검색 |
| 18 | builder 생성 시 enabled seat 집합을 만들고 hot path에서 Contains를 사용한다. | `R18`: 10,000회 lookup 0 B, explicit-empty seats 유지 |
| 19 | 행렬 round-trip 검사를 실제 ScenePoint→tile/core picker 경로로 바꿨다. | `R19`: 독립 physical-pixel oracle, DPI 4종 × zoom 4종 × pan 2종 |
| 20 | U001–U007의 실제 서비스 검사를 재실행하고 U008 Skip을 제거했다. | `edwire`, `P0`; xUnit 자체 실행은 복원 차단으로 미검증 |

수정 전 source를 worktree 안에서 임시 복원하여 P0/cache와 1/2/3/6/8/9/10/11/13/14/16/17/18의 15개 검사 실패를 확인했다. 3번의 수정 전 실행은 1025개 입력으로 제한했다. 50,000개에 대한 수정 전 수십억 건 오류 할당은 실행하지 않았다. 19번은 ScenePoint에 역변환을 두 번 넣은 변형에서 실패하고 정상 경로에서 통과했다. 테스트 기대값이나 시각 임계값은 낮추지 않았다.

## 성능과 검증 보존

.NET 10.0.11, 논리 프로세서 18개, Windows Debug build다. 실제 storage/composition/merged pack의 34-object/12-seat layout에서 ClearSeat/SetSeat+Validate 100회와 서로 다른 Move Preview 100회를 실행했다. fixture-only benchmark를 대신 쓰지 않았다.

| 측정 | 수정 전 | 최종 측정 | 기준 |
| --- | ---: | ---: | ---: |
| command+validation p95 | 2236.3591 ms | 6.6827 ms | ≤100 ms |
| preview p95 | 1888.4207 ms | 10.0868 ms | ≤16 ms |
| open/closed idle | 각각 0 B/frame | 각각 0 B/frame | 0 B |
| 동일 preview | 0 B/call | 0 B/call | 0 B |
| import | 5129.7294 ms | 1400.2006 ms | 보고, hard budget 없음 |

최종 순서 제약 최적화 뒤 연속 세 번의 별도 측정도 통과했다: command p95 10.0195/12.8448/5.2051 ms, preview p95 10.7883/13.1571/11.6469 ms. 동일 머신에서도 부하와 GC에 따라 값이 달라진다.

원인은 eager chair/desk frame 바인딩, 매 검증의 전체 rig/phase 장면 재구성, 정렬의 전역 pair scan·enum 비교·mask 조회였다. PNG decode가 매 호출 반복된다는 가설은 확인되지 않았다. 기존 decoded image/mask/compiled rig 참조는 이미 공유했지만 파생 opacity/frame-change cache가 clone에서 사라졌다. 파생 cache도 보존하며, 정렬은 동일한 separating-plane/support/seat 관계를 유지한 채 broad phase와 stable priority queue를 사용한다. 관계가 0인 pair의 mask 조회는 생략한다.

조립체 bounding union과 body/contact volume으로 모든 가능한 edge를 보수적으로 포함한다. SCC 사이의 condensation graph는 DAG이므로 순환은 component 내부에만 있을 수 있다. 내부에는 원래 8개 캐릭터 × 10개 phase/frame 검사가 모두 남아 있다. 겹치는 component는 검증한 각 조립체의 정확한 프레임을 평행 이동한 뒤 같은 IsoFurnitureOrder로 다시 정렬한다. 4096 draw-item 제한은 각 phase의 정확한 count delta를 더해서 확인한다. cache는 pack/immutable manifest별이며 최대 256개 probe와 65,536개 frame entry를 보관한다. closed/idle 경로에서는 이 cache를 구축하지 않는다.

`P0-equivalence`는 기본/12개 ClearSeat/11개 승인 가능한 Move 후보, 총 24 layout × 80 = 1,920개 전체 점유 장면과 비교한다. 기존 seated ownership·접점·walker·depth oracle과 픽셀 분모/임계값은 유지한다.

Import의 별도 stopwatch 분해는 prepare 131.16 ms, consumer preflight 338.13 ms, WPF decode 139.61 ms, 재검증·게시 115.12 ms, adoption 67.86 ms, 총 약 793.51 ms였다. 새로운 merged pack의 saved/default/history/runtime 검증과 decoded sheet 준비가 주요 비용이다. Prepare/Validate는 dialog worker에서 실행되지만, 사전 검사·게시·소비자 adoption은 기존 계약에 따라 UI thread에서 동기 실행한다. 해당 commit 구간은 이 측정에서 662.35 ms였다. 불변 built-in의 WPF bitmap과 파생 cache를 identity 기준으로 더 공유하면 검증을 줄이지 않고 추가로 단축할 여지가 있다. 이 작업에서는 pointer/command 예산을 우선 해결했고 import transaction 순서는 유지했다.

## 실행 결과

- App/Tracker/CLI와 Editor/Iso/Depth/Sharing runner에 대한 7개 직접 build 명령(Core 포함 8개 프로젝트): 모두 경고 0, 오류 0. Tracker 소스는 변경 없음.
- EditorChecks: **19 통과, 0 실패**, U001–U008의 같은 assertion body 포함.
- IsoChecks: **13 통과, 0 실패**. 10,000회 idle 0 B; mixed occupied layout 48개와 기존 seated 픽셀 임계값 유지.
- DepthChecks: **8,804 checks, 0 실패**; 기본 351/source-default 49 overlap pair의 violation 0, random legal layout 100, idle 0 B.
- SharingChecks: **77 통과, 1 실패**. 실패는 합성 DPAPI roundtrip: sandbox CurrentUser DPAPI unavailable, Win32 2. 실제 사용자 profile을 로드하지 않았다.
- 전체 `dotnet build Officelings.sln`: **경고 0, 오류 1**, NU1301 (`api.nuget.org:443` 소켓 접근 거부).
- 전체 `dotnet test Officelings.sln --no-restore`: 같은 NU1301, **xUnit 실행 완료 0개**. 새 xUnit wrapper의 발견/컴파일/전체 결과는 미확인이다.
- Python `-I -B -m unittest discover -s tools -p 'test_*.py'`: **3 통과, 8 module import 오류**(11개 발견). numpy/Pillow가 없으며 worktree venv 설치도 `pypi.org:443`, socket 10013으로 차단됐다. 기존 91개 suite 통과를 이번 결과로 주장하지 않는다.
- Windows CLI: 유효 fixture exit 0, frame -1 합성 fixture O007/exit 1, **2개 확인 통과**. 비 Windows runtime, native DPI/window/tray/minimized QA는 미실행이다.

재현 시 TEMP/TMP를 `artifacts/edfix/tmp`, DOTNET_CLI_HOME을 `artifacts/edfix/dotnet`으로 지정한다. `dotnet run --project tests/Officelings.EditorChecks -- P0`는 실제 merged-pack 측정만 실행하며 인수를 생략하면 모든 19개 회귀 검사를 실행한다. 같은 case body가 xUnit `IsoEditorReviewTests`에도 연결돼 있다. Runner는 solution에 추가하지 않았다.

## 공유 파일과 병합

| 공유 파일 | 수정 member/행 |
| --- | --- |
| `src/Officelings.Core/Iso/IsoOffice.cs` | 125: Update의 hidden-pet pending retreat deadline 한 줄 |
| `src/Officelings.Core/Iso/IsoPack.cs` | 16: opaque bitset cache. 322: OpaqueBounds, 338: AlphaOverlaps, 364: NextFrameChange lock, 395: HeadTop cache |
| `src/Officelings.Core/Iso/IsoPack.Custom.cs` | 17: WithCustomObjects의 파생 cache 복사와 displayName; EditorTitle 추가 |
| `src/Officelings.Core/Iso/IsoSceneBuilder.cs` | 32: constructor의 seat 집합/checked bounds, 125: AddFurniturePart, 139: AddStatic, 149: BuildWithPets의 authored animation, 203: AddDynamic checked 좌표 |
| `src/Officelings.Core/Iso/IsoSceneBuilder.Editor.cs` | 7: EditorEntries snapshot hook, 9: enabled-seat cache, 10: EnabledSeat |
| `src/Officelings.Core/Iso/IsoSeatChairs.cs` | 11: CacheSeatChairs, 24: lazy CacheSeatChair, 84: SortEntries |
| `src/Officelings.Core/Iso/IsoFurnitureOrder.cs` | 49: constructor scratch comparer, 66: Sort의 broad phase/local chain/stable topology/metadata·mask cache |
| `src/Officelings.App/IsoView.cs` | 174: BuildStatic의 bitmap budget guard, System.IO using |
| `src/Officelings.App/IsoView.Editor.cs` | 25: revision field, 36: AttachEditor, 54: RefreshEditor, 104: PickHost |

Editing의 validator/command/object geometry/issues/palette와 App overlay, 새 scene-validation/render-budget helper, tests/CLI도 같이 합쳐야 한다. 성능 helper와 scene builder 내부 hook을 따로 cherry-pick하면 컴파일/동작 불일치가 생긴다. 공개 API 변경은 없다.

전체 xUnit/Python, 합성 DPAPI와 native UI 확인은 별도 환경에서 다시 필요하다. Windows FileBoundary의 긴 경로 제한은 base에도 있었다. 짧은 합성 root의 import는 통과했고 긴 root의 hashed PNG는 OpenRead 실패를 재현했다. 이번 검토 범위에서는 FileBoundary를 변경하지 않았다. Snapshot/cache와 shared sorter hunk가 다른 lane의 depth/render 수정과 겹칠 수 있으므로 member 단위로 병합한다.