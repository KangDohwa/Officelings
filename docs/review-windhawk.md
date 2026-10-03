# Windhawk 독립 검토

검토일: 2026-10-04. 대상은 `windhawk/vibedotcom.wh.cpp`, `windhawk/README.md`, `windhawk/compile.ps1`, `tests/windhawk/renderer_tests.cpp`, `tools/windhawk_*.ps1`, `docs/windhawk-notes.md`, `docs/state-schema.md`의 현재 작업 트리다. 실제 빌드 경로를 확인하기 위해 `windhawk/poc/compile.ps1`과 설치된 GDI+ 헤더도 읽었다.

**Explorer 배포 전 우선 해결할 문제는 타임아웃 메시지의 포인터 수명과 GDI+ 할당 실패다.** 두 문제는 Explorer 프로세스의 메모리 안전성에 영향을 준다. 데스크톱 자식 창의 입력 큐 결합과 중단 불가능한 작업도 종료 안정성의 중요한 위험이다. 일반 입력 검증은 상당 부분 구현되어 있지만, 캐시가 있는 경우의 잘못된 PNG 메타데이터와 v4 repaint 누락은 별도로 재현됐다.

소스·설정·설치 상태는 수정하지 않았다. 저장소에 작성한 파일은 이 보고서뿐이다. 빌드, 변형 검증 소스, 입력, 실행 파일, PNG는 모두 `%TEMP%`에 두었다. DLL을 Explorer에 로드하지 않았고, 창 생성 테스트, 키·마우스 입력, 최소화, Explorer 재시작, 작업 등록·실행, 커밋도 하지 않았다.

## 수행한 검증과 한계

- Windhawk clang **20.1.3**, `x86_64-w64-mingw32`, C++23, `-O2`로 현재 mod DLL과 현재 `renderer_tests.cpp`를 빌드했다. 두 빌드 모두 exit 0, 경고 없음.
- `windhawk/compile.ps1:3-7`은 출력 위치를 저장소로 고정하므로 직접 실행하지 않았다. 같은 하위 `poc/compile.ps1`에 `-OutputDirectory`를 지정하고 wrapper와 같은 bcrypt/ole32 및 테스트용 `-municode` 옵션을 전달했다. 첫 호출의 PowerShell 실행 정책 오류는 자식 PowerShell의 `-ExecutionPolicy Bypass`로 해결했으며 시스템 정책은 변경하지 않았다.
- 기존 v3 `artifacts/demo`와 v4 `artifacts/demo-latest` fixture로 각각 **54개 검사 통과**. README의 55개는 `--profile`의 추가 검사까지 포함한 기록이다. 이번에는 CPU profile을 실행하지 않았다.
- BOM이 있는 정상 입력과 `%TEMP%\…\한글 사용자\state.json` / 한글 pack·출력 경로도 각각 54개 검사 통과.
- 아래 잘못된 입력을 현재 `renderer_tests.exe`의 첫 번째 인자로 전달했다. 모두 exit 1로 정상 거부됐고, 접근 위반은 없었다.

| 임시 입력 | 결과 |
| --- | --- |
| 정상 JSON의 절반만 남긴 파일 | `JSON syntax` |
| 4 MiB + 1 byte 파일 | `File size limit` |
| 중복 `sequence` 키 | `Duplicate JSON key` |
| 70단계 중첩 배열 | `JSON complexity limit` |
| 문자열 안의 잘못된 UTF-8 `C0 AF` | `Invalid UTF-8` |
| `sequence=9223372036854775808` | `Integer bounds` |
| 65,537 byte 문자열 | `String too large` |

- 실제 mod를 `VIBEDOTCOM_TEST`로 include한 별도 임시 실행 파일로 F2, F5, F6을 검증했다. F2에서는 임시 실행 파일 안에서만 `GdipAlloc` 실패를 주입했다. 오류 대화상자를 억제한 이 프로세스가 `0xC0000005`로 종료됐으며 Explorer는 관련되지 않았다.
- PowerShell 파일 5개(wrapper, 하위 compiler, verify, runner, installer)의 구문 오류는 0개였다. `windhawk_verify.ps1` 전체, tracker runner와 installer는 실행하지 않았다.

검증 자료 위치:

```text
%TEMP%\vibedotcom-windhawk-review-d0f45a615b254ac7b58e0f985a7205c7\
  build.ps1, vibedotcom.log, renderer_tests.log
  inputs\*.json, baseline\, baseline-v4\, 한글 사용자\
  probe.cpp, probe.ps1, probe.exe
  alloc_probe.cpp, alloc_probe.ps1, alloc_probe.exe
```

**증거 수준:** “재현”은 독립 offscreen 프로세스에서 확인한 결과다. “정적/API 근거”는 코드 경로와 Windows/Windhawk 계약을 대조한 결과이며 실제 셸에서 장애를 발생시킨 것은 아니다. Win+D, 가상 데스크톱, mixed DPI, 여러 날의 메모리·GDI 추이는 실기 미검증이다.

## 발견 사항

### F1 — High / P1 위험: 타임아웃 뒤에도 사용될 수 있는 스택 포인터

**위치:** `windhawk/vibedotcom.wh.cpp:771-772` (`Hover`, `LVHITTESTINFO hit`).

**실패 시나리오:** wallpaper host와 icon host의 입력 큐가 분리된 topology 또는 shell/control 재구성 경합에서, 데스크톱 툴팁이 다른 스레드의 list-view에 `&hit`를 보내며 40ms 뒤 반환할 수 있다. 수신 WndProc가 메시지를 이미 받았지만 셸 부하나 다른 mod의 subclass 처리 때문에 늦게 완료되면, 송신 함수가 반환한 뒤에도 수신 측이 `hit.flags` 등에 쓸 수 있다. `LVM_HITTEST`는 `WM_USER` 이상의 control 메시지이고 자동 마샬링으로 보호되는 스택 복사본이 없다. 이는 Explorer 안에서 스택 수명 종료 후 쓰기를 만들 수 있다. [Microsoft의 타임아웃 후 메시지 수명 설명](https://devblogs.microsoft.com/oldnewthing/20110915-00/?p=9643), [hit-test 입력·출력 포인터 계약](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-listview_hittest).

**근거:** 정적/API 근거, 조건부 메모리 수명 위험. raised topology에서 Progman/DefView/list-view가 같은 입력 큐로 연결되어 있으면 timeout이 무시되어 이 특정 반환 후 쓰기 시나리오는 성립하지 않는다. 그 경우는 F3의 hang 위험이며, code는 hit-test 대상과의 queue 관계를 확인하거나 수신 완료 수명을 관리하지 않는다. 실제 분리된 topology에서의 접근 위반을 재현한 것은 아니다.

**최소 수정:** 우선 cross-thread pointer hit-test를 제거하고 안전하게 판정할 수 없는 list-view 위에서는 툴팁을 숨긴다. 기능을 유지하려면 수신 완료가 확인될 때까지 살아 있는 요청 객체를 사용하고, unload 전 완료 통지까지 배수해야 한다. 단순히 heap으로 바꾸고 타임아웃 직후 해제하거나, `PostMessage`로 교체해서는 안 된다. [완료 callback 계약](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagecallbackw).

### F2 — High / P1: GDI+ 객체 할당 실패가 Explorer 접근 위반으로 이어짐

**위치:** `windhawk/vibedotcom.wh.cpp:452`, `:488`, `:566`; 관련 오류 처리 `:753`, `:814`, `:832`.

**실패 시나리오:** `std::make_unique<Bitmap>` / `Font`가 일반 C++의 throwing allocation이라고 가정하고 곧바로 객체를 사용한다. 설치된 Windhawk 헤더의 `GdiplusBase::operator new`는 `GdipAlloc` 결과를 그대로 반환한다. 이 결과가 null이면 현재 `-O2` 빌드에서는 객체 생성 중 접근 위반이 발생할 수 있으며 `catch(std::exception)`나 바깥 `catch(...)`가 복구하지 못한다. 메모리 압박이나 큰 pack 교체 때 사용자에게 Explorer crash로 나타날 수 있다. [GDI+ 객체의 별도 할당 연산자](https://learn.microsoft.com/en-us/windows/win32/api/gdiplusbase/nf-gdiplusbase-gdiplusbase-operatornew).

**근거:** 재현. 실제 소스를 include하고 첫 render의 객체 할당만 실패시키는 `alloc_probe.exe`가 `-1073741819` (`0xC0000005`)로 종료됐다. `caught:` 출력은 없었다. 또한 오류 handler 안의 `Wide(e.what())`는 새 문자열을 할당하므로 `bad_alloc` 처리 중 다시 예외를 던질 수 있다. 로깅이 켜진 경우 최종 `UiThread` catch 바깥으로도 이 재실패가 나갈 수 있다.

**최소 수정:** GDI+ wrapper 객체를 throwing global allocation/명시적 placement construction 또는 안전한 flat API 소유 방식으로 생성한다. `Clone`의 내부 객체 생성 경로도 함께 확인한다. 생성 후 `if (!bitmap)`만 추가하면 생성자 자체의 null 접근을 막지 못한다. 실패 시 기존 buffer를 유지하고, 최종 오류 로그는 추가 할당·예외가 없는 고정 메시지로 작성한다. 접근 위반을 일반 C++ catch로 덮는 해결책은 부적절하다.

### F3 — High / P1 위험: desktop parenting이 셸 입력 큐와 느린 UI 작업을 결합함

**위치:** `windhawk/vibedotcom.wh.cpp:713-714`, `:727`, `:772`, `:798-802`, `:836`; IO `:242-257`.

**실패 시나리오:** Explorer의 Progman/WorkerW와 다른 스레드에서 child를 만든다. parent/child 관계는 입력 큐를 자동 결합하므로, 이 스레드는 더 이상 셸과 완전히 독립된 UI 실행기가 아니다. 그런데 같은 스레드가 동기 파일 읽기, JSON 파싱, PNG decode/hash, 전체 렌더를 수행한다. 허용된 removable drive가 멈추거나 큰 새 pack 처리에 시간이 걸리고 셸이 공유 큐의 동기 작업을 기다리면 데스크톱 입력이 같이 정체될 수 있다. 이후의 shell 메시지는 같은 큐 조건에서 40/250ms timeout이 보장되지 않을 수 있다. disable/reload는 `WaitForSingleObject(INFINITE)`에서 이 작업을 끝없이 기다린다. [자동 입력 큐 결합 설명](https://devblogs.microsoft.com/oldnewthing/20130607-00/?p=4143), [같은 큐에서는 timeout을 무시하는 계약](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw).

**근거:** 정적/API 근거, 조건부 hang 위험. Windhawk의 unload callback은 Engine thread에서 호출되므로 “항상 셸 UI thread가 직접 join해 교착한다”는 주장과는 구별해야 한다. 이 코드에서 lock 순환이나 반드시 발생하는 unload deadlock을 재현한 것은 아니다. [Windhawk mod lifetime](https://github.com/ramensoftware/windhawk/wiki/Mod-lifetime).

**최소 수정:** desktop child를 소유하는 UI thread에서는 blocking IO/parse/decode/render를 빼고 완료된 snapshot/buffer만 받게 한다. IO는 stop-aware/cancellable하게 만든다. 더 작은 초기 대응은 해당 안전성이 확보될 때까지 desktop embedding을 사용하지 않고 top-level fallback을 사용하는 것이다. **join timeout 뒤 그대로 DLL을 unload하거나 `TerminateThread`하는 방식은 수정안이 아니다.** 종료 확인 없이 callback 코드가 unmap되면 crash 위험이 생긴다. `docs/windhawk-notes.md:132`의 한계는 현재 viewer에도 적용된다.

### F4 — Medium / P2: native move/size·menu modal loop가 stop event를 보지 않음

**위치:** `windhawk/vibedotcom.wh.cpp:688`, `:795`, `:811-812`, `:836`.

**실패 시나리오:** normal/topmost 창의 이동·크기 변경 또는 시스템 메뉴가 `DefWindowProc`의 내부 modal loop를 돌고 있을 때 settings reload/disable이 들어온다. `g_stop`을 읽는 외부 loop로 아직 돌아오지 않았으므로 event만 signal해도 UI thread가 종료되지 않는다. unload가 사용자의 modal 동작 종료에 종속되고, native loop가 계속 남으면 reload도 완료되지 않는다. [move/size loop는 DefWindowProc 반환까지 지속됨](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-entersizemove).

**근거:** 정적/API 근거. 키·마우스 조작으로 재현하지 않았다.

**최소 수정:** event와 함께 소유 UI에 shutdown 메시지를 전달해 native mode/capture를 취소하고 외부 loop가 종료되게 한다. 필요하면 modal 구간에만 stop 검사 timer를 둔다. HWND 전달과 메시지 종료 경로는 unload 완료 전까지 유효해야 한다. event wait의 timeout만 줄이는 수정은 modal loop에 영향을 주지 않는다. [WM_CANCELMODE 동작](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-cancelmode).

### F5 — Medium / P2: warm image cache가 잘못된 sheet 치수를 승인함

**위치:** `windhawk/vibedotcom.wh.cpp:554`, `:746`, `:802`, `:814`; 정상 cold 검증 `:415`.

**실패 시나리오:** 정상 pack을 읽은 뒤 새 JSON이 같은 sheet ID/path/SHA를 유지하면서 `width`를 두 배로 늘리고 sprite.x를 실제 PNG 바깥으로 지정한다. parser는 새 manifest 치수에 맞는 frame이라 승인한다. `Renderer::Load`는 ID/path/SHA만 비교하므로 기존 decoded bitmap의 실제 치수를 재검증하지 않는다. snapshot이 먼저 commit된 다음 `Crop failed`가 render에서 발생하고 전체 UI thread가 종료된다. 이후 정상 JSON이 돌아와도 읽는 스레드가 없으므로 settings reload 전에는 회복하지 않는다. 이는 malformed candidate에서 마지막 정상 scene을 보존한다는 `windhawk/README.md:81-85`, `docs/state-schema.md:18`의 기대를 깨뜨린다.

**근거:** 재현. 128px agent sheet의 manifest width를 256으로, 해당 sprite.x를 128로 변경한 임시 입력에서:

```text
reused-sheet: load_accepted=1 render_error=Crop failed
cold-sheet: rejected=1 reason=PNG metadata mismatch
```

**최소 수정:** cached image 재사용 시 실제 `GetWidth()/GetHeight()`가 candidate sheet와 같은지 검사해 `Load` 단계에서 거부한다. graphics 오류도 한 snapshot의 실패로 처리해 기존 buffer를 남기고 loop를 유지해야 한다. 상태·image commit 순서를 유지하며 renderer 내부 실패를 UI 전체의 영구 종료로 만들지 않는다.

### F6 — Medium / P2: v4 semantic state가 repaint key에서 빠짐

**위치:** `windhawk/vibedotcom.wh.cpp:574`, `:591`, `:617`, `:801-802`.

**실패 시나리오:** `agent.state`만 working/reporting에서 idle 등으로 바뀌고 pose·sprite는 같은 순간에 v4의 관계선 또는 desk nameplate 표시 조건이 달라진다. 하지만 `VisualKey`는 `a.state`를 넣지 않는다. 새 snapshot과 tooltip은 갱신돼도 repaint를 생략하므로 화면에는 이전 관계선/이름표가 남는다. 추가적인 pose 변화가 없는 동안 계속 유지된다.

**근거:** 재현. v4로 설정한 동일 scene에서 parent 근처의 child state만 working→idle로 변경했다. 결과: `equal_visual_key=1 different_pixels=1`. 실제 renderer 두 출력의 픽셀 비교와 hash 비교가 서로 다른 결과를 보였다.

**최소 수정:** `VisualKey`에 `a.state`를 추가한다. 같은 scene/같은 frame에서 state만 바뀌는 기존 harness의 최소 regression case를 추가한다.

### F7 — Medium / P2: WorkerW 생성 실패·소실 후 같은 Progman에서는 복구 요청이 없음

**위치:** `windhawk/vibedotcom.wh.cpp:651`, `:712-715`, `:717-738`; 방송 처리 `:693`.

**실패 시나리오:** 첫 `0x052C` 요청이 timeout/failure로 끝나도 `requestedProgman`을 성공한 것처럼 기록한다. 또는 wallpaper/display 재구성으로 WorkerW만 소실되고 Progman HWND는 유지된다. helper는 probe를 앞당기지만 다음 probe도 같은 Progman이라는 이유로 layer 생성 요청을 하지 않는다. shell이 WorkerW를 스스로 복원하지 않는 경우 viewer는 계속 bottom-level fallback에 남는다. fallback은 desktop child와 Win+D/아이콘 동작이 같다고 보장할 수 없다.

**근거:** 정적 제어 흐름으로 확인. `windhawk/README.md:79`의 “repair”는 존재하는 host 재탐색·order 조정이며 없어진 layer의 재생성까지 보장하지 않는다. `docs/windhawk-notes.md:202`에도 같은 PoC 제한이 기록돼 있다.

**최소 수정:** 반환 결과를 확인하고, host가 사라졌거나 생성에 실패했을 때만 제한된 backoff로 요청을 재시도한다. 성공한 host identity와 “요청해 본 Progman”을 같은 값으로 취급하지 않는다. 모든 probe에서 무조건 요청하거나 shell 창을 hide/destroy하는 대응은 필요 없다.

### F8 — Medium / P2: cloaked office의 hover가 다른 가상 데스크톱에 나타날 수 있음

**위치:** `windhawk/vibedotcom.wh.cpp:757`, `:765-767`, `:778-783`, `:799`.

**실패 시나리오:** top-level click-through/fallback office가 다른 virtual desktop이나 shell suppression 때문에 DWM-cloaked 상태가 된다. `IsWindowVisible`은 true일 수 있다. code는 cloak/current-desktop 상태를 확인하지 않고 전역 z-order에서 office를 찾으면 `visible=true`로 승인한다. desktop-level 경로도 Progman/icon-host root라는 이유로 허용한다. 따라서 보이지 않는 office 위치에서 새 topmost tooltip이 생성될 수 있다. 같은 조건에서는 보이지 않는 scene의 렌더도 계속될 수 있다. [WS_VISIBLE 상태와 실제 shell cloaking의 차이](https://devblogs.microsoft.com/oldnewthing/20200302-00/?p=103507).

**근거:** 정적/API 근거. 실제 virtual desktop 전환이나 Win+D 실행은 하지 않았으며 각 topology의 shell suppression 방식은 실기 확인이 필요하다.

**최소 수정:** hover/렌더 진입에서 office 및 필요한 ancestor의 `DWMWA_CLOAKED`를 확인하고 숨겨진 경우 tooltip을 끈다. top-level normal/topmost/fallback에는 필요하면 `IVirtualDesktopManager::IsWindowOnCurrentVirtualDesktop`로 가시성 판정을 보완한다. z-order 비교에서도 cloaked 창을 visible 창처럼 취급하지 않는다. helper에 tooltip을 소유시키는 것만으로 office와 같은 virtual desktop 수명이 보장되지는 않는다.

### F9 — Medium / P2: display 변경 시 geometry 복구가 없고 viewport clamp가 실제 HWND 크기와 갈라짐

**위치:** `windhawk/vibedotcom.wh.cpp:673`, `:675-678`, `:693`, `:725-738`, `:759`.

**실패 시나리오:** office가 있는 보조 모니터를 제거하거나 virtual-screen 원점을 변경해도 probe는 host HWND와 sibling order만 비교한다. 설정 좌표를 유효한 monitor 영역으로 보정하지 않으며 같은 parent의 child 좌표를 다시 screen→parent로 변환하지도 않는다. 해당 shell이 window 위치를 대신 보정하지 않는 경우 office가 화면 밖에 남는다. 별도로 maximize 또는 DPI 권장 rect가 실제 client를 4096×2048보다 크게 만들면 `WM_SIZE`는 실제 HWND를 줄이지 않고 `ui.width/height`만 clamp한다. 예를 들어 client 높이 2100이면 아래 52px는 buffer 영역 밖이 되고 magenta color key로 비거나 hover 대상에서 빠진다. `ptMaxTrackSize`만 설정한 것은 모든 programmatic/maximize 크기의 제한이 아니다.

**근거:** 정적 코드 근거. monitor removal·maximize·DPI 변경은 실행하지 않았다. current README가 의도한 정수 device-pixel zoom 자체를 결함으로 보는 것은 아니다.

**최소 수정:** display 변경에서 현재 window rect를 유효한 monitor에 맞추고, child라면 target DPI context에서 좌표를 다시 계산한다. 실제 HWND client와 render/hit-test extent를 일치시킨다. buffer 상한을 유지한다면 DPI rect/maximize에도 실제 window 크기를 제한하고, `WM_GETMINMAXINFO`의 maximize 항목도 적절히 처리한다. 정상 사용자 이동·음수 좌표는 보존하고 완전히 offscreen인 경우에만 복구한다.

### F10 — Medium / P2 위험: parent와 다른 DPI context에서 hover 좌표를 계산하고 tooltip은 고정 DPI

**위치:** `windhawk/vibedotcom.wh.cpp:719-728`, `:758-772`, `:781-783`, `:794`; `TipProc` `:697-703`.

**실패 시나리오:** embedded office는 parent context로 생성하지만 UI thread를 곧 PMv2로 복원한다. 반면 `WM_SIZE`/paint callback은 생성 당시 context에서 실행되며 hover는 PMv2 outer loop에서 cursor·client·list-view 좌표를 계산한다. parent가 system/unaware context인 mixed-DPI 환경에서는 서로 다른 좌표 공간을 암묵적으로 같다고 가정하므로 sprite hover와 icon hit-test가 틀어질 위험이 있다. tooltip 자체도 -13px font, 폭 364px, 높이 상한 440px를 전 생애에 고정하고 `WM_DPICHANGED`를 처리하지 않아 100→200% 이동에서 글자/레이아웃이 DPI에 맞게 갱신되지 않는다. [HWND callback context의 자동 전환과 API 가상화](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-improvements-for-desktop-applications), [DPI별 값의 재계산 요구](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows).

**근거:** context 차이는 코드와 API 계약으로 확인했다. 실제 좌표 오차의 수치는 측정하지 않았으므로 conditional DPI 위험으로 분류한다. tooltip의 고정 크기·font와 DPI handler 부재는 정적 확인 사항이다.

**최소 수정:** office와 list-view의 좌표 변환을 각각 해당 HWND의 DPI context에서 수행하거나 명시적 physical/logical 변환을 사용한다. tooltip은 cursor monitor DPI로 font·padding·measure bounds를 갱신하고 DPI 메시지를 처리한다. pixel-art의 정수 zoom 정책을 바꿀 필요는 없다.

### F11 — Low / P3: relative path 검증이 존재하지 않을 수 있는 C: 드라이브에 의존함

**위치:** `windhawk/vibedotcom.wh.cpp:321`; `LocalPath` `:224-225`.

**실패 시나리오:** 실제 state/pack이 유효한 D: 등의 local drive에 있어도 C:가 없는 환경에서는 모든 sheet가 `SheetPath(L"C:\\Vibedotcom", ...)`에서 실패한다. 이 검사는 relative 문자열 문법 확인용인데 `GetDriveTypeW(C:)`까지 실행한다. 결과적으로 viewer가 영구 offline으로 남는다. 비ASCII username 문제와는 별개이며 한글 경로는 실제 검사에서 통과했다.

**근거:** 정적 코드 근거. 이 머신의 C: 드라이브를 변경하거나 해당 환경을 재현하지 않았다.

**최소 수정:** relative component 문법 검사와 실제 root/drive 확인을 분리한다. 실제 local drive 여부 및 containment 검사는 configured root를 아는 `Renderer::Load` 단계에서 수행하면 된다.

## 추가 검토 결과

| 영역 | 확인 결과와 남은 한계 |
| --- | --- |
| 정상 unload/settings reload | 설정은 thread 시작 전에 복사되고 join 이후에만 변경된다. UI graphics는 UI scope에서 `GdiplusShutdown` 전에 파괴된다. stop event·thread handle은 정상 경로에서 close된다. F3/F4가 종료 지연 위험이다. 실제 reload 반복은 미검증. |
| message loop exit | `WM_CLOSE`는 running을 false로 만들고, `WM_QUIT`도 처리하며, host 소실의 `WM_NCDESTROY`는 window를 null로 만든다. 재생성을 위해 모든 destroy에서 quit하지 않는 설계는 타당하다. F4의 native modal loop는 별도 경로다. |
| shell instance / secondary Explorer | known shell PID가 다르면 init 또는 probe에서 거부/중단한다. shell이 없을 때는 창을 만들지 않고 기다린다. folder-only process에 office를 무조건 생성하는 결함은 찾지 못했다. guard 통과 후 shell이 교체되는 좁은 race와 실제 시작 순서는 실기 미검증. |
| Explorer process restart | 프로세스 exit는 controlled mod unload와 다른 경로다. raw HANDLE과 heap-only Settings global에 blocking destructor가 없으므로 global thread join/graphics destructor 문제는 발견하지 못했다. 새 Explorer에서 재주입·창 복원은 시험하지 않았다. [Windhawk process shutdown 계약](https://github.com/ramensoftware/windhawk/wiki/Global-objects-and-process-shutdown). |
| GDI/GDI+ 자원 | 정상 경로에서 file handles, BCrypt provider, PNG stream/HGLOBAL, brush, selected tooltip font/DC, bitmap/font caches가 해제된다. 일반적인 매 paint handle 누수는 찾지 못했다. F2와 예외 도중 cleanup의 신뢰성, 실제 GDI object 추이는 별도다. |
| 여러 날의 memory growth | snapshot은 교체되며 agents를 누적하지 않는다. pack 합계는 128 MiB, crop cache는 64 MiB/512 entries, room은 최대 2048×2048, viewport는 8,388,608 pixels로 제한된다. fonts의 호출 size는 제한된 상수다. unbounded cache를 찾지 못했다. old/new pack·buffers 동시 보유에 따른 순간 peak는 이 단일 한도보다 크며 여러 날의 soak test는 하지 않았다. |
| JSON/BOM/partial write | file 크기·mtime 전후 확인, read/write/delete sharing, strict Unicode, depth/node/string/number limits가 있다. malformed 입력 거부를 재현했다. timestamp+length 검사는 임의의 in-place writer의 동시 내용 변경을 완전한 transaction으로 보장하지 않는다. 현재 schema의 flush+atomic rename 계약은 이 문제를 줄인다. 모든 유효 candidate의 graphics 안전성은 F5에서 깨진다. |
| path | wide Win32 API 사용과 한글 경로 검증이 통과했다. traversal/UNC/ADS 거부와 opened handle의 reparse containment 검사가 있다. URL/network 경로 지원은 의도적으로 없다. drive-root 및 long-path의 모든 조합은 시험하지 않았다. F11은 별도 portability 결함이다. |
| integer overflow / null | sequence는 exact int64이고 geometry 곱셈·frame 합산은 검증된 범위 안이다. 범위 초과 입력 거부도 확인했다. 여기서 일반적인 geometry integer overflow는 찾지 못했다. GDI+ allocator의 null은 F2로 재현됐다. |
| idle CPU | live repaint는 visual key가 같으면 생략되고 파일 stamp는 5Hz, tooltip polling은 10Hz다. offline은 100ms wake 후 1Hz 시각 변화에 맞춰 그린다. occluded/cloaked 상태는 `IsWindowVisible`만으로 그려질 수 있다(F8). 이번에 실제 Explorer CPU를 측정하지 않았다. |
| offline/stale state | 10s보다 오래되거나 10s보다 더 미래인 timestamp는 offline이다. 정상 scene/PNG를 남기고 idle frame을 선택하며 work bubble을 숨긴다. sequence reset은 허용된다. malformed/누락 input 실패만으로 즉시 offline을 표시하는 것이 아니라 마지막 정상 generatedAt의 10s 판정을 기다린다. F5/F2로 thread가 종료되면 이 복구 정책도 실행되지 않는다. |
| click-through / focus | layered + transparent, desktop/through의 NOACTIVATE, TOOLWINDOW, manual show의 SWP_NOACTIVATE 조합은 적절하다. normal mode의 의도적인 클릭 활성화는 README와 일치한다. input 전달·foreground 유지의 실기는 미검증이며 HTTRANSPARENT만으로 타 thread click-through를 보장한다고 판단하지 않았다. |
| 24H2/classic/z-order | raised topology는 Progman child를 DefView 바로 아래에, classic은 icon-host 뒤의 visible WorkerW에 넣는다. shell 창을 직접 restyle/hide/destroy하지 않는다. z-order/creation API 실패를 충분히 로그하지 않으며 HWND identity 변화 시 destroy→recreate의 blank interval이 있어 flicker/새 geometry는 실기 확인 대상이다. F7은 자동 layer 복구 결함이다. |
| Win+D / virtual desktops | bottom-level top-level fallback과 desktop child의 Show Desktop 동작은 같은 계약이 아니다. 코드에 Win+D suppression/restore 확인 경로가 없고 cloak 처리도 없다(F8). hide/minimize/restore를 실행하지 않았으므로 “Win+D가 반드시 창을 없앤다/복원한다”는 단정은 하지 않는다. |
| tracker scripts | paths는 literal resolve 또는 인자로 전달되고 현재 사용자 SID/limited interactive task를 사용한다. installer는 registration만 수행한다. runner child의 정상 종료 처리는 있으나 강제 task-stop의 child 수명과 restart 동작은 문서도 미검증으로 명시한다. parser 통과가 이러한 runtime 동작의 증거는 아니다. |

### 기존 검증 수치의 해석

`tests/windhawk/renderer_tests.cpp:1-3`과 `windhawk/vibedotcom.wh.cpp:624`에서 HWND host를 통째로 제외한다. 따라서 tests 통과는 F1/F3/F4/F7/F8/F9/F10의 반증이 아니다. `Profile`은 `next=idle`로 고정한 합성 scene의 key를 검사한다(`renderer_tests.cpp:23-35`). 이 측정을 실제 tracker→host→paint의 상태 변화나 shell lifetime 검증으로 확대하면 **Tautology / verifier=designer**에 해당한다. 독립 확인은 실제 export의 semantic state 변화, Win32 host 수명 및 Explorer CPU/GDI 관찰이 필요하다.

README는 CPU와 WPF 비교가 offscreen/synthetic이며 실제 Explorer 수치가 아니라고 명시한다(`windhawk/README.md:116-120`). 그 제한은 타당하다. 이번 검토는 0.19%/1.89%와 99.71% pixel match를 재측정하지 않았고, 임의 pack/agent 수나 Explorer에서의 목표 CPU를 달성했다고 인정하지 않는다. 이번 F6의 key-vs-실제-pixel 비교는 key 자체를 정답으로 사용하지 않아 기존 repaint 검사와 다른 결함을 드러냈다.

## 후속 작업용 우선순위

1. **P1 — Explorer 메모리 안전성:** F1의 pointer timeout hit-test 제거/안전한 completion 수명 처리, F2의 GDI+ allocation 경로와 무할당 최종 오류 처리. 완료 기준은 느린 receiver 뒤의 pointer 수명 검사와 standalone allocation fault가 접근 위반 없이 기존 scene을 보존하는 것이다. 아직 Explorer에 주입하지 않는다.
2. **P1 — desktop UI/종료 안전성:** F3의 blocking 작업을 공유 input queue에서 제거하고 중단 가능한 수명을 확보한다. F4의 native modal stop도 함께 처리한다. 실제 join 완료 전 DLL 반환/unmap을 허용하지 않는다. 첫 확인은 독립 Win32 test host에서 수행하고 실제 셸 검증은 별도 승인된 단계로 둔다.
3. **P2 — 입력 복구와 v4 repaint:** F5의 cached bitmap 치수 재검증 및 render 실패 격리, F6의 `a.state` key 추가. 위 두 재현 case를 기존 harness에 최소 regression으로 추가한다.
4. **P2 — host 복구/가시성:** F7의 실패·소실 시 제한된 재요청, F8의 cloak/current-desktop 판정과 tooltip 억제. 이후 24H2와 classic에서 wallpaper/display 변경, Win+D, virtual desktop switch를 확인한다. 실패하는 shell API는 결과·오류를 남긴다.
5. **P2 — monitor/DPI:** F9의 offscreen 복구 및 실제 viewport 크기 일치, F10의 target-context 좌표와 tooltip DPI 갱신. 100/150/200%, 음수 좌표, monitor 제거, maximize를 포함해 확인한다. 정수 pixel-art zoom은 유지한다.
6. **P3 — 경로 portability:** F11의 C: 의존성을 제거한다. 마지막으로 이미 수정된 상태에서 실제 reload 반복·idle/occluded CPU·memory/GDI soak 및 task-stop의 child 종료를 별도 검증한다. offscreen 통과를 이 실기 검증의 완료로 기록하지 않는다.
