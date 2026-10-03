"""Compare the same offscreen WPF demo snapshots and standalone C++ renders.

Python 3.9+; standard library only. Never builds, injects DLLs, or operates input
or existing windows. PNG comparison also works outside Windows. Examples:

  python tools/windhawk_parity.py --run-wpf --run-native --diff
  python tools/windhawk_parity.py --wpf-dir windhawk/artifacts/parity/wpf --run-native --diff
  python tools/windhawk_parity.py --pair small WPF.png CPP.png --out TEMP_DIR

--run-wpf accepts only the existing, audited v4 Debug/Release binaries below.
Their PDBs matched Program.cs, OfficeView.cs and OfficeEditorPanel.cs on
2026-10-04: Main returns Smoke before constructing OfficeWindow; Render uses
RenderTargetBitmap; SimulateHover draws inline and never opens the Popup.
For another build, review its smoke path first and supply --wpf-dir instead.
--run-native invokes only an already-built renderer_tests.exe with argv[4]
--render-only, whose VIBEDOTCOM_TEST build excludes the entire HWND host.
It refuses older harnesses instead of repeating the broad regression suite.
It never runs compile.ps1.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import zlib


ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUT = ROOT / "windhawk/artifacts/parity"
AUDITED_BUILDS = {
    "c03932c94bf1dcf2d25ea4d5be1745376031a1ddcd9b630ba9f9d15220f87e37":
        "cbf6b88b025900413357054dddc8d53f769dd2aad7ae6d63cbbacbc7ca512c40",
    "9700d519c83f38b08cd15747f5bdff41c5e231bbbc5e7c1d01af6f9b87286d76":
        "0f10cff6c29b22b167b2734c952d59f22181ff1c88078e2a169009f00115667b",
}
SCENARIOS = {"main": "state.json", "normal": "normal-state.json",
             "reserved-tags": "reserved-tags-state.json", "overflow": "overflow-state.json"}
CASES = [("office-2x", "main", "office-2x.png", "windhawk-2x.png"),
         ("office-auto", "main", "office.png", "windhawk-auto.png"),
         ("transparent", "main", "transparent.png", "windhawk-transparent.png"),
         ("normal", "normal", "normal.png", "windhawk-2x.png"),
         ("reserved-tags", "reserved-tags", "reserved-tags.png", "windhawk-2x.png"),
         ("overflow", "overflow", "overflow.png", "windhawk-2x.png"),
         # ReviewRenders captures this before creating OfficeEditorPanel: it is
         # the exact normal-state pose/furniture set, with SetZoom(0), 400x300.
         ("auto-fit-small", "normal", "auto-fit-small.png", "windhawk-small.png")]


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def output_path(path):
    path = path.resolve()
    if not any(path == base or base in path.parents
               for base in (DEFAULT_OUT.resolve(), Path(tempfile.gettempdir()).resolve())):
        raise ValueError("Outputs must be under windhawk/artifacts/parity or TEMP")
    return path


def read_png(path):
    """Decode the 8-bit, non-interlaced RGB/RGBA PNGs emitted by both renderers.

    Compare stored straight RGBA bytes, including RGB beneath zero alpha.
    There is no compositing, color conversion, resampling, or tolerance.
    """
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"Not a PNG: {path}")
    pos, compressed, header, transparency = 8, bytearray(), None, False
    while pos < len(data):
        length, = struct.unpack_from(">I", data, pos)
        kind = data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + length]
        crc, = struct.unpack_from(">I", data, pos + 8 + length)
        if zlib.crc32(kind + payload) & 0xffffffff != crc:
            raise ValueError(f"PNG CRC mismatch: {path}")
        pos += length + 12
        if kind == b"IHDR":
            header = struct.unpack(">IIBBBBB", payload)
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"tRNS":
            transparency = True
        elif kind == b"IEND":
            break
    if header is None:
        raise ValueError(f"Missing PNG IHDR: {path}")
    width, height, depth, color, compression, filtering, interlace = header
    if depth != 8 or color not in (2, 6) or compression or filtering or interlace:
        raise ValueError(f"Expected 8-bit non-interlaced RGB/RGBA PNG: {path}")
    if color == 2 and transparency:
        raise ValueError(f"RGB transparency keys are unsupported: {path}")
    channels = 4 if color == 6 else 3
    stride = width * channels
    raw = zlib.decompress(compressed)
    if len(raw) != (stride + 1) * height:
        raise ValueError(f"PNG scanline length mismatch: {path}")
    previous, rgba = bytearray(stride), bytearray()
    for y in range(height):
        start = y * (stride + 1)
        mode = raw[start]
        row = bytearray(raw[start + 1:start + 1 + stride])
        if mode not in range(5):
            raise ValueError(f"Unknown PNG filter {mode}: {path}")
        if mode:
            for x in range(stride):
                left = row[x - channels] if x >= channels else 0
                above = previous[x]
                corner = previous[x - channels] if x >= channels else 0
                if mode == 1:
                    predictor = left
                elif mode == 2:
                    predictor = above
                elif mode == 3:
                    predictor = (left + above) // 2
                else:
                    p = left + above - corner
                    dl, da, dc = abs(p - left), abs(p - above), abs(p - corner)
                    predictor = left if dl <= da and dl <= dc else above if da <= dc else corner
                row[x] = (row[x] + predictor) & 255
        if channels == 4:
            rgba.extend(row)
        else:
            for x in range(0, stride, 3):
                rgba.extend(row[x:x + 3])
                rgba.append(255)
        previous = row
    return width, height, rgba


def write_png(path, width, height, rgba):
    def chunk(kind, payload):
        return (struct.pack(">I", len(payload)) + kind + payload
                + struct.pack(">I", zlib.crc32(kind + payload) & 0xffffffff))
    stride = width * 4
    scanlines = b"".join(b"\0" + rgba[y * stride:(y + 1) * stride] for y in range(height))
    path.write_bytes(b"\x89PNG\r\n\x1a\n"
                     + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(scanlines)) + chunk(b"IEND", b""))


def compare(name, reference, candidate, out, differences):
    w, h, a = read_png(reference)
    cw, ch, b = read_png(candidate)
    result = {"case": name, "wpf": str(reference), "native": str(candidate),
              "wpfDimensions": [w, h], "nativeDimensions": [cw, ch],
              "wpfSha256": sha256(reference), "nativeSha256": sha256(candidate)}
    if (w, h) != (cw, ch):
        result.update(status="dimension-mismatch", exactRgbaPercent=None)
        return result
    mismatch, totals, maximum = 0, [0] * 4, [0] * 4
    left, top, right, bottom = w, h, -1, -1
    diff = bytearray(w * h * 4) if differences else None
    # Integer grouping tests all four channels; machine byte order is irrelevant.
    for pixel, (av, bv) in enumerate(zip(memoryview(a).cast("I"), memoryview(b).cast("I"))):
        offset = pixel * 4
        if av == bv:
            if diff is not None:
                diff[offset + 3] = 255
            continue
        mismatch += 1
        x, y = pixel % w, pixel // w
        left, top, right, bottom = min(left, x), min(top, y), max(right, x), max(bottom, y)
        delta = [abs(a[offset + c] - b[offset + c]) for c in range(4)]
        for c in range(4):
            totals[c] += delta[c]
            maximum[c] = max(maximum[c], delta[c])
        if diff is not None:
            # Opaque RGB error *4; alpha error also lights red/blue (magenta).
            diff[offset:offset + 4] = bytes((min(255, max(delta[0], delta[3]) * 4),
                                          min(255, delta[1] * 4),
                                          min(255, max(delta[2], delta[3]) * 4), 255))
    total = w * h
    result.update(status="compared", pixels=total, matchingPixels=total - mismatch,
                  differingPixels=mismatch, exactRgbaPercent=100 * (total - mismatch) / total,
                  meanAbsoluteRgbaError=[v / total for v in totals], maxAbsoluteRgbaError=maximum,
                  differenceBounds=None if not mismatch else [left, top, right - left + 1, bottom - top + 1])
    if diff is not None:
        diff_path, panel_path = out / f"{name}-diff.png", out / f"{name}-comparison.png"
        write_png(diff_path, w, h, diff)
        panel = bytearray()
        for y in range(h):
            sl = slice(y * w * 4, (y + 1) * w * 4)
            panel.extend(a[sl] + b[sl] + diff[sl])
        write_png(panel_path, w * 3, h, panel)
        result.update(diff=str(diff_path), inspectionImage=str(panel_path))
    return result


def run_hidden(command, log):
    if os.name != "nt":
        raise ValueError("Renderer execution requires Windows; PNG comparison is portable")
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    with log.open("wb") as stream:
        process = subprocess.run([str(a) for a in command], cwd=Path(command[0]).parent,
                                 stdout=stream, stderr=subprocess.STDOUT, timeout=60,
                                 creationflags=subprocess.CREATE_NO_WINDOW, startupinfo=startup)
    if process.returncode:
        raise RuntimeError(f"Offscreen process exited {process.returncode}; see {log}")


def run_wpf(app, output, log):
    dll = app.with_suffix(".dll") if app.suffix.lower() == ".exe" else app
    app_hash, core_hash = sha256(dll), sha256(dll.parent / "Vibedotcom.Core.dll")
    if AUDITED_BUILDS.get(app_hash) != core_hash:
        raise ValueError("Unreviewed WPF build: use existing --wpf-dir after auditing its smoke path")
    if app.suffix.lower() != ".exe":
        raise ValueError("Use the built App.exe so its compatibility manifest applies")
    output.mkdir(parents=True, exist_ok=True)
    run_hidden([app, "--demo", "--smoke-test", output], log)
    (output / "parity-build.json").write_text(json.dumps(
        {"path": str(app), "appDllSha256": app_hash, "coreDllSha256": core_hash,
         "arguments": ["--demo", "--smoke-test", str(output)], "offscreen": True}, indent=2) + "\n", encoding="utf-8")


def prepare_states(wpf, out):
    exported = json.loads((wpf / "manifest.json").read_text(encoding="utf-8-sig"))
    sheets = {s["id"]: s for s in exported["sheets"]}
    for sheet in sheets.values():
        image = (wpf / sheet["file"]).resolve()
        if wpf not in image.parents or sha256(image) != sheet["sha256"]:
            raise ValueError(f"Exported image path/hash mismatch: {sheet['id']}")
    inputs = out / "native-input"
    inputs.mkdir(parents=True, exist_ok=True)
    states = {}
    for scenario, filename in SCENARIOS.items():
        source = wpf / filename
        snapshot = json.loads(source.read_text(encoding="utf-8-sig"))
        if snapshot["schemaVersion"] != 4:
            raise ValueError(f"Expected a real v4 WPF smoke snapshot: {source}")
        relocated = 0
        for sheet in snapshot["assets"]["sheets"]:
            target = sheets[sheet["id"]]
            if {k: v for k, v in sheet.items() if k != "file"} != {k: v for k, v in target.items() if k != "file"}:
                raise ValueError(f"Scenario image metadata differs: {source}, {sheet['id']}")
            relocated += sheet["file"] != target["file"]
            sheet["file"] = target["file"]
        # ReviewRenders exports assets.Manifest filenames, while Smoke exports
        # content-addressed filenames. Relocate ONLY those paths in a new copy.
        native_input = inputs / filename
        if relocated:
            native_input.write_text(json.dumps(snapshot, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        else:
            native_input.write_bytes(source.read_bytes())
        states[scenario] = {"source": str(source), "sourceSha256": sha256(source),
                            "nativeInput": str(native_input), "nativeInputSha256": sha256(native_input),
                            "relocatedSheetPaths": relocated, "schemaVersion": 4,
                            "officeTiles": [snapshot["office"]["widthTiles"], snapshot["office"]["heightTiles"]],
                            "agents": len(snapshot["agents"])}
    return states


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--wpf-dir", type=Path, help="Existing --demo --smoke-test output; defaults to OUT/wpf")
    parser.add_argument("--native-dir", type=Path, help="Per-scenario native outputs; defaults to OUT/native")
    parser.add_argument("--app", type=Path, default=ROOT / "src/Vibedotcom.App/bin/Release/net10.0-windows/Vibedotcom.App.exe")
    parser.add_argument("--renderer", type=Path, default=ROOT / "windhawk/artifacts/renderer_tests.exe")
    parser.add_argument("--run-wpf", action="store_true", help="Run only the audited 2026-10-04 Debug/Release smoke build, hidden; use --wpf-dir for other audited exports")
    parser.add_argument("--run-native", action="store_true", help="Replay the same snapshots using argv[4]=--render-only; never run broad regressions")
    parser.add_argument("--diff", action="store_true", help="Write error maps and WPF/native/error panels, without opening images")
    parser.add_argument("--pair", action="append", nargs=3, metavar=("NAME", "WPF_PNG", "CPP_PNG"), help="Compare explicit PNG pairs instead of the seven smoke cases")
    parser.add_argument("--min-match", type=float, help="Exit 1 when any exact RGBA match is below this percent")
    args = parser.parse_args()
    if args.min_match is not None and not 0 <= args.min_match <= 100:
        parser.error("--min-match must be in [0, 100]")
    out = output_path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    wpf = (args.wpf_dir or out / "wpf").resolve()
    native = (args.native_dir or out / "native").resolve()
    app, renderer = args.app.resolve(), args.renderer.resolve()
    if args.run_wpf:
        run_wpf(app, output_path(wpf), out / "wpf-smoke.log")
    states = prepare_states(wpf, out) if args.run_native or not args.pair else {}
    if args.run_native:
        native = output_path(native)
        if renderer.name.lower() != "renderer_tests.exe":
            raise ValueError("--renderer must be the standalone renderer_tests.exe, never a mod DLL")
        renderer_bytes = renderer.read_bytes()
        if b"--render-only" not in renderer_bytes and "--render-only".encode("utf-16le") not in renderer_bytes:
            raise ValueError("Existing harness lacks --render-only; use the updated existing binary after its owner's build completes. No regression tests were run.")
        renderer_hash = hashlib.sha256(renderer_bytes).hexdigest()
        for scenario, state in states.items():
            destination = native / scenario
            destination.mkdir(parents=True, exist_ok=True)
            print(f"Offscreen native replay: {scenario}", flush=True)
            run_hidden([renderer, Path(state["nativeInput"]), wpf, destination, "--render-only"], out / f"{scenario}-native.log")
        if sha256(renderer) != renderer_hash:
            raise ValueError("Native binary changed during replay; rerun after compilation completes")
    pairs = args.pair or [(name, wpf / wp, native / scenario / cpp) for name, scenario, wp, cpp in CASES]
    results = []
    for name, reference, candidate in pairs:
        if not name or any(c not in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_" for c in name):
            raise ValueError("Pair names must contain only letters, numbers, '-' and '_'")
        result = compare(name, Path(reference).resolve(), Path(candidate).resolve(), out, args.diff)
        results.append(result)
        match = result["exactRgbaPercent"]
        detail = f"RGBA exact {match:.6f}%" if match is not None else "DIMENSION MISMATCH (no resampling)"
        print(f"{name}: WPF {result['wpfDimensions']}, C++ {result['nativeDimensions']}; {detail}", flush=True)
    priority = {"overflow": 0, "reserved-tags": 1, "normal": 2, "auto-fit-small": 3,
                "office-2x": 4, "transparent": 5, "office-auto": 6}
    inspection = [r["inspectionImage"] for r in sorted(results, key=lambda r: priority.get(r["case"], 7)) if "inspectionImage" in r][:6]
    report = {"comparison": "Exact decoded straight RGBA; every channel must match, including zero-alpha RGB",
              "states": states, "results": results,
              "inspectionPlan": {"images": inspection, "maxImages": 6,
                                 "panels": "Left WPF, middle C++, right opaque absolute error x4; alpha error is magenta",
                                 "viewedByTool": False},
              "unpaired": ["1x/3x/cropped/panned: existing harness has no matching viewport/pan",
                           "phase PNGs: smoke does not export their corresponding snapshots",
                           "tooltip/editor-ui/offline: different UI/scenario; not same-state parity pairs"]}
    if args.run_native:
        report["renderer"] = {"path": str(renderer), "sha256": renderer_hash, "mode": "--render-only"}
    if (wpf / "parity-build.json").is_file():
        report["wpfBuild"] = json.loads((wpf / "parity-build.json").read_text(encoding="utf-8"))
    (out / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Report: {out / 'report.json'}; images opened: 0", flush=True)
    return int(any(r["exactRgbaPercent"] is None or (args.min_match is not None and r["exactRgbaPercent"] < args.min_match) for r in results))


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError, struct.error, zlib.error, RuntimeError, subprocess.TimeoutExpired) as error:
        print(f"Parity failed: {error}", file=sys.stderr)
        sys.exit(2)
