// Read-only topology probe: no messages, parenting, registry writes or injection.
#include <windows.h>
#include <cstdio>
#include <cwchar>

static void Print(HWND window) {
    wchar_t name[128]{};
    GetClassNameW(window, name, 128);
    DWORD processId = 0;
    DWORD threadId = GetWindowThreadProcessId(window, &processId);
    RECT rect{};
    GetWindowRect(window, &rect);
    std::printf("hwnd=%p parent=%p class=%ls pid=%lu tid=%lu "
                 "style=%08llX ex=%08llX visible=%d dpi=%u rect=%ld,%ld,%ld,%ld\n",
                 window, GetParent(window), name, processId, threadId,
                 static_cast<unsigned long long>(GetWindowLongPtrW(window, GWL_STYLE)),
                 static_cast<unsigned long long>(GetWindowLongPtrW(window, GWL_EXSTYLE)),
                 IsWindowVisible(window), GetDpiForWindow(window),
                 rect.left, rect.top, rect.right, rect.bottom);
}

static BOOL CALLBACK PrintChild(HWND window, LPARAM) {
    wchar_t name[128]{};
    GetClassNameW(window, name, 128);
    if (!wcscmp(name, L"WorkerW") || !wcscmp(name, L"SHELLDLL_DefView") ||
        !wcscmp(name, L"SysListView32")) Print(window);
    return TRUE;
}

static BOOL CALLBACK PrintTop(HWND window, LPARAM) {
    wchar_t name[128]{};
    GetClassNameW(window, name, 128);
    if (!wcscmp(name, L"Progman") || !wcscmp(name, L"WorkerW")) {
        Print(window);
        EnumChildWindows(window, PrintChild, 0);
    }
    return TRUE;
}

int main() {
    SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    wchar_t name[128]{};
    DWORD needed = 0;
    GetUserObjectInformationW(GetProcessWindowStation(), UOI_NAME, name, sizeof(name), &needed);
    std::printf("Window station: %ls\n", name);
    GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), UOI_NAME, name, sizeof(name), &needed);
    std::printf("Calling desktop: %ls\n", name);
    SetLastError(0);
    BOOL result = EnumWindows(PrintTop, 0);
    std::printf("EnumWindows result=%d error=%lu\n", result, GetLastError());
    HDESK desktop = OpenDesktopW(L"Default", 0, FALSE, DESKTOP_READOBJECTS | DESKTOP_ENUMERATE);
    if (desktop) {
        SetLastError(0);
        result = EnumDesktopWindows(desktop, PrintTop, 0);
        std::printf("EnumDesktopWindows(Default) result=%d error=%lu\n", result, GetLastError());
        CloseDesktop(desktop);
    } else {
        std::printf("OpenDesktop(Default) error=%lu\n", GetLastError());
    }
    return 0;
}
