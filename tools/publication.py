"""Atomic files and manifest-committed batches, using only the standard library."""
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import tempfile
import time
from safe_files import safe_path, relative_file, read_bytes, write_boundary, make_directory, open_lock
from safe_files import atomic_write as write_file


def atomic_write(path, data):
    write_file(Path(path), data)


@contextmanager
def writer_lock(output, timeout=10):
    output = safe_path(Path(output))
    make_directory(output)
    # Keep the inode/name after release: deleting it would let another writer
    # lock a different file while a waiter still holds the old one.
    with write_boundary(output / ".boundary"), open_lock(output / ".publish.lock") as stream:
        if stream.seek(0, 2) == 0:
            stream.write(b"0")
            stream.flush()
        deadline = time.monotonic() + timeout
        while True:
            try:
                stream.seek(0)
                if os.name == "nt":
                    import msvcrt
                    msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    raise TimeoutError(f"Another writer holds {output}")
                time.sleep(.025)
        try:
            yield
        finally:
            stream.seek(0)
            if os.name == "nt":
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)


def publish_batch(output, files, manifest_name):
    """Immutable generation first; one root manifest is the commit marker."""
    output = safe_path(Path(output))
    generation = hashlib.sha256(b"".join(name.encode() + b"\0" + data for name, data in sorted(files.items()))).hexdigest()
    prefix = f".generations/{generation}"
    target = relative_file(output, prefix)
    make_directory(output)
    for name in files:
        relative_file(output, name)
    relative_file(output, manifest_name)

    def references(value):
        if isinstance(value, dict):
            return {k: references(v) for k, v in value.items()}
        if isinstance(value, list):
            return [references(v) for v in value]
        return f"{prefix}/{value}" if isinstance(value, str) and value in files else value

    manifest = references(json.loads(files[manifest_name]))
    manifest["publication"] = {"generation": prefix}
    commit = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode()
    with write_boundary(output / ".boundary"), tempfile.TemporaryDirectory(dir=output, prefix=".stage-") as staging:
        stage = Path(staging) / "generation"
        for name, data in files.items():
            if name.endswith(".facelock.json"):
                report = json.loads(data)
                for key in ("sourceManifest", "sourceSheet", "sourceFrames"):
                    if report.get(key):
                        report[key] = os.path.relpath(output / report[key], target).replace("\\", "/")
                data = (json.dumps(report, ensure_ascii=False, indent=2) + "\n").encode()
            atomic_write(stage / name, data)
        with writer_lock(output):
            make_directory(target.parent)
            with write_boundary(target):
                if not target.exists():
                    os.replace(stage, target)
            if not (output / manifest_name).is_file() or read_bytes(output / manifest_name, 4 * 1024 * 1024) != commit:
                atomic_write(output / manifest_name, commit)
    # Older generations intentionally remain usable by readers of old manifests.
    return manifest
