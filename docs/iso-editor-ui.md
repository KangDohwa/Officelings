# Iso 편집기 UI 구현과 QA

작업 기준은 `210c9fd`, 브랜치는 `lane/edui`다. 공개 계약, Core/objects 구현, 원본 팩과 다른 lane 파일은 수정하지 않았다. 이 문서는 UI 구현 범위와 통합 전에 남은 작업을 기록한다.

## 구현 범위

| 명세 | 구현 |
| --- | --- |
| §4, §8 / U003 | `IsoEditorSession`이 문서와 서비스 참조를 보관한다. Edit office/Done editing, 임시 normal 모드, click-through·borderless·투명도 복원, flat↔iso 세션 보존을 연결했다. 일반 네이티브 창 교체는 같은 office·tracker·session·sheets를 전달한다. privacy 교체는 기존 텍스트 캐시 폐기 방식과 안전한 마지막 유효 문서를 사용한다. |
| §6 / U001, U002 | Core hit/pick 위임과 handle 우선순위, Preview가 반환한 후보의 footprint·차단 셀·접점·원본 부품·shadow·opacity, 선택 handle, 유효/오류/경고의 색·아이콘·문구, drag release 1회 commit과 취소를 구현했다. 마스크와 occlusion은 Core 책임으로 남는다. |
| §8 / U004, U005 | 보이는 grid 커서, R/Delete/Ctrl+D/Z/Y/S, 좌표 입력, wall host 커서와 복제 preview, 텍스트/대화상자 shortcut 제외, middle/Space pan, 현재 PhysicalCamera 역변환 1회, 물리 픽셀 snap과 편집 marker bounds를 연결했다. 좌표 파싱 오류는 기존 pending command를 취소한다. |
| §7–§8 / U006, O011 | 검색·전체 logical thumbnail·rotation/state 선택, seat 지정/해제, Undo/Redo, Validate/Save/Reset, 그룹별 issue와 객체/host 이동, custom 관리 및 live/draft/history/saved/backup 참조 guard를 구현했다. Import는 pure Validate 뒤에만 호출한다. 실제 pack 교체는 `CommitPack`이 제공될 때만 진행한다. |
| §8 / U007 | system font, Automation Name/HelpText, palette/issue 개수, status announcement, system focus, 스크롤 가능한 panel과 200% 글자 크기 capture를 추가했다. 선택 outline에는 흰 halo를 사용했다. |
| §9 / U008 | 편집 event/overlay는 입장 때 연결하고 퇴장 때 해제한다. 동일 command preview를 캐시하고, editor timer/watcher를 추가하지 않았다. 닫힌 편집기의 Present 경로는 LiveApplied를 호출하지 않는다. idle allocation/latency 목표는 측정하지 않았다. |

`IsoEditorComposition.Load`가 실제 서비스의 단일 구성 지점이다. Local importer와 `IIsoEditorRuntime`이 없으면 편집 서비스를 사용할 수 없다고 표시한다. 실제 stub의 `NotImplementedException`을 잡아서 성공이나 가짜 서비스로 바꾸지 않는다. `IsoEditorFakes.cs`의 scripted 서비스는 테스트와 `--demo` smoke에서만 명시적으로 사용한다. UI의 명령 전달/그림은 Core 명령·충돌·history·영속성 수락 증거가 아니다.

## 검증

scratch, bin/obj, 도구 결과는 커밋하지 않는다. 검토한 golden과 hash만 render fixture 폴더에 보관한다.

- App 빌드: `--no-restore -p:UseSharedCompilation=false`, 경고 0개·오류 0개.
- 최종 editor-only smoke: exit 0, 17개 편집 상태의 1x/2x/3x+crop 68개, controls 4개, 200% top/bottom 2개, import dialog 2개, art 전용 3개, 합계 PNG 79개. 9개 contact sheet로 모든 PNG를 직접 보고 선택 desk/invalid ghost crop, grouped controls, 200% bottom을 원본 크기로 추가 확인했다. 검토한 golden 14개를 보관했다.
- art 전용 nearest-neighbor 비교: 2x 1,290,240픽셀, 3x 2,903,040픽셀 모두 차이 0개. import dialog smoke는 fake의 pure Validate가 Import를 호출하지 않고, 성공 흐름만 Import 1회를 호출하는지 검사했다. metadata·catalog·geometry는 scripted 값이며 실제 custom import 성공 증거가 아니다.
- 입력 smoke는 기존 linked chair의 designation 필드 유지, 회전 field preview를 R로 정확히 전달하기, 좌표 preview 유지/취소 복원, accepted move/delete 뒤 필드·선택 참조 동기화를 검사한다. Rotate button과 R는 같은 session method를 사용한다.
- 전체 solution build/test는 시도했지만 NuGet `api.nuget.org:443` 접근이 `NU1301`로 차단됐다. 이후 no-restore 시도도 미복원 테스트 의존성 때문에 완료하지 못했다. Tracker/MSBuild 출력 접근 오류와 렌더 중 DLL 잠금도 관찰했다. 네트워크 차단을 우회하지 않았다.
- 최종 전체 build는 Core/Tracker/App를 빌드했으나 테스트 의존성의 NU1301 3개로 exit 1, 경고 0개였다. 최종 `dotnet test --no-restore --no-build -m:1`은 테스트 DLL을 찾지 못해 exit 1이었다.
- .NET suite 실제 발견 수: 확인 불가. 실행 완료 테스트: 0개. UI assertion 13개를 추가했고 기존 U001–U008 skeleton 8개의 body를 관련 assertion으로 바꿨다. 실행 증거가 없어 기존 Skip은 유지했다.
- Python tools suite: `python -m unittest discover -s tools -p 'test_*.py'` 실행을 시도했으나 `python`이 없다. 발견 수 확인 불가, 실행 0개. 과거 baseline 수치를 결과로 사용하지 않았다.
- 모든 실행은 worktree의 `artifacts/tmp`를 TEMP/TMP로 사용했고 CLI home·tracker home도 synthetic 경로로 지정했다. 실제 사용자 프로필/catalog/layout을 읽지 않았다.

재현 명령은 다음과 같다. 실제 서비스가 합쳐지기 전 smoke는 UI fake suite다.

```powershell
$env:TEMP=Join-Path $PWD 'artifacts/tmp'
$env:TMP=$env:TEMP
$env:DOTNET_CLI_HOME=Join-Path $PWD 'artifacts/dotnet-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet build src/Vibedotcom.App/Vibedotcom.App.csproj --no-restore -p:UseSharedCompilation=false
$env:VIBEDOTCOM_ISO_EDITOR_SMOKE='1'
dotnet src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.dll --demo --view iso --smoke-test (Join-Path $PWD 'artifacts/editor-smoke-acceptance') --home (Join-Path $PWD 'artifacts/synthetic-tracker')
```

## 계약 변경 요청과 통합 제한

공개 서명은 그대로 두고 다음 의존 부분은 완료로 표시하지 않았다.

| 근거 | 현재 계약의 부족한 부분 / 제안 |
| --- | --- |
| §7–§8 / U006 | `Validate(string)`는 issue만 반환한다. 검증된 displayName·variant 수·PNG preview와 검증 대상 bytes identity가 없다. UI가 원본 JSON/PNG를 다시 읽으면 검증 시점과 다른 데이터를 볼 수 있다. **추가** `ValidatePreview(string, CancellationToken)`과 immutable verified metadata/thumbnail/source identity 결과를 제안한다. 현재 dialog는 검증 결과만 표시하고 원본 art를 읽지 않는다. |
| §7–§8 / O009, C006, U006 | `Import/Remove`가 카탈로그를 먼저 영속화하고 `UpdatePack`은 editor/runtime 별도 mutation이다. 모든 소비자와 disk를 원자적으로 바꾸는 사전 검증·commit이 없다. **추가** staged `PrepareImport/PrepareRemove`와 publish commit, editor/history/runtime pack preflight를 제안한다. 현재 실제 구성의 `CommitPack`은 null이고 실제 Import/Remove를 막는다. supervisor가 원자적 구성점을 연결해야 한다. |
| §8 / U006 | runtime에는 assigned/away reservation 상태를 읽는 API가 없다. UI는 enabled `/12`만 표시한다. **추가** seat ID와 상태 enum만 반환하는 transient accessor를 제안한다. 이름·agent 정보는 문서에 넣지 않는다. |
| §6 / U001 | 현재 HitTest는 draft layout과 마지막 published scene을 받는다. pending/invalid draft의 새 위치·새 객체에 해당하는 authoritative picking queue/mask가 없다. **추가** editor가 현재 draft picking scene을 제공하는 hook, 또는 Core가 authoritative draft hit를 처리하는 계약을 제안한다. 실제 queue/mask와 가짜 hit 위임을 혼동하지 않는다. |
| §3.3 대 lanes §3 | lanes 흐름은 Errors가 없을 때만 runtime apply라고 쓰지만, invalid draft로 돌아오면 pending valid swap도 취소해야 한다. UI는 changed invalid state도 `ApplyEditorState`로 전달한다. runtime이 invalid state를 게시하지 않고 pending swap을 취소하도록 lanes 흐름 문장을 명확히 고치는 것을 제안한다. |

`LiveApplied`는 runtime이 받은 layout 참조를 실제 게시한 layout에 유지한다고 가정한다. Core가 deep copy를 게시하면 Applying 상태 해제를 위한 revision/ack가 필요하다. privacy 교체는 안전한 문서/history를 유지하지만 기존 방식대로 office 캐시를 재생성한다. 전체 actor clock/RNG/retiring 상태 연속성은 Core와 함께 확인해야 한다.

추가 서명 제안은 `IsoCustomValidationPreview ValidatePreview(string path, CancellationToken cancellationToken)`, `IsoCustomObjectPlan PrepareImport(IsoCustomImportRequest request, CancellationToken cancellationToken)`, `IsoCustomObjectPlan PrepareRemove(IsoCustomRemoveRequest request)`, `IsoCustomObjectResult Commit(IsoCustomObjectPlan plan)`, `ImmutableArray<IsoValidationIssue> IIsoEditor.PreviewPack(IsoPack pack)`, `ImmutableArray<IsoValidationIssue> IIsoEditorRuntime.PreviewEditorPack(IsoPack pack)`, `ImmutableArray<IsoEditorSeatStatus> IIsoEditorRuntime.GetEditorSeatStatus()`다. preview는 검증된 immutable PNG bytes·이름·variant 수·source identity를, plan은 검증된 staged pack/catalog를, seat status는 ID·상태 enum만 가진다. 같은 revision에서 pack commit이 실패하지 않는 보장이 필요하다. 서명/결과 타입은 supervisor의 조정 대상으로 남겼다. draft hit는 기존 `IsoHitTest`가 이미 처리할 수 있으면 추가 API 없이 통합 테스트로 확정하고, 그렇지 않으면 `IsoScene IIsoEditor.GetPickingScene(IsoScene publishedScene, IsoPixel camera)` 같은 authoritative scene hook을 제안한다.

실제 사용자 custom thumbnail, import 실패의 disk+editor+runtime 원자성, 협력 취소, same-hash 재가져오기, mounted computer/mask, 모든 built-in command, occupied/away/overflow/Undo, 저장·backup·재시작은 미검증이다. 네이티브 tray/desktop/topmost/click-through 복원, 100/125/150/200% 실제 DPI, OS 200% text setting, 키보드 전용 전체 흐름, minimized/hidden idle, privacy actor cache 및 p95 100-operation/idle allocation도 수행하지 못했다. offscreen 200% capture와 camera assertion 작성은 이 항목들의 통과를 의미하지 않는다.

## 공유 파일 변경 위치

최종 source 기준으로 기록한다. 다른 lane은 이 다섯 파일의 해당 member만 합친다.

| 파일 | 변경 member / 줄 |
| --- | --- |
| `AppStorage.cs` | 13: Home 기반 `IsoLayoutPath` 한 property |
| `IsoApplication.cs` | 12: Run smoke/session/window 구성; 35: Prepare의 storage/session resolver 및 resolved pack/layout |
| `IsoWindow.cs` | 22: sheets 교체 가능; 41: constructor session/tracker 전달·effective native flags·panel·입력; 107: Closed editor detach/tracker ownership; 115: ApplyModes; 140: Change; 150: Reopen office/session/sheets/tracker 유지; 173: MenuItems Edit/Done; 224: RefreshHover suppression; 249: PollAsync tracker lock/ownership dispose |
| `IsoView.cs` | 18: sheets 교체 가능; 89: editor 입력/resize hooks; 124: editing 중 LiveApplied; 229: UpdateCamera overlay bounds/pan clamp; 246: OnDpiChanged capture 취소 |
| `Program.Iso.cs` | 12: SwitchIso resolver/session; 24: SwitchFlat leave/retain; 36: SetPrivacy tracker redaction·안전한 last-valid 문서·session/tracker 전달 |

Models.cs, Office.cs, OfficeView.cs, Program.cs, Settings, Tracker/Program.cs는 수정하지 않았다. 가장 큰 병합 의존성은 Core runtime/stub 교체와 objects importer의 실제 namespace/생성자, pack transaction 구성이다. 전체 suite와 native QA가 끝나기 전 U001–U008 전체 수락으로 취급하면 안 된다.
