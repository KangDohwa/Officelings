"""Small local-file boundaries shared by the art and download tools."""
from __future__ import annotations

from io import BytesIO
from contextlib import contextmanager
import json
import os
from pathlib import Path
import stat
import tempfile
import uuid

if os.name == "nt":
    import ctypes
    import msvcrt
    from ctypes import wintypes
    _kernel = ctypes.WinDLL("kernel32.dll", use_last_error=True, winmode=0x800)
    _kernel.CreateFileW.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                                    wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p)
    _kernel.CreateFileW.restype = wintypes.HANDLE
    _kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    _kernel.GetDriveTypeW.argtypes = (wintypes.LPCWSTR,)
    _kernel.GetFileInformationByHandleEx.argtypes = (wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD)


def _attributes(handle):
    info = (wintypes.DWORD * 2)()
    if not _kernel.GetFileInformationByHandleEx(handle, 9, ctypes.byref(info), ctypes.sizeof(info)):
        raise ctypes.WinError(ctypes.get_last_error())
    if info[0] & 0x400:
        raise ValueError("reparse/symlink paths are not allowed")


@contextmanager
def boundary(path: Path, *, writing=False):
    path = Path(os.path.abspath(path))
    if len(str(path)) > 4096 or len(path.parts) > 128:
        raise ValueError("file path length/depth budget exceeded")
    handles = []
    try:
        if os.name == "nt":
            if str(path).startswith("\\\\") or ":" in str(path)[2:] or _kernel.GetDriveTypeW(path.anchor) not in (2, 3):
                raise ValueError("local paths without alternate streams are required")
            for parent in reversed(path.parents):
                share = 3 if writing or parent != path.parent else 1
                handle = _kernel.CreateFileW(str(parent), 0x81, share, None, 3, 0x02200000, None)
                if handle == ctypes.c_void_p(-1).value:
                    error = ctypes.get_last_error()
                    if error in (2, 3):
                        break
                    raise ctypes.WinError(error)
                handles.append(handle)
                _attributes(handle)
        for part in (*reversed(path.parents), path):
            try:
                info = part.lstat()
            except FileNotFoundError:
                break
            if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                raise ValueError("reparse/symlink paths are not allowed")
        yield path
    finally:
        if os.name == "nt":
            for handle in reversed(handles):
                _kernel.CloseHandle(handle)


def safe_path(path: Path) -> Path:
    with boundary(path, writing=True) as checked:
        return checked


@contextmanager
def write_boundary(path: Path):
    path = safe_path(path)
    guard = None
    try:
        if os.name == "nt":
            with boundary(path):
                name = path.parent / (".boundary-" + uuid.uuid4().hex + ".lock")
                guard = _kernel.CreateFileW(str(name), 0x40010000, 0, None, 1, 0x04200000, None)
                if guard == ctypes.c_void_p(-1).value:
                    guard = None
                    raise ctypes.WinError(ctypes.get_last_error())
        with boundary(path, writing=True):
            try:
                yield path
            finally:
                if guard is not None:
                    _kernel.CloseHandle(guard)
                    guard = None
    finally:
        if guard is not None:
            _kernel.CloseHandle(guard)


def make_directory(path: Path):
    path = safe_path(path)
    with boundary(path):
        try:
            if stat.S_ISDIR(path.lstat().st_mode):
                return
        except FileNotFoundError:
            pass
    if path == path.parent:
        raise ValueError("directory has no local parent")
    make_directory(path.parent)
    with boundary(path):
        try:
            path.mkdir()
        except FileExistsError:
            pass
        safe_path(path)
        if not stat.S_ISDIR(path.lstat().st_mode):
            raise ValueError("expected a local directory")


@contextmanager
def open_lock(path: Path):
    with boundary(path, writing=True) as path:
        if os.name != "nt":
            descriptor = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
        else:
            handle = _kernel.CreateFileW(str(path), 0xc0000000, 3, None, 4, 0x00200000, None)
            if handle == ctypes.c_void_p(-1).value:
                raise ctypes.WinError(ctypes.get_last_error())
            try:
                _attributes(handle)
                descriptor = msvcrt.open_osfhandle(handle, os.O_RDWR | os.O_BINARY)
            except Exception:
                _kernel.CloseHandle(handle)
                raise
        with os.fdopen(descriptor, "a+b") as stream:
            if os.fstat(stream.fileno()).st_nlink > 1:
                raise ValueError("publication lock must not be a hard link")
            yield stream


@contextmanager
def _open_read(path):
    if os.name != "nt":
        with path.open("rb") as stream:
            yield stream
        return
    handle = _kernel.CreateFileW(str(path), 0x80000000, 7, None, 3, 0x00200000, None)
    if handle == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        _attributes(handle)
        descriptor = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
    except Exception:
        _kernel.CloseHandle(handle)
        raise
    with os.fdopen(descriptor, "rb") as stream:
        yield stream


def relative_file(root: Path, name: str) -> Path:
    if not isinstance(name, str) or not name or "\\" in name or ":" in name or name.startswith("/") or any(
            part in ("", ".", "..") or part.endswith((".", " ")) for part in name.split("/")):
        raise ValueError("safe relative file path required")
    root = safe_path(root)
    path = safe_path(root / name)
    if root not in path.parents:
        raise ValueError("file escapes allowed root")
    return path


def read_bytes(path: Path, maximum: int = 32 * 1024 * 1024) -> bytes:
    with boundary(path) as path, _open_read(path) as stream:
        if os.fstat(stream.fileno()).st_size > maximum:
            raise ValueError("file byte budget exceeded")
        data = stream.read(maximum + 1)
    if len(data) > maximum:
        raise ValueError("file byte budget exceeded")
    return data


def read_json(path: Path):
    text = read_bytes(path, 4 * 1024 * 1024).decode("utf-8-sig")
    depth, quoted, escaped = 0, False, False
    for char in text:
        if quoted:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                quoted = False
        elif char == '"':
            quoted = True
        elif char in "[{":
            depth += 1
            if depth > 64:
                raise ValueError("JSON depth budget exceeded")
        elif char in "]}":
            depth -= 1
    value = json.loads(text)
    pending, nodes = [(value, 0)], 0
    while pending:
        item, depth = pending.pop()
        nodes += 1
        if depth > 64 or nodes > 250000:
            raise ValueError("JSON depth/node budget exceeded")
        if isinstance(item, dict):
            pending.extend((v, depth + 1) for v in item.values())
        elif isinstance(item, list):
            pending.extend((v, depth + 1) for v in item)
    return value


def bounded_glob(root: Path, pattern: str = "*", maximum: int = 256):
    root = safe_path(root)
    with boundary(root / ".boundary"):
        entries = []
        for path in root.glob(pattern):
            entries.append(safe_path(path))
            if len(entries) > maximum:
                raise ValueError("directory entry budget exceeded")
        return sorted(entries)


def open_png(path: Path):
    from PIL import Image
    image = Image.open(BytesIO(read_bytes(path)), formats=("PNG",))
    if max(image.size) > 8192 or image.width * image.height > 16_777_216:
        image.close()
        raise ValueError("PNG dimension/pixel budget exceeded")
    return image


def atomic_write(path: Path, data: bytes):
    path = safe_path(path)
    make_directory(path.parent)
    temporary = None
    with write_boundary(path):
        try:
            with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".safe-", suffix=".tmp", delete=False) as stream:
                temporary = Path(stream.name)
                stream.write(data)
                stream.flush()
                os.fsync(stream.fileno())
            safe_path(path)
            os.replace(temporary, path)
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)
