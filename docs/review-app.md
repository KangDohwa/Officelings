# App 안정성 독립 코드 리뷰

검토 커밋: `33a3cebf7e5efa93f8af7db6baf43c5fa18f17cd` (2026-10-04). 요청한 `git worktree add --detach %TEMP%\vd-review HEAD`로 만든 worktree만 읽고 빌드했다. 생성 직전에 다른 작업으로 HEAD가 이동했으므로 이 해시가 실제 기준이다. 진행 중인 작업 트리의 수정은 검토 대상에 섞지 않았다. 아래 줄 번호도 이 커밋 기준이다. 검토 후 worktree의 소스 변경이 없음을 확인하고 `git worktree remove --force`로 제거했다. 커밋하지 않았다.

원본 저장소에는 이 문서만 작성했다. 재현 입력·출력은 `%TEMP%`에 두었고, Tracker 실행에는 `--home`, `--output`, `--asset-dir`, `--layout`을 모두 임시 경로로 지정했다. 실제 `%LOCALAPPDATA%\Vibedotcom`에 접근하지 않았다. 앱 창 표시, 키 입력, 마우스 이동, 최소화, 전경 변경은 하지 않았다. WPF 검증은 창 없이 메모리의 visual을 렌더했다.

## 실제 수행한 검증

| 검사 | 결과 |
| --- | --- |
| `dotnet build Vibedotcom.sln --nologo -v minimal` | 성공, 경고 0 / 오류 0 |
| `dotnet test Vibedotcom.sln --no-build --no-restore --nologo -v minimal` | 134 통과, 실패·건너뜀 0 |
| 문서에 지정된 sprite-gen Python으로 `-B -m unittest discover -s tools -p test_build_pack.py` | 4 통과 |
| Core DLL을 참조하는 임시 C# 프로그램 | 자동 그리드 오버플로, 잘못된 에셋, 검증 시간, 자리 중복, 이력 증가 재현 |
| 빌드된 App의 Settings 역직렬화 및 OfficeView 메모리 렌더 | 비정상 숫자 저장 예외와 폭 1 구역 렌더 예외 재현 |
| Windows 파일 잠금 | editor의 목적지·백업 잠금 실패 시 원본 보존과 임시 파일 정리 확인; Tracker의 일시 잠금 종료 재현 |
| Python의 제어된 writer interleaving / I/O 실패 주입 | 팩의 누락 PNG, seated 임시 파일 충돌, facelock의 불일치 출판 재현 |
| `ko-KR`, `de-DE` | 레이아웃 저장/로드 경고 0; Settings의 `0.85`, `1.5`, `120.5` JSON 왕복 및 소수점 `.` 유지 |

P1은 앱/Tracker 종료, 시작을 오래 막는 입력, 사용자 데이터 또는 출판된 팩의 손상을 우선 처리해야 하는 문제다. P2는 특정 배치·편집·동시 실행·장기 실행에서 발생하는 정확성/복구 문제다. 각 항목에 실제 재현과 정적 경로 확인을 구분했다.

## 발견 사항

### F01 — [P1] 적용 시 보정한 비정상 Settings 숫자가 저장할 때 다시 예외를 일으킨다

- **위치:** `src/Vibedotcom.App/Program.cs:321`, `:331`, `:337`.
- **실패 상황:** `{"opacity":1e999}`는 JsonException 없이 `Infinity`로 읽힌다. UI 적용에는 `Finite`가 정상 값을 쓰지만 `settings` 레코드에는 Infinity가 남는다. 이후 설정 저장은 `SerializeToUtf8Bytes`에서 ArgumentException을 던지고, Save의 예외 필터는 이를 잡지 않는다. 자동 위치/크기 저장이나 메뉴 저장에서 앱 실행을 중단시킬 수 있다. `workingSeconds`, `goneMinutes`에도 같은 문제가 있다.
- **재현:** 빌드된 내부 Settings 타입을 reflection으로 사용했다. `parsed=∞`, 이어서 JSON 직렬화가 ArgumentException을 던졌다. 개인 설정 파일과 OfficeWindow는 사용하지 않았다.
- **최소 수정:** 읽기/CLI 적용 직후 **레코드 자체의 모든 double 필드**를 유한하고 허용 범위인 값으로 정규화한다. 저장 경계에서도 직렬화 오류를 사용자에게 보고한다. JSON에 NaN/Infinity 문자열을 허용하는 방식으로 우회하지 않는다.

### F02 — [P1] 자동 책상 그리드의 정수 오버플로가 layout fallback을 벗어난다

- **위치:** `src/Vibedotcom.Core/LayoutLoader.cs:108`, `:142`, `:160`, `:15`.
- **실패 상황:** `autoDesks.x`, `y`, 간격에 상한이 없다. 마이그레이션에서 int 곱셈·덧셈이 먼저 오버플로되어 음수 width/배열 길이가 생긴다. OverflowException은 사용자 레이아웃을 거부하는 catch에 포함되지 않는다.
- **재현:** 유효한 24×17 all-floor 방에 `new AutoDesks("desk", 2147483640, 5, 1, 1, 1, Capacity:1)`을 넣어 임시 JSON을 로드했다. `LayoutLoader.Load`가 경고/fallback 대신 OverflowException을 던졌다. App은 시작 실패, Tracker의 바깥 예외 필터도 이 예외를 처리하지 않는다.
- **최소 수정:** 좌표·간격을 레이아웃에서 의미 있는 범위로 제한하고, 필요한 그리드 크기를 long/checked로 계산해 8–128 범위를 확인한 뒤 int로 변환한다. 입력 오류는 InvalidDataException으로 반환한다.

### F03 — [P1] 시트 clip 범위 검사도 오버플로되어 잘못된 crop을 허용한다

- **위치:** `src/Vibedotcom.Core/Assets.cs:72`, `:144`, `:193`; `src/Vibedotcom.App/OfficeView.cs:151`.
- **실패 상황:** row/column은 음수만 거부한다. `(row+1)*cellH`와 `(column+frames)*cellW`가 오버플로하면 실제 PNG 범위를 벗어난 clip이 검증을 통과한다. 렌더 경계의 CroppedBitmap 예외 처리도 없다.
- **재현:** 정상 fallback PNG를 별도 `people` 시트로 사용하고 `cellH=24`, `row=2147483647`, `frames=1`로 설정했다. 팩 경고 0; 반환된 frame은 `Y=-24`; 실제 .NET 10 WPF CroppedBitmap이 ArgumentException을 던졌다.
- **최소 수정:** clip 끝 좌표를 long으로 계산해 실제 PNG 범위와 비교한다. frame을 만들기 전에 row/column 및 픽셀 좌표가 검증된 값인지 보장한다.

### F04 — [P1] 필수 character sheetId에 null/누락을 허용한다

- **위치:** `src/Vibedotcom.Core/Assets.cs:147`, `:148`, `:186`, `:203`.
- **실패 상황:** 가구·타일의 선택적 sheetId에 쓰는 `HasSheet(null) == true`를 캐릭터에도 사용한다. 캐릭터에는 시트가 필수인데 잘못된 manifest가 수락되고, 첫 에이전트의 frame 선택에서 `First`가 실패한다.
- **재현:** 정상 PNG/시트와 `Characters=[new CharacterDesign("person", null!)]`를 사용했다. 팩 경고 0; `OfficeAnimator.Update`가 `InvalidOperationException: Sequence contains no matching element`를 던졌다.
- **최소 수정:** 캐릭터에는 비어 있지 않은 sheetId와 실제 시트 존재를 별도로 요구한다. 필수 참조와 선택적 참조 검사를 구분한다.

### F05 — [P1] 수락된 사용자 팩 때문에 bundled fallback 레이아웃 자체가 로드되지 않는다

- **위치:** `src/Vibedotcom.Core/LayoutLoader.cs:9`, `:17`; `src/Vibedotcom.Core/Assets.cs:80`.
- **실패 상황:** `cozy-desk`가 하나라도 있으면 전체 cozy 레이아웃을 선택하지만 그 레이아웃의 타일·가구 의존성이 팩에 있다는 보장이 없다. 사용자 레이아웃이 없거나 거부된 후에도 동일 팩으로 fallback을 준비하므로 다시 예외가 발생한다. fallback의 실패는 catch 밖이다. 기본 desk 정의의 footprint를 바꾸는 팩에도 같은 호환성 문제가 생길 수 있다.
- **재현:** 정상 시트·캐릭터와 `cozy-desk` 정의 하나만 제공한 사용자 팩은 경고 0으로 수락됐다. `LayoutLoader.Load(pack, null)`은 InvalidDataException으로 실패했다.
- **최소 수정:** 기본 레이아웃 선택을 한 에셋 ID 존재에 의존하지 않는다. 기본 레이아웃의 전체 의존성/geometry 호환성을 확인하고, 호환 불가 시 알려진 정상 팩·레이아웃 쌍으로 fallback한다. 명시적 사용자 레이아웃과 사용자 팩 조합은 계속 허용한다.

### F06 — [P1] 저장 검증을 통과하는 폭 1칸 구역이 렌더링을 중단시킨다

- **위치:** `src/Vibedotcom.App/OfficeView.cs:221`, `:129`; `src/Vibedotcom.Core/OfficeEditor.cs:284`.
- **실패 상황:** fallback 팩에서 workspace/pantry 라벨의 폭은 `Math.Min(72, zone.Width*16-24)`다. 폭 1칸 구역은 loader/editor에서 유효하지만 라벨 Rect의 폭은 -8이 된다. UI에서 구역을 한 칸으로 설정해도 발생할 수 있다.
- **재현:** fallback 팩 + 유효한 방의 workspace만 Width=1로 변경했다. `LayoutLoader.Validate` 성공, `OfficeEditor.Validate().Count == 0`. 빌드된 실제 OfficeView를 reflection으로 만들고 창 없이 measure/arrange/render하자 ArgumentException이 발생했다.
- **최소 수정:** 공간이 부족하면 라벨을 생략하거나 계산한 폭을 0 이상으로 제한한다. 의미 있는 구역 크기를 렌더러가 안전하게 다룬다.

### F07 — [P1] 작은 유효 JSON도 지점마다 반복하는 A* 때문에 시작을 오래 막는다

- **위치:** `src/Vibedotcom.Core/LayoutLoader.cs:75`; `src/Vibedotcom.App/Program.cs:71`.
- **실패 상황:** 각 interaction point에 대해 같은 door에서 A*를 다시 수행한다. zone point 개수 제한도 없고 동일 좌표를 deduplicate하지 않는다. 4 MiB 파일 크기 제한은 이 계산량을 제한하지 못한다. App은 창을 만들기 전에 동기 로드하므로 사용자는 시작이 멈춘 것처럼 보게 된다.
- **재현:** 128×128 all-floor, door `(1,1)`, 가구 없음, 이름이 다른 zone point 1,500개가 모두 `(126,126)`을 가리키는 JSON. **358,734 bytes**, 경고 0, `LayoutLoader.Load` 단독 **31,813 ms**. 전체 OfficeAnimator 생성 시간으로 오인하지 않았다.
- **최소 수정:** 문/blocked geometry로 한 번 flood-fill해서 모든 지점의 도달 가능성을 확인한다. zone/interaction point 총량에도 합리적 한도를 둔다. UI의 Validate/Save에도 같은 도달성 결과를 사용한다. 단순히 백그라운드로 옮겨 반복 계산을 남기는 것보다 계산량을 먼저 줄인다.

### F08 — [P1] snapshot 소비자의 일시 파일 잠금이 Tracker를 영구 종료시킨다

- **위치:** `src/Vibedotcom.Tracker/Program.cs:64`, `:67`, `:78`; 관련 쓰기 `src/Vibedotcom.Core/Models.cs`의 `StateJson.WriteAtomic`.
- **실패 상황:** 소비자가 `state.json`을 delete-sharing 없이 잠깐 읽으면 Windows에서 원자적 교체가 실패한다. 쓰기 오류가 바깥 catch까지 전파되어 프로세스가 종료된다. 잠금 해제 후 재시도하지 않는다. runtime-layout 또는 다른 출판 대상의 잠금도 같은 경로다.
- **재현:** 임시 입력/출력으로 5초 Tracker를 시작하고 첫 snapshot 후 자체 `state.json`을 `CreateFileW(..., FILE_SHARE_READ|FILE_SHARE_WRITE)`로 **0.7초** 열었다. 잠금 중 종료 코드 **1**, stderr `Access to the path is denied.`. 기존 JSON은 schemaVersion=4로 유효했다. 즉 이 재현은 파일 손상보다 **관찰 서비스 중단**을 증명한다.
- **최소 수정:** snapshot 출판 경계에서 일시적 sharing/access 오류를 처리하고, 마지막 정상 파일을 보존한 채 다음 tick 또는 짧고 제한된 backoff로 재시도한다. 설정/입력 오류와 영구 I/O 실패는 따로 보고한다.

### F09 — [P1] demo/qa 편집기 저장이 개인 layout.json 경로로 연결된다

- **위치:** `src/Vibedotcom.App/OfficeEditorPanel.cs:169`; 호출 `src/Vibedotcom.App/Program.cs:444`; 대조되는 settings 보호 `:327`.
- **실패 상황:** `--demo`는 bundled 레이아웃으로 시작한다. 편집기를 열고 Save/Ctrl+S를 누르면 언제나 실제 `TrackerOptions.OutputDirectory/layout.json`에 저장한다. 기존 개인 레이아웃이 demo의 기본/편집 레이아웃으로 교체된다. 일반 설정 Save에는 demo/qa 보호가 있지만 편집기에는 전달되지 않는다.
- **근거:** 생성자 → HandleKey/Save → 하드코딩된 개인 경로의 정적 호출 경로 확인. **실제 개인 경로로 저장하는 재현은 하지 않았다.** 교체 시 `.bak`이 생기는 정상 동작도 현재 파일의 의도치 않은 교체를 막지는 않는다.
- **최소 수정:** 편집기에 저장 가능 여부와 대상 경로를 전달한다. demo/qa에서는 개인 경로 저장을 차단하고, 사용자가 명시적으로 정한 별도 경로로만 내보내게 한다.

### F10 — [P1] 동시 pack 빌드가 모두 성공해도 최종 manifest의 PNG를 삭제한다

- **위치:** `tools/build_pack.py:676`, `:678`, `:680`.
- **실패 상황:** 출력 디렉터리를 잠그지 않고 각 writer가 자기 `pngs` 집합 기준으로 stale 파일을 지운다. A가 manifest를 출판한 후 B가 새 디자인/PNG와 manifest를 출판하면, A의 cleanup이 B가 방금 참조한 PNG를 삭제한다. 콘텐츠 해시 이름과 파일별 atomic write만으로 writer 간 cleanup 경쟁은 막지 못한다.
- **재현:** 기존 테스트의 임시 art fixture를 사용했다. A의 manifest 출판 후 새 색상의 roster 디자인을 추가하여 B를 시작하고, B의 출판 뒤 A cleanup을 진행시키는 barrier를 적용했다. writer 예외 **0**, 최종 characters=37, 최종 manifest가 참조하는 PNG **1개 누락**.
- **최소 수정:** 같은 출력 디렉터리의 출판과 cleanup 전체를 writer lock으로 직렬화한다. 잠금에 실패한 writer는 명시적으로 중단한다. 외부 reader가 이전 manifest를 읽는 동안 이전 PNG가 사라지는 문제도 보존 기간/지연 cleanup으로 다룬다.

### F11 — [P2] 가상 화면의 빈 영역에 창을 복원하며 Recover도 위치를 수정하지 않는다

- **위치:** `src/Vibedotcom.App/Program.cs:266`, `:370`.
- **실패 상황:** 개별 모니터 대신 VirtualScreen의 전체 외곽 사각형과 교차하는지만 검사한다. L자 배치의 빈 사분면이나 화면에 극히 일부만 걸친 창도 허용한다. tray의 Recover는 모드/입력 상태만 바꾸므로 정상 창도 여전히 화면 밖에 남을 수 있다.
- **재현:** 실제 모니터는 `(0,0,1920,1080)`, `(1920,0,1920,1080)`, `(0,1080,1920,1080)`으로 가정. 복원 Rect `(2200,1400,720,480)`은 현재 식으로 True, 개별 모니터 교차 수 **0**이다. 물리 모니터나 창은 이동하지 않았다.
- **최소 수정:** 개별 monitor working area에 최소한의 조작 가능한 영역이 보이는지 확인하고 가까운 작업 영역에 clamp한다. Recover에서도 이를 적용한다. 좌표 변환은 DPI와 함께 일관되게 처리한다.

### F12 — [P2] 편집 preview의 대체 좌석은 타일 단위 예약을 확인하지 않는다

- **위치:** `src/Vibedotcom.Core/Office.cs:267`, `:269`, `:332`.
- **실패 상황:** 기존 desk/overflow 소유자는 ID 기준으로 유지하지만 `NearestFree`에 다른 소유자의 배정 타일을 제외 목록으로 전달하지 않는다. 편집으로 여러 원래 좌석이 막히면 모두 같은 가까운 reachable tile을 받을 수 있다.
- **재현:** 서로 다른 두 책상에 a/b를 배정하고, door `(1,1)`만 floor로 남긴 draft를 `ReloadLayout(validate:false)`로 preview했다. 두 에이전트는 각각 desk-a/desk-b ID를 유지하면서 둘 다 `(1,1)`에 배정됐다.
- **최소 수정:** 이번 배정에 사용한 Tile 집합도 유지해 대체 목적지를 제외한다. 유효한 대체 타일이 없으면 겹친 작업 좌석 대신 대기/표시 제한 상태로 처리한다. **엄격히 검증된 저장 레이아웃의 일반 책상 중복으로 확대해서 주장하지 않는다.**

### F13 — [P2] 대기 타일이 소진되면 첫 타일을 무제한 재사용한다

- **위치:** `src/Vibedotcom.Core/Office.cs:303`.
- **실패 상황:** 문에서 거리 4 이내의 queueTiles가 꽉 차면 `queueTiles.FirstOrDefault(...)`로 다시 돌아간다. 이미 usedQueue에 들어 있는 타일이므로 초과 인원은 같은 자리/이름표에 겹친다. 저장 레이아웃이 유효해도 발생한다.
- **재현:** 유효한 24×17 빈 방, 문 `(1,1)`, 책상/overflow 없이 Working 에이전트 40명. `queued=40`, `unique_tiles=23`, `largest_overlap=18`.
- **최소 수정:** 가까운 대기 구역이 소진되면 다른 미예약 reachable tile로 확장하거나 초과 인원을 명시적인 집계/비표시 대기 상태로 처리한다. 이미 예약된 타일을 새 좌석처럼 반환하지 않는다.

### F14 — [P2] 떠난 세션의 deskHistory가 프로세스 수명 동안 무제한 유지된다

- **위치:** `src/Vibedotcom.Core/Office.cs:35`, `:77`, `:285`.
- **실패 상황:** 떠난 Character는 삭제하지만 그 ID 문자열과 책상 이력은 제거/만료하지 않는다. 새 세션 ID가 계속 생기는 unattended 실행에서는 현재 인원과 무관하게 메모리가 늘어난다.
- **재현:** 책상 하나인 유효한 방에 서로 다른 ID를 가진 세션 10,000개를 순서대로 넣고 마지막에는 빈 목록을 업데이트했다. reflection으로 `active=0`, `deskHistory.Count=10000` 확인. 수일 실측/OOM을 주장하는 결과는 아니다.
- **최소 수정:** 재입장 이력을 보존할 합리적 TTL 또는 개수 제한을 둔다. 현재 활성 ID는 보존하고 오래전에 떠난 ID부터 제거한다.

### F15 — [P2] facelock의 파일별 atomic write는 결과 묶음의 실패 원자성을 보장하지 않는다

- **위치:** `tools/facelock.py:493`, `:518`, `:524`, `:530`.
- **실패 상황:** 고정 이름의 PNG를 교체한 다음 manifest/report를 각각 교체한다. 후속 교체가 권한/공유/디스크 오류로 실패하면 이전 manifest/report와 새로운 픽셀이 섞인 상태가 남는다. sourceHashes 검사도 입력만 확인하므로 출판된 파일들의 동일 세대를 보장하지 않는다.
- **재현:** 임시 p5 원본으로 정상 출판 후 입력은 그대로 두고 다른 custom profile을 적용했다. 새 PNG 교체 후 manifest 쓰기에 OSError를 주입했다. `png_changed=True`, `manifest_unchanged=True`, **기존 report의 sourceHashes는 여전히 모두 일치**했다. 이전 정상 결과가 온전히 유지되지 않았다.
- **최소 수정:** PNG/report를 세대별 이름으로 stage하고 검증한 뒤 그 세대를 가리키는 manifest 하나를 마지막에 교체한다. 고정 alias가 필요하면 동일 세대 디렉터리/포인터를 통해 제공한다. 여러 고정 이름을 차례로 바꾸는 방식에 파일별 atomic을 덧붙이는 것만으로는 부족하다.

### F16 — [P2] seated 동시 출판이 같은 .tmp 이름을 사용해 충돌한다

- **위치:** `tools/seated.py:81`, `:83`, `:368`, `:491`.
- **실패 상황:** 같은 캐릭터를 두 프로세스가 만들면 두 writer가 동일한 `pose.png.tmp`를 쓴다. B가 파일을 replace한 뒤 A의 replace는 FileNotFoundError로 실패할 수 있다. main은 이를 SKIP으로 처리하지만 그 전에 캐릭터의 다른 파일이 이미 출판되어 결과 묶음도 불완전해질 수 있다.
- **재현:** 임시 목적지에서 A의 temp 쓰기 직후 pause → B 쓰기/replace → A 재개. `B success`, `A FileNotFoundError`, 목적지 내용 B. 원본 에셋 파일에는 쓰지 않았다.
- **최소 수정:** 유일한 sibling temp 파일을 사용하고 finally에서 정리한다. 같은 캐릭터의 다중 파일 출판 전체에는 writer lock을 두며, 묶음 교체 실패 시 이전 세대가 유지되도록 F15와 같은 출판 방식으로 처리한다.

## 정상 동작과 검증 한계

- 기본적인 join/leave, 재입장 이력의 재사용, Gone의 자리 반환, 부모 우선 배정, overflow 유지, 경로 변경, custom seat 이동/회전, undo/redo의 immutable snapshot 복원은 기존 테스트가 다루며 이번 커밋에서도 통과했다. overflow 소유자가 빈 책상으로 자동 승격하지 않는 것은 기존 테스트가 명시한 동작이므로 버그로 보고하지 않았다.
- editor의 기존 목적지와 `.bak`을 각각 delete-sharing 없이 잠근 재현에서는 IOException이 발생했지만 목적지·기존 백업이 보존되고 `*.tmp`가 남지 않았다. 기존 atomic-save 구현의 일반적인 파일 잠금 실패를 데이터 손상으로 주장하지 않는다. facelock의 **다중 파일 출판** 실패와 구별한다.
- sprite/text/furniture 캐시가 시간 자체에 따라 무제한 커진다는 증거는 찾지 못했다. textCache에는 256개 제한이 있고 furnitureCache는 layout 변경 시 비운다. 다만 팩 전체의 byte/pixel 예산은 없어서 여러 대형 시트의 총 메모리 부담은 별도 검사 대상이다. 메모리 고갈을 유발하는 실제 대형 팩 실행은 하지 않았다.
- `WindowModePolicy`/opacity의 순수 정책 테스트는 통과했다. 실제 normal/topmost/desktop HWND의 투명도, desktop reparenting, Explorer 재시작, mixed DPI 전환은 실행하지 않았다. `OpacityVerification`의 per-pixel 경로는 RootVisual을 캡처해 자체 host에 합성하는 검사이므로 Explorer/DWM에서 보이는 최종 화면의 독립 증거와 동일하게 취급하면 안 된다. 이번 리뷰는 해당 verifier의 통과를 주장하지 않는다.
- 기존 주요 animation timer는 Closed에서 Stop하고, WindowModes는 Closed에서 hook을 제거하고 예약된 opacity 복원을 Abort한다. 실제 반복 reopen/장시간 idle에서의 GC·handle·timer 누수 검사는 하지 않았다. WPF Dispatcher/렌더 예외 경계는 F01/F03/F04/F06의 실패를 안전하게 회복할 보호가 부족하다. 반면 PollAsync는 백그라운드 로그 읽기 예외를 잡고 finally에서 종료 시 tracker를 정리한다.
- App의 좁은 구역 전체 렌더는 처음 리뷰어의 FSI 참조 문제로 실행되지 않았지만, 이후 임시 C#/.NET 10 프로그램에서 빌드된 OfficeView를 직접 사용해 재현했다. F09는 정적 호출 경로, F11은 모니터 기하 판정 재현이다. 실제 개인 파일 교체나 물리 화면 테스트로 오인하지 않는다.

## 후속 작업 한 건에 사용할 우선순위

**작업명: 개인 데이터 보존과 unattended 실행의 입력·출판 경계 보강.** 진행 중인 수정과 먼저 대조하고, 아래 순서대로 국소 수정한다. 이 보고서 자체는 수정 패치가 아니다.

1. **데이터 손상 방지:** F09의 demo/qa 개인 저장 차단, F10의 pack writer lock, F15/F16의 세대별 출판 및 유일한 temp 파일. 실패·동시 실행 후 마지막 정상 결과가 계속 로드되는 것이 완료 기준이다.
2. **앱 종료 방지:** F01 Settings 정규화, F02/F03 overflow-safe 검증, F04 필수 시트 참조 검사, F05 호환 가능한 fallback, F06 좁은 구역 렌더. 잘못된 입력은 경고/정상 fallback으로 처리되고, 정상 입력은 저장·렌더까지 유지되어야 한다.
3. **오래 멈추거나 관찰이 중단되는 경로:** F07 한 번의 도달성 계산, F08 일시 파일 잠금의 제한된 재시도. 지점 수가 증가해도 같은 geometry를 반복 탐색하지 않고, 짧은 공유 위반이 풀리면 snapshot 출판이 재개되어야 한다.
4. **자리와 복구의 정확성:** F11 실제 monitor working area 기반 restore/Recover, F12/F13 타일 예약과 queue 포화 처리, F14 이력 보존 상한. 생존자의 ID·책상 안정성 및 기존 undo/redo 동작은 유지한다.

기존 검사와 아래 관련 회귀 검사를 사용한다. UI 정책을 바꾸거나 새 기능/테스트 체계를 추가하는 작업으로 확대할 필요는 없다.

## 누락된 테스트

1. **설정:** `1e999`/`-1e999`, 숫자 필드 null/잘못된 타입, CLI NaN/Infinity, 거대한 settings 파일에 대해 읽기→적용→저장 전체 경로. ko-KR/de-DE의 실제 소수 JSON 필드 왕복도 상시 회귀 검사로 남긴다.
2. **레이아웃/팩 입력:** 큰 autoDesks 원점·간격, row/column의 int 최대값, 필수 sheetId 누락/null, 정상 PNG지만 불완전한 cozy 의존성, fallback 팩 정의를 바꾼 사용자 팩, 배열의 null 요소와 중첩 필수 필드 누락. 크기·타일·중복 ID·missing asset에 대한 기존 거부 검사는 재사용하고, 아직 없는 조합에서 fallback과 원본 파일 보존까지 확인한다.
3. **계산량:** 128×128에서 많은 반복/서로 다른 interaction point. 절대 시간 하나보다는 도달성 탐색을 geometry당 한 번 수행하는지 확인한다. zone/point 및 전체 PNG bytes/pixels 한도 검사도 필요하다.
4. **메모리 렌더/편집:** 폭 1 구역의 actual OfficeView 렌더, 막힌 좌석들의 대체 타일 중복, queue 용량을 넘는 인원, 막았다가 통로를 다시 열었을 때 경로 재계산. failed edit/save가 undo/redo와 직전 정상 파일·백업을 바꾸지 않는지 검사한다.
5. **원자적 저장/출판:** 목적지·백업 잠금, temp/replace/cleanup 오류, 기존 파일 생성 경쟁, facelock/seated의 중간 파일 실패, 같은 pack/캐릭터에 두 writer가 겹치는 경우. 최종 manifest의 모든 참조와 결과 세대가 일치해야 한다.
6. **Tracker 프로세스:** consumer의 delete-sharing 없는 일시 잠금 후 회복, 같은 canonical output 디렉터리의 이중 실행 차단, junction/다른 경로 표기로 동일 디렉터리를 가리킬 때의 배타성, 초깃값 layout 생성과 editor 저장의 경쟁. 반드시 모든 경로 override를 임시 경로로 지정한다.
7. **장기 실행:** 많은 서로 다른 session ID가 떠난 뒤의 history 상한, 실제 agent churn과 반복 reopen 후 bitmap/handler/timer/handle의 잔존, polling 중 close. 현재 active 수가 줄었을 때 회수될 항목을 명확히 검사한다.
8. **창/화면:** normal/topmost/desktop × transparent/click-through/borderless × opacity preset/live change; 100/150/200% mixed DPI, 음수 모니터 좌표, L자 빈 영역, 모니터 제거, 화면 끝에 일부만 걸친 복원, tray Recover, desktop host 유실. RootVisual 값과 별도로 실제 HWND/DWM 결과를 독립적으로 확인해야 한다. 사용자 입력/전경을 바꾸는 검사는 이번 요청에서 실행하지 않았다.
