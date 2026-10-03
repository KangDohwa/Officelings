"""Atomic files and manifest-committed batches, using only the standard library."""
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import tempfile
import time


def atomic_write(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".publish-", suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


@contextmanager
def writer_lock(output, timeout=10):
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    # Keep the inode/name after release: deleting it would let another writer
    # lock a different file while a waiter still holds the old one.
    with (output / ".publish.lock").open("a+b") as stream:
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
    output = Path(output).resolve()
    generation = hashlib.sha256(b"".join(name.encode() + b"\0" + data for name, data in sorted(files.items()))).hexdigest()
    prefix = f".generations/{generation}"
    target = output / prefix
    output.mkdir(parents=True, exist_ok=True)
    for name in files:
        if not (output / name).resolve().is_relative_to(output) or (output / name).is_symlink():
            raise ValueError("output escapes publication directory")

    def references(value):
        if isinstance(value, dict):
            return {k: references(v) for k, v in value.items()}
        if isinstance(value, list):
            return [references(v) for v in value]
        return f"{prefix}/{value}" if isinstance(value, str) and value in files else value

    manifest = references(json.loads(files[manifest_name]))
    manifest["publication"] = {"generation": prefix}
    commit = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode()
    with tempfile.TemporaryDirectory(dir=output, prefix=".stage-") as staging:
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
            target.parent.mkdir(parents=True, exist_ok=True)
            if not target.exists():
                os.replace(stage, target)
            if not (output / manifest_name).is_file() or (output / manifest_name).read_bytes() != commit:
                atomic_write(output / manifest_name, commit)
    # Older generations intentionally remain usable by readers of old manifests.
    return manifest
