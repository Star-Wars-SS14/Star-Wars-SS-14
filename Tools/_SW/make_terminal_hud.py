#!/usr/bin/env python3
"""Builds the art of the SW terminal HUD theme from the default theme: flat slots, steel icons, no stripes.

Run from the repository root after the default art changes: python Tools/_SW/make_terminal_hud.py
Needs Pillow. Only files listed here are written; anything else falls back to the default theme.
"""
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / "Resources/Textures/Interface"
TARGET = REPO / "Resources/Textures/_SW/Interface/Terminal"

FILL = (16, 18, 21)
BORDER = (79, 86, 102)
ICON_DARK = (62, 69, 82)
ICON_BRIGHT = (142, 151, 168)
HIGHLIGHT = (200, 205, 216)
PIECE_FILL = (42, 46, 54)
PIECE_EDGE = (12, 14, 15)


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def luminance(pixel):
    return (0.2126 * pixel[0] + 0.7152 * pixel[1] + 0.0722 * pixel[2]) / 255


def is_edge(image, x, y):
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if not (0 <= nx < image.width and 0 <= ny < image.height) or image.getpixel((nx, ny))[3] == 0:
            return True
    return False


def slot(source):
    """A flat box with a one pixel border. The bluish silhouette stays as a steel icon."""
    out = source.copy()
    pixels = [source.getpixel((x, y)) for y in range(source.height) for x in range(source.width)]
    icon = [p for p in pixels if p[3] and p[2] - p[0] >= 10 and p[2] > 40]
    low = min((luminance(p) for p in icon), default=0)
    high = max((luminance(p) for p in icon), default=1)
    for y in range(source.height):
        for x in range(source.width):
            pixel = source.getpixel((x, y))
            if not pixel[3]:
                continue
            if is_edge(source, x, y):
                color = BORDER
            elif pixel[2] - pixel[0] >= 10 and pixel[2] > 40:
                color = lerp(ICON_DARK, ICON_BRIGHT, (luminance(pixel) - low) / max(high - low, 0.001))
            else:
                color = FILL
            out.putpixel((x, y), (*color, 255))
    return out


def tint(source, color):
    """One colour, the source's alpha."""
    out = source.copy()
    for y in range(source.height):
        for x in range(source.width):
            out.putpixel((x, y), (*color, source.getpixel((x, y))[3]))
    return out


def piece(source):
    """Storage pieces: their dark edge and a flat body, without the scan lines."""
    out = source.copy()
    for y in range(source.height):
        for x in range(source.width):
            pixel = source.getpixel((x, y))
            if pixel[3]:
                out.putpixel((x, y), (*(PIECE_EDGE if luminance(pixel) < 0.12 else PIECE_FILL), pixel[3]))
    return out


def steel(source, bright=ICON_BRIGHT):
    """Removes the hue and maps brightness from near-black to steel."""
    out = source.copy()
    for y in range(source.height):
        for x in range(source.width):
            pixel = source.getpixel((x, y))
            if pixel[3]:
                out.putpixel((x, y), (*lerp(PIECE_EDGE, bright, min(luminance(pixel) * 1.6, 1)), pixel[3]))
    return out


def checkbox(checked):
    """Our own flat box, 20 px like the default one. The tick is two lines of a white stroke."""
    out = Image.new("RGBA", (20, 20), (*FILL, 255))
    for i in range(20):
        for edge in ((i, 0), (i, 19), (0, i), (19, i)):
            out.putpixel(edge, (*ICON_BRIGHT, 255))
    if checked:
        for x, y in [(5 + i, 10 + i) for i in range(4)] + [(8 + i, 13 - i) for i in range(7)]:
            for dy in (0, 1):
                out.putpixel((x, y + dy), (*HIGHLIGHT, 255))
    return out


def write(relative, make):
    source = Image.open(SOURCE / relative).convert("RGBA")
    # The theme looks files up from its own root, as the default theme does. Radial icons keep their folder.
    target = TARGET / (relative.removeprefix("Default/"))
    target.parent.mkdir(parents=True, exist_ok=True)
    make(source).save(target)


def main():
    default = SOURCE / "Default"
    for path in sorted((default / "Slots").glob("*.png")):
        write(path.relative_to(SOURCE).as_posix(), slot)
    for name in ("SlotBackground", "template_small", "item_status_left", "item_status_right"):
        write(f"Default/{name}.png", slot)
    # White, because the game tints the active hand's outline with the player's faction.
    for name in ("slot_highlight", "item_status_left_highlight", "item_status_right_highlight"):
        write(f"Default/{name}.png", lambda s: tint(s, (255, 255, 255)))
    for path in sorted((default / "Storage").glob("piece_*.png")):
        write(path.relative_to(SOURCE).as_posix(), piece)
    for path in sorted((default / "Storage").glob("sidebar_*.png")):
        write(path.relative_to(SOURCE).as_posix(), slot)
    for name in ("left_arrow", "right_arrow", "filled_left_arrow", "filled_right_arrow"):
        write(f"Default/{name}.svg.192dpi.png", lambda s: tint(s, ICON_BRIGHT))
    checkbox(False).save(TARGET / "checkbox_unchecked.png")
    checkbox(True).save(TARGET / "checkbox_checked.png")
    for name in ("button_normal", "close_normal", "back_normal"):
        write(f"Nano/Radial/{name}.png", steel)
    for name in ("button_hover", "close_hover", "back_hover"):
        write(f"Nano/Radial/{name}.png", lambda s: steel(s, HIGHLIGHT))


if __name__ == "__main__":
    main()
