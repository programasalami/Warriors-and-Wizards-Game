"""Generates the forest REALM map (WaW-Server/Common/Resources/World/Data/Realm.jm) for the live-testing server.

The realm is the open-world zone the Nexus's Realm Portal leads to. Same aesthetic as the compact Nexus (Woodland grass / dirt / water / bridge,
forest decor, tree wall round the coast) but a proper explorable island (about 120 tiles across - the old stock realm maps were 2048x2048 with
~250,000 objects, far too heavy for this project's hardware target):

    centre        : spawn clearing (dirt), meadow all round it; a reserved spot on its north edge for an exit portal (none yet - see EXIT_PORTAL_OBJECT)
    west          : a winding river (two plank bridges) and, beyond it, a far-west outpost + a north-west outpost
    north / south : woodland outposts up and down the central paths
    east          : rocky highlands with an outpost, a big lake to the north-east
    south-west    : marsh (ponds + reeds)
    south-east    : dense forest with an outpost

Everything on the map is the new forest tileset (Woodland* grounds, Forest* objects) - no stock art. Players leave with the Escape-to-Nexus key.

Every outpost is a dirt clearing joined to the spawn by a dirt path; the script checks each one is reachable on foot from spawn. There are no
enemies here yet - the server has no realm spawner - the outposts are where encounters will go.

Self-contained. Run:  python Tools/ForestContent/gen_realm.py [out.jm] [preview.png]
"""
import base64
import os
import json
import math
import random
import struct
import sys
import zlib
from collections import Counter, deque

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')).replace(os.sep, '/') + '/'   # the repo root, wherever the folder lives
OUT = ROOT + 'WaW-Server/Common/Resources/World/Data/Realm.jm'

W, H = 140, 140
CX, CY, R = 70, 70, 58            # island centre and rough radius

rng = random.Random(20260920)
PH = [rng.random() * 6.28 for _ in range(8)]


def noise(x, y):
    return (math.sin(x * 0.31 + PH[0]) * math.cos(y * 0.27 + PH[1])
            + 0.6 * math.sin((x + y) * 0.19 + PH[2])
            + 0.4 * math.sin(x * 0.53 - y * 0.47 + PH[3])) / 2.0


def coarse(x, y):
    return (math.sin(x * 0.11 + PH[4]) * math.cos(y * 0.09 + PH[5]) + 0.5 * math.sin((x - y) * 0.07 + PH[6])) / 1.5


G, D, WT, BEW = 'grass', 'dirt', 'water', 'bridge_ew'
ground = [[None] * W for _ in range(H)]


def inb(x, y):
    return 0 <= x < W and 0 <= y < H and ground[y][x] is not None


def paint_ellipse(cx, cy, rx, ry, kind, wobble=0.16, create=False):
    for y in range(int(cy - ry - 3), int(cy + ry + 4)):
        for x in range(int(cx - rx - 3), int(cx + rx + 4)):
            if not (0 <= x < W and 0 <= y < H):
                continue
            if not create and ground[y][x] is None:
                continue
            d = ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2
            if d + noise(x, y) * wobble < 1.0:
                ground[y][x] = kind


def paint_rrect(x0, y0, x1, y1, r, kind, wobble=0.7):
    for y in range(y0 - 3, y1 + 4):
        for x in range(x0 - 3, x1 + 4):
            if not inb(x, y):
                continue
            qx = max(x0 + r - (x + 0.5), 0, (x + 0.5) - (x1 + 1 - r))
            qy = max(y0 + r - (y + 0.5), 0, (y + 0.5) - (y1 + 1 - r))
            sdf = math.hypot(qx, qy) - r
            inside_box = x0 <= x <= x1 and y0 <= y <= y1
            if (inside_box or sdf < 1.5) and sdf + noise(x, y) * wobble < 0.0:
                ground[y][x] = kind


def stroke(pts, width, kind, meander=0.0, freq=0.35, skip_water=False):
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        L = math.hypot(bx - ax, by - ay)
        steps = max(1, int(L * 2))
        nx_, ny_ = -(by - ay) / L, (bx - ax) / L
        for i in range(steps + 1):
            t = i / steps
            x = ax + (bx - ax) * t
            y = ay + (by - ay) * t
            off = math.sin((x + y) * freq) * meander
            x += nx_ * off
            y += ny_ * off
            r = width / 2.0
            for yy in range(int(y - r - 1), int(y + r + 2)):
                for xx in range(int(x - r - 1), int(x + r + 2)):
                    if not inb(xx, yy):
                        continue
                    if math.hypot(xx + 0.5 - x, yy + 0.5 - y) <= r:
                        if skip_water and ground[yy][xx] == WT:
                            continue
                        ground[yy][xx] = kind


# ---------------------------------------------------------------- layout
PLAZA = (63, 65, 77, 75)                                  # spawn clearing
SPAWN_REGION = [(x, y) for x in range(68, 72) for y in range(68, 72)]
EXIT_PORTAL_SPOT = (70, 66)                               # north edge of the clearing, kept free for an exit portal
EXIT_PORTAL_OBJECT = None                                 # the only existing portal art is stock (Lofi), so there is no portal yet: put a custom portal's id here
RIVER_X = lambda y: 46 + 6 * math.sin(y * 0.07)
BRIDGES = {'main': (69, 70, 71), 'north': (37, 38, 39)}   # rows where the river is crossed
CAMPS = {                                                  # name: (cx, cy, rx, ry)
    'north': (70, 22, 7, 5),
    'south': (70, 118, 7, 5),
    'east': (114, 72, 6.5, 6.5),
    'far west': (20, 70, 6, 6),
    'north-west': (35, 42, 5.5, 4.5),
    'south-east': (108, 106, 6.5, 5.5),
}

# ---------------------------------------------------------------- island
for y in range(H):
    for x in range(W):
        d = math.hypot((x + 0.5 - CX) / R, (y + 0.5 - CY) / R)
        if d + coarse(x, y) * 0.30 + noise(x, y) * 0.05 < 1.0:
            ground[y][x] = G
for _ in range(3):                                         # close pin-holes / hairline gaps
    fill = []
    for y in range(1, H - 1):
        for x in range(1, W - 1):
            if ground[y][x] is None and sum(ground[y + dy][x + dx] is not None for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))) >= 3:
                fill.append((x, y))
    for (x, y) in fill:
        ground[y][x] = G


def edge_distance():
    ed = [[10 ** 6] * W for _ in range(H)]
    dq0 = deque()
    for yy in range(H):
        for xx in range(W):
            if ground[yy][xx] is None or xx in (0, W - 1) or yy in (0, H - 1):
                ed[yy][xx] = 0 if ground[yy][xx] is None else 1
                dq0.append((xx, yy))
    while dq0:
        xx, yy = dq0.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = xx + dx, yy + dy
            if 0 <= nx < W and 0 <= ny < H and ed[ny][nx] > ed[yy][xx] + 1:
                ed[ny][nx] = ed[yy][xx] + 1
                dq0.append((nx, ny))
    return ed


# ---------------------------------------------------------------- water: lake, marsh ponds, river
paint_ellipse(98, 40, 12, 9, WT, wobble=0.25)
for (px, py, rx, ry) in ((28, 104, 7, 5), (41, 112, 5, 4), (22, 92, 4, 3.5), (36, 97, 4, 3), (48, 106, 3.5, 3)):
    paint_ellipse(px, py, rx, ry, WT, wobble=0.3)
coast = edge_distance()
before = [row[:] for row in ground]
stroke([(RIVER_X(y), y) for y in range(6, 134, 3)], 5.0, WT)
for yy in range(H):                                        # the river stops 3 tiles short of the coast: it always has a bank
    for xx in range(W):
        if ground[yy][xx] == WT and coast[yy][xx] <= 3 and before[yy][xx] != WT:
            ground[yy][xx] = G

# ---------------------------------------------------------------- dirt: spawn clearing, camps, paths
paint_rrect(*PLAZA, 3, D)
for (cx, cy, rx, ry) in CAMPS.values():
    paint_ellipse(cx, cy, rx, ry, D, wobble=0.22)
skip = dict(skip_water=True)
PATHS = [
    [(63, 70), (20, 70)],                                  # west, over the main bridge, to the far-west outpost
    [(70, 65), (70, 22)],                                  # north
    [(70, 75), (70, 118)],                                 # south
    [(77, 70), (114, 72)],                                 # east
    [(56, 70), (56, 38), (36, 40)],                        # north-west: up beside the river, over the north bridge
    [(70, 78), (70, 88), (108, 106)],                      # south-east
]
for pts in PATHS:
    stroke(pts, 3.2, D, meander=0.35, freq=0.5, **skip)
for rows in BRIDGES.values():                              # every water tile in a crossing row becomes plank, with a dry approach either side
    for y in rows:
        for x in range(28, 66):
            if inb(x, y) and ground[y][x] == WT:
                ground[y][x] = BEW
        xs = [x for x in range(W) if ground[y][x] == BEW]
        for x in (min(xs) - 1, min(xs) - 2, max(xs) + 1, max(xs) + 2):
            if inb(x, y) and ground[y][x] == G:
                ground[y][x] = D
for (x, y) in SPAWN_REGION + [EXIT_PORTAL_SPOT]:
    ground[y][x] = D

# ---------------------------------------------------------------- distance fields
edge_dist = edge_distance()


def dist_to(pred, cap=8):
    src = [(x, y) for y in range(H) for x in range(W) if pred(x, y)]
    out = [[cap] * W for _ in range(H)]
    for (sx, sy) in src:
        for y in range(max(0, sy - cap + 1), min(H, sy + cap)):
            for x in range(max(0, sx - cap + 1), min(W, sx + cap)):
                d = max(abs(x - sx), abs(y - sy))
                if d < out[y][x]:
                    out[y][x] = d
    return out


special = set(SPAWN_REGION) | {EXIT_PORTAL_SPOT}
d_walk = dist_to(lambda x, y: ground[y][x] in (D, BEW))
d_water = dist_to(lambda x, y: ground[y][x] == WT)

objs = {}
tree_cells = set()


def free(x, y):
    return inb(x, y) and (x, y) not in objs and ground[y][x] == G and (x, y) not in special


def near_tree(x, y, r):
    for yy in range(y - r + 1, y + r):
        for xx in range(x - r + 1, x + r):
            if (xx, yy) in tree_cells:
                return True
    return False


def put(x, y, oid, tree=False):
    objs[(x, y)] = oid
    if tree:
        tree_cells.add((x, y))


TREES = ['Forest Tree', 'Forest Oak', 'Forest Pine', 'Forest Pine Tall']
PINES = ['Forest Pine', 'Forest Pine Tall']
UNDER = ['Forest Bush', 'Forest Bush Wide', 'Forest Shrub', 'Forest Mushroom Red', 'Forest Mushroom Big', 'Forest Grass Tuft', 'Forest Flowers']
MEADOW = ['Forest Flowers', 'Forest Grass Tuft', 'Forest Flowers', 'Forest Mushroom Red', 'Forest Bush']
ROCKS = ['Forest Rock Cluster', 'Forest Small Rock', 'Forest Small Rock', 'Forest Rock Cluster']


def blob(x, y, cx, cy, r):
    return max(0.0, 1.0 - math.hypot((x - cx) / r, (y - cy) / r))


def forest_amount(x, y):
    return max(blob(x, y, 32, 30, 30), blob(x, y, 108, 108, 27), blob(x, y, 62, 114, 20), blob(x, y, 112, 26, 18) * 0.7, blob(x, y, 24, 64, 14) * 0.7)


def rock_amount(x, y):
    return blob(x, y, 112, 66, 24)


# 1. tree wall round the coast (thick, so the void is never visible from inside)
for y in range(H):
    for x in range(W):
        e = edge_dist[y][x]
        if e < 1 or e > 5 or not free(x, y) or d_walk[y][x] < 2 or d_water[y][x] < 1:
            continue
        p = {1: 1.0, 2: 1.0, 3: 0.85, 4: 0.55, 5: 0.3}[e]
        if rng.random() < p and not near_tree(x, y, 2 if e <= 3 else 3):
            put(x, y, rng.choice(TREES), tree=True)

# 2. the land: woods, understory, highlands, meadow
for y in range(H):
    for x in range(W):
        if not free(x, y) or edge_dist[y][x] <= 5 or d_walk[y][x] < 2 or d_water[y][x] < 1:
            continue
        f = forest_amount(x, y)
        ro = rock_amount(x, y)
        r = rng.random()
        if r < 0.012 + 0.30 * f ** 0.8 and not near_tree(x, y, 2):
            put(x, y, rng.choice(PINES if ro > 0.35 else TREES), tree=True)
        elif r < 0.05 + 0.30 * f ** 0.8 + 0.05 * f + 0.14 * ro ** 0.8:
            if ro > 0.15 and rng.random() < 0.7:
                put(x, y, rng.choice(ROCKS))
            elif f > 0.15:
                put(x, y, rng.choice(UNDER))
            else:
                put(x, y, rng.choice(MEADOW))

# 3. outposts: logs round a fire spot, mushrooms, rocks
for name, (cx, cy, rx, ry) in CAMPS.items():
    for k in range(6):
        a = k * math.pi / 3 + 0.3
        x, y = int(round(cx + math.cos(a) * (rx * 0.6))), int(round(cy + math.sin(a) * (ry * 0.6)))
        if inb(x, y) and (x, y) not in objs and (x, y) not in special:
            objs[(x, y)] = 'Forest Log' if k % 2 == 0 else 'Forest Log Mossy'
    for _ in range(9):
        a, rr = rng.random() * 6.283, rng.random() ** 0.6
        x, y = int(round(cx + math.cos(a) * rr * (rx + 1.5))), int(round(cy + math.sin(a) * rr * (ry + 1.5)))
        if inb(x, y) and (x, y) not in objs and ground[y][x] in (G, D) and (x, y) not in special and math.hypot(x - cx, y - cy) > 1.8:
            objs[(x, y)] = rng.choice(['Forest Mushroom Red', 'Forest Mushroom Big', 'Forest Small Rock', 'Forest Rock Cluster', 'Forest Flowers'])

# 4. waterside: reeds and a few rocks along every bank
for y in range(H):
    for x in range(W):
        if free(x, y) and d_water[y][x] == 1 and edge_dist[y][x] > 2:
            r = rng.random()
            if r < 0.30:
                put(x, y, 'Forest Reeds')
            elif r < 0.36:
                put(x, y, 'Forest Small Rock')

# 5. verges: a little dressing along the paths
for y in range(H):
    for x in range(W):
        if free(x, y) and d_walk[y][x] == 1 and rng.random() < 0.10:
            put(x, y, rng.choice(['Forest Bush', 'Forest Shrub', 'Forest Bush Wide', 'Forest Grass Tuft', 'Forest Flowers']))

# ---------------------------------------------------------------- checks
BLOCKING = {'Forest Tree', 'Forest Oak', 'Forest Pine', 'Forest Pine Tall', 'Forest Log', 'Forest Log Mossy', 'Forest Rock Cluster'}


def passable(x, y):
    if not (0 <= x < W and 0 <= y < H) or ground[y][x] in (None, WT):
        return False
    if (x, y) == EXIT_PORTAL_SPOT and EXIT_PORTAL_OBJECT:
        return False
    return objs.get((x, y)) not in BLOCKING


seen = {SPAWN_REGION[0]}
dq = deque([SPAWN_REGION[0]])
while dq:
    x, y = dq.popleft()
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        n = (x + dx, y + dy)
        if n not in seen and passable(*n):
            seen.add(n)
            dq.append(n)
ok = True
for name, (cx, cy, rx, ry) in CAMPS.items():
    reach = (int(cx), int(cy)) in seen or any((int(cx) + dx, int(cy) + dy) in seen for dx in (-1, 0, 1) for dy in (-1, 0, 1))
    ok &= reach
    print('reachable from spawn:', name, 'outpost', reach, '(ground at its centre: %s)' % ground[int(cy)][int(cx)])
land = sum(1 for y in range(H) for x in range(W) if ground[y][x] not in (None, WT))
print('walkable land reached from spawn: %d of %d tiles' % (len(seen), land))
assert ok, 'an outpost is cut off from spawn'
assert len(seen) > land * 0.6, 'most of the island should be walkable from spawn'


# ---------------------------------------------------------------- encode
def tile_key(x, y):
    g = ground[y][x]
    if g is None:
        return ('', None, None)
    gname = {G: 'Woodland Grass', D: 'Woodland Dirt', WT: 'Woodland Water', BEW: 'Woodland Bridge EW'}[g]
    reg = [{'id': 'Spawn'}] if (x, y) in SPAWN_REGION else None
    oid = EXIT_PORTAL_OBJECT if (x, y) == EXIT_PORTAL_SPOT else objs.get((x, y))
    return (gname, json.dumps([{'id': oid}]) if oid else None, json.dumps(reg) if reg else None)


dict_list, key_idx, buf = [], {}, []
for y in range(H):
    for x in range(W):
        k = tile_key(x, y)
        if k not in key_idx:
            key_idx[k] = len(dict_list)
            e = {}
            if k[0]:
                e['ground'] = k[0]
            if k[1]:
                e['objs'] = json.loads(k[1])
            if k[2]:
                e['regions'] = json.loads(k[2])
            dict_list.append(e)
        buf.append(key_idx[k])
data = base64.b64encode(zlib.compress(struct.pack('>%dh' % (W * H), *buf), 9)).decode('ascii')
out = json.dumps({'width': W, 'data': data, 'height': H, 'dict': dict_list}, separators=(',', ':'))
target = sys.argv[1] if len(sys.argv) > 1 else OUT
open(target, 'w', encoding='utf-8', newline='').write(out)

c = Counter(objs.values())
print('wrote', target, '%dx%d' % (W, H))
print('objects:', sum(c.values()) + 1, dict(c.most_common()))
print('ground:', dict(Counter(g for row in ground for g in row)))
print('dict entries:', len(dict_list))

# ---------------------------------------------------------------- preview (uses the real sheets, 10px tiles)
if len(sys.argv) > 2:
    from PIL import Image, ImageDraw
    S = ROOT + 'Tools/Sheets/source_old/'      # the old (pre-2026-09-20) sheets, only used for this preview
    gsheet = Image.open(S + 'ForestGround.png').convert('RGBA')
    dsheet = Image.open(S + 'ForestDecor.png').convert('RGBA')
    fsheet = Image.open(S + 'ForestFlat.png').convert('RGBA')
    T = 10
    prev = Image.new('RGBA', (W * T, H * T), (12, 14, 12, 255))
    tex_pool = {G: [0, 2, 0, 3, 0, 2, 3, 4], D: [1, 7, 8, 1, 9, 5, 7, 6, 8, 9], WT: [10, 11, 10, 12, 10, 11, 12, 13], BEW: [14]}
    cols = gsheet.width // 16
    prng = random.Random(5)
    for y in range(H):
        for x in range(W):
            g = ground[y][x]
            if g is None:
                continue
            i = prng.choice(tex_pool[g])
            tile = gsheet.crop(((i % cols) * 16, (i // cols) * 16, (i % cols) * 16 + 16, (i // cols) * 16 + 16)).resize((T, T), Image.NEAREST)
            prev.paste(tile, (x * T, y * T))
    DECOR = {'Forest Tree': 0, 'Forest Pine': 1, 'Forest Bush': 2, 'Forest Shrub': 3, 'Forest Reeds': 5, 'Forest Grass Tuft': 6,
             'Forest Rock Cluster': 7, 'Forest Mushroom Red': 8, 'Forest Mushroom Big': 9, 'Forest Flowers': 10, 'Forest Oak': 11,
             'Forest Pine Tall': 12, 'Forest Bush Wide': 13, 'Forest Small Rock': 14}
    FLATI = {'Forest Log': 5, 'Forest Log Mossy': 6}
    SCALE = T / 22.0
    for (x, y), oid in sorted(objs.items(), key=lambda kv: kv[0][1]):
        if oid in FLATI:
            i = FLATI[oid]
            sp = fsheet.crop(((i % 4) * 48, (i // 4) * 48, (i % 4) * 48 + 48, (i // 4) * 48 + 48))
            sp = sp.crop(sp.getbbox())
            sp = sp.resize((max(1, int(sp.width * SCALE)), max(1, int(sp.height * SCALE))), Image.NEAREST)
            prev.alpha_composite(sp, (x * T + T // 2 - sp.width // 2, y * T + T // 2 - sp.height // 2))
            continue
        i = DECOR[oid]
        sp = dsheet.crop(((i % 4) * 80, (i // 4) * 120, (i % 4) * 80 + 80, (i // 4) * 120 + 120))
        sp = sp.crop(sp.getbbox())
        sp = sp.resize((max(1, int(sp.width * SCALE)), max(1, int(sp.height * SCALE))), Image.NEAREST)
        prev.alpha_composite(sp, (x * T + T // 2 - sp.width // 2, y * T + T - sp.height - 1))
    dr = ImageDraw.Draw(prev)
    for (x, y) in SPAWN_REGION:
        dr.rectangle((x * T + 2, y * T + 2, x * T + T - 3, y * T + T - 3), outline=(255, 255, 0, 255))
    dr.rectangle((EXIT_PORTAL_SPOT[0] * T, EXIT_PORTAL_SPOT[1] * T, EXIT_PORTAL_SPOT[0] * T + T - 1, EXIT_PORTAL_SPOT[1] * T + T - 1), outline=(80, 160, 255, 255), width=2)   # reserved exit spot
    prev.convert('RGB').save(sys.argv[2])
    print('preview', sys.argv[2])
