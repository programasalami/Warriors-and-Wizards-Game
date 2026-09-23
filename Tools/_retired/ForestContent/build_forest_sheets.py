# OBSOLETE since 2026-09-20: the game's art sheets are now edited directly (see Tools/Sheets/README.md), so this script would recreate sheets that no longer
# exist. Kept as the record of how the old art was made / where it came from. Run with --force only if you know why.
import sys
if '--force' not in sys.argv:
    raise SystemExit('obsolete: see Tools/Sheets/README.md (run with --force to run it anyway)')
import random
from PIL import Image
SRC = 'C:/Users/cbart/Desktop/Assets and GUI/TopDownFantasy-Forest'
OUT = 'WaW-Client/WaWClient/Content/Sheets'

# ============ Decor sheet: 4 cols x 4 rows of 80x120 cells, native scale, bottom-centred ============
CW, CH, COLS, ROWS = 80, 120, 4, 4
BOTTOM = 3
deco = Image.open(f'{SRC}/Decorations/Decorations.png').convert('RGBA')
sprites = [
    (10, 142, 72, 100),   # 0 Tree
    (162, 162, 40, 76),   # 1 Pine
    (12, 3, 37, 29),      # 2 Bush
    (135, 8, 18, 16),     # 3 Shrub
    (74, 0, 42, 33),      # 4 Log
    (67, 66, 26, 29),     # 5 Reeds
    (7, 69, 16, 24),      # 6 Grass Tuft
    (162, 132, 28, 24),   # 7 Rock Cluster
    (169, 9, 15, 15),     # 8 Mushroom Red
    (202, 7, 15, 16),     # 9 Mushroom Big
    (234, 8, 13, 16),     # 10 Flowers
    (87, 142, 72, 96),    # 11 Oak (tree B)
    (210, 162, 40, 72),   # 12 Pine B
    (14, 34, 33, 27),     # 13 Bush B
    (199, 108, 18, 14),   # 14 Small Rock
    (74, 31, 41, 32),     # 15 Log B
]
sheet = Image.new('RGBA', (CW * COLS, CH * ROWS), (0, 0, 0, 0))
for i, (x, y, w, h) in enumerate(sprites):
    s = deco.crop((x, y, x + w, y + h))
    s = s.crop(s.getbbox())
    assert s.width <= CW and s.height <= CH - BOTTOM, (i, s.size)
    cx, cy = (i % COLS) * CW, (i // COLS) * CH
    sheet.paste(s, (cx + (CW - s.width) // 2, cy + CH - BOTTOM - s.height), s)
sheet.save(f'{OUT}/ForestDecor.png')

# ============ Flat-prop sheet: 4 cols x 3 rows of 48x48 cells, art CENTRED (flat sprites pivot on their centre) ============
FW = FH = 48
flat_sprites = [
    (169, 9, 15, 15),     # 0 Mushroom Red
    (202, 7, 15, 16),     # 1 Mushroom Big
    (234, 8, 13, 16),     # 2 Flowers
    (7, 69, 16, 24),      # 3 Grass Tuft
    (199, 108, 18, 14),   # 4 Small Rock
    (74, 0, 42, 33),      # 5 Log
    (74, 31, 41, 32),     # 6 Log Mossy
    (162, 132, 28, 24),   # 7 Rock Cluster
    (135, 8, 18, 16),     # 8 Shrub
    (12, 3, 37, 29),      # 9 Bush
    (14, 34, 33, 27),     # 10 Bush Wide
]
fsheet = Image.new('RGBA', (FW * 4, FH * 3), (0, 0, 0, 0))
for i, (x, y, w, h) in enumerate(flat_sprites):
    sp = deco.crop((x, y, x + w, y + h))
    sp = sp.crop(sp.getbbox())
    assert sp.width <= FW and sp.height <= FH, (i, sp.size)
    cx, cy = (i % 4) * FW, (i // 4) * FH
    fsheet.paste(sp, (cx + (FW - sp.width) // 2, cy + (FH - sp.height) // 2), sp)
fsheet.save(f'{OUT}/ForestFlat.png')

# ============ Ground sheet: 16x16 tiles ============
ts = Image.open(f'{SRC}/Tiles/Tileset.png').convert('RGBA')


def pack(x, y):
    return ts.crop((x, y, x + 16, y + 16))


GRASS, GDARK, GLIGHT = (163, 179, 21, 255), (112, 128, 26, 255), (182, 198, 38, 255)
DIRT, DDARK, DMID, DLIGHT = (164, 97, 43, 255), (128, 72, 30, 255), (146, 84, 36, 255), (186, 118, 60, 255)
WATER = (19, 141, 89, 255)


def flat(c):
    return Image.new('RGBA', (16, 16), c)


def sprinkle(base, seed, dark, light, nd=7, nl=5, pair=0.5):
    t = flat(base)
    r = random.Random(seed)
    for _ in range(nd):
        x, y = r.randrange(16), r.randrange(16)
        t.putpixel((x, y), dark)
        if r.random() < pair:
            t.putpixel(((x + 1) % 16, y), dark)
    for _ in range(nl):
        t.putpixel((r.randrange(16), r.randrange(16)), light)
    return t


def dirt_worn(seed):
    # Dirt with clumps of darker/lighter soil - gives the depth a flat swatch lacks.
    r = random.Random(seed)
    t = flat(DIRT)
    for _ in range(3):
        cx, cy = r.randrange(16), r.randrange(16)
        for dx, dy in r.sample([(0, 0), (1, 0), (0, 1), (1, 1), (2, 0), (-1, 0), (0, -1)], r.randint(3, 5)):
            t.putpixel(((cx + dx) % 16, (cy + dy) % 16), DMID)
    for _ in range(2):
        cx, cy = r.randrange(16), r.randrange(16)
        t.putpixel((cx, cy), DDARK)
        t.putpixel(((cx + 1) % 16, cy), DDARK)
    for _ in range(4):
        t.putpixel((r.randrange(16), r.randrange(16)), DLIGHT)
    return t


def water_ripple(seed):
    r = random.Random(seed)
    t = flat(WATER)
    for _ in range(4):
        x, y = r.randrange(1, 12), r.randrange(16)
        for k in range(r.randint(2, 4)):
            t.putpixel((x + k, y), (36, 176, 128, 255))
    for _ in range(3):
        x, y = r.randrange(16), r.randrange(16)
        t.putpixel((x, y), (10, 112, 80, 255))
        t.putpixel(((x + 1) % 16, y), (10, 112, 80, 255))
    return t


def bridge(horizontal):
    # Plank bridge. horizontal=True: walking east-west, planks run north-south, rails along top/bottom.
    W, WD, WL, RAIL, RD = (160, 108, 58, 255), (122, 78, 40, 255), (184, 132, 76, 255), (96, 58, 30, 255), (62, 36, 20, 255)
    t = Image.new('RGBA', (16, 16), W)
    for a in range(16):
        for b in range(16):
            x, y = (a, b) if horizontal else (b, a)
            c = W
            if a % 4 == 3:
                c = WD
            elif a % 4 == 0:
                c = WL
            if b < 2 or b >= 14:
                c = RAIL
            if b == 2 or b == 13:
                c = RD
            t.putpixel((x, y), c)
    r = random.Random(5 if horizontal else 6)
    for _ in range(6):
        a, b = r.randrange(16), r.randrange(3, 13)
        if a % 4 != 3:
            x, y = (a, b) if horizontal else (b, a)
            t.putpixel((x, y), WD)
    return t


tiles = [
    flat(GRASS),                                  # 0 grass
    pack(96, 0),                                  # 1 dirt (pack flat)
    sprinkle(GRASS, 11, GDARK, GLIGHT),           # 2 grass speckled
    sprinkle(GRASS, 29, GDARK, GLIGHT),           # 3 grass speckled
    pack(80, 0),                                  # 4 grass tuft (pack)
    pack(112, 0),                                 # 5 dirt pebbles A (pack)
    pack(96, 32),                                 # 6 dirt (pack)
    dirt_worn(101),                               # 7 dirt worn A
    dirt_worn(202),                               # 8 dirt worn B
    dirt_worn(303),                               # 9 dirt worn C
    pack(16, 176),                                # 10 water (pack flat)
    water_ripple(7),                              # 11 water ripple A
    water_ripple(13),                             # 12 water ripple B
    pack(96, 208),                                # 13 water + lily pad (pack)
    bridge(True),                                 # 14 bridge east-west
    bridge(False),                                # 15 bridge north-south
]
g = Image.new('RGBA', (16 * len(tiles), 16))
for i, t in enumerate(tiles):
    g.paste(t, (i * 16, 0))
g.save(f'{OUT}/ForestGround.png')


# ============ Edge sheet: [dirt edge, dirt corner, water edge, water corner] ============
# Authored for the LEFT side / TOP-LEFT corner; the engine rotates them for the other sides.
def edge(seed, rim, mid, extra, depth=(2, 3), extra_p=0.0, mid_w=1, mid2=None):
    t = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    r = random.Random(seed)
    for y in range(16):
        d = r.choice(depth)
        for x in range(d):
            t.putpixel((x, y), rim)
        for k in range(mid_w):
            if r.random() < (0.55 if mid_w == 1 else 0.9):
                t.putpixel((d + k, y), mid)
        if mid2 and r.random() < 0.6:
            t.putpixel((d + mid_w, y), mid2)
        if r.random() < extra_p:
            t.putpixel((0, y), extra)
    return t


def corner(seed, rim, R=5):
    # Filled, slightly ragged cap in the corner so the two side edges meet in a rounded shape.
    t = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    r = random.Random(seed)
    for y in range(16):
        for x in range(16):
            if x + y < R + (1 if r.random() < 0.4 else 0):
                t.putpixel((x, y), rim)
    return t


DEDGE_RIM, DEDGE_MID = (104, 60, 26, 255), (140, 80, 34, 255)
WBANK_RIM, WBANK_MID = (74, 110, 26, 255), (10, 96, 72, 255)   # grass overhang, then dark water shadow
edges = [
    edge(21, DEDGE_RIM, DEDGE_MID, GDARK, extra_p=0.12),
    corner(22, DEDGE_RIM),
    edge(23, WBANK_RIM, WBANK_MID, GLIGHT, depth=(1, 2, 2), extra_p=0.08, mid_w=2, mid2=(15, 120, 80, 255)),
    corner(24, WBANK_RIM),
]
e = Image.new('RGBA', (16 * len(edges), 16))
for i, t in enumerate(edges):
    e.paste(t, (i * 16, 0))
e.save(f'{OUT}/ForestGroundEdge.png')

# preview strip for eyeballing
bg = Image.new('RGBA', (g.width, 36), (255, 0, 255, 255))
bg.alpha_composite(g, (0, 0))
bg.alpha_composite(e, (0, 20))
bg.resize((bg.width * 6, bg.height * 6), Image.NEAREST).save('ground_preview.png')
print('ok', sheet.size, g.size, e.size)
