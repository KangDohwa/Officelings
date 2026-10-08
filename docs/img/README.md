# Demo screenshots

Captured on 2026-10-04 from the documentation lane's base checkout. Only fabricated sessions and bundled art were used; no provider logs, personal brand overrides, credentials or server captures.

| File | Smoke source | Size |
| --- | --- | --- |
| `demo-office-2x.png` | `flat/office-2x.png` | 1152×672; 70,387 bytes |
| `demo-iso-2x.png` | `iso/iso-twelve-working-2x.png` | 1440×896; 106,734 bytes |
| `demo-night-2x.png` | `ambient/night.png` | 1152×672; 78,166 bytes |
| `demo-provider-tags-2x.png` | `ambient/tags-2x.png` | 680×108; 5,305 bytes |

From the repository root on Windows, build and render into ignored scratch folders:

```powershell
dotnet build Officelings.sln
$appExe = Join-Path $PWD 'src/Officelings.App/bin/Debug/net10.0-windows/Officelings.App.exe'
foreach ($capture in @(
    '--view flat --demo --smoke-test artifacts/docs-refresh/flat',
    '--view iso --demo --smoke-test artifacts/docs-refresh/iso',
    '--view flat --demo --smoke-test artifacts/docs-refresh/ambient --smoke-ambient'
)) {
    $process = Start-Process -FilePath $appExe -ArgumentList $capture -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    "Exit: $($process.ExitCode)"
}
```

Check exit codes, `smoke.txt`/`smoke.json`/`iso-smoke.json`, visible text and art before copying only these PNGs. Do not copy state, tokens, traces or scratch outputs into docs. Source-art changes must be assembled into the runtime pack and rebuilt first.

The published PNGs were losslessly recompressed at the IDAT layer; decompressed scanline bytes stayed identical. For an equivalent refresh using an existing Python/Pillow environment (compressed bytes may differ by encoder):

```powershell
@'
from io import BytesIO
from pathlib import Path
from PIL import Image
for source, target in [
    ("flat/office-2x.png", "demo-office-2x.png"),
    ("iso/iso-twelve-working-2x.png", "demo-iso-2x.png"),
    ("ambient/night.png", "demo-night-2x.png"),
    ("ambient/tags-2x.png", "demo-provider-tags-2x.png"),
]:
    original = Path("artifacts/docs-refresh", source).read_bytes()
    image = Image.open(BytesIO(original)).convert("RGBA")
    output = BytesIO()
    image.save(output, format="PNG", optimize=True, compress_level=9)
    data = output.getvalue()
    assert Image.open(BytesIO(data)).convert("RGBA").tobytes() == image.tobytes()
    Path("docs/img", target).write_bytes(data if len(data) < len(original) else original)
'@ | python -B -
```

Recorded results: iso exit 0 (48 indexed renders plus camera/tag images); ambient exit 0 (four room views and three tag crops, zero log bytes, two pets, no pack warnings). Flat wrote the office, tooltip and v5 exports with `demo=True`, nine agents and `logBytesRead=0`, then exited 1 during later checks; it is **not** a complete smoke pass. The old `demo-tooltip.png` is a historical fabricated capture and is not embedded in the refreshed READMEs. Offscreen images do not verify Explorer embedding or the hosted web viewer.
