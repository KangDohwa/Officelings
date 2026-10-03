"""Build original provider pixel art, without network calls or external tools.

Run: python assets/art/provider-icons/build_icons.py
Requires Pillow (already used by tools/build_pack.py). Every native size below
is an independently authored pixel map. Only density exports use resizing.
All output paths are relative to this file; nothing outside this folder is read
or written. No official marks, image inputs, or generated art are used.
"""

from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
SIZES = (10, 12, 16, 24, 32)
PROVIDERS = ("codex", "claude", "antigravity", "other-agent")
BADGES = ("supervisor", "sub-agent")
LABELS = {
    "codex": "Codex", "claude": "Claude", "antigravity": "Antigravity",
    "other-agent": "Other agent", "supervisor": "Supervisor", "sub-agent": "Sub-agent",
}

# '#' is selective dark outline, s/m/h are shadow/body/light; p is paper,
# i is ink, w is a recessed screen. The first three are flat color steps,
# never gradients. Shared colors follow the env2/env5 warm office palette.
SHARED = {"#": "#10282c", "p": "#f2e7c0", "i": "#10282c", "w": "#1d3a39"}
PALETTES = {
    "codex": {**SHARED, "s": "#235e5c", "m": "#4eb5a9", "h": "#a2dfc8"},
    "claude": {**SHARED, "s": "#9a5c3a", "m": "#e0a64e", "h": "#f6cf78"},
    "antigravity": {**SHARED, "s": "#423f7e", "m": "#827dcc", "h": "#bfc6ed"},
    "other-agent": {**SHARED, "s": "#325571", "m": "#6492a3", "h": "#bdd5cc"},
    "supervisor": {**SHARED, "s": "#966334", "m": "#f0ba39", "h": "#faecc9"},
    "sub-agent": {**SHARED, "s": "#325571", "m": "#48728f", "h": "#6492a3"},
}

# Each entry is (top-left x, top-left y, explicit rows). Short right margins
# are transparent; no interpolation, geometric drawing or cross-size scaling
# constructs a native icon. Empty rows use dots to keep vertical spacing.
MAPS = {
    "codex": {
        10: (1, 1, """
            .#hhhh#.
            #hmmmms#
            #mwwwws#
            #wpwwws#
            #wwpwws#
            #wpwhhs#
            #ssssss#
            .######.
        """),
        12: (1, 2, """
            .#hhhhhh#.
            #hmppmmms#
            #mwwwwwms#
            #wppwwwws#
            #wwwpwwws#
            #wppwhhws#
            #mwwwwwss#
            #ssssssss#
            .########.
        """),
        16: (1, 3, """
            .#hhhhhhhhhh#.
            #hmmppmmmmmms#
            #hmmmmmmmmmmss
            #mwwwwwwwwwws#
            #mwppwwwwwwws#
            #mwwwpwwwwwws#
            #mwppwwhhwwws#
            #mwwwwwsswwws#
            #mwwwwwwwwwss#
            #ssssssssssss#
            .############.
        """),
        24: (2, 5, """
            ..#hhhhhhhhhhhhhh#..
            .hhmmmmmmmmmmmmmmms#.
            #hmmppmmppmmmmmmmms#
            #hmmppmmppmmmmmmmms#
            #hmmmmmmmmmmmmmmmss#
            #mwwwwwwwwwwwwwwwws#
            #mwppwwwwwwwwwwwwws#
            #mwwwppwwwwwwwwwwws#
            #mwwwwwppwwwwwwwwws#
            #mwwwppwwwwwwwwwwws#
            #mwppwwwwhhhhwwwwws#
            #mwwwwwwwsssswwwwws#
            #mwwwwwwwwwwwwwwwss#
            #mwwwwwwwwwwwwwwwss#
            .mssssssssssssssss#.
            ..################..
        """),
        32: (2, 6, """
            ..#hhhhhhhhhhhhhhhhhhhhhh#..
            .hhmmmmmmmmmmmmmmmmmmmmmms#.
            #hhmmmmmmmmmmmmmmmmmmmmmms##
            #hmmmppmmppmmppmmmmmmmmmms##
            #hmmmppmmppmmppmmmmmmmmmms##
            #hmmmmmmmmmmmmmmmmmmmmmmss##
            #mwwwwwwwwwwwwwwwwwwwwwwws##
            #mwwppwwwwwwwwwwwwwwwwwwws##
            #mwwwwppwwwwwwwwwwwwwwwwws##
            #mwwwwwwppwwwwwwwwwwwwwwws##
            #mwwwwwwppwwwwwwwwwwwwwwws##
            #mwwwwppwwwwwwwwwwwwwwwwws##
            #mwwppwwwwhhhhhhwwwwwwwwws##
            #mwwwwwwwwsssssswwwwwwwwws##
            #mwwwwwwwwwwwwwwwwwwwwwwws##
            #mwwsssssswwssswwwwwwwwwws##
            #mwwwwwwwwwwwwwwwwwwwwwwss##
            #mwwwwwwwwwwwwwwwwwwwwwwss##
            .msssssssssssssssssssssss##.
            ..########################..
        """),
    },
    "claude": {
        10: (1, 1, """
            ..hhhh..
            .hmmmmh.
            hmimmmms
            hmmmipms
            hmmmpims
            .mssss#.
            .#ms##..
            .##.....
        """),
        12: (1, 1, """
            ..#hhhh#..
            .hhmmmmms.
            #hmmmmmmms
            hmipmmmmms
            hmipmipmms
            hmmmmpimms
            .msssssss#
            .#mms###..
            .#ms#.....
            .###......
        """),
        16: (1, 1, """
            ...#hhhhh#....
            ..hhmmmmmmh#..
            .hhmmmmmmmms#.
            #hmmmmmmmmmms#
            #hmmppmmmmmms#
            #mmpipmmmmmms#
            #mmpppmipmmms#
            #mmmmmmpipmms#
            #mmmmmmmpimms#
            #msssssssssss#
            .#msssssssss#.
            ..#mms######..
            ..#ms#........
            ..###.........
        """),
        24: (2, 2, """
            .....#hhhhhhhh#.....
            ...hhmmmmmmmmmmh#...
            ..hhmmmmmmmmmmmms#..
            .hhmmmmmmmmmmmmmms#.
            #hhmmmmmmmmmmmmmmms#
            #hmmmmmmmmmmmmmmmms#
            #hmmmppmmmmmmmmmmms#
            #hmmppipmmmmmmmmmmss
            #mmmppipmmmmmmmmmmss
            #mmmmppmmmmippmmmms#
            #mmmmmmmmmmmippmmms#
            #mmmmmmmmmmmpipmmms#
            #mmmmmmmmmmmmpimmmss
            #mmmmmmmmmmmmmmmmmss
            .mssssssssssssssss#.
            ..#mmssssssssssss#..
            ...#mmss#########...
            ...#mss#............
            ...#ms#.............
            ...###..............
        """),
        32: (2, 2, """
            ........#hhhhhhhhhh#........
            .....hhhmmmmmmmmmmmmhh#.....
            ...hhhmmmmmmmmmmmmmmmmms#...
            ..hhmmmmmmmmmmmmmmmmmmmms#..
            .hhmmmmmmmmmmmmmmmmmmmmmms#.
            #hhmmmmmmmmmmmmmmmmmmmmmmms#
            #hhmmmmmmmmmmmmmmmmmmmmmmms#
            #hmmmmmmmmmmmmmmmmmmmmmmmss#
            #hmmmmpppmmmmmmmmmmmmmmmmss#
            #hmmmppippmmmmmmmmmmmmmmmss#
            #hmmmppiipmmmmmmmmmmmmmmmss#
            #mmmmppippmmmmmmmmmmmmmmmss#
            #mmmmmpppmmmmmmippmmmmmmmss#
            #mmmmmmmmmmmmmmmippmmmmmmss#
            #mmmmmmmmmmmmmmmmpipmmmmmss#
            #mmmmmmmmmmmmmmmmmpipmmmmss#
            #mmmmmmmmmmmmmmmmmmipmmmmss#
            #mmmmmmmmmmmmmmmmmmmimmmmss#
            #mmmmmmmmmmmmmmmmmmmmmmmsss#
            .msssssssssssssssssssssss#.
            ..#mmsssssssssssssssssss#..
            ...#mmmsss#############....
            ....#mmss#.................
            ....#mss#..................
            ....#ms#...................
            ....###....................
        """),
    },
    "antigravity": {
        10: (1, 1, """
            ..hhh...
            .hmmm#..
            hmhmmm#.
            hmmmms#.
            .msss#..
            ..#s#...
            ...h....
            ....s...
            .sss.s..
        """),
        12: (1, 0, """
            ...hhh....
            ..hmmmh...
            .hhmmmms..
            #hmmmmmms.
            #hmmmmmss.
            .mmmmmss#.
            ..msss#...
            ...#s#....
            ....h.....
            .....m....
            ....m.....
            .sss..ss..
        """),
        16: (2, 1, """
            ...#hhh#....
            ..hhmmmmh...
            .hhmmmmmms..
            #hhmmmmmmss.
            #hmmmmmmmss.
            #hmmmmmmmss.
            .mmmmmmmss#.
            ..mmmmmss#..
            ...msss#....
            ....#s#.....
            .....h......
            ......m.....
            ......s.....
            .....s......
            .sss....sss.
        """),
        24: (4, 1, """
            .....#hhhh#......
            ...hhhmmmmhh....
            ..hhmmmmmmmms#..
            .hhmmmmmmmmmms#.
            #hhmmmmmmmmmmss#
            #hhmmmmmmmmmmss#
            #hmmmmmmmmmmmss#
            #hmmmmmmmmmmmss#
            #mmmmmmmmmmmsss#
            .mmmmmmmmmmsss#.
            ..mmmmmmmmsss#..
            ...mmmmmmsss#...
            ....mmmsss#.....
            .....#sss#......
            ......#s#.......
            ......hhh.......
            .......m........
            ........m.......
            .........m......
            .........s......
            ........s.......
            .......s........
            .ssss.....ssss..
        """),
        32: (5, 1, """
            .......#hhhhhh#.......
            .....hhhmmmmmmhh.....
            ...hhhmmmmmmmmmms#...
            ..hhhmmmmmmmmmmmms#..
            .hhhmmmmmmmmmmmmmss#.
            #hhhmmmmmmmmmmmmmsss#
            #hhmmmmmmmmmmmmmmsss#
            #hhmmmmmmmmmmmmmmsss#
            #hmmmmmmmmmmmmmmmsss#
            #hmmmmmmmmmmmmmmmsss#
            #mmmmmmmmmmmmmmmmsss#
            #mmmmmmmmmmmmmmmmsss#
            .mmmmmmmmmmmmmmmsss#.
            ..mmmmmmmmmmmmmsss#..
            ...mmmmmmmmmmmsss#...
            ....mmmmmmmmmsss#....
            .....mmmmmmmsss#.....
            ......mmmmsss#.......
            .......#ssss#........
            ........#ss#.........
            ........hhhh.........
            .........m...........
            ..........m..........
            ...........m.........
            ............m........
            ............s........
            ...........s.........
            ..........s..........
            .........s...........
            ..sssss......sssss...
        """),
    },
    "other-agent": {
        10: (0, 1, """
            ....h.....
            ....#.....
            ..#hhh##..
            #hmmmmms##
            #mwpwpms##
            #mwwwwms##
            .#ssss##..
            ...##.....
        """),
        12: (0, 1, """
            .....h......
            .....m......
            .....#......
            ..#hhhhhh#..
            #hhmmmmmms##
            #hmwpwwpwss#
            #hmwwwwwwss#
            .#mwwhhwws#.
            .#ssssssss#.
            ..########..
        """),
        16: (1, 1, """
            ......hh......
            ......mm......
            ......##......
            ...#hhhhhh#...
            ..hhmmmmmms#..
            #hhmmmmmmmms##
            #hmwwwwwwwws##
            #hmwppwwppws##
            #smwppwwppws##
            #smwwwwwwwws##
            ..mwsshhssws#.
            ..msssssssss#.
            ...#########..
            .....#ss#.....
        """),
        24: (1, 2, """
            .........hhh..........
            .........hmm..........
            .........###..........
            ..........s...........
            ....#hhhhhhhhhhhh#....
            ...hhmmmmmmmmmmmms#...
            ..hhmmmmmmmmmmmmmss#..
            #hhmmmmmmmmmmmmmmss###
            #hmwwwwwwwwwwwwwwssms#
            #hmwwpppwwwwpppwwssms#
            #hmwwpppwwwwpppwwssms#
            #smwwpppwwwwpppwwssms#
            #smwwwwwwwwwwwwwwssms#
            #smwwwwwwwwwwwwwwssms#
            .#mwwwsssshhsssswss##.
            ..mwwwwwwwwwwwwwwss#..
            ..mssssssssssssssss#..
            ...################...
            .......#ssssss#.......
            ........######........
        """),
        32: (1, 2, """
            .............hhhh.............
            .............hmmm.............
            .............msss.............
            .............####.............
            ..............ss..............
            ......#hhhhhhhhhhhhhhhh#......
            ....hhhmmmmmmmmmmmmmmmms##....
            ...hhmmmmmmmmmmmmmmmmmmss##...
            ..hhmmmmmmmmmmmmmmmmmmmsss##..
            #hhmmmmmmmmmmmmmmmmmmmmsss####
            #hmwwwwwwwwwwwwwwwwwwwwsssmm##
            #hmwwwwwwwwwwwwwwwwwwwwsssmm##
            #hmwwwppppwwwwwwppppwwwsssmm##
            #hmwwwppppwwwwwwppppwwwsssmm##
            #smwwwppppwwwwwwppppwwwsssmm##
            #smwwwppppwwwwwwppppwwwsssmm##
            #smwwwwwwwwwwwwwwwwwwwwsssmm##
            #smwwwwwwwwwwwwwwwwwwwwsssmm##
            #smwwwwssssshhhhssssswwsssmm##
            .#mwwwwwwwwwwwwwwwwwwwwsss###.
            ..mwwwwwwwwwwwwwwwwwwwwsss##..
            ..mwwwwwwwwwwwwwwwwwwwwsss##..
            ...mssssssssssssssssssssss#...
            ....######################....
            ..........#ssssssss#..........
            ...........########...........
        """),
    },
    "supervisor": {
        10: (5, 0, """
            m.h.m
            hmhms
            mmsms
            .###.
        """),
        12: (6, 0, """
            ..h...
            h#m#h#
            hmmms#
            #msss#
            .####.
        """),
        16: (9, 0, """
            ...h...
            h..m..h
            hmhmhms
            #mmmmm#
            #mssms#
            .#####.
        """),
        24: (14, 0, """
            ....hh....
            hh..hm..hh
            hmhhmmhhms
            #hmmmmmms#
            #mmmmmmms#
            #msssssms#
            .#mmmmm#..
            ..######..
        """),
        32: (19, 0, """
            .....hh......
            .....hm......
            hh...hm...hh.
            hmhhhmmhhhms.
            #hmmmmmmmmms#
            #hmmmmmmmmss#
            #mmmmmmmmmss#
            #mssssssssms#
            .#mmmmmmmms#.
            ..#########..
        """),
    },
    "sub-agent": {
        10: (6, 5, """
            .ss.
            m..s
            #hh#
            #pi#
            .##.
        """),
        12: (7, 6, """
            .#.#.
            .m.s.
            #hhh#
            #ppp#
            #pip#
            .###.
        """),
        16: (10, 8, """
            .#..#.
            .m..s.
            ..ms..
            #hhhh#
            #pppp#
            #pipp#
            #ppss#
            .####.
        """),
        24: (16, 12, """
            .#....#.
            .mm..ss.
            ..m..s..
            ..m..s..
            ...ms...
            .#hhhh#.
            #hpppps#
            #ppippp#
            #piiipp#
            #pppppp#
            #ppsssps
            .######.
        """),
        32: (22, 16, """
            .#......#.
            .mm....ss.
            ..m....s..
            ..mm..ss..
            ...m..s...
            ...m..s...
            ....ms....
            .#hhhhhh#.
            #hhppppps#
            #hpppppps#
            #pppiippp#
            #ppiiiipp#
            #pppppppp#
            #ppsssspp#
            #ppppppps#
            .########.
        """),
    },
}


def rows_for(icon_id: str, size: int) -> list[str]:
    return [row.strip() for row in MAPS[icon_id][size][2].strip().splitlines()]


def render(icon_id: str, size: int) -> Image.Image:
    x0, y0, _ = MAPS[icon_id][size]
    rows = rows_for(icon_id, size)
    assert x0 >= 0 and y0 >= 0
    assert y0 + len(rows) <= size, (icon_id, size, "height")
    result = Image.new("RGBA", (size, size))
    for y, row in enumerate(rows):
        assert x0 + len(row) <= size, (icon_id, size, "width", y, len(row))
        for x, token in enumerate(row):
            if token != ".":
                result.putpixel((x0 + x, y0 + y), rgba(PALETTES[icon_id][token]))
    assert result.getbbox(), (icon_id, size, "empty")
    used = set("".join(rows))
    assert {"s", "m", "h"} <= used, (icon_id, size, "missing shading tone")
    return result


def rgba(value: str) -> tuple[int, int, int, int]:
    return tuple(int(value[i:i + 2], 16) for i in (1, 3, 5)) + (255,)


def enlarged(icon: Image.Image, scale: int) -> Image.Image:
    return icon.resize((icon.width * scale, icon.height * scale), Image.Resampling.NEAREST)


# Simple linear-light red/green deficiency approximations, intentionally not
# presented as clinical validation. Both common failure modes are shown.
CVD = {
    "protanopia": ((0.152286, 1.052583, -0.204868),
                   (0.114503, 0.786281, 0.099216),
                   (-0.003882, -0.048116, 1.051998)),
    "deuteranopia": ((0.367322, 0.860646, -0.227968),
                    (0.280085, 0.672501, 0.047413),
                    (-0.011820, 0.042940, 0.968881)),
}


def linear(channel: int) -> float:
    value = channel / 255
    return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4


def srgb(value: float) -> int:
    value = max(0.0, min(1.0, value))
    value = value * 12.92 if value <= 0.0031308 else 1.055 * value ** (1 / 2.4) - 0.055
    return round(value * 255)


def simulate(icon: Image.Image, mode: str) -> Image.Image:
    result = Image.new("RGBA", icon.size)
    transformed = []
    for r, g, b, alpha in icon.get_flattened_data():
        channels = (linear(r), linear(g), linear(b))
        transformed.append(tuple(srgb(sum(a * c for a, c in zip(row, channels)))
                                 for row in CVD[mode]) + (alpha,))
    result.putdata(transformed)
    return result


def silhouette(icon: Image.Image, color: str) -> Image.Image:
    result = Image.new("RGBA", icon.size, color)
    result.putalpha(icon.getchannel("A"))
    return result


def pixel_plate(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], color: str,
                edge: str = "#35505a") -> None:
    x0, y0, x1, y1 = box
    draw.rectangle((x0 + 2, y0, x1 - 2, y1), fill=color)
    draw.rectangle((x0, y0 + 2, x1, y1 - 2), fill=color)
    draw.line((x0 + 2, y0, x1 - 2, y0), fill=edge)
    draw.line((x0 + 2, y1, x1 - 2, y1), fill=edge)
    draw.line((x0, y0 + 2, x0, y1 - 2), fill=edge)
    draw.line((x1, y0 + 2, x1, y1 - 2), fill=edge)


def preview(images: dict[tuple[str, int], Image.Image]) -> Image.Image:
    width = 2584
    top = 154
    row_heights = {size: size * 9 + 76 for size in SIZES}
    main_bottom = top + sum(row_heights.values())
    out = Image.new("RGB", (width, main_bottom + 958), "#0d191f")
    draw = ImageDraw.Draw(out)
    title_font = ImageFont.load_default(size=29)
    font = ImageFont.load_default(size=18)
    small = ImageFont.load_default(size=14)
    draw.text((34, 22), "ORIGINAL PROVIDER ICONS / HAND-AUTHORED PIXEL MAPS", font=title_font, fill="#f2e7c0")
    draw.text((34, 64), "Independent 10 / 12 / 16 / 24 / 32 px designs. Flat three-tone shading. Transparent RGBA. Nearest-neighbor zoom.", font=font, fill="#a2b9ba")
    for index, icon_id in enumerate((*PROVIDERS, *BADGES)):
        x = 34 + index * 420
        draw.text((x + 8, 108), LABELS[icon_id], font=font, fill=PALETTES[icon_id]["m"])
        draw.text((x + 8, 133), "dark #14252b / light #f2ebd3", font=small, fill="#a2b9ba")
    y = top
    for size in SIZES:
        height = row_heights[size]
        for index, icon_id in enumerate((*PROVIDERS, *BADGES)):
            x = 34 + index * 420
            icon = images[icon_id, size]
            for panel, background in enumerate(("#14252b", "#f2ebd3")):
                px = x + panel * 206
                edge = "#35505a" if panel == 0 else "#c1b797"
                ink = "#a2b9ba" if panel == 0 else "#435454"
                pixel_plate(draw, (px, y, px + 200, y + height - 10), background, edge)
                draw.text((px + 8, y + 7), f"{size}px", font=small, fill=ink)
                # 1x and 3x stay on the same plate, with a separate 6x below.
                for scale, ix in ((1, px + 54), (3, px + 103)):
                    draw.text((ix, y + 7), f"{scale}x", font=small, fill=ink)
                    out.paste(enlarged(icon, scale), (ix, y + 27), enlarged(icon, scale))
                large = enlarged(icon, 6)
                large_y = y + height - 20 - large.height
                out.paste(large, (px + (200 - large.width) // 2, large_y), large)
                draw.text((px + 8, large_y - 19), "6x", font=small, fill=ink)
        y += height

    y = main_bottom + 12
    draw.text((34, y), "SILHOUETTE ONLY / COLOR REMOVED / 6x OR 3x", font=title_font, fill="#f2e7c0")
    draw.text((34, y + 38), "Same alpha, one flat ink. Terminal rectangle / bubble with left tail / tall balloon with tether / robot ears + antenna.", font=font, fill="#a2b9ba")
    sy = y + 74
    for size_index, size in enumerate(SIZES):
        gx = 34 + size_index * 505
        scale = 6 if size <= 16 else 3
        draw.text((gx, sy), f"{size}px / {scale}x", font=font, fill="#f2e7c0")
        for column, icon_id in enumerate(PROVIDERS):
            # All five sizes fit; 32px uses 3x here, smallest sizes use 6x.
            source = silhouette(images[icon_id, size], "#10282c")
            stamp = enlarged(source, scale)
            px = gx + column * 122
            pixel_plate(draw, (px, sy + 29, px + 114, sy + 139), "#f2ebd3", "#c1b797")
            out.paste(stamp, (px + (114 - stamp.width) // 2, sy + 34 + (100 - stamp.height) // 2), stamp)
            draw.text((px + 4, sy + 146), LABELS[icon_id], font=small, fill="#a2b9ba")

    y = sy + 188
    draw.text((34, y), "RED / GREEN DEFICIENCY / SIMPLE LINEAR-LIGHT MATRIX", font=title_font, fill="#f2e7c0")
    draw.text((34, y + 38), "12px at 6x on both plates. Approximate simulation; hue is secondary to shape. Both plates are transformed too.", font=font, fill="#a2b9ba")
    for mode_index, mode in enumerate(CVD):
        gx = 34 + mode_index * 1260
        draw.text((gx, y + 76), mode.upper(), font=font, fill="#f2e7c0")
        for column, icon_id in enumerate((*PROVIDERS, *BADGES)):
            px = gx + column * 206
            for panel, background in enumerate(("#14252b", "#f2ebd3")):
                tile = Image.new("RGBA", (96, 96), background)
                tile.alpha_composite(enlarged(images[icon_id, 12], 6), (12, 12))
                out.paste(simulate(tile, mode).convert("RGB"), (px + panel * 100, y + 106))
            draw.text((px + 4, y + 209), LABELS[icon_id], font=small, fill="#a2b9ba")

    y += 259
    draw.text((34, y), "UNIVERSAL ROLE OVERLAYS / 12px at 6x / SAME-CANVAS PLACEMENT", font=title_font, fill="#f2e7c0")
    for column, icon_id in enumerate(PROVIDERS):
        gx = 34 + column * 620
        draw.text((gx, y + 41), LABELS[icon_id], font=font, fill="#a2b9ba")
        for role_index, role in enumerate(BADGES):
            composite = images[icon_id, 12].copy()
            composite.alpha_composite(images[role, 12])
            for panel, background in enumerate(("#14252b", "#f2ebd3")):
                px = gx + role_index * 274 + panel * 108
                pixel_plate(draw, (px, y + 71, px + 98, y + 167), background)
                stamp = enlarged(composite, 6)
                out.paste(stamp, (px + 13, y + 82), stamp)
            draw.text((gx + role_index * 274, y + 175), LABELS[role], font=small, fill="#a2b9ba")

    y += 221
    draw.text((34, y), "OFFICE TAG MOCKUPS / NATIVE 10 & 12px / EXACT INTEGER ZOOM", font=title_font, fill="#f2e7c0")
    # Native label pixels are rendered once, then zoomed with the entire tag.
    tag_font = ImageFont.load_default()
    for size_index, size in enumerate((10, 12)):
        for column, icon_id in enumerate(PROVIDERS):
            tag = Image.new("RGBA", (145, size + 8))
            td = ImageDraw.Draw(tag)
            pixel_plate(td, (0, 0, 144, size + 7), "#14252b")
            tag.alpha_composite(images[icon_id, size], (4, 4))
            td.text((size + 9, (size + 8 - 10) // 2), f"{LABELS[icon_id]} worker", font=tag_font, fill="#f2e7c0")
            out.paste(enlarged(tag, 3), (34 + column * 620, y + 47 + size_index * 73), enlarged(tag, 3))
    draw.text((34, out.height - 31), "No official logo input. No knot / flower / asterisk / starburst / sparkle. Crown and ID identify roles, independently of provider.", font=small, fill="#a2b9ba")
    return out


def write_assets() -> None:
    images = {(icon_id, size): render(icon_id, size)
              for icon_id in (*PROVIDERS, *BADGES) for size in SIZES}
    records = []
    exported = {}
    for icon_id in (*PROVIDERS, *BADGES):
        for size in SIZES:
            exported[icon_id, size] = images[icon_id, size]
    for icon_id in PROVIDERS:
        for role in BADGES:
            for size in SIZES:
                composite = images[icon_id, size].copy()
                composite.alpha_composite(images[role, size])
                exported[f"{icon_id}-{role}", size] = composite
    for (icon_id, size), icon in exported.items():
        folder = ROOT / str(size)
        folder.mkdir(exist_ok=True)
        paths = {}
        for scale in (1, 2, 3):
            suffix = "" if scale == 1 else f"@{scale}x"
            filename = f"{icon_id}{suffix}.png"
            path = folder / filename
            enlarged(icon, scale).save(path)
            paths[str(scale)] = f"{size}/{filename}"
        records.append({
            "id": icon_id, "size": size, "anchor": {"x": 0, "y": 0},
            "contentBounds": list(icon.getbbox()), "files": paths,
        })
    manifest = {
        "version": 1, "kind": "original-provider-icons", "sizes": list(SIZES),
        "sampling": "nearest-neighbor", "alpha": "straight", "pixelFormat": "RGBA8",
        "anchor": {"x": 0, "y": 0}, "anchorMeaning": "top-left of full square canvas",
        "densityMeaning": "@2x and @3x are exact integer repeats of each independently designed native size; anchors/padding multiply by density",
        "suggestedPlateColors": {"dark": "#14252b", "light": "#f2ebd3"},
        "suggestedPlatePadding": {str(size): {"left": 3 if size <= 12 else 4,
                                              "top": 3 if size <= 12 else 4,
                                              "right": 3 if size <= 12 else 4,
                                              "bottom": 3 if size <= 12 else 4,
                                              "labelGap": 4 if size <= 12 else 6}
                                  for size in SIZES},
        "palette": PALETTES,
        "icons": [{"id": icon_id, "label": LABELS[icon_id], "sizes": list(SIZES),
                   "accentColors": {"shadow": PALETTES[icon_id]["s"],
                                    "body": PALETTES[icon_id]["m"],
                                    "highlight": PALETTES[icon_id]["h"]},
                   "type": "overlay" if icon_id in BADGES else "provider",
                   "metaphor": {"codex": "terminal with >_ prompt",
                                "claude": "speech bubble, ink dot and pen stroke",
                                "antigravity": "rising balloon, loose tether and ground line",
                                "other-agent": "robot head, antenna and side ears",
                                "supervisor": "three-tip gold crown",
                                "sub-agent": "navy lanyard and paper ID"}[icon_id]}
                  for icon_id in (*PROVIDERS, *BADGES)],
        "overlays": [{"id": role, "appliesTo": list(PROVIDERS),
                      "placement": "top-right" if role == "supervisor" else "bottom-right",
                      "compositeOffset": {"x": 0, "y": 0},
                      "drawOrder": "provider first, then same-size transparent overlay",
                      "worker": "use the base provider icon without a badge",
                      "precomposedIdPattern": "{provider}-" + role}
                     for role in BADGES],
        "assets": records,
        "preview": "preview-provider-icons.png",
        "colorVisionPreview": {"matrices": CVD, "colorSpace": "linear sRGB",
                               "scope": "approximate full protanopia/deuteranopia, not clinical validation"},
        "integration": "art deliverable only; tools/build_pack.py and the runtime pack are unchanged",
    }
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    preview(images).save(ROOT / "preview-provider-icons.png")
    (ROOT / "design-notes.txt").write_text(
        "Original provider icon design notes\n\n"
        "Codex: a teal terminal and >_ prompt communicate code and command-line work. "
        "The silhouette is a wide rounded rectangle, with no interlocking knot or floral loops.\n"
        "Claude: an amber speech bubble with a small ink dot and a diagonal pen stroke "
        "communicates conversation and writing. Its left tail is not radial or asterisk-shaped; "
        "the yellow-amber fill is paired with blue-green ink, not an orange-on-tan brand treatment.\n"
        "Antigravity: a violet balloon, loose tether and quiet ground line communicate lift. "
        "Its tall rounded silhouette is neither a four-point sparkle nor a gradient star. "
        "All shading uses discrete flat pixel colors.\n"
        "Other agent: a slate robot head with ears and an antenna is a friendly generic fallback.\n"
        "Supervisor: a small gold crown marks responsibility. "
        "Sub-agent: a navy lanyard with a paper ID marks a supporting agent. "
        "Both transparent badges share the provider canvas and can be overlaid at (0,0).\n\n"
        "Every 10/12/16/24/32px image has its own authored pixel map. Only @2x/@3x "
        "exports repeat pixels. Small versions simplify detail intentionally.\n"
        "Limits: the pen is only a two-pixel ink gesture at 10px; the balloon tether and "
        "ground line are one pixel wide. Badges partly cover the upper/lower right of icons. "
        "Prefer unbadged 10px or 12px icons for the cleanest provider silhouette. "
        "Color-vision matrices are approximate visual checks, not accessibility certification.\n"
        "No official artwork or source images were used. These generic object metaphors "
        "avoid the forbidden logo motifs. Runtime integration is outside this asset-only task.\n",
        encoding="utf-8",
    )
    print(f"Wrote {len(exported) * 3} transparent PNGs, manifest, preview and design notes to {ROOT}")


if __name__ == "__main__":
    write_assets()
