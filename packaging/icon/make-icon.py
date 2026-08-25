#!/usr/bin/env python3
"""
Generates the application icon as a PNG, with no image library required.

The build machines for this project are not guaranteed to have PIL,
ImageMagick or Inkscape, and an icon is effectively required for a Linux
desktop entry. Writing the PNG by hand keeps icon generation working from a
bare checkout, the same principle the rest of the project follows.

The design is the jet stream: a band sweeping across a dark field, brightest
and widest where the core is fastest, shaded with the same blue-to-red ramp
the app uses for wind speed.
"""

import math
import struct
import zlib
import sys

SIZE = 512          # overridden by the second command-line argument
CORNER = 0.18          # corner radius as a fraction of the icon size

BACKGROUND = (0x12, 0x16, 0x1C)

# The wind-speed ramp from Theme.JetSpeedColor, slowest to fastest.
RAMP = [
    (0x2E, 0x5E, 0x85),
    (0x45, 0x96, 0xE0),
    (0x7A, 0xD1, 0x6E),
    (0xE8, 0xC6, 0x3A),
    (0xE8, 0x97, 0x3A),
    (0xE0, 0x52, 0x3F),
]


def ramp_color(t):
    """Samples the speed ramp at t in [0, 1] with linear interpolation."""
    t = max(0.0, min(1.0, t))
    scaled = t * (len(RAMP) - 1)
    low = int(math.floor(scaled))
    high = min(low + 1, len(RAMP) - 1)
    f = scaled - low

    return tuple(
        int(round(RAMP[low][i] + (RAMP[high][i] - RAMP[low][i]) * f))
        for i in range(3)
    )


def rounded_alpha(x, y):
    """Anti-aliased coverage for a rounded square, so the icon has soft corners."""
    r = CORNER * SIZE
    cx = min(max(x, r), SIZE - r)
    cy = min(max(y, r), SIZE - r)
    d = math.hypot(x - cx, y - cy)

    if d <= r - 1:
        return 1.0
    if d >= r + 1:
        return 0.0
    return (r + 1 - d) / 2.0


def jet_band(x, y):
    """
    Coverage and speed for the jet stream band at a pixel.

    The band is a sine curve across the icon. It thickens and speeds up toward
    the middle, which is what a real jet core looks like on a chart.
    """
    u = x / SIZE                                    # 0..1 across the icon
    centre = SIZE * (0.52 + 0.17 * math.sin((u - 0.15) * math.pi * 1.7))

    # Fastest in the middle third, tapering to nothing at the edges.
    speed = math.sin(min(1.0, max(0.0, (u - 0.04) / 0.92)) * math.pi)
    speed = speed ** 0.65

    half_width = SIZE * (0.030 + 0.055 * speed)
    distance = abs(y - centre)

    if distance > half_width:
        return 0.0, speed

    # Soft edges: fully opaque in the core, feathering out to the rim.
    edge = 1.0 - (distance / half_width) ** 2
    return edge, speed


def build_pixels():
    rows = []
    for y in range(SIZE):
        row = bytearray()
        for x in range(SIZE):
            r, g, b = BACKGROUND
            alpha = rounded_alpha(x + 0.5, y + 0.5)

            coverage, speed = jet_band(x + 0.5, y + 0.5)
            if coverage > 0:
                jr, jg, jb = ramp_color(speed)
                r = int(r + (jr - r) * coverage)
                g = int(g + (jg - g) * coverage)
                b = int(b + (jb - b) * coverage)

            row += bytes((r, g, b, int(round(alpha * 255))))
        rows.append(row)
    return rows


def write_png(path, rows):
    raw = b"".join(b"\x00" + bytes(row) for row in rows)   # filter type 0 per scanline

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0)   # 8-bit RGBA

    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        handle.write(chunk(b"IHDR", header))
        handle.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        handle.write(chunk(b"IEND", b""))


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else "weather.png"

    # Android wants a smaller launcher icon than a desktop entry does, so the
    # size is a parameter rather than a second copy of this script.
    if len(sys.argv) > 2:
        globals()["SIZE"] = int(sys.argv[2])


    write_png(target, build_pixels())
    print("wrote %s (%dx%d)" % (target, SIZE, SIZE))
