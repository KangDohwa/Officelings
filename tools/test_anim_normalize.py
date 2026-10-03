"""Run with the sprite-gen Python: python -B tools/test_anim_normalize.py."""
import copy

import numpy as np
from PIL import Image, ImageDraw

from anim_normalize import normalize_frames, normalize_sheet


def check():
    reference = Image.new("RGBA", (48, 48))
    draw = ImageDraw.Draw(reference)
    draw.rectangle((6, 28, 37, 41), fill=(140, 80, 45, 255))
    draw.rectangle((9, 6, 34, 27), fill=(45, 42, 40, 255))
    draw.rectangle((13, 10, 30, 22), fill=(16, 55, 58, 255))
    variant = reference.copy()
    variant.putpixel((20, 15), (90, 200, 185, 255))  # Deliberate screen change.
    variant.putpixel((12, 34), (141, 81, 44, 255))   # Regenerated cabinet noise.
    variant.putpixel((6, 29), (0, 0, 0, 0))         # Local silhouette noise.
    shifted = Image.new("RGBA", reference.size)
    shifted.paste(variant, (3, -2))                 # Independent, known displacement.
    originals = [reference.tobytes(), shifted.tobytes()]
    result, mask, report = normalize_frames([reference, shifted], "tv")
    assert report["shifts_applied"] == [[0, 0], [-3, 2]]
    assert report["animated_region_bbox"] == [13, 10, 31, 23]
    expected_screen = np.zeros((48, 48), dtype=bool)
    expected_screen[10:23, 13:31] = True
    assert np.array_equal(mask, expected_screen)
    assert np.array_equal(np.array(result[1])[~expected_screen], np.array(reference)[~expected_screen])
    assert result[1].getpixel((20, 15)) == variant.getpixel((20, 15))
    assert report["pixels_made_identical"] == 2
    assert [reference.tobytes(), shifted.tobytes()] == originals

    blink = reference.copy()
    ImageDraw.Draw(blink).rectangle((15, 32, 16, 33), fill=(70, 205, 185, 255))
    blink.putpixel((24, 36), (141, 79, 45, 255))
    result, mask, report = normalize_frames([reference, blink], "unknown")
    assert mask[32:34, 15:17].all() and not mask[36, 24]
    assert result[1].getpixel((15, 32)) == blink.getpixel((15, 32))
    assert result[1].getpixel((24, 36)) == reference.getpixel((24, 36))

    pet = reference.copy()
    ImageDraw.Draw(pet).rectangle((35, 14, 38, 16), fill=(160, 100, 50, 255))
    result, mask, report = normalize_frames([reference, pet], "cat")
    assert report["shifts_applied"] == [[0, 0], [0, 0]]
    assert result[1].tobytes() == pet.tobytes() and mask[15, 38]

    result, mask, report = normalize_frames([reference, blink], "tv", animated_mask=expected_screen)
    assert report["mask_strategy"] == "caller" and np.array_equal(mask, expected_screen)

    sheet = Image.new("RGBA", (104, 104))
    rects = [{"x": 2, "y": 3, "w": 48, "h": 48}, {"x": 54, "y": 5, "w": 48, "h": 48}]
    choices = [{"x": 2, "y": 54, "w": 48, "h": 48}, {"x": 54, "y": 54, "w": 48, "h": 48}]
    for r, frame in zip(rects + choices, [reference, shifted, reference, pet]):
        sheet.paste(frame, (r["x"], r["y"]))
    sheet.putpixel((103, 103), (1, 2, 3, 4))
    manifest = {"characterId": "tv", "animation": {"rows": {"play": {"frames": 2}, "variants": {"frames": 2}}},
                "frame_layout": {"sheetWidth": 104, "sheetHeight": 104, "rows": {"play": rects, "variants": choices}}}
    before_manifest, before_pixels = copy.deepcopy(manifest), sheet.tobytes()
    result, report = normalize_sheet(sheet, manifest)
    assert manifest == before_manifest and sheet.tobytes() == before_pixels
    assert report["rows"][0]["shifts_applied"][1] == [-3, 2]
    assert result.crop((0, 54, 104, 104)).tobytes() == sheet.crop((0, 54, 104, 104)).tobytes()
    assert result.getpixel((103, 103)) == (1, 2, 3, 4)
    try:
        normalize_frames([reference, shifted], animated_mask=np.zeros((1, 1)))
    except ValueError:
        pass
    else:
        raise AssertionError("Wrong-sized caller mask was accepted")
    print("PASS: known integer shift, TV body/outline lock, screen preservation, automatic LEDs, full pet motion, caller mask, sparse layout, choices, immutable inputs")


if __name__ == "__main__":
    check()
