"""Read-only Windows process metrics, including File handles rather than only all handles."""
import ctypes as c
from contextlib import contextmanager
from ctypes import wintypes as w
import msvcrt
import os

k = c.WinDLL("kernel32", use_last_error=True)
psapi = c.WinDLL("psapi", use_last_error=True)
nt = c.WinDLL("ntdll")
SIZE = c.c_size_t


class Entry(c.Structure):
    _fields_ = [("size", w.DWORD), ("usage", w.DWORD), ("pid", w.DWORD), ("heap", SIZE),
                ("module", w.DWORD), ("threads", w.DWORD), ("parent", w.DWORD),
                ("priority", w.LONG), ("flags", w.DWORD), ("name", w.WCHAR * 260)]


class Memory(c.Structure):
    _fields_ = [("cb", w.DWORD), ("faults", w.DWORD)] + [(name, SIZE) for name in (
        "peak_rss", "rss", "peak_paged", "paged", "peak_nonpaged", "nonpaged", "pagefile", "peak_pagefile")]


class HandleEntry(c.Structure):
    _fields_ = [("object", c.c_void_p), ("pid", SIZE), ("handle", SIZE), ("access", w.DWORD),
                ("backtrace", w.USHORT), ("type", w.USHORT), ("attributes", w.DWORD), ("reserved", w.DWORD)]


k.CreateToolhelp32Snapshot.argtypes = [w.DWORD, w.DWORD]
k.CreateToolhelp32Snapshot.restype = w.HANDLE
k.Process32FirstW.argtypes = k.Process32NextW.argtypes = [w.HANDLE, c.POINTER(Entry)]
k.Process32FirstW.restype = k.Process32NextW.restype = w.BOOL
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k.OpenProcess.restype = w.HANDLE
k.CloseHandle.argtypes = [w.HANDLE]
k.CloseHandle.restype = w.BOOL
k.GetProcessTimes.argtypes = [w.HANDLE] + [c.POINTER(w.FILETIME)] * 4
k.GetProcessTimes.restype = w.BOOL
k.GetProcessHandleCount.argtypes = [w.HANDLE, c.POINTER(w.DWORD)]
k.GetProcessHandleCount.restype = w.BOOL
psapi.GetProcessMemoryInfo.argtypes = [w.HANDLE, c.POINTER(Memory), w.DWORD]
psapi.GetProcessMemoryInfo.restype = w.BOOL
nt.NtQuerySystemInformation.argtypes = [w.ULONG, c.c_void_p, w.ULONG, c.POINTER(w.ULONG)]
nt.NtQuerySystemInformation.restype = w.LONG
nt.NtSuspendProcess.argtypes = nt.NtResumeProcess.argtypes = [w.HANDLE]
nt.NtSuspendProcess.restype = nt.NtResumeProcess.restype = w.LONG


@contextmanager
def suspended(pid):
    """Pause only our tracker during checkpoint copies; always resume it."""
    handle = k.OpenProcess(0x0800, False, pid)  # PROCESS_SUSPEND_RESUME
    if not handle:
        raise c.WinError(c.get_last_error())
    paused = False
    try:
        status = nt.NtSuspendProcess(handle)
        if status != 0:
            raise OSError("Cannot suspend owned tracker: 0x%08x" % (status & 0xffffffff))
        paused = True
        time_to_settle = .01
        import time
        time.sleep(time_to_settle)  # Let a rename already in kernel mode complete.
        yield
    finally:
        if paused:
            status = nt.NtResumeProcess(handle)
            k.CloseHandle(handle)
            if status != 0:
                raise OSError("Cannot resume owned tracker: 0x%08x" % (status & 0xffffffff))
        else:
            k.CloseHandle(handle)


def children(root):
    handle = k.CreateToolhelp32Snapshot(2, 0)  # TH32CS_SNAPPROCESS
    if handle == c.c_void_p(-1).value:
        raise c.WinError(c.get_last_error())
    entries = []
    try:
        entry = Entry(size=c.sizeof(Entry))
        more = k.Process32FirstW(handle, c.byref(entry))
        while more:
            entries.append((entry.pid, entry.parent, entry.name))
            more = k.Process32NextW(handle, c.byref(entry))
    finally:
        k.CloseHandle(handle)
    family = {root}
    while True:
        added = {pid for pid, parent, _ in entries if parent in family} - family
        if not added:
            break
        family.update(added)
    return [(pid, name) for pid, _, name in entries if pid in family]


def file_handles(pid, anchor):
    # An owned read-only anchor tells us the File object type index on this boot.
    fd = os.open(anchor, os.O_RDONLY)
    try:
        anchor_handle = msvcrt.get_osfhandle(fd)
        length = 1024 * 1024
        while length <= 64 * 1024 * 1024:
            buffer = c.create_string_buffer(length)
            needed = w.ULONG()
            status = nt.NtQuerySystemInformation(64, buffer, length, c.byref(needed))
            if status == 0:
                break
            if status != -1073741820:  # STATUS_INFO_LENGTH_MISMATCH
                raise OSError("NtQuerySystemInformation failed: 0x%08x" % (status & 0xffffffff))
            length = max(length * 2, needed.value + 65536)
        else:
            raise OSError("System handle table exceeds 64 MiB QA bound")
        count = SIZE.from_buffer(buffer).value
        offset = 2 * c.sizeof(SIZE)
        if offset + count * c.sizeof(HandleEntry) > length:
            raise OSError("Invalid system handle table size")
        entries = (HandleEntry * count).from_buffer(buffer, offset)
        file_type = next(e.type for e in entries if e.pid == os.getpid() and e.handle == anchor_handle)
        return sum(e.pid == pid and e.type == file_type for e in entries)
    finally:
        os.close(fd)


def sample(pid, anchor):
    handle = k.OpenProcess(0x1010, False, pid)  # QUERY_LIMITED_INFORMATION | VM_READ
    if not handle:
        raise c.WinError(c.get_last_error())
    try:
        times = [w.FILETIME() for _ in range(4)]
        memory = Memory(cb=c.sizeof(Memory))
        handles = w.DWORD()
        if not k.GetProcessTimes(handle, *(c.byref(t) for t in times)):
            raise c.WinError(c.get_last_error())
        if not psapi.GetProcessMemoryInfo(handle, c.byref(memory), c.sizeof(memory)):
            raise c.WinError(c.get_last_error())
        if not k.GetProcessHandleCount(handle, c.byref(handles)):
            raise c.WinError(c.get_last_error())
        cpu = sum((t.dwHighDateTime << 32) | t.dwLowDateTime for t in times[2:]) / 10_000_000
        return dict(pid=pid, cpu_seconds=cpu, rss_mb=memory.rss / 1024**2,
                    handles=handles.value, file_handles=file_handles(pid, anchor))
    finally:
        k.CloseHandle(handle)
