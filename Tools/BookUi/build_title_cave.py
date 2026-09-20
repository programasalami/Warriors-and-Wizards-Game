"""Bakes the main-menu background: a dark cave built from the RPGW Caves pack (Desktop/RPGW_Caves_v2.1, license: free for
personal/commercial use incl. modification; not for logos/trademarks; credit appreciated).

Output 1: AlloyClient/AlloyClient/Content/Title/TitleMap.png - ONE image, the map (1280 x 720 = the UI design canvas, so map
coordinates ARE design coordinates) with a strip of soft glow sprites appended below it (used by CaveBackdrop's ambience).
It is loaded as its own texture (TextureType.TitleBackground), not packed into the crowded UI atlas.

Output 2: AlloyClient/AlloyClient/Screens/Components/CaveBackdropData.g.cs - where the ambience goes (crystal glows, warm
wall light, water glints, spore spots, mist), generated from the same layout so it always lines up with the props.

Layers, bottom to top: floor tiles -> patches -> pond -> cliff masses (irregular, cropped by the screen edges, so the map
reads as bigger than the screen) -> contact shadows -> props (rock clusters, crystals, mushroom clumps) -> vignette.

Run from the repo root: python Tools/BookUi/build_title_cave.py [preview.png]
"""
import os
import random
import sys

import cv2
import numpy as np
from PIL import Image

PACK = os.path.expanduser('~/Desktop/RPGW_Caves_v2.1')
OUT = 'AlloyClient/AlloyClient/Content/Title/TitleMap.png'
DATA_OUT = 'AlloyClient/AlloyClient/Screens/Components/CaveBackdropData.g.cs'
W, H = 1280, 720
T = 32
EXT = 320                      # scratch margin on every side so pieces can hang off the edge
SEED = 20260919

rng = random.Random(SEED)
sheet = Image.open(f'{PACK}/MainLev2.0.png').convert('RGBA')
deco = Image.open(f'{PACK}/decorative.png').convert('RGBA')
canvas = Image.new('RGBA', (W + 2 * EXT, H + 2 * EXT), (0, 0, 0, 255))


def crop(img, x, y, w, h):
    return img.crop((x, y, x + w, y + h))


def trimmed(img):
    bb = img.getbbox()
    return img.crop(bb) if bb else img


def put(img, x, y, flip=False):
    """Paste with alpha at map coordinates (may be off-screen)."""
    if flip:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
    canvas.alpha_composite(img, (int(x) + EXT, int(y) + EXT))


# ---------------------------------------------------------------- floor ------------------------------------------------
# The sheet's bottom strip holds seamless ground textures: any fully opaque 32 px tile of a panel tiles with any other.
def floor_tiles(x0):
    out = []
    for ty in range(1280, 1536, T):
        for tx in range(x0, x0 + 288, T):
            t = crop(sheet, tx, ty, T, T)
            if np.array(t)[:, :, 3].min() == 255:
                out.append(t)
    return out


FLOOR = floor_tiles(576)     # deep plum
for ty in range(-EXT // T - 1, (H + EXT) // T + 2):
    for tx in range(-EXT // T - 1, (W + EXT) // T + 2):
        put(rng.choice(FLOOR), tx * T, ty * T)


# ---------------------------------------------------------------- patches ----------------------------------------------
def blob(x, y, w, h, fade=0.72):
    img = crop(sheet, x, y, w, h)
    a = np.array(img)[:, :, 3]
    n, lab, stats, _ = cv2.connectedComponentsWithStats((a > 0).astype(np.uint8), connectivity=4)
    big = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    arr = np.array(img)
    arr[lab != big] = 0
    out = trimmed(Image.fromarray(arr, 'RGBA'))
    o = np.array(out)
    o[:, :, 3] = (o[:, :, 3] * fade).astype(np.uint8)
    return Image.fromarray(o, 'RGBA')


PATCH = {'moss': blob(320, 1058, 160, 158), 'slate': blob(800, 1058, 160, 158), 'dirt': blob(1280, 1058, 160, 158)}
# (kind, x, y, scale, flip) - placed in the open floor, away from the cliffs
PATCH_PLAN = [('slate', 120, 250, 1.0, False), ('dirt', 1015, 40, 0.9, True), ('moss', 40, 470, 0.8, False),
              ('moss', 950, 500, 1.1, True), ('slate', 700, 70, 0.8, True),
              ('moss', 470, 30, 0.7, False)]
for kind, x, y, sc, flip in PATCH_PLAN:
    img = PATCH[kind]
    if sc != 1.0:
        img = img.resize((int(img.width * sc), int(img.height * sc)), Image.LANCZOS)
    put(img, x, y, flip)

# ---------------------------------------------------------------- pond -------------------------------------------------
POND = trimmed(crop(sheet, 1400, 1320, 180, 190))      # the small rounded pond (the sheet also has a big diamond one)
POND_POS = (526, 150)
put(POND, *POND_POS)


# ---------------------------------------------------------------- cliff masses -----------------------------------------
# Pieces from the top-left block of the sheet: glowing amber rock (y=0..) and the colder brown set (y=416..). Each is a
# rock mass with a dark top and a lit front face; they are dropped in at irregular spots around the edge and cropped by
# the screen, with open floor running to the edge in between - so the map keeps going past the window.
def piece(x, y, w, h):
    return trimmed(crop(sheet, x, y, w, h))


RING_G, RING_C = piece(64, 0, 192, 277), piece(64, 416, 192, 277)
HEX_G, HEX_C = piece(320, 0, 128, 232), piece(320, 416, 128, 232)

# (piece, x, y, flip, lit) - x, y = top-left in map coordinates; lit=True pieces also get warm ambience
CLIFFS = [
    # left edge
    (RING_G, -122, -70, False, True), (RING_C, -100, 190, True, False), (HEX_G, -66, 470, False, True),
    # top edge (only the lit faces hang into view)
    (HEX_C, 236, -168, False, False), (HEX_G, 410, -178, False, True), (RING_G, 606, -214, True, True),
    (HEX_G, 830, -176, True, True), (RING_C, 1010, -206, False, False),
    # right edge
    (RING_G, 1204, -50, True, True), (RING_C, 1190, 236, False, False), (HEX_G, 1236, 520, True, True),
    # a free-standing mass between the logo and the scroll, and one in the lower right
    (HEX_C, 552, 470, False, False), (RING_C, 1110, 590, True, False),
]
cliff_mask = np.zeros((H + 2 * EXT, W + 2 * EXT), np.uint8)
GLOW_SPOTS = []      # warm-light centres (map coords) taken from the lit pieces' faces
for img, x, y, flip, lit in CLIFFS:
    put(img, x, y, flip)
    a = (np.array(img.transpose(Image.FLIP_LEFT_RIGHT) if flip else img)[:, :, 3] > 0).astype(np.uint8)
    x0, y0 = int(x) + EXT, int(y) + EXT
    hh, ww = min(a.shape[0], cliff_mask.shape[0] - y0), min(a.shape[1], cliff_mask.shape[1] - x0)
    cliff_mask[y0:y0 + hh, x0:x0 + ww] |= a[:hh, :ww]
    if lit:
        cx, cy = x + img.width / 2, y + img.height - 40       # the lit front face
        if -20 < cx < W + 20 and -20 < cy < H + 20:
            GLOW_SPOTS.append((int(cx), int(min(max(cy, 30), H - 30))))

pond_mask = np.zeros_like(cliff_mask)
pond_mask[POND_POS[1] + EXT:POND_POS[1] + EXT + POND.height, POND_POS[0] + EXT:POND_POS[0] + EXT + POND.width] = \
    (np.array(POND)[:, :, 3] > 0).astype(np.uint8)
KEEP_OUT = cv2.dilate(cliff_mask, np.ones((9, 9), np.uint8), iterations=6) | cv2.dilate(pond_mask, np.ones((9, 9), np.uint8), iterations=7)


# ---------------------------------------------------------------- props ------------------------------------------------
def prop(box):
    x, y, w, h = box
    return trimmed(crop(deco, x, y, w, h))


ROCK_BIG = [prop(b) for b in [(0, 30, 90, 90), (96, 44, 70, 80), (162, 56, 60, 70), (296, 160, 84, 88), (222, 170, 70, 80),
                              (162, 176, 66, 80)]]
ROCK_SMALL = [prop(b) for b in [(232, 70, 60, 55), (300, 76, 50, 50), (356, 96, 30, 30), (0, 230, 34, 30), (44, 206, 50, 60),
                               (100, 200, 60, 60)]]
CRYSTAL = {'teal': prop((0, 570, 130, 145)), 'green': prop((150, 570, 130, 145))}
MUSH = [prop(b) for b in [(0, 730, 90, 60), (90, 730, 90, 60), (180, 730, 90, 60), (0, 800, 90, 70), (90, 800, 90, 70),
                         (180, 800, 90, 70)]]

LOGO_RECT = (180, 110, 560, 545)       # where the logo covers the map (design px) - props here are mostly hidden
SCROLL_RECT = (670, 60, 1110, 660)


def hidden(x, y):
    return any(a <= x <= c and b <= y <= d for a, b, c, d in (LOGO_RECT, SCROLL_RECT))


PLACED = []     # (image, base x, base y, kind, w, h)


def free(x, y, w, h):
    """True if a prop with its base at (x, y) and this size clears the cliffs, the pond, the edges and other props."""
    if x - w / 2 < 60 or x + w / 2 > W - 60 or y > H - 45 or y - h < 45:
        return False
    x0, y0 = int(x - w / 2) + EXT, int(y - h) + EXT
    if KEEP_OUT[y0:y0 + h + 1, x0:x0 + w + 1].any():
        return False
    for _, px, py, _k, pw, ph in PLACED:
        if abs(px - x) < (pw + w) * 0.55 + 14 and abs(py - y) < (ph + h) * 0.4 + 12:
            return False
    return True


def scatter(imgs, count, kind, tries=1200, visible_only=True):
    got = 0
    for _ in range(tries):
        if got >= count:
            break
        img = rng.choice(imgs) if isinstance(imgs, list) else imgs
        x, y = rng.randint(70, W - 70), rng.randint(90, H - 50)
        if visible_only and hidden(x, y) and rng.random() < 0.9:
            continue
        if free(x, y, img.width, img.height):
            PLACED.append((img, x, y, kind, img.width, img.height))
            got += 1
    return got


print('big rocks', scatter(ROCK_BIG, 3, 'rock'))
print('crystals', scatter(CRYSTAL['teal'], 2, 'crystal_teal'), scatter(CRYSTAL['green'], 2, 'crystal_green'))
print('small rocks', scatter(ROCK_SMALL, 3, 'rock'))
print('mushrooms', scatter(MUSH, 7, 'mush'))
PLACED.sort(key=lambda p: p[2])


# contact shadows: every separate piece of a prop (each crystal, each mushroom, the rock) gets its own flattened shadow
# under its own base - never one blob for the whole sprite - so shadows always line up with what they belong to.
def shadow_for(img, x, y, acc):
    a = np.array(img)[:, :, 3]
    comp_src = cv2.dilate((a > 24).astype(np.uint8), np.ones((3, 3), np.uint8))
    n, lab, stats, _ = cv2.connectedComponentsWithStats(comp_src, connectivity=8)
    ox, oy = int(x - img.width / 2) + EXT, int(y - img.height) + EXT
    for i in range(1, n):
        cx0, cy0, cw, ch, area = stats[i]
        if area < 14:
            continue
        bx, by = ox + cx0 + cw / 2.0, oy + cy0 + ch - 1
        rx, ry = cw * 0.62 + 3, max(2.5, ch * 0.13 + 2)
        y0, y1 = int(by - ry * 1.6), int(by + ry * 1.6) + 1
        x0, x1 = int(bx - rx * 1.4), int(bx + rx * 1.4) + 1
        yy, xx = np.ogrid[y0:y1, x0:x1]
        d = ((xx - bx) / rx) ** 2 + ((yy - (by + 1)) / ry) ** 2
        sh = np.clip(1.0 - d, 0, 1) ** 0.7 * 0.5
        acc[y0:y1, x0:x1] = np.maximum(acc[y0:y1, x0:x1], sh)


shadow_acc = np.zeros((canvas.height, canvas.width), np.float32)
for img, x, y, kind, w, h in PLACED:
    shadow_for(img, x, y, shadow_acc)
sh_img = np.zeros((canvas.height, canvas.width, 4), np.uint8)
sh_img[:, :, 3] = (np.clip(cv2.GaussianBlur(shadow_acc, (0, 0), 1.6), 0, 0.62) * 255).astype(np.uint8)
canvas.alpha_composite(Image.fromarray(sh_img, 'RGBA'))
for img, x, y, kind, w, h in PLACED:
    put(img, x - img.width / 2, y - img.height)

# ---------------------------------------------------------------- ambience data ----------------------------------------
crystal_glows = [(x, y - h * 0.4, kind.split('_')[1]) for _, x, y, kind, w, h in PLACED if kind.startswith('crystal')]
mush_spots = [(x, y - h * 0.5) for _, x, y, kind, w, h in PLACED if kind == 'mush']
pond_cx, pond_cy = POND_POS[0] + POND.width * 0.5, POND_POS[1] + POND.height * 0.55

# ---------------------------------------------------------------- vignette ---------------------------------------------
final = canvas.crop((EXT, EXT, EXT + W, EXT + H))
yy, xx = np.mgrid[0:H, 0:W]
d = np.sqrt(((xx - W / 2) / (W * 0.66)) ** 2 + ((yy - H / 2) / (H * 0.70)) ** 2)
a = np.clip((d - 0.55) / 0.75, 0, 1) ** 1.5 * 0.6
vig = np.zeros((H, W, 4), np.uint8)
vig[:, :, 3] = (a * 255).astype(np.uint8)
vig[:, :, :3] = (6, 4, 10)
final.alpha_composite(Image.fromarray(vig, 'RGBA'))
map_rgb = final.convert('RGB')

# ---------------------------------------------------------------- glow sprite strip -------------------------------------
GLOW = 96
KINDS = {'teal': (60, 220, 230), 'green': (90, 240, 110), 'amber': (255, 150, 50), 'purple': (170, 90, 255),
         'white': (225, 235, 255), 'fog': (80, 68, 100)}
strip = Image.new('RGBA', (GLOW * len(KINDS), GLOW), (0, 0, 0, 0))
gy, gx = np.mgrid[0:GLOW, 0:GLOW]
r = np.sqrt(((gx + 0.5 - GLOW / 2) / (GLOW / 2)) ** 2 + ((gy + 0.5 - GLOW / 2) / (GLOW / 2)) ** 2)
falloff = np.clip(1 - r, 0, 1) ** 2
for i, (name, rgb) in enumerate(KINDS.items()):
    tile = np.zeros((GLOW, GLOW, 4), np.uint8)
    tile[:, :, :3] = rgb
    tile[:, :, 3] = (falloff * 255).astype(np.uint8)
    strip.paste(Image.fromarray(tile, 'RGBA'), (i * GLOW, 0))

sheet_rgba = Image.new('RGBA', (max(W, strip.width), H + GLOW), (0, 0, 0, 255))
sheet_rgba.paste(map_rgb.convert('RGBA'), (0, 0))
sheet_rgba.paste(strip, (0, H))            # replace (not composite) so the strip keeps its own alpha
os.makedirs(os.path.dirname(OUT), exist_ok=True)
sheet_rgba.save(OUT, optimize=True)
print('saved', OUT, sheet_rgba.size, os.path.getsize(OUT) // 1024, 'KB')
if len(sys.argv) > 1:
    map_rgb.save(sys.argv[1])

# ---------------------------------------------------------------- C# data ---------------------------------------------------
kind_index = {name: i for i, name in enumerate(KINDS)}
lines = ['// <auto-generated> by Tools/BookUi/build_title_cave.py - do not edit by hand. </auto-generated>',
         'namespace AlloyClient.Screens.Components;', '',
         'internal static class CaveBackdropData {',
         f'    public const int MapWidth = {W};', f'    public const int MapHeight = {H};',
         f'    public const int SheetWidth = {sheet_rgba.width};', f'    public const int SheetHeight = {sheet_rgba.height};',
         f'    public const int GlowSize = {GLOW};', '',
         '    // Sprite index in the glow strip: ' + ', '.join(f'{v} {k}' for k, v in kind_index.items()) + '.',
         '    // Glow(kind, centre x, centre y, drawn size, base alpha, alpha swing, period ms, phase 0..1, flicker)',
         '    internal readonly record struct Glow(int Kind, int X, int Y, int Size, float Base, float Swing, float PeriodMs, float Phase, bool Flicker);',
         '    internal static readonly Glow[] Glows = [']
for x, y, k in crystal_glows:
    lines.append(f'        new({kind_index[k]}, {int(x)}, {int(y)}, 150, 0.10f, 0.12f, {rng.randint(3200, 5200)}f, {rng.random():.2f}f, false),')
for x, y in mush_spots:
    lines.append(f'        new({kind_index["purple"]}, {int(x)}, {int(y)}, 70, 0.05f, 0.07f, {rng.randint(4200, 6800)}f, {rng.random():.2f}f, false),')
for x, y in GLOW_SPOTS:
    lines.append(f'        new({kind_index["amber"]}, {x}, {y}, 240, 0.10f, 0.09f, {rng.randint(1300, 2100)}f, {rng.random():.2f}f, true),')
lines.append(f'        new({kind_index["teal"]}, {int(pond_cx)}, {int(pond_cy)}, 240, 0.05f, 0.05f, 6000f, 0.3f, false),')
lines += ['    ];', '',
          '    // Mist(centre x, centre y, width, height, base alpha, alpha swing, period ms, phase)',
          '    internal readonly record struct Mist(int X, int Y, int Width, int Height, float Base, float Swing, float PeriodMs, float Phase);',
          '    internal static readonly Mist[] Mists = [']
for i in range(5):
    lines.append(f'        new({rng.randint(160, W - 160)}, {rng.randint(120, H - 120)}, {rng.randint(420, 640)}, {rng.randint(220, 340)}, 0.10f, 0.10f, {rng.randint(9000, 15000)}f, {i / 5:.2f}f),')
lines += ['    ];', '',
          '    // Places a spore / firefly can twinkle: around the crystals, the mushrooms and the water, plus scattered floor.',
          '    internal static readonly (int X, int Y)[] SporeSpots = [']
spots = []
for x, y, k in crystal_glows:
    spots += [(int(x + rng.randint(-60, 60)), int(y + rng.randint(-50, 40))) for _ in range(6)]
for x, y in mush_spots:
    spots += [(int(x + rng.randint(-40, 40)), int(y + rng.randint(-40, 20))) for _ in range(3)]
for _ in range(30):
    spots.append((rng.randint(40, W - 40), rng.randint(60, H - 40)))
for x, y in spots:
    lines.append(f'        ({min(max(x, 20), W - 20)}, {min(max(y, 20), H - 20)}),')
lines += ['    ];', '',
          '    // Pond glints: small light flecks on the water surface.',
          '    internal static readonly (int X, int Y)[] PondGlints = [']
pond_a = np.array(POND)[:, :, 3]
for _ in range(14):
    for _try in range(30):
        px, py = rng.randint(30, POND.width - 30), rng.randint(50, POND.height - 25)
        if pond_a[py, px] > 200:
            lines.append(f'        ({POND_POS[0] + px}, {POND_POS[1] + py}),')
            break
lines += ['    ];', '}', '']
open(DATA_OUT, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))
print('wrote', DATA_OUT, 'glows', len(crystal_glows) + len(mush_spots) + len(GLOW_SPOTS) + 1, 'spore spots', len(spots))
