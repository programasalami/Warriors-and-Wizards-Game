"""Bakes the character-select backdrop's ground into one image, using the SAME tile art and the SAME
edge/blend rules as the in-game map renderer (Game/TileBuilder.cs), so it looks like the real world
instead of flat green/brown squares.

  * tile variants + weights come from Ground.xml ("Woodland Grass/Dirt/Water" RandomTexture lists)
  * dirt and water tiles get their <Edge>/<Corner> overlays where a neighbour is a different type
  * grass tiles get the neighbouring dirt blended over them through the 8x8 alpha masks
    (Content/Sheets/AlphaTileBlends.png), including the engine's quirks (e.g. bottom-right uses mask 96)

Output: AlloyClient/AlloyClient/Content/Ui/Backdrop/ForestGround.png, native 16px tiles (the UI draws it 2x).
Run from the repo root:  python Tools/BookUi/build_backdrop_ground.py
"""
import math
import os
import random

import numpy as np
from PIL import Image

SHEETS = 'AlloyClient/AlloyClient/Content/Sheets'
OLD_SHEETS = 'Tools/Sheets/source_old'      # the pre-2026-09-20 sheets (ground + edge art the backdrop bakes); the game itself now uses Grasslands_16x16
OUT = 'AlloyClient/AlloyClient/Content/Ui/Backdrop/ForestGround.png'

COLS, ROWS = 40, 24            # 40 * 32 = 1280 design px wide, 24 * 32 = 768 tall (2x native tiles)
SEED = 20260918

# tile art -------------------------------------------------------------------------------------
ground = Image.open(f'{OLD_SHEETS}/ForestGround.png').convert('RGBA')
edges = Image.open(f'{OLD_SHEETS}/ForestGroundEdge.png').convert('RGBA')
masks_img = Image.open(f'{SHEETS}/AlphaTileBlends.png').convert('RGBA')


def tile(i):
    return ground.crop((i * 16, 0, i * 16 + 16, 16))


VARIANTS = {                                       # index lists copied from Ground.xml (repeats = weights)
    'grass': [0, 2, 0, 3, 0, 2, 3, 4],
    'dirt': [1, 7, 8, 1, 9, 5, 7, 6, 8, 9],
    'water': [10, 11, 10, 12, 10, 11, 12, 13],
}
PRIORITY = {'grass': 0, 'dirt': 1, 'water': 0}     # BlendPriority
HAS_EDGE = {'grass': False, 'dirt': True, 'water': True}
EDGE_IDX = {'dirt': (0, 1), 'water': (2, 3)}       # (edge, corner) index in ForestGroundEdge.png


def mask(i):
    m = masks_img.crop(((i % 8) * 8, (i // 8) * 8, (i % 8) * 8 + 8, (i // 8) * 8 + 8))
    return np.array(m)[:, :, 3].repeat(2, axis=0).repeat(2, axis=1) / 255.0    # 8x8 alpha -> 16x16


MASK_GROUP = {0: 0, 1: 4, 2: 8, 3: 12, 4: 16, 5: 20, 6: 24, 7: 28}               # random 3 variants each
MASK_FIXED = {80: 32, 81: 33, 82: 34, 83: 35, 84: 36, 85: 37, 86: 38, 87: 39,
              90: 40, 91: 41, 92: 42, 93: 43, 94: 44, 95: 45, 96: 46, 97: 47}

rng = random.Random(SEED)


def pick_mask(code):
    if code in MASK_GROUP:
        return mask(MASK_GROUP[code] + rng.randint(0, 2))
    return mask(MASK_FIXED[code])


# layout ---------------------------------------------------------------------------------------
PH = [rng.random() * 6.28 for _ in range(4)]


def noise(x, y):
    return (math.sin(x * 0.42 + PH[0]) * math.cos(y * 0.37 + PH[1])
            + 0.6 * math.sin((x + y) * 0.21 + PH[2]) + 0.4 * math.sin(x * 0.6 - y * 0.5 + PH[3])) / 2.0


kind = [['grass'] * COLS for _ in range(ROWS)]


def blob(cx, cy, rx, ry, k, wobble=0.75):
    for y in range(ROWS):
        for x in range(COLS):
            d = ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2
            if d + noise(x, y) * wobble < 1.0:
                kind[y][x] = k


# The book covers the middle (and is slightly see-through), so every hard-edged feature stays inside the side
# margins - the area under the pages is plain grass so no dirt rims ghost through the paper.
blob(-1.5, 12.0, 4.6, 5.5, 'dirt')         # left clearing
blob(0.5, 17.5, 3.6, 3.0, 'dirt')          # ...running down toward the pond
blob(0.0, 22.5, 3.6, 2.4, 'water')         # pond, bottom-left corner
blob(41.0, 13.0, 4.3, 4.8, 'dirt')         # right clearing
blob(40.5, 20.5, 3.2, 2.4, 'dirt')
blob(41.5, 4.0, 3.4, 2.4, 'water')         # pond, top-right corner


def kind_at(x, y):
    if 0 <= x < COLS and 0 <= y < ROWS:
        return kind[y][x]
    return None


variant = [[tile(rng.choice(VARIANTS[kind[y][x]])) for x in range(COLS)] for y in range(ROWS)]


def prio(k):
    return PRIORITY[k]


def get_tile(nx, ny, sx, sy):
    """TileBuilder.GetTile: the neighbour if its BlendPriority is higher than self's, otherwise self."""
    k = kind_at(nx, ny)
    if k is None:
        return (sx, sy)
    return (nx, ny) if prio(k) > prio(kind[sy][sx]) else (sx, sy)


def key(t):
    return kind[t[1]][t[0]]


def overlay(base, src_img, alpha):
    """Composite src (RGB of a tile) onto base using an alpha mask array."""
    b = np.array(base).astype(float)
    s = np.array(src_img).astype(float)
    a = alpha[:, :, None]
    out = b.copy()
    out[:, :, :3] = b[:, :, :3] * (1 - a) + s[:, :, :3] * a
    return Image.fromarray(out.astype(np.uint8), 'RGBA')


def blends(sx, sy):
    """TileBuilder.BuildBlends -> list of (source tile coords, mask code)."""
    n = lambda dx, dy: get_tile(sx + dx, sy + dy, sx, sy)
    me = (sx, sy)
    out = []

    def corner(t1, t2, t3, all_same, both_diff_same_type, both_diff_a, both_diff_b, only1, only3):
        if not (me != t1 or me != t2 or me != t3):
            return
        if me == t1 and me == t3:
            out.append((t2, all_same))
        elif me != t1 and me != t3:
            if key(t1) != key(t3):
                out.append((t1, both_diff_a))
                out.append((t3, both_diff_b))
            else:
                out.append((t1, both_diff_same_type))
        elif me != t1:
            out.append((t1, only1))
        else:
            out.append((t3, only3))

    corner(n(-1, 0), n(-1, -1), n(0, -1), 80, 84, 90, 94, 0, 4)     # top left
    corner(n(0, -1), n(1, -1), n(1, 0), 81, 85, 91, 95, 5, 2)       # top right
    corner(n(0, 1), n(-1, 1), n(-1, 0), 82, 86, 92, 96, 6, 1)       # bottom left
    corner(n(1, 0), n(1, 1), n(0, 1), 83, 87, 93, 96, 3, 7)         # bottom right (mask 96 is the engine's own quirk)
    return out


def edge_layers(sx, sy):
    """TileBuilder.BuildEdges (SameTypeEdgeMode): overlays where a neighbour is a different type."""
    me = kind[sy][sx]
    e_i, c_i = EDGE_IDX[me]
    edge = edges.crop((e_i * 16, 0, e_i * 16 + 16, 16))
    corner_img = edges.crop((c_i * 16, 0, c_i * 16 + 16, 16))

    def same(dx, dy):
        if dx == 0 and dy == 0:
            return True
        k = kind_at(sx + dx, sy + dy)
        return k is None or k == me

    s = {(dx, dy): same(dx, dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)}
    layers = []
    if not s[(0, -1)]:
        layers.append(edge.transpose(Image.TRANSPOSE))                                       # top
    if not s[(-1, 0)]:
        layers.append(edge)                                                                   # left
    if not s[(1, 0)]:
        layers.append(edge.transpose(Image.FLIP_LEFT_RIGHT))                                  # right
    if not s[(0, 1)]:
        layers.append(edge.transpose(Image.TRANSPOSE).transpose(Image.FLIP_TOP_BOTTOM))       # bottom
    if not s[(-1, 0)] and not s[(0, -1)] and not s[(-1, -1)]:
        layers.append(corner_img)
    if not s[(0, -1)] and not s[(1, 0)] and not s[(1, -1)]:
        layers.append(corner_img.transpose(Image.FLIP_LEFT_RIGHT))
    if not s[(1, 0)] and not s[(0, 1)] and not s[(1, 1)]:
        layers.append(corner_img.transpose(Image.FLIP_LEFT_RIGHT).transpose(Image.FLIP_TOP_BOTTOM))
    if not s[(-1, 0)] and not s[(0, 1)] and not s[(-1, 1)]:
        layers.append(corner_img.transpose(Image.FLIP_TOP_BOTTOM))
    return layers


canvas = Image.new('RGBA', (COLS * 16, ROWS * 16))
for y in range(ROWS):
    for x in range(COLS):
        t = variant[y][x].copy()
        if HAS_EDGE[kind[y][x]]:
            for layer in edge_layers(x, y):
                t.alpha_composite(layer)
        else:
            for (nx, ny), code in blends(x, y):
                t = overlay(t, variant[ny][nx], pick_mask(code))
        canvas.paste(t, (x * 16, y * 16))

# decor -----------------------------------------------------------------------------------------
# (OLD decor-sheet index - mapped to the new sheets below - , base X, base Y in the 1280x720 design canvas, sways?) - base = where the object stands.
# Sheet indices: 0 Tree 1 Pine 2 Bush 3 Shrub 4 Log 5 Reeds 6 Tuft 7 Rock cluster 8/9 Mushrooms 10 Flowers
#                11 Oak 12 Pine tall 13 Bush wide 14 Small rock 15 Log mossy
DECOR = [
    # left margin
    (0, 62, 168, True), (2, 26, 200, True), (11, 150, 92, True),
    (7, 66, 338, False), (14, 30, 392, False), (15, 96, 500, False), (14, 44, 566, False),
    (5, 112, 676, False), (5, 64, 662, False), (3, 150, 690, True),
    # right margin
    (11, 1236, 248, True), (1, 1176, 214, True), (13, 1196, 262, True),
    (7, 1230, 402, False), (14, 1252, 470, False), (10, 1196, 330, False),
    (12, 1262, 610, True), (2, 1240, 696, True),
    # top and bottom slivers
    (6, 640, 30, False), (8, 700, 44, False), (10, 420, 24, False), (6, 880, 700, False), (9, 560, 706, False),
]
INSET = {0: .067, 1: .062, 2: .035, 3: .03, 4: .04, 5: .03, 6: .028, 7: .035, 8: .03, 9: .03, 10: .028, 11: .067, 12: .062,
         13: .035, 14: .03, 15: .04}
SHADOW = {0: (46, 18), 1: (34, 14), 2: (36, 14), 3: (24, 10), 4: (44, 14), 5: (26, 10), 6: (16, 7), 7: (34, 12), 8: (16, 7),
          9: (16, 7), 10: (14, 6), 11: (46, 18), 12: (34, 14), 13: (40, 15), 14: (20, 8), 15: (44, 14)}   # design px w, h
PIXEL_SCALE = 1.5        # design px per art px for sprites - the in-game objects run ~1.4-1.6, the ground 2.0
import json
MOVED = json.load(open('Tools/Sheets/migration_map_2026-09-20.json'))                       # 'forestDecor:n' -> 'newSheet:m'
CELL = {'smallPlants': (24, 36), 'mediumPlants': (40, 60), 'smallTrees': (56, 84), 'mediumTrees': (80, 120)}

# the game's soft ground shadow under every object, baked into the ground so the sprites sit on it
sh = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
shp = sh.load()
for idx, bx, by, _ in DECOR:
    w, h = SHADOW[idx]
    cx, cy = bx / 2.0, by / 2.0                       # design -> native px
    rx, ry = w / 4.0, h / 4.0
    for yy in range(int(cy - ry - 2), int(cy + ry + 3)):
        for xx in range(int(cx - rx - 2), int(cx + rx + 3)):
            if 0 <= xx < sh.width and 0 <= yy < sh.height:
                d = ((xx + 0.5 - cx) / rx) ** 2 + ((yy + 0.5 - cy) / ry) ** 2
                if d < 1.0:
                    a = 0.34 * (1 - d) ** 0.6
                    old = shp[xx, yy][3] / 255.0
                    shp[xx, yy] = (0, 0, 0, int(255 * min(0.5, old + a)))
canvas.alpha_composite(sh)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
canvas.save(OUT)
print('saved', OUT, canvas.size)

# C# table for ForestBackdrop -----------------------------------------------------------------
cs = ['// <auto-generated> by Tools/BookUi/build_backdrop_ground.py - do not edit by hand.',
      'namespace AlloyClient.Screens.Components.CharacterList;', '',
      'internal static class BackdropData {',
      '    // Sheet/Index = the decor cell (SmallPlants/MediumPlants/SmallTrees/MediumTrees); Base = where it stands (1280x720 design px); Width/Height = drawn cell size;',
      '    // InsetPx = how far the cell bottom hangs below the base (per-object BottomInset).',
      '    internal readonly record struct Decor(string Sheet, int Index, int BaseX, int BaseY, int Width, int Height, int InsetPx, bool Sway);', '',
      '    internal static readonly Decor[] Items = [']
for idx, bx, by, sway in DECOR:
    sheet, new_idx = MOVED[f'forestDecor:{idx}'].split(':')
    cw, ch = CELL[sheet]
    w, h = round(cw * PIXEL_SCALE), round(ch * PIXEL_SCALE)
    inset = round(INSET[idx] * 120 * PIXEL_SCALE)          # the art keeps the same pixel gap under it on every sheet, so this does not depend on the cell
    cs.append(f'        new("{sheet}", {new_idx}, {bx}, {by}, {w}, {h}, {inset}, {str(sway).lower()}),')
cs += ['    ];', '}', '']
open('AlloyClient/AlloyClient/Screens/Components/CharacterList/BackdropData.g.cs', 'w', encoding='utf-8', newline=chr(10)).write(chr(10).join(cs))
print('decor items', len(DECOR))
