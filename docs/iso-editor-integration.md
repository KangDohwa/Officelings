# Iso 편집기 실제 서비스 통합 QA

2026-10-05 edfix 후속 검증: merged-pack command/preview p95 **6.6827/10.0868 ms**, idle **0 B**, import **1400.2006 ms**이며 U008 Skip을 제거했다. 회귀 검사 19개와 전체 장면 1,920개 비교를 통과했다. 아래는 edwire 당시 기록이며 후속 수정·검사·미검증 항목은 [독립 검토 수정 보고](iso-editor-review.md)에 있다. 전체 xUnit/Python suite의 통과를 뜻하지 않는다.

기준 커밋은 `9cec3a9`, 브랜치는 `lane/edwire`다. 중간 저장 커밋 `f4f30f5`에 승인된 계약 1.1과 실제 서비스 연결이 들어 있다. 기능 검증과 렌더 QA를 수행했지만 전체 xUnit suite와 U008 지연 기준은 아직 수락하지 못했다. 과거 723 .NET / 88 Python baseline을 이번 실행 결과로 계산하지 않는다.

## 계약과 구성

추가한 공개 서명은 다음과 같다. 기존 Import/Remove는 prepare+commit wrapper다. JSON 버전·capability·원본 fixture/art/pack은 변경하지 않았다.

| 경계 | 추가 서명 |
| --- | --- |
| IsoCustomObjectImporter | `IsoCustomValidationPreview ValidatePreview(string manifestPath, CancellationToken cancellationToken)` |
| IsoCustomObjectImporter | `IsoCustomObjectPlan PrepareImport(IsoCustomImportRequest request, CancellationToken cancellationToken)` |
| IsoCustomObjectImporter | `IsoCustomObjectPlan PrepareRemove(IsoCustomRemoveRequest request)` |
| IsoCustomObjectImporter | `IsoCustomObjectResult Commit(IsoCustomObjectPlan plan, CancellationToken cancellationToken = default)` |
| IIsoEditor | `ImmutableArray<IsoValidationIssue> PreviewPack(IsoPack pack)` |
| IIsoEditor | `IsoScene GetPickingScene(IsoPixel camera)` |
| IIsoEditorRuntime | `ImmutableArray<IsoValidationIssue> PreviewEditorPack(IsoPack pack)` |
| IIsoEditorRuntime | `ImmutableArray<IsoEditorSeatStatus> GetEditorSeatStatus()` |
| IIsoEditorRuntime | `long AppliedEditorRevision { get; }` |

`IsoCustomValidationPreview`는 검증된 DisplayName, VariantCount, SourceIdentityHash, immutable ThumbnailPng와 Issues를 반환한다. 원본 capture는 내부에만 보관하고 dialog는 원본 JSON/PNG를 다시 읽지 않는다. ImportRequest의 trailing VerifiedPreview와 RemoveRequest의 trailing BuiltInPack은 선택 인수다. 좌석 상태는 SeatId와 Free/Assigned/Away enum뿐이며 문서·history에 저장하지 않는다.

`IsoEditorComposition.Load`는 `new LocalIsoCustomObjectImporter()`와 `IsoEditorStorage.Load`를 직접 호출한다. 선택한 단일 merged pack을 editor, IsoOffice와 sheet source가 사용한다. CommitPack은 editor의 draft/saved/default/Undo/Redo와 runtime의 live/pending pack preflight, WPF sheet decode를 먼저 수행한다. 모두 수락한 뒤에만 catalog를 게시하고 같은 UI thread에서 소비자를 교체한다. 취소 토큰은 ValidatePreview와 PrepareImport에 전달하며 await 뒤와 importer Commit의 게시 직전에도 확인한다. 실패와 same-hash 재가져오기는 소비자와 catalog를 유지한다.

Invalid draft도 runtime에 전달해 pending swap을 취소한다. runtime은 유효한 live layout을 유지하고 UI는 AppliedEditorRevision으로 Applying을 해제한다. Picking은 현재 draft queue와 정확한 clip mask를 사용한다. Privacy 교체는 LastValidLayout과 editor/history를 유지한다. Production과 demo smoke는 실제 서비스만 사용한다. UI fake는 테스트 프로젝트로 옮겼다.

## 실제 검증 범위

아래 숫자는 real offscreen runner의 assertion 수이며 xUnit 실행 수가 아니다. 모든 root, tracker home, TEMP/TMP와 CLI home은 worktree 안의 synthetic 경로다.

| 그룹 | assertion 수 | 확인한 동작 |
| --- | ---: | --- |
| commands | 168 | 모든 built-in family의 Place/Move/authoring rotation/Delete/Duplicate, Undo/Redo, 독립 chair/computer 지정, ClearSeat, linked root 이동·회전·복제 |
| persistence | 12 | Save→재시작 load 동일 문서, 지정 해제 좌석 유지, 이전 Save backup, invalid Save의 무쓰기, flat 파일 분리, privacy의 안전한 문서/history 유지 |
| objects | 19 | 검증 capture 뒤 원본 변경, prepare 무쓰기, 취소 실패, 실제 CommitPack import, same-hash 무변경, 참조/history preflight 제거 차단, missing-object startup 진단/fallback, 실제 CommitPack 제거 |
| runtime | 9 | assigned/away/복귀 예약, 이름 없는 status, occupied edit의 안전한 대기, invalid draft의 pending 취소, 유효한 재시도의 revision ack |

U001–U007은 offscreen QA와 xUnit이 같은 실제 서비스 assertion body를 호출한다. Draft mask/handle 우선순위, 순수 preview·네 방향 접점, 모드 복원·copied-layout ack, TextBox shortcut 제외와 키보드 명령, DPI/zoom 역변환과 physical snap, 실제 dialog import·privacy 검색, automation 이름과 200% text scroll을 통과해 일곱 Skip을 제거했다. 이 결과가 xUnit runner 실행이나 실제 OS DPI의 통과를 뜻하지는 않는다.

E2E test는 네 개 Fact와 mounted rig 네 방향 Theory case를 추가했다. Mounted rig는 desk의 embedded monitor/status/screen/glow가 억제되고 computer 부품이 한 번씩만 나오는지 검사한다. 실제 WPF adapter로 렌더한 mounted/embedded off-state rig의 BGRA 픽셀도 네 방향에서 동일했다. 전체 xUnit 복원이 차단돼 이 프로젝트의 컴파일·발견 수·실행 수는 확인하지 못했다. U008은 100 ms/16 ms 임계값을 유지하고 실패 이유를 명시한 Skip을 남겼다.

## 렌더와 측정

최종 결과는 `artifacts/edqa7/editor-real/iso-editor-smoke.json`에 있다. 원본 PNG 78개를 `contact-01.png`부터 `contact-10.png`까지 모두 직접 확인했다. Selected desk 네 방향 3x, invalid ghost 3x, 실제 thumbnail이 있는 import-success dialog 3x와 grouped-validation scene/controls-bottom은 원본 크기로도 확인했다. Dialog 2x/3x 잘림과 방 밖 marker 잘림을 수정했다. 후자는 `Rect` property 복사본의 Union이 버려지는 것이 원인이었다. Overlay의 경계를 backing field에 누적하고 캡처도 `EditorRenderBounds()`를 사용한다. U005의 음수 위치 marker/camera assertion도 통과했다.

| 측정 | 결과 | 기준 |
| --- | ---: | ---: |
| command+validation p95 / 100회 | 2432.5559 ms | ≤100 ms, 실패 |
| preview p95 / 100회 | 1822.4468 ms | ≤16 ms, 실패 |
| editor open / closed idle | 각각 0 B/frame | 0 B |
| 동일 preview cache | 0 B/call | 0 B |
| fixture import | 4947.4441 ms | 측정 보고 |

Smoke는 기능 검증과 PNG/JSON 저장 뒤 `Editor latency budget exceeded.`를 출력해 지연 검증에 실패했다.

PNG와 JSON은 ignored artifacts이며 커밋하지 않는다. 검토 범위는 selected/mounted desk 네 방향, valid/invalid ghost, lone chair/computer, designated/cleared seat, linked duplicate, two-edge wall, invalid draft, grouped validation, 실제 fixture thumbnail이 있는 import 성공과 palette 오류 실패다. 장면·dialog는 1x/2x/3x, panel은 일반 글자와 200% 글자 top/bottom을 캡처한다. Seed 42, epoch `2026-10-04T00:00:00Z`, .NET 10.0.11, 논리 프로세서 18개, Debug build다. Dialog 확대는 native layout을 유지한 96/192/288 DPI offscreen bitmap이며 실제 모니터 DPI 검증은 아니다.

측정은 34-object/12-seat fixture에서 command+Validate 100회와 서로 다른 Move preview 100회다. 마스크/기본 검증을 warmup하고 p95를 계산한다. Idle은 같은 scene Present open/closed 각 1,000회와 같은 preview 1,000회의 thread managed allocation이다. 기존 임계값을 높이거나 검증하는 캐릭터·phase·frame 범위를 줄이지 않았다. 지연의 주요 비용은 장면 순서 검증이며 효과 없는 최적화 실험은 최종 코드에서 제거했다.

재현은 worktree의 TEMP/TMP를 `artifacts/tmp`로 지정하고 `VIBEDOTCOM_ISO_EDITOR_SMOKE=1`로 설정한 뒤 App에 `--demo --view iso --home <synthetic-home> --smoke-test <synthetic-output>`을 전달한다. Seed/epoch/hash와 각 PNG 경로는 결과 JSON에 기록된다. Tracker watcher는 기존 비활성 옵션을 사용했다. Editor 코드에 새 timer/watcher는 없으며 editor open idle에서 overlay rebuild와 animation timer 추가가 없음을 검사했다. OS watcher 수나 minimized 상태의 idle을 계측한 결과는 아니다.

## 빌드·suite와 미검증 항목

- Core/App `dotnet build --no-restore -p:UseSharedCompilation=false`: 경고 0, 오류 0.
- 전체 `dotnet build Vibedotcom.sln`: NU1301, `api.nuget.org:443` 소켓 접근 거부, 경고 0·오류 1. 복원 차단을 우회하지 않았다.
- 전체 `dotnet test Vibedotcom.sln --no-restore`: NU1301, 실행 완료 0개. 기존 .NET regression과 추가 xUnit test의 통과 수는 미확인이다.
- Python tools suite: `-I -B -m unittest discover -s tools -p 'test_*.py'`, 91개 / 269.404초 / OK. PATH의 python은 없었지만 공용 설치 venv를 사용했다. Synthetic TEMP/TMP로 실행했으며 사용자 profile/catalog/layout이나 `.codex/.claude/.gemini`를 입력으로 사용하지 않았다.
- Native tray/desktop/topmost/click-through 복원, 실제 100/125/150/200% DPI, OS 200% text setting, keyboard-only 전체 흐름, minimized/hidden idle, privacy 교체의 actor clock/RNG 연속성은 미검증이다.

## 공유 파일과 병합 위험

| 파일 | 9cec3a9 대비 변경 member |
| --- | --- |
| `src/Vibedotcom.App/AppStorage.cs` | 16: constructor의 trailing isolatedHome과 Home 결정식 |
| `src/Vibedotcom.App/IsoApplication.cs` | 24: Run의 real editor smoke 및 measurement 경로. 42: Prepare의 기존 resolver는 실제 Composition을 사용한다. |
| `src/Vibedotcom.App/IsoView.cs` | 125: Present의 editor-only revision ack/status 갱신 한 hunk |
| `src/Vibedotcom.App/Program.Iso.cs` | 24: IsoWindow.PrivacyOffice helper; 49: SetPrivacy의 해당 호출 |
| `src/Vibedotcom.App/IsoView.Editor.cs` | 25: EditorRenderBounds를 internal로 열어 QA도 실제 marker 범위 사용; 64/133: EditorHit/Enter의 authoritative draft scene 호출 |
| `src/Vibedotcom.App/IsoWindow.Editor.cs` | 75: PackChanged에서 사전 decode한 CommittedSheets 사용 |
| `src/Vibedotcom.Core/Iso/IsoOffice.Editor.cs` | 23/192: ApplyEditorState/CompleteEditorSwap revision ack; 46/53: UpdateEditorPack/PreviewEditorPack; 67: GetEditorSeatStatus |
| `src/Vibedotcom.Core/Iso/Editing/IIsoEditor.cs` | 19/21: PreviewPack/GetPickingScene 추가 |
| `src/Vibedotcom.Core/Iso/Editing/IsoEditorContracts.cs` | 69–73: runtime revision/preflight/status; 이름 없는 seat status record/enum 추가 |
| `src/Vibedotcom.Core/Iso/Editing/IsoCustomObjectContracts.cs` | 44–50: verified preview/prepare/commit; request trailing 선택 인수 추가 |

Core Editing API·implementation, IsoEditor App 파일, spec/lanes/objects 문서와 editor tests도 함께 합친다. IsoWindow.cs, Models/Office/OfficeView/Settings/Tracker/Program.cs와 shipped art/pack에는 변경이 없다. Interface 추가와 test-only fake 이동을 분리해 합치면 구현체 컴파일 오류가 날 수 있다. 전체 suite 차단과 U008 지연 실패를 해결하기 전 전체 수락이나 출시 가능 상태로 표시하면 안 된다. 긴 synthetic 출력 경로의 초기 smoke는 unused import에서 실패했으며 원인은 확정하지 못했다; 짧은 root의 성공만 증거로 사용했다.
