// ==WindhawkMod==
// @id              officelings-desktop-poc
// @name            Officelings desktop square PoC
// @description     Animated GDI+ square between wallpaper and desktop icons
// @version         0.1.0
// @author          Officelings contributors
// @include         explorer.exe
// @architecture    amd64
// @compilerOptions -lgdiplus -lgdi32 -luser32
// ==/WindhawkMod==

// ==WindhawkModReadme==
/*
Scratch PoC, not the full viewer. Creates a 320x160 transparent, click-through
desktop child window with a moving, color-changing 32px square. Windows 11
raised desktop: layered child of Progman below DefView and above WorkerW.
Classic desktop: child of the wallpaper WorkerW behind the icon host.
Disable the mod to stop its thread and remove its window. Settings reload it.
See docs/windhawk-notes.md for compilation, testing and unverified limitations.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- X: 80
  $name: Horizontal position (screen pixels)
- Y: 180
  $name: Vertical position (screen pixels)
*/
// ==/WindhawkModSettings==

#include <windhawk_api.h>
#include <gdiplus.h>

namespace {
constexpr wchar_t kClass[] = L"Officelings.Windhawk.DesktopPoc";
constexpr int kWidth = 320, kHeight = 160;
HANDLE g_stop = nullptr;
HANDLE g_thread = nullptr;
HINSTANCE g_module = nullptr;
int g_x = 80, g_y = 180;  // Immutable until the UI thread is joined.

struct Host {
    HWND parent = nullptr;
    HWND icons = nullptr;
    HWND wallpaper = nullptr;
};

bool InThisProcess(HWND window) {
    DWORD process = 0;
    return window && GetWindowThreadProcessId(window, &process) &&
           process == GetCurrentProcessId();
}

BOOL CALLBACK FindClassicHost(HWND top, LPARAM parameter) {
    HWND icons = FindWindowExW(top, nullptr, L"SHELLDLL_DefView", nullptr);
    if (!icons || !InThisProcess(top)) return TRUE;
    HWND worker = FindWindowExW(nullptr, top, L"WorkerW", nullptr);
    if (InThisProcess(worker) && IsWindowVisible(worker) &&
        !FindWindowExW(worker, nullptr, L"SHELLDLL_DefView", nullptr)) {
        auto* host = reinterpret_cast<Host*>(parameter);
        *host = {worker, nullptr, worker};
        return FALSE;
    }
    return TRUE;
}

Host FindHost() {
    HWND progman = FindWindowW(L"Progman", nullptr);
    if (!InThisProcess(progman)) return {};
    HWND icons = FindWindowExW(progman, nullptr, L"SHELLDLL_DefView", nullptr);
    HWND worker = FindWindowExW(progman, nullptr, L"WorkerW", nullptr);
    if (icons && worker &&
        (GetWindowLongPtrW(progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP)) {
        // 24H2+ raised/raw desktop: a sibling of WorkerW, NOT its child.
        if (InThisProcess(icons) && InThisProcess(worker))
            return {progman, icons, worker};
        return {};
    }
    Host host;
    EnumWindows(FindClassicHost, reinterpret_cast<LPARAM>(&host));
    return host;
}

LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case WM_NCCREATE: {
            auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            auto* tracked = static_cast<HWND*>(create->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(tracked));
            *tracked = window;
            break;
        }
        case WM_NCDESTROY: {
            auto* tracked = reinterpret_cast<HWND*>(GetWindowLongPtrW(window, GWLP_USERDATA));
            if (tracked) *tracked = nullptr;  // Parent destruction cannot leave a reused HWND.
            SetWindowLongPtrW(window, GWLP_USERDATA, 0);
            break;
        }
        case WM_ERASEBKGND:
            return 1;
        case WM_TIMER:
            InvalidateRect(window, nullptr, FALSE);
            return 0;
        case WM_PAINT: {
            PAINTSTRUCT paint{};
            HDC dc = BeginPaint(window, &paint);
            {
                Gdiplus::Graphics graphics(dc);
                graphics.SetSmoothingMode(Gdiplus::SmoothingModeNone);
                graphics.Clear(Gdiplus::Color(255, 255, 0, 255));  // Color key.
                ULONGLONG tick = GetTickCount64();
                int step = static_cast<int>((tick / 25) % 128);
                int x = 4 * (step <= 64 ? step : 128 - step);
                BYTE red = static_cast<BYTE>(64 + (tick / 8) % 192);
                Gdiplus::SolidBrush brush(Gdiplus::Color(255, red, 180, 240));
                graphics.FillRectangle(&brush, x, 64, 32, 32);
            }  // Dispose Graphics before EndPaint and before GdiplusShutdown.
            EndPaint(window, &paint);
            return 0;
        }
        case WM_MOUSEACTIVATE:
            return MA_NOACTIVATE;
        case WM_NCHITTEST:
            return HTTRANSPARENT;  // WS_EX_LAYERED | WS_EX_TRANSPARENT is primary.
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

DWORD WINAPI UiThread(void*) {
    // All HWND/GDI+ work belongs to this thread. No hooks or global UI destructors.
    ULONG_PTR gdiplus = 0;
    Gdiplus::GdiplusStartupInput input;
    if (Gdiplus::GdiplusStartup(&gdiplus, &input, nullptr) != Gdiplus::Ok) {
        Wh_Log(L"GDI+ startup failed");
        return 1;
    }
    WNDCLASSW windowClass{};
    windowClass.lpfnWndProc = WindowProc;
    windowClass.hInstance = g_module;
    windowClass.lpszClassName = kClass;
    if (!RegisterClassW(&windowClass)) {
        Wh_Log(L"RegisterClass failed: %lu", GetLastError());
        Gdiplus::GdiplusShutdown(gdiplus);
        return 1;
    }

    HWND window = nullptr;
    HWND requestedProgman = nullptr;
    Host attached;
    ULONGLONG nextProbe = 0;
    bool running = true;
    bool loggedMissingHost = false;
    while (running && WaitForSingleObject(g_stop, 0) == WAIT_TIMEOUT) {
        if (GetTickCount64() >= nextProbe) {
            nextProbe = GetTickCount64() + 1000;
            HWND progman = FindWindowW(L"Progman", nullptr);
            if (InThisProcess(progman) && progman != requestedProgman) {
                // Undocumented shell request; bounded and once per Progman HWND.
                // No shell windows/styles are destroyed, hidden or patched.
                DWORD_PTR result = 0;
                SendMessageTimeoutW(progman, 0x052C, 0xD, 0,
                                    SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT, 250, &result);
                SendMessageTimeoutW(progman, 0x052C, 0xD, 1,
                                    SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT, 250, &result);
                requestedProgman = progman;
            }
            Host host = FindHost();
            if (!host.parent && !loggedMissingHost) {
                Wh_Log(L"No compatible desktop host yet; retrying every second");
                loggedMissingHost = true;
            }
            if (window && (!IsWindow(window) || host.parent != attached.parent ||
                           host.icons != attached.icons || host.wallpaper != attached.wallpaper)) {
                if (IsWindow(window)) DestroyWindow(window);
                window = nullptr;
            }
            if (!window && host.parent) {
                // Match the parent's DPI context. Never alter Explorer's process DPI.
                DPI_AWARENESS_CONTEXT previous = SetThreadDpiAwarenessContext(
                    GetWindowDpiAwarenessContext(host.parent));
                if (!previous) {
                    Wh_Log(L"Cannot match desktop DPI: %lu", GetLastError());
                    continue;
                }
                POINT position{g_x, g_y};
                MapWindowPoints(nullptr, host.parent, &position, 1);
                window = CreateWindowExW(WS_EX_LAYERED | WS_EX_TRANSPARENT |
                                             WS_EX_NOACTIVATE | WS_EX_NOPARENTNOTIFY,
                                         kClass, L"Officelings desktop PoC", WS_CHILD,
                                         position.x, position.y, kWidth, kHeight,
                                         host.parent, nullptr, g_module, &window);
                SetThreadDpiAwarenessContext(previous);
                if (window) {
                    // Do not mix this redirected WM_PAINT path with UpdateLayeredWindow.
                    bool ready = SetLayeredWindowAttributes(window, RGB(255, 0, 255),
                                                            255, LWA_COLORKEY | LWA_ALPHA) &&
                                 SetTimer(window, 1, 33, nullptr) &&
                                 SetWindowPos(window, host.icons ? host.icons : HWND_TOP,
                                              0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE |
                                              SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    if (!ready) {
                        Wh_Log(L"Window setup failed: %lu", GetLastError());
                        DestroyWindow(window);
                        window = nullptr;
                    } else {
                        attached = host;
                        loggedMissingHost = false;
                        Wh_Log(L"Attached hwnd=%p parent=%p icons=%p wallpaper=%p",
                               window, host.parent, host.icons, host.wallpaper);
                    }
                } else {
                    Wh_Log(L"CreateWindow failed: %lu", GetLastError());
                }
            }
        }
        DWORD wait = MsgWaitForMultipleObjectsEx(1, &g_stop, 100, QS_ALLINPUT,
                                                 MWMO_INPUTAVAILABLE);
        if (wait == WAIT_OBJECT_0 || wait == WAIT_FAILED) break;
        MSG message;
        // Check stop between messages; a stream of messages must not starve unload.
        while (WaitForSingleObject(g_stop, 0) == WAIT_TIMEOUT &&
               PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            if (message.message == WM_QUIT) { running = false; break; }
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    if (IsWindow(window)) {
        KillTimer(window, 1);
        DestroyWindow(window);
    }
    UnregisterClassW(kClass, g_module);
    Gdiplus::GdiplusShutdown(gdiplus);
    Wh_Log(L"UI thread stopped; window/class/GDI+ released");
    return 0;
}
}  // namespace

BOOL Wh_ModInit() {
    // @include also matches separate folder Explorer processes; only the shell hosts UI.
    HWND shell = GetShellWindow();
    if (shell && !InThisProcess(shell)) return FALSE;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                               GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCWSTR>(UiThread), &g_module)) return FALSE;
    g_x = Wh_GetIntSetting(L"X");
    g_y = Wh_GetIntSetting(L"Y");
    g_stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    return g_stop != nullptr;
}

void Wh_ModAfterInit() {
    // Do not wait for Explorer UI during early Wh_ModInit/injection.
    g_thread = CreateThread(nullptr, 0, UiThread, nullptr, 0, nullptr);
    if (!g_thread) Wh_Log(L"CreateThread failed: %lu", GetLastError());
}

BOOL Wh_ModSettingsChanged(BOOL* reload) {
    *reload = TRUE;  // Immutable settings; controlled stop/join/recreate.
    return TRUE;
}

void Wh_ModUninit() {
    if (g_stop) SetEvent(g_stop);
    if (g_thread) {
        // Returning on a timeout would let Windhawk unmap a live WndProc/thread.
        // The UI loop has no synchronous dependency on this unload callback.
        WaitForSingleObject(g_thread, INFINITE);
        CloseHandle(g_thread);
        g_thread = nullptr;
    }
    if (g_stop) { CloseHandle(g_stop); g_stop = nullptr; }
}
