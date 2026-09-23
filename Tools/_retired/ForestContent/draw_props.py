# OBSOLETE since 2026-09-20: the game's art sheets are now edited directly (see Tools/Sheets/README.md), so this script would recreate sheets that no longer
# exist. Kept as the record of how the old art was made / where it came from. Run with --force only if you know why.
import sys
if '--force' not in sys.argv:
    raise SystemExit('obsolete: see Tools/Sheets/README.md (run with --force to run it anyway)')
"""Draws the Nexus props that are not part of the forest pack into WaW-Client/WaWClient/Content/Sheets/ForestProps.png (cells of 80x120 like ForestDecor.png,
4 cells per row; game atlas name "forestProps"). Everything is drawn here in code, in the forest palette, so it is new art and reproducible.

    cell 0 : the Bug Board - a wooden notice board on two posts with paper notes pinned to it
    cell 1 : the Jukebox (placeholder until there is a proper jukebox texture) - a wooden cabinet with a glowing screen, buttons and speakers
    cell 2 : the Vault Chest - the bought "Treasure Chests" pack (style 6, fully open), from Tools/ForestContent/source/vault; your stored items live in these
    cell 3 : the Closed Vault Chest - the same chest, closed, with the pack's gold padlock; a spot where you can later get another chest

Run:  python Tools/ForestContent/draw_props.py [preview.png]
"""
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')).replace(os.sep, '/') + '/'
OUT = ROOT + 'WaW-Client/WaWClient/Content/Sheets/ForestProps.png'
CELL_W, CELL_H, COLS = 80, 120, 4

# palette (the forest pack's own dark outline / wood / grass colours + the UI's parchment)
OUTLINE = (16, 20, 28, 255)
WOOD_DARK = (76, 48, 52, 255)
WOOD = (127, 74, 20, 255)
WOOD_LIGHT = (176, 136, 34, 255)
POST = (96, 58, 24, 255)
PAPER = (218, 206, 164, 255)
PAPER_SHADE = (174, 164, 126, 255)
INK = (69, 77, 89, 255)
PIN = (183, 65, 50, 255)
PIN_LIGHT = (230, 113, 70, 255)
GRASS_DARK = (35, 93, 49, 255)
GRASS = (74, 134, 54, 255)
SCREEN_DARK = (24, 66, 78, 255)
SCREEN = (42, 125, 117, 255)
SCREEN_LIGHT = (109, 186, 121, 255)
GOLD = (235, 184, 91, 255)
IRON = (96, 104, 118, 255)
IRON_LIGHT = (150, 160, 174, 255)
INSIDE = (30, 22, 24, 255)


def rect(img, x0, y0, x1, y1, color):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            img.putpixel((x, y), color)


def outlined(img, x0, y0, x1, y1, fill):
    rect(img, x0 - 1, y0 - 1, x1 + 1, y1 + 1, OUTLINE)
    rect(img, x0, y0, x1, y1, fill)


def note(img, x0, y0, w, h, lines):
    outlined(img, x0, y0, x0 + w - 1, y0 + h - 1, PAPER)
    rect(img, x0, y0 + h - 1, x0 + w - 1, y0 + h - 1, PAPER_SHADE)                 # shaded lower edge
    img.putpixel((x0 + w // 2, y0), PIN)                                            # the pin
    img.putpixel((x0 + w // 2 - 1, y0), PIN_LIGHT)
    img.putpixel((x0 + w // 2, y0 + 1), PIN)
    for i in range(lines):                                                          # scribbled lines of writing
        y = y0 + 4 + i * 3
        if y >= y0 + h - 2:
            break
        for x in range(x0 + 2, x0 + w - 2 - (1 if i % 2 else 0)):
            if (x + i) % 5 != 0:
                img.putpixel((x, y), INK)


def bug_board(img):
    """Drawn into a scratch image of the sprite's own size, then placed on the cell so its feet are on the cell's bottom edge."""
    W, H = 54, 62
    s = Image.new('RGBA', (W, H), (0, 0, 0, 0))

    # posts
    for px in (7, 41):
        outlined(s, px, 34, px + 5, H - 3, POST)
        rect(s, px + 1, 34, px + 1, H - 3, WOOD)                                    # lit edge
    # grass at the feet
    for px in (4, 38):
        for i, gx in enumerate(range(px, px + 12)):
            h = 2 if i % 3 else 3
            for gy in range(H - 2 - h, H - 1):
                s.putpixel((gx, gy), GRASS if (gx + gy) % 2 else GRASS_DARK)

    # roof cap and board
    outlined(s, 1, 4, W - 2, 8, WOOD_DARK)
    rect(s, 2, 5, W - 3, 5, WOOD)
    outlined(s, 3, 9, W - 4, 40, WOOD)
    rect(s, 4, 10, W - 5, 10, WOOD_LIGHT)                                           # lit top edge
    rect(s, 4, 39, W - 5, 39, WOOD_DARK)                                            # shaded bottom edge
    for x in (18, 32):                                                              # plank seams
        rect(s, x, 11, x, 38, WOOD_DARK)

    # pinned notes
    note(s, 6, 13, 12, 14, 3)
    note(s, 21, 12, 13, 12, 2)
    note(s, 37, 14, 11, 15, 3)
    note(s, 9, 29, 11, 10, 2)
    note(s, 24, 27, 12, 11, 2)
    note(s, 39, 32, 8, 7, 1)
    return s


def circle(img, cx, cy, r, color):
    for y in range(cy - r, cy + r + 1):
        for x in range(cx - r, cx + r + 1):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r + 1:
                img.putpixel((x, y), color)


def jukebox(img):
    """A wooden music-player cabinet: arched top, a glowing screen with a music note and level bars, three buttons, two speakers."""
    W, H = 50, 62
    s = Image.new('RGBA', (W, H), (0, 0, 0, 0))

    # body: arched top, straight sides (outline first, then the fill one pixel inside)
    rows = {3: (15, 34), 4: (10, 39), 5: (8, 41), 6: (7, 42), 7: (6, 43)}
    for y in range(8, 58):
        rows[y] = (5, 44)
    for y, (x0, x1) in rows.items():
        rect(s, x0 - 1, y, x1 + 1, y, OUTLINE)
    for y, (x0, x1) in rows.items():
        rect(s, x0, y, x1, y, WOOD if y > 3 else OUTLINE)
    rect(s, 5, 8, 6, 56, WOOD_LIGHT)                                               # lit left edge
    rect(s, 43, 8, 44, 56, WOOD_DARK)                                              # shaded right edge
    for x in range(12, 38, 3):                                                     # little lights along the arch
        s.putpixel((x, 5), GOLD)

    # screen with a music note and level bars
    outlined(s, 12, 11, 37, 29, SCREEN_DARK)
    rect(s, 13, 12, 36, 28, SCREEN)
    rect(s, 13, 12, 36, 13, SCREEN_LIGHT)                                          # glass shine
    for x, h in ((15, 5), (18, 9), (21, 6)):                                       # level bars
        rect(s, x, 27 - h, x + 1, 27, SCREEN_LIGHT)
    rect(s, 31, 15, 31, 25, PAPER)                                                 # the note: stem, head, flag
    rect(s, 28, 24, 30, 26, PAPER)
    rect(s, 32, 15, 34, 16, PAPER)
    s.putpixel((34, 17), PAPER)

    # buttons
    for x, color in ((13, PIN), (21, PIN_LIGHT), (29, GOLD)):
        outlined(s, x, 34, x + 6, 38, color)

    # speakers
    for cx in (16, 33):
        circle(s, cx, 48, 5, OUTLINE)
        circle(s, cx, 48, 4, WOOD_DARK)
        for dx, dy in ((0, 0), (-2, -1), (2, -1), (-1, 2), (1, 2), (0, -3), (0, 3)):
            s.putpixel((cx + dx, 48 + dy), OUTLINE)

    # feet
    for x in (7, 36):
        outlined(s, x, 58, x + 6, 60, POST)
    return s


def vault_chest(opened):
    """The bought chest art (Desktop/Treasure Chests, 32x32, style 6), cropped to its pixels and kept at its native size."""
    src = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'source', 'vault')
    if opened:
        img = Image.open(os.path.join(src, 'chest_open.png')).convert('RGBA')
    else:
        img = Image.open(os.path.join(src, 'chest_static.png')).convert('RGBA')
        img.alpha_composite(Image.open(os.path.join(src, 'chest_lock_gold.png')).convert('RGBA'))
    return img.crop(img.getbbox())


def build():
    sheet = Image.new('RGBA', (CELL_W * COLS, CELL_H), (0, 0, 0, 0))
    board = bug_board(sheet)
    # cell 0: centred, feet on the bottom row (the same 3 empty rows under the sprites as the forest pack)
    sheet.alpha_composite(board, ((CELL_W - board.width) // 2, CELL_H - 3 - board.height))
    box = jukebox(sheet)
    sheet.alpha_composite(box, (CELL_W + (CELL_W - box.width) // 2, CELL_H - 3 - box.height))     # cell 1
    for cell, opened in ((2, True), (3, False)):
        img = vault_chest(opened)
        sheet.alpha_composite(img, (cell * CELL_W + (CELL_W - img.width) // 2, CELL_H - 3 - img.height))
    return sheet


if __name__ == '__main__':
    sheet = build()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.save(OUT)
    print('wrote', OUT, sheet.size, 'bbox of cell 0:', sheet.crop((0, 0, CELL_W, CELL_H)).getbbox())
    if len(sys.argv) > 1:
        bg = Image.new('RGBA', (CELL_W * 6, CELL_H * 3), (90, 110, 70, 255))
        bg.alpha_composite(sheet.crop((0, 0, CELL_W * 2, CELL_H)).resize((CELL_W * 6, CELL_H * 3), Image.NEAREST))
        bg.convert('RGB').save(sys.argv[1])
        print('preview', sys.argv[1])
