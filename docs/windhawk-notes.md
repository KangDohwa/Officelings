# Windhawk 조사 및 desktop PoC

조사일: 2026-10-03. 프로젝트 맥락은 `SPEC.md`를 따랐다. 이번 결과는 조사와 최소 C++ PoC이며, 전체 agent viewer는 아니다. 작성 범위는 이 문서와 `windhawk/poc/`뿐이다.

**결론:** 설치된 Windhawk 1.7.3의 clang으로 GDI+를 사용하는 실제 mod DLL을 컴파일할 수 있다. Explorer 내부의 전용 UI 스레드에서 desktop child window를 만드는 설계가 가능하다. 다만 WorkerW 방식은 비공개 shell 동작에 의존하며, 컴파일 성공은 화면 표시·언로드·Windows 버전 호환성의 실행 검증을 의미하지 않는다. LogonUI의 보안 잠금 화면 표시 기능은 지원하지 않는다.

## 1. 실제 설치에서 확인한 것

| 항목 | 직접 확인한 값/파일 |
| --- | --- |
| 앱 버전 | `C:\Program Files\Windhawk\windhawk.exe`: FileVersion/ProductVersion **1.7.3** |
| 엔진 | `windhawk.ini`의 `EnginePath=Engine\1.7.3`; `Engine\1.7.3\64\windhawk.dll` 버전 **1.7.3** |
| 컴파일러 | `Compiler\bin\clang++.exe --version`: **clang 20.1.3**, LLVM revision `923a5c4f83d2b3675bb88e9fe441daeaa4d69488` |
| 주의할 기본 target | clang 단독 실행은 `i686-w64-windows-gnu`; x64 DLL은 **`-target x86_64-w64-mingw32`를 명시**해야 함 |
| 공용 API 헤더 | `Compiler\include\windhawk_api.h`, 내부 연결 헤더 `windhawk_api_internal.h` |
| 링크 라이브러리 | `Engine\1.7.3\64\windhawk.lib`; 별도로 `32`, `arm64` 엔진도 존재 |
| GDI+ | `Compiler\include\gdiplus.h`, `Compiler\x86_64-w64-mingw32\lib\libgdiplus.a` 존재; 실제 링크 성공 |
| 컴파일 구현 | `UI\resources\app\extensions\windhawk\dist\extension.js`의 `compileModInternal` 직접 확인 |
| OS | 레지스트리 `DisplayVersion=25H2`, build **26200.9457**. 오래된 `ProductName` 값은 Windows 10 Home으로 남아 있으므로 그 값만으로 OS 세대를 판단하지 않음 |

로컬 실제 예제는 `C:\ProgramData\Windhawk\EditorWorkspace\mod.wh.cpp`의 **Codex Gauge Taskbar 0.4.0**이다. `@include explorer.exe`, `@architecture amd64`, YAML settings, `Wh_GetIntSetting`, 문자열 setting의 조회/해제, 스레드, 설정 변경 시 unload/reload, UI 정리 콜백을 확인했다. 이것은 기존 taskbar mod의 예제이며 이 PoC에 그 구현을 복사하지 않았다.

`C:\ProgramData\Windhawk\ModsSource\local@codex-gauge-taskbar.wh.cpp`와 아래의 실제 산출물도 확인했다. 이 설치·소스·설정은 변경하지 않았다.

```text
C:\ProgramData\Windhawk\Engine\Mods\64\
  local@codex-gauge-taskbar_0.4.0_961002.dll
  local@codex-gauge-taskbar_0.3.2_936815.dll
  libc++.whl
  libunwind.whl
  windhawk-mod-shim.dll
```

### 컴파일 및 로딩 방식

설치된 UI 확장은 단일 `.wh.cpp` 소스를 stdin으로 clang에 전달한다. 주요 인자는 C++23, `-O2 -shared`, Unicode/Windows version 매크로, `-DWH_MOD`, 실제 `WH_MOD_ID`/`WH_MOD_VERSION`, 아키텍처에 맞는 `windhawk.lib`, `-include windhawk_api.h`, `-Wl,--export-all-symbols`와 metadata의 `@compilerOptions`이다. 선택적으로 PCH를 사용한다. 결과 이름은 `{modId}_{version}_{6자리 난수}.dll`이며 target별 `Engine\Mods\64` 등의 디렉터리에 배치한다. 엔진은 include/exclude 대상 프로세스에 DLL을 로드하고 콜백을 호출한다. 파일을 임의로 Mods 폴더에 복사하는 것은 mod 등록의 대체 수단이 아니다. [공식 mod 형식 설명](https://github.com/ramensoftware/windhawk/wiki/Creating-a-new-mod), [공식 CLI 컴파일 스크립트](https://github.com/ramensoftware/windhawk-mods/blob/main/scripts/compile_mod.py).

실제 DLL은 `libc++.whl`, `libunwind.whl`을 import한다. 설치된 확장도 compiler runtime을 이 이름으로 복사한다. 따라서 scratch DLL은 일반 EXE처럼 직접 실행하거나 `rundll32`로 실행할 대상이 아니다. 실행 등록은 Windhawk 편집기에서 수행한다.

### 확인한 API와 수명주기

| API/콜백 | 사용 및 제약 |
| --- | --- |
| `BOOL Wh_ModInit()` | 성공 시 TRUE. FALSE를 반환하면 후속 콜백 없이 unload되므로 실패 경로에서 이미 만든 자원을 직접 정리해야 함 |
| `void Wh_ModAfterInit()` | 초기화와 엔진의 hook 적용 이후. 초기 Explorer 시작을 막지 않도록 여기서 UI 스레드를 시작하는 것이 적절함 |
| `BOOL Wh_ModSettingsChanged(BOOL* reload)` | `*reload=TRUE; return TRUE;`로 설정 변경 시 controlled unload/reload 요청 가능. 인자 없는 void 변형도 있음 |
| `int Wh_GetIntSetting(PCWSTR, ...)` | 없는 값/오류는 0. YAML default와 별개로 유효 범위를 검증해야 함 |
| `PCWSTR Wh_GetStringSetting(PCWSTR, ...)` | 없는 값/오류는 빈 문자열. 사용 후 반드시 `Wh_FreeStringSetting`; 반환 포인터를 다른 스레드에 그대로 넘기지 말고 복사 |
| `Wh_Log(L"...", ...)` | 로깅을 켰을 때만 출력. 인자도 그때 평가되므로 필수 동작을 로그 인자 안에서 수행하면 안 됨 |
| `BOOL Wh_SetFunctionHook(void*, void*, void**)` | 원래 함수 trampoline 반환 가능. 이 viewer에는 필요 없음. init에서 등록한 hook은 엔진이 적용하고 unload 때 제거하지만, mod의 HWND/스레드는 엔진이 정리하지 않음 |
| `void Wh_ModBeforeUninit()` | hook 제거 전; hook이 있다면 신규 작업을 중단시키는 단계 |
| `void Wh_ModUninit()` | hook 제거 후. 자신의 창, thread, callback, GDI 자원을 정리하고 나서 반환 |

이 표는 설치된 헤더와 [공식 콜백 문서의 기록](https://github.com/ramensoftware/windhawk/wiki/Creating-a-new-mod/1166df7429ab1076b7bdf70453b1570d77ae7004)을 교차 확인했다. `Compiler\compile_flags.txt`와 EditorWorkspace의 flags에 있는 **`WH_EDITING`은 편집기용 stub 모드**다. 이를 실제 DLL 컴파일에 넣으면 실제 API 연결을 검증한 것이 아니다. PoC 빌드에는 넣지 않았다.

## 2. 전체 viewer에 권장하는 설계

### 프로세스와 스레드

`@include explorer.exe`만 사용한다. 별도 folder Explorer 프로세스에도 주입될 수 있으므로 `GetShellWindow()` 또는 Progman의 PID가 현재 프로세스와 같은지 확인하고 shell Explorer에서만 창을 만든다. shell이 아직 없으면 UI 스레드가 중단 가능한 대기로 재탐색한다. Explorer와 시스템 전체의 함수 hook은 사용하지 않는다.

agent 로그 파싱은 SPEC의 별도 tracker에 남긴다. mod는 `%LOCALAPPDATA%\Vibedotcom\state.json`과 `atlas.png`만 읽는 consumer다. 실제 스키마는 tracker 구현과 맞춰야 한다. JSON/이미지는 크기와 좌표를 제한하고, 읽기 공유에는 `FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE`를 사용해 atomic rename을 방해하지 않는다. 실패 시 마지막 정상 snapshot을 유지하되 stale/missing 상태를 표시한다. atlas decode 결과는 캐시하고 version/mtime가 바뀔 때만 다시 읽는다. 매 프레임 agent 원본 로그나 PNG를 재파싱하지 않는다.

하나의 전용 UI 스레드가 HWND, message pump, timer, tooltip, GDI+와 render cache를 소유한다. 초기 버전은 작은 snapshot만 낮은 빈도로 읽되 네트워크 경로는 받지 않는다. IO가 UI를 막는 것이 확인되면 중단 가능한 reader 스레드 하나를 추가하고 불변 snapshot을 전달한다. thread에서 C++ 예외가 escape하지 않도록 실제 JSON/이미지 reader의 실패 경계를 둔다. PoC에는 IO/JSON reader가 없다.

### Desktop host: 두 구조를 분리해야 함

창 클래스 이름 하나나 OS version만 믿지 말고 HWND의 **부모 관계, PID, styles와 Z-order**를 검사한다. 필요한 경우 Progman에 비공개 `0x052C` 요청을 `SendMessageTimeout`으로 보내 desktop layer 생성을 요청한다. PoC는 `(wParam=0xD,lParam=0)`와 `(0xD,1)`을 각각 250ms 제한으로 한 Progman당 한 번 보낸다. 이 요청은 shell layout을 바꿀 수 있으며 공식 Windows API 계약은 아니다.

1. **Classic WorkerW:** top-level Progman/WorkerW 중 `SHELLDLL_DefView`를 가진 icon host를 찾는다. 그 뒤의 top-level WorkerW 중 DefView가 없는 wallpaper host를 확인한다. 그 WorkerW 아래에 **우리 WS_CHILD 창**을 만든다. 무관한 숨은 WorkerW를 첫 번째 이름 일치만으로 선택하지 않는다.
2. **Windows 11 24H2 이후 raised/raw desktop:** Progman의 `WS_EX_NOREDIRECTIONBITMAP`, 그 아래 DefView 및 **자식 WorkerW**를 탐색한다. 우리 창은 **Progman의 WS_EX_LAYERED 자식**이며, WorkerW의 형제다. 원하는 위→아래 순서는 `DefView → 우리 창 → WorkerW`. `SetWindowPos(ourWindow, defView, ..., SWP_NOACTIVATE)`로 DefView 아래에 넣고 실제 순서를 검증한다. 이 구조에서 WorkerW에 단순히 parent하거나 Progman에 일반 GDI child만 만들면 표시 실패 가능성이 있다. [Lively 개발자의 설명](https://github.com/lively-community/lively/discussions/3004), [고정 revision의 실제 desktop 탐색 코드](https://github.com/lively-community/lively/blob/d27589c821ed48248f2b9ff95e236400c20540a3/src/Lively/Lively/Core/WinDesktopCore.cs).

```text
Classic:                    Raised/raw:
icon host                   Progman [NOREDIRECTIONBITMAP]
  SHELLDLL_DefView             SHELLDLL_DefView [LAYERED]
    SysListView32                 SysListView32
wallpaper WorkerW              our child [LAYERED]
  our child                    WorkerW [wallpaper]
```

호환성을 위해 shell HWND를 hide/destroy하거나 shell의 style을 변경하지 않는다. host를 찾지 못하면 기다리면서 로그를 출력한다. 전체 viewer에서는 미지원 구조일 때 일반 window로 명시적으로 전환하고, desktop 모드가 성공한 것으로 처리하지 않는다. PoC는 desktop host 대기를 계속한다.

shell 재구성으로 자식 HWND가 파괴될 수 있다. `WM_NCDESTROY`에서 자신의 HWND를 무효화하고, host/DefView/WorkerW 변화를 재탐색해 창을 다시 만든다. 전체 viewer는 1초 정도의 검사와 함께 숨은 top-level helper window로 `TaskbarCreated`, display/theme/DPI 관련 알림을 받는다. message-only window는 broadcast 수신을 대신하지 못한다. 기존 host의 Z-order 변화도 검사한다. PoC는 1초마다 host identity를 검사하고 창을 재생성하지만, 기존 창의 모든 Z-order 복구·monitor 이동·broadcast 처리는 구현하지 않았다.

### 이 PC의 읽기 전용 검사

`windhawk/poc/inspect-desktop.cpp`를 번들 clang으로 EXE로 만들어 실행했다. 실행기의 calling desktop은 `CodexSandboxDesktop-...`여서 일반 `EnumWindows`로는 shell을 얻지 못했다. `OpenDesktopW(L"Default", ..., DESKTOP_READOBJECTS | DESKTOP_ENUMERATE)`와 `EnumDesktopWindows`로 일반 desktop을 **열거만** 했다. desktop 전환·`0x052C` 전송·window 생성·Windhawk 등록은 하지 않았다. 결과는 `desktop-topology.txt`에 있다.

```text
Progman:          hwnd=0x1015C, pid=1176, ex=0x00200080, rect=0,0,2560,1600
SHELLDLL_DefView: hwnd=0x10160, parent=0x1015C, ex=0x00080000
SysListView32:    hwnd=0x10162, parent=0x10160
```

`0x00200000`은 `WS_EX_NOREDIRECTIONBITMAP`, `0x00080000`은 `WS_EX_LAYERED`다. 읽기 시점에는 Progman 아래 WorkerW가 없었다. 이 결과는 PoC 실행 시 layer 생성 후의 화면이나 계층을 검증한 증거가 아니다. 무관한 비표시 top-level WorkerW가 여러 개 있었으므로, 첫 번째 WorkerW를 선택하는 구현은 이 PC에서도 부적절하다.

### 모드·조작·렌더링

| 挙動 | 実装方針 |
| --- | --- |
| Normal | 자신의 top-level window. 일반 frame/resize를 허용하고 focus 시 `+/-`, wheel/ctrl+wheel로 zoom |
| Always-on-top | 자신의 top-level window에 `SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE)`. 해제는 `HWND_NOTOPMOST`. desktop child에 TOPMOST를 붙이는 동작과 구분 |
| Desktop | 위 host의 자식으로 일반 앱과 아이콘 아래에 표시. 모드 전환 시 UI 스레드에서 기존 창을 파괴하고 새 창을 만들면서 geometry/zoom을 옮기면 DPI 차이에 대처하기 쉬움 |
| Click-through | **layered window**에 `WS_EX_TRANSPARENT`, 수동 표시에는 `WS_EX_NOACTIVATE`. 일반 window의 `WS_EX_TRANSPARENT`만으로 모든 input 통과를 보장하지 않음. `HTTRANSPARENT`에도 같은 thread의 하위 window를 탐색한다는 제약이 있어 그것만 사용하지 않음 |
| Tooltip | UI 스레드가 `TOOLTIPS_CLASS` tracking tooltip을 관리. desktop/through에서는 `WM_MOUSEMOVE`를 기대하지 않고 `GetCursorPos`를 낮은 빈도로 읽어 sprite 좌표로 역변환. 일반 앱에 가려졌거나 desktop이 전경이 아니면 숨김. tooltip도 nonactivate이며 필요하면 layered/transparent style로 입력을 통과시킴 |
| Resize | Normal/topmost의 `WM_SIZE`로 viewport와 buffer 갱신. frame이 없는 desktop에서는 설정의 폭/높이로 변경하거나 일시적으로 Normal로 전환해 resize 후 복귀. through 중 직접 drag는 비활성 |
| Zoom | camera scale과 window size를 분리. 일반 window의 wheel/keys와 Windhawk 설정에서 변경. desktop/through에서도 설정을 통해 변경할 수 있도록 함. cursor/focus를 가로채는 global input hook은 만들지 않음 |
| 설정·복구 조작 | mode, click-through, width/height, zoom을 YAML settings로 공개하고 Windhawk에서 영속화. 초기 버전은 변경 시 reload. 창에 input이 도달하지 않아도 Windhawk UI에서 해제 가능. runtime resize/zoom 저장을 추가한다면 `Wh_SetIntValue`를 쓰고 명시적 settings 변경 시 저장 override를 해제하는 규칙을 둠 |

layered window의 투명 영역과 `WS_EX_TRANSPARENT` input 동작은 [Microsoft Window Features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features), topmost 지정은 [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos), `HTTRANSPARENT`의 thread 제약은 [WM_NCHITTEST](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest)를 근거로 한다. desktop의 DefView가 배경 영역 input을 받는 경우도 있으므로 tooltip/zoom을 하위 window에 도달하는 mouse message만으로 구현하지 않는다.

GDI+는 UI thread에서 startup하고 정수 좌표, nearest-neighbor interpolation, smoothing 비활성화로 pixel atlas를 그린다. 초기 버전은 PoC처럼 `SetLayeredWindowAttributes` + `WM_PAINT` 경로를 사용한다. color key에는 사용하지 않을 색 하나를 예약한다. 임의의 pixel alpha가 필요해지면 premultiplied BGRA DIB와 `UpdateLayeredWindow`로 변경한다. `SetLayeredWindowAttributes`를 호출한 window에 `UpdateLayeredWindow`를 혼용하지 않는다(layered style reset 필요). [Microsoft layered window 설명](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features).

### DPI

Explorer process 전체에 `SetProcessDpiAwareness*`를 호출하지 않는다. 일반 top-level 모드는 전용 UI thread를 PMv2로 만들고 `WM_DPICHANGED`의 권장 rect와 `GetDpiForWindow`를 사용한다. desktop child는 생성 전 **parent의 `GetWindowDpiAwarenessContext`와 thread context를 맞춘다**. DPI awareness가 다른 동일 process 내 `SetParent`는 Windows 10 1703 이후 `ERROR_INVALID_STATE`로 실패할 수 있다. SetParent는 styles를 자동 변환하지 않으므로 reparent한다면 WS_CHILD/WS_POPUP과 frame 갱신도 명시한다. PoC는 처음부터 자식 창으로 만들어 SetParent를 피한다. [SetParent 사양](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent), [thread 단위 DPI API](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddpiawarenesscontext).

화면 위치는 `MapWindowPoints`로 screen↔parent client 좌표를 변환한다. pixel-art 배율은 정수 device-pixel scale을 선택한다. shell 부모의 awareness에 제약되는 desktop 모드에서 일반 PMv2 창과 같은 좌표/DPI 값을 무조건 쓰지 않는다. 100/150/200%, 배율이 다른 monitor 사이, 음수 virtual-screen 좌표를 실기 검증한다. PoC는 부모 awareness만 맞추며 320×160/32px라는 고정 렌더 크기를 사용한다. mixed-DPI pixel 정확도를 증명하지 않았다.

## 3. unload 조건

안전하게 반환할 조건은 mod 코드로 돌아오는 thread/callback이나 WndProc 참조가 남지 않는 것이다. hook 자동 제거만으로는 부족하다.

1. `Wh_ModUninit`에서 stop event를 signal한다. 이미 닫힌 HWND에 대한 `PostMessage`에만 의존하지 않고 UI loop가 event와 message queue를 함께 기다린다.
2. UI thread가 timer를 중단하고 tooltip/helper/render window를 **생성한 thread에서** destroy한다. `WM_NCDESTROY`로 window tracking을 무효화한다.
3. event hook/subclass/알림 등록이 있다면 해제한다. window class를 unregister하고 GDI+ objects/cache를 해제한 다음 GDI+ shutdown한다.
4. UI thread가 끝났음을 join으로 확인한 후 thread/event handle을 close하고 `Wh_ModUninit`에서 반환한다. reader thread를 추가했다면 모두 join한다.

PoC는 동일 UI thread에서 timer/window/class/GDI+를 해제하고, stop event 이후 `WaitForSingleObject(thread, INFINITE)`로 join한다. parent 알림을 피하는 `WS_EX_NOPARENTNOTIFY`도 사용한다. function callback을 가진 timer, shell subclass, hook, 복잡한 global destructor는 사용하지 않는다. [다른 thread의 창은 DestroyWindow로 파괴할 수 없다](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-destroywindow). GDI+ startup/shutdown은 DllMain에서 하면 안 된다. [GDI+ lifetime 사양](https://learn.microsoft.com/en-us/windows/win32/api/gdiplusinit/nf-gdiplusinit-gdiplusstartup).

**join에는 명시적인 trade-off가 있다.** timeout 후 DLL을 unmap하는 것보다 안전하지만, OS/GDI/다른 mod 때문에 UI thread가 멈추면 disable 처리도 기다린다. 250ms timeout은 shell message의 제한이지 모든 Win32/GDI 작업의 종료 시간 보장이 아니다. 전체 viewer에서 bounded unload가 필요하다면 처음부터 별도 module lease를 잡고, 종료를 확인하지 못할 때 DLL 참조와 필요한 상태를 보존해 unmap을 막아야 한다. 이때 남은 callback은 engine context를 참조하는 Wh API를 더 이상 호출해서도 안 된다. 단순 timeout return이나 `TerminateThread`는 대안이 아니다.

Explorer 종료·sign-out에서는 일반 mod unload와 같은 cleanup callback이 호출되는 것은 아니다. blocking thread join을 global destructor에 넣지 않고, 정상 disable/update/reload에서 명시적으로 정리한다. [Windhawk 공식 process shutdown 설명](https://github.com/ramensoftware/windhawk/wiki/Global-objects-and-process-shutdown).

## 4. 빌드 재현과 확인 결과

루트 디렉터리에서 실제로 성공한 wrapper command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windhawk\poc\compile.ps1
```

`ExecutionPolicy Bypass`는 이 자식 PowerShell process에만 적용한다. 시스템 policy 설정은 변경하지 않았다. script가 실행하는 clang command는 다음과 같다.

```powershell
& 'C:\Program Files\Windhawk\Compiler\bin\clang++.exe' '@C:\Codex-Workspace\Vibedotcom\windhawk\poc\compile.rsp'
```

성공 시 response file 전체 내용(UTF-8 BOM 없음):

```text
-std=c++23
-O2
-shared
-target x86_64-w64-mingw32
-DUNICODE
-D_UNICODE
-DWINVER=0x0A00
-D_WIN32_WINNT=0x0A00
-D_WIN32_IE=0x0A00
-DNTDDI_VERSION=0x0A000008
-D__USE_MINGW_ANSI_STDIO=0
-DWH_MOD
-DWH_MOD_ID=L\"vibedotcom-desktop-poc\"
-DWH_MOD_VERSION=L\"0.1.0\"
-include windhawk_api.h
-Wl,--export-all-symbols
-Wall
-Wextra
-lgdiplus
-lgdi32
-luser32
-I"C:/Program Files/Windhawk/Compiler/include"
"C:/Program Files/Windhawk/Engine/1.7.3/64/windhawk.lib"
"C:/Codex-Workspace/Vibedotcom/windhawk/poc/poc.wh.cpp"
-o "C:/Codex-Workspace/Vibedotcom/windhawk/poc/poc.dll"
```

response file은 Windows PowerShell 5.1에서 `L"..."` quotes를 정확히 전달하기 위해 사용했다. response의 path는 forward slash를 사용한다. 처음 backslash path 버전은 clang response parser가 backslash를 escape로 해석해 실패했고, 이 형식으로 수정해 성공했다.

確認結果:

- clang **exit 0, warning/error 출력 없음**(`compile.log`). `poc.dll`: **75,776 bytes**.
- PE header에서 **AMD64 0x8664 / PE32+ 0x20B** 확인.
- imports: `USER32.dll`, **`gdiplus.dll`**, **`windhawk.dll`**, `libc++.whl`, `libunwind.whl`, `msvcrt.dll`, `KERNEL32.dll`。
- callback exports: `_Z10Wh_ModInitv`, `_Z12Wh_ModUninitv`, `_Z15Wh_ModAfterInitv`, `_Z21Wh_ModSettingsChangedPi`. 이 toolchain의 C++ mangled symbols로 export된다.
- SHA256: `5D2D5794ADAB20EB83D76A32D56D1D25B9C2AA335EC1DA4156BC4A59D9749DA6`. 상세 기록은 `pe-verification.txt`.

DLL을 LoadLibrary로 실행한 것이 아니라 PE를 직접 읽어 확인했다. Windhawk 편집용 API stub이나 직접 만든 API stub으로 링크한 결과가 아니다.

## 5. PoC 실기 테스트 절차

아래는 실행을 위한 절차이며, 이번 조사에서 이미 수행한 항목이 아니다.

1. Windhawk UI의 새 mod 생성 메뉴에서 새 로컬 mod를 만들고 `windhawk/poc/poc.wh.cpp` 전체 내용을 붙인다. 기존 Codex Gauge mod를 편집하지 않는다.
2. metadata의 `@include explorer.exe`, `@architecture amd64`를 유지하고 컴파일한 뒤 활성화한다. Windhawk가 metadata를 읽어 등록·runtime 배치를 처리하므로 scratch DLL 수동 복사는 필요 없다.
3. mod logging을 활성화한다. desktop을 표시하면 X=80/Y=180 부근의 320×160 영역에서 32px 사각형이 좌우로 움직이며 색이 바뀌는 것이 예상 동작이다. 배경은 transparent, click-through는 항상 ON이다. `Attached hwnd=... parent=... icons=... wallpaper=...` 로그를 확인한다.
4. 사각형과 아이콘을 겹쳐 아이콘이 위에 그려지는지, 사각형 위에서 desktop 선택 조작이 가능한지, 일반 앱을 올리면 앱 뒤에 가려지는지 확인한다. raised 구조에서는 부모가 Progman이고 icons/WorkerW가 그 자식인지와 Z-order를 확인한다.
5. X/Y setting을 바꾸고 저장한다. 기존 UI thread 종료 로그와 새 attach를 확인한다. mode/zoom/resize/tooltip은 이 PoC에 구현하지 않았다.
6. mod를 비활성화한다. 사각형이 사라지고 `UI thread stopped; window/class/GDI+ released`가 출력되는지 확인한다. 활성/비활성을 몇 번 반복해 잔류 window, handle/thread 증가, Explorer 응답 중단이 없는지 확인한다.
7. 추가 검증 대상: 24H2 실기, classic WorkerW 구조, mixed DPI, display/wallpaper 변경, Show Desktop, shell 재구성, 다른 wallpaper mod와의 공존. 기존 WorkerW가 사라진 뒤 다시 만들어지지 않으면 PoC를 reload한다. PoC는 지속적인 layer 생성 요청까지 수행하지 않는다.

사각형이 나오지 않으면 로그를 확인한다. `No compatible desktop host yet`는 지원하는 계층을 찾지 못했다는 의미다. 부모 window 크기, DPI, Z-order, 다른 wallpaper 앱과의 충돌도 확인한다. 재시도 목적으로 shell window를 hide/destroy하지 않는다. 이번 **실행 검증 완료** 항목은 읽기 전용 desktop probe뿐이다. mod 활성화, 사각형 시각 확인, click-through, disable cleanup은 미검증이다.

probe 재컴파일 command:

```powershell
& 'C:\Program Files\Windhawk\Compiler\bin\clang++.exe' -std=c++23 -O2 -target x86_64-w64-mingw32 -DUNICODE -D_UNICODE -D_WIN32_WINNT=0x0A00 windhawk/poc/inspect-desktop.cpp -o windhawk/poc/inspect-desktop.exe -luser32
& .\windhawk\poc\inspect-desktop.exe
```

## 6. 잠금 화면의 현실 확인

Windows 잠금 화면을 모두 같은 desktop이라고 설명하는 것은 부정확하다. Microsoft는 **LogonUI의 Secure Lock screen은 SYSTEM의 Winlogon secure desktop**에서, Win+L 후의 **LockApp은 user session**에서 실행된다고 구분한다. [현재 Microsoft Support 설명](https://support.microsoft.com/en-us/windows/experience/secure-lock-screen-clock-may-appear-up-to-30-seconds-behind).

이번 `@include explorer.exe` window는 일반 application desktop에 속한다. TOPMOST, WorkerW parent, Windhawk injection은 이 창을 Winlogon desktop으로 옮기지 않는다. 별도 desktop은 접근 권한을 가진 별도 object다. 이름을 알거나 OpenDesktop API가 있다고 create/draw 권한을 얻는 것은 아니다. 기존 window/hook을 가진 thread의 `SetThreadDesktop`도 실패한다. [Winlogon desktop 보호](https://learn.microsoft.com/en-us/windows/win32/secauthn/initializing-winlogon), [SetThreadDesktop 제약](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddesktop).

**제품 결론: 잠금 화면 viewer는 지원하지 않는다.** 일반 user-mode Explorer mod가 LogonUI secure desktop에 표시할 수 있다고 약속할 수 없다. LockApp이 user session에 있다는 사실도 Explorer overlay가 잠금 UI 위에 안정적으로 표시된다는 supported contract를 뜻하지 않는다. SYSTEM 권한이나 secure-desktop 전용 구현이라면 조건이 달라지지만 이 viewer 범위 밖이다. `LogonUI.exe`/`winlogon.exe` 주입·security 변경·desktop 전환은 시도하지 않았다.

선택적으로 session lock/unlock 알림을 받아 비표시 기간 animation을 멈출 수 있다. unlock 후 snapshot을 갱신하고 다시 그린다. 이 동작을 잠금 화면 표시와 혼동하지 않는다.

## 7. F1-F11 수정 및 v4 offscreen 검증 (2026-10-04)

이번 작업에서는 mod DLL을 Explorer에 로드하지 않았다. 기존 앱과 Core 소스는 읽기 전용으로 확인했으며, 테스트는 독립 실행 파일의 offscreen 렌더와 자체 소유한 숨겨진 HWND만 사용했다. 키/마우스 입력, 기존 창의 최소화·foreground 변경, 설치·배포·커밋은 수행하지 않았다. 위의 과거 PoC 조사 기록과 이번 검증 범위를 구분한다.

| 항목 | 상태 | 변경 및 확인 근거 |
| --- | --- | --- |
| F1 | fixed | pointer-bearing `LVM_HITTEST` 제거. list-view 및 자식 영역에서는 hover를 억제한다. 실제 숨겨진 list-view의 message observer로 hit-test/pointer 메시지가 없음을 확인했다. |
| F2 | fixed | GDI+ wrapper의 null-returning allocator/Clone 사용 제거, global/placement allocation과 일치하는 cleanup, 모든 graphics status/null/stride 검사. native 호출은 별도 noinline thunk와 SEH leaf를 거친 뒤 C++ Error로 변환한다. 실제 GdipAlloc null 주입, 17개 cold allocation 실패 지점과 warm 복구, RaiseException 및 PAGE_NOACCESS 실제 AV에서 RAII/COM cleanup과 이후 렌더 복구를 확인했다. |
| F3 | mitigated | HWND 없는 scene worker로 IO/parse/hash/decode/GDI+ 렌더와 alpha hit/tooltip 계산을 옮겼다. UI는 완성된 immutable pixel packet을 표시한다. shell 요청은 worker의 scalar-only SendMessageTimeout이며 결과를 확인한다. 자체 worker packet의 pixel/hit depth/cache를 검사했다. child parenting의 입력 큐 결합 자체와 native presentation/COM 지연 가능성은 남으므로 완전한 shell hang 방지를 주장하지 않는다. |
| F4 | fixed | move/size/menu 진입 때 50ms stop timer, stop 시 자체 창에 WM_CANCELMODE 및 capture 정리, 창 파괴 후 join. 실제 WndProc에 modal-entry/timer 메시지를 전달해 종료를 확인했다. 사용자 modal 조작은 실행하지 않았다. |
| F5 | fixed | 같은 manifest/ID/path/SHA의 warm hit도 bitmap 치수를 후보 sheet와 비교한다. 잘못된 width/height를 cold/warm 모두 거부하고 기존 픽셀이 유지되는 회귀 검사 통과. |
| F6 | fixed | state, desk/overflow ID, owner/monitor/state/frame 등 표시 의미를 repaint key에 포함한다. semantic state 변경의 key와 실제 pixel 변화, reserved desk tag 상태/거리 규칙을 검사했다. |
| F7 | fixed | host 소실/실패 뒤 같은 Progman에서도 재요청, 1초부터 최대 30초 backoff, invalid HWND 복구. 실제 공용 retry 정책 검사는 통과했으며 WorkerW 재생성은 실 Explorer 미검증. |
| F8 | fixed | ancestor visibility/cloak와 current virtual desktop을 검사한다. query 실패는 보수적으로 억제하고 서비스 부재 시 top-level hover를 비활성화한다. cloaked occluder 처리와 hover 정책 검사 통과. 실제 desktop 전환은 미검증. |
| F9 | fixed | WM_DISPLAYCHANGE 및 DPI/parent 변경 시 placement 갱신, 완전히 offscreen인 창만 복구. 실제 popup/framed HWND의 client와 pixel extent를 4096×2048 이하로 일치시킨다. 자체 창의 실제 resize 및 음수/부분/offscreen geometry 검사 통과. |
| F10 | fixed | target-HWND DPI context에서 physical/client 좌표 변환. tooltip font/padding/measurement를 cursor monitor DPI로 갱신. 자체 font의 96/144/192 DPI와 반올림 검사 통과. 실제 mixed-DPI 이동은 미검증. |
| F11 | fixed | relative sheet grammar를 실제 root 접근과 분리해 가상의 C: drive 의존성을 제거했다. 절대/UNC/traversal/ADS/NUL/UTF 오류와 정상 Unicode 경로 검사 통과. |

독립 정적 검토에서 추가로 발견한 종료 경쟁도 수정했다. join 대기 중 CancelSynchronousIo를 반복하므로 첫 취소 이후 시작된 동기 IO도 취소된다. 자체 pipe/barrier 회귀에서 첫 실제 취소는 ERROR_NOT_FOUND(1168), 이후 read는 ERROR_OPERATION_ABORTED(995), 취소 호출은 2회, watchdog 강제 해제는 0회였다. 이 API는 pending IO 취소 요청이며 driver별 완료를 보장하는 deadline은 아니다. [Microsoft 계약](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelsynchronousio).

추가로 ULW/DIB 표시 실패 시 새 packet 없이도 재시도를 예약하고, UnlockBits 호출 전에 ownership flag를 해제해 native fault 이후 중복 unlock을 피했다. 기다림 화면의 Font도 별도 소유한 FontFamily overload를 사용한다. Clang의 같은-frame SEH 한계를 피하는 noinline thunk는 설치된 Windhawk clang의 최적화 빌드에서 실제 AV로 확인했다. [Clang SEH 제한](https://clang.llvm.org/docs/MSVCCompatibility.html#abi-features). callback body의 직접 fault 주입, unlock/font 내부 API별 fault 주입, presentation 실패 재시도의 runtime 주입 검사는 수행하지 않았다.

v4 렌더는 고정된 office/layout, supplied frames/manifest rotation·variants·state names를 사용한다. 캐릭터 머리 위 provider icon/name tag, 자리를 비운 owner의 reserved desk tag, assigned monitor 상태, overflow laptop 및 queued 표시를 반영했다. additive v4+의 미지 필드는 무시하며 새 seated pose/env3 side desk 통합은 이 작업에 포함하지 않았다.

### Alpha와 embedding의 확인 범위

Desktop child와 transparent frameless 창은 premultiplied BGRA + UpdateLayeredWindow(ULW_ALPHA), AC_SRC_ALPHA, SourceConstantAlpha를 사용한다. framed normal/topmost는 worker의 opaque buffer + BitBlt + LWA_ALPHA를 사용하며 두 정책을 같은 HWND에서 혼용하지 않는다. Windows 8부터 layered child가 지원된다. [Microsoft ULW 계약](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow), [LWA/ULW 상호작용](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes).

이 환경의 test EXE에서는 sidecar activation만으로 layered child 생성이 거부됐고, Windows 8+ compatibility manifest를 EXE에 포함한 뒤 자체 숨겨진 WS_CHILD가 ULW_ALPHA와 50%/100% constant alpha를 허용했다. compile.ps1은 별도 resource compiler 없이 표준 .res를 만들어 test EXE에 링크한다. [resource format](https://learn.microsoft.com/en-us/windows/win32/menurc/resource-file-formats). 이 검사는 visible DWM 결과나 Explorer manifest 정책을 증명하지 않는다. 실제 child 생성/표시 실패 시 mod는 자체 child만 제거하고 bottom-level top-level per-pixel 창으로 fallback한다. shell 부모의 style/manifest/DPI 정책을 변경하지 않는다.

### 최종 검증 및 parity

`windhawk/compile.ps1 -Tests`: DLL 및 두 test EXE 컴파일 성공. renderer **238 checks**, hidden host **48 checks** 전부 통과. host include에서 사용하지 않는 production entry/helper 4개에 대한 unused-function 경고가 있으며 mod/renderer compile 실패는 없다. 관련 PowerShell 4개 syntax 검사 및 git diff whitespace 검사도 통과했다.

기존 Release app의 `--demo --smoke-test` 결과와 동일한 state를 actual C++ renderer에 전달했다. build/source/PNG hash와 snapshot identity는 parity harness에 기록했다. 보조 snapshot에서는 sheet filename만 동일 SHA256의 exported PNG 경로로 바꾸며 나머지 내용은 보존한다. 네 RGBA 채널 모두 정확히 같은 pixel만 일치로 계산하고 tolerance/resizing은 사용하지 않는다.

| 비교 장면 | 정확한 RGBA pixel 일치율 |
| --- | ---: |
| 기본 2x | 99.631851% |
| 기본 auto | 99.716964% |
| transparent | 99.716964% |
| normal | 99.490534% |
| reserved tags | 99.386161% |
| overflow | 98.513972% |
| 작은 auto-fit (400×300) | 99.226667% |
| 7개 장면 pixel 수 가중 합계 | **99.334461%** (3,862,696 / 3,888,576) |

타일을 16px intermediate room으로 축소하던 문제를 제거해 authored detail을 최종 scale에서 보존했다. DirectWrite ideal advance로 tag 폭/collision을 맞추고 GDI+의 조기 이름 생략을 제거했다. 남은 차이는 주로 WPF/GDI+ glyph와 얇은 선 rasterization이며 pixel identity를 주장하지 않는다. 이번 수정 과정에서 comparison image를 **총 6개** 확인했다.

결과: [parity report](../windhawk/artifacts/parity/report.json), [최종 기본 비교](../windhawk/artifacts/parity/office-2x-comparison.png), [최종 overflow 비교](../windhawk/artifacts/parity/overflow-comparison.png), [renderer log](../windhawk/artifacts/tests.log), [host log](../windhawk/artifacts/host-tests.log). artifacts는 git에 포함되지 않는다.

남은 실기 검증은 Explorer parenting/WorkerW loss/reload, 실제 modal 중 unload, Win+D 및 virtual desktop 전환, mixed-DPI/monitor removal, visible embedded alpha, CPU/GDI/memory soak다. offscreen/hidden 검사로 이를 대신하지 않는다. native driver/library 내부의 부분 완료나 heap corruption 등 모든 Explorer crash를 절대적으로 차단한다는 보장은 할 수 없다.
