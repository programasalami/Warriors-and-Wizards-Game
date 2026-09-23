"""Generates the COMPACT forest Nexus test map (WaW-Server/Common/Resources/World/Data/Nexus.jm) for the live-testing server.

Same aesthetic as the big Nexus (gen_nexus.py: Woodland grass/dirt/water/bridge, forest decor, tree wall round the island) but
about 1/6 of the area, laid out as a test bench around a central spawn plaza:

    north  : tree / bush grove with a small pond
    west   : target-shooting yard (DPS dummies + strong targets)
    east   : river with a plank bridge -> rocky clearing
    south  : campsite clearing (flat logs, mushrooms)
    centre : spawn square with the guild fixtures on its north side; a dedicated portal pad (guild hall / realm spot / vault) touching its south side

Self-contained: does not read the old map or git. Run:  python Tools/ForestContent/gen_nexus_small.py [out.jm] [preview.png]
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
OUT = ROOT + 'WaW-Server/Common/Resources/World/Data/Nexus.jm'

W, H = 72, 56
OX, OY = 4, 3          # design-space -> array offset (keeps a void margin round everything)

rng = random.Random(20260919)
PH = [rng.random() * 6.28 for _ in range(6)]


def noise(x, y):
    return (math.sin(x * 0.31 + PH[0]) * math.cos(y * 0.27 + PH[1])
            + 0.6 * math.sin((x + y) * 0.19 + PH[2])
            + 0.4 * math.sin(x * 0.53 - y * 0.47 + PH[3])) / 2.0


def X(v):
    return v + OX


def Y(v):
    return v + OY


G, D, WT, BEW, BNS = 'grass', 'dirt', 'water', 'bridge_ew', 'bridge_ns'
ground = [[None] * W for _ in range(H)]


def inb(x, y):
    return 0 <= x < W and 0 <= y < H and ground[y][x] is not None


def paint_ellipse(cx, cy, rx, ry, kind, wobble=0.16, only_over=None, create=False):
    for y in range(int(cy - ry - 3), int(cy + ry + 4)):
        for x in range(int(cx - rx - 3), int(cx + rx + 4)):
            if not (0 <= x < W and 0 <= y < H):
                continue
            if not create and ground[y][x] is None:
                continue
            d = ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2
            if d + noise(x, y) * wobble < 1.0 and (only_over is None or ground[y][x] in only_over):
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


# ---------------------------------------------------------------- layout (design space; X()/Y() add the margin)
PLAZA = (X(22), Y(17), X(40), Y(29))            # spawn square + fixtures + portals, dirt
SPAWN_REGION = [(X(x), Y(y)) for x in range(29, 33) for y in range(21, 25)]    # 4x4 in the middle of the plaza
FIX_ROW_Y = Y(19)                               # guild fixtures, north side of the plaza
PORTAL_ROW_Y = Y(32)                            # portals, on their own pad directly south of the plaza
PAD = (X(24), Y(30), X(38), Y(34))              # the dedicated portal area (dirt), touching the plaza's south edge
FIXTURES = {
    (X(26), FIX_ROW_Y): 'Armoire',
    (X(29), FIX_ROW_Y): 'Guild Register',
    (X(33), FIX_ROW_Y): 'Guild Chronicle',
    (X(36), FIX_ROW_Y): 'Guild Board',
    (X(35), Y(23)): 'Bug Board',                  # the players' bug board, two tiles east of the spawn square
    (X(37), Y(23)): 'Jukebox',                    # the shared music player, next to the bug board
    (X(27), PORTAL_ROW_Y): 'Guild Hall Portal',
    (X(35), PORTAL_ROW_Y): 'Vault Portal',
}
# where the Realm Portal spawns (server: Nexus.AddRealmPortal puts one live portal on each region tile, up to RealmCount): between the guild hall
# and vault portals
REALM_REGION = [(X(31), PORTAL_ROW_Y)]
# target yard (west): dummies far, strong targets near
YARD = (X(3), Y(16), X(14), Y(30))
DUMMIES = {
    (X(5), Y(19)): 'DpsDummy0def',
    (X(5), Y(23)): 'DpsDummy40def',
    (X(5), Y(27)): 'DpsDummy100def',
    (X(9), Y(21)): 'Target Strong',
    (X(9), Y(25)): 'Dummy Strong',
    (X(11), Y(23)): 'Target Strong',
}
RIVER_X = lambda y: X(47) + 1.6 * math.sin(y * 0.16)
BRIDGE_ROWS = (Y(22), Y(23), Y(24))
GROVE_POND = (X(18), Y(9), 3.6, 2.6)
CLEAR_E = (X(57), Y(23), 5.5, 7.0)              # rocky clearing (east)
CLEAR_S = (X(31), Y(41), 6.5, 4.2)              # camp clearing (south)
CLEAR_N = (X(31), Y(8), 4.5, 3.0)               # grove clearing (north)

# ---------------------------------------------------------------- island mask (union of ellipses)
for (cx, cy, rx, ry) in ((31, 24, 27, 15.5), (9.5, 23, 11.5, 11.5), (31, 10, 18, 8.5), (56, 23, 8.5, 13.5), (31, 40, 13, 9.5)):
    paint_ellipse(X(cx), Y(cy), rx, ry, G, wobble=0.10, create=True)
# close pin-holes / hairline gaps in the mask
for _ in range(2):
    fill = []
    for y in range(1, H - 1):
        for x in range(1, W - 1):
            if ground[y][x] is None and sum(ground[y + dy][x + dx] is not None for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))) >= 3:
                fill.append((x, y))
    for (x, y) in fill:
        ground[y][x] = G

def _edge_dist():
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


# ---------------------------------------------------------------- water
paint_ellipse(*GROVE_POND[:2], GROVE_POND[2], GROVE_POND[3], WT, wobble=0.25)
_coast = _edge_dist()
_before = [row[:] for row in ground]
stroke([(RIVER_X(y), y) for y in range(Y(3), Y(46), 3)], 5.0, WT)
for yy in range(H):          # the river stops 3 tiles short of the coast so it always has a bank of grass / trees
    for xx in range(W):
        if ground[yy][xx] == WT and _coast[yy][xx] <= 3 and _before[yy][xx] != WT:
            ground[yy][xx] = G

# ---------------------------------------------------------------- dirt: plaza, yard, clearings, paths
paint_rrect(*PLAZA, 4, D)
paint_rrect(*PAD, 2, D)
paint_rrect(*YARD, 3, D)
paint_ellipse(*CLEAR_E, D, wobble=0.22)
paint_ellipse(*CLEAR_S, D, wobble=0.22)
paint_ellipse(*CLEAR_N, D, wobble=0.22)
skip = dict(skip_water=True)
stroke([(X(22), Y(23.5)), (X(13), Y(23.5))], 3.2, D, **skip)     # W arm: plaza -> yard
stroke([(X(31), Y(18)), (X(31), Y(9))], 3.2, D, meander=0.4, freq=0.5, **skip)   # N arm: plaza -> grove
stroke([(X(31), Y(34)), (X(31), Y(39))], 3.2, D, meander=0.4, freq=0.5, **skip)  # S arm: portal pad -> camp
stroke([(X(40), Y(23.5)), (X(62), Y(23.5))], 3.2, D, **skip)     # E arm: plaza -> bridge -> rocky clearing

# bridge: every water tile in the three crossing rows becomes plank; keep a dry approach either side
for y in BRIDGE_ROWS:
    for x in range(X(40), X(62)):
        if inb(x, y) and ground[y][x] == WT:
            ground[y][x] = BEW
    xs = [x for x in range(W) if ground[y][x] == BEW]
    for x in (min(xs) - 1, min(xs) - 2, max(xs) + 1, max(xs) + 2):
        if inb(x, y) and ground[y][x] == G:
            ground[y][x] = D

# every fixture / region tile is dirt
for (x, y) in list(FIXTURES) + list(DUMMIES) + SPAWN_REGION + REALM_REGION:
    ground[y][x] = D

# ---------------------------------------------------------------- distance fields
edge_dist = [[0] * W for _ in range(H)]
q = deque()
for y in range(H):
    for x in range(W):
        if ground[y][x] is None:
            edge_dist[y][x] = 0
            q.append((x, y))
        else:
            edge_dist[y][x] = 10 ** 6
for y in range(H):
    for x in range(W):
        if x in (0, W - 1) or y in (0, H - 1):
            if ground[y][x] is not None:
                edge_dist[y][x] = 1
                q.append((x, y))
while q:
    x, y = q.popleft()
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < W and 0 <= ny < H and edge_dist[ny][nx] > edge_dist[y][x] + 1:
            edge_dist[ny][nx] = edge_dist[y][x] + 1
            q.append((nx, ny))


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


special = set(FIXTURES) | set(DUMMIES) | set(SPAWN_REGION) | set(REALM_REGION)
d_walk = dist_to(lambda x, y: ground[y][x] in (D, BEW, BNS))
d_water = dist_to(lambda x, y: ground[y][x] == WT)
d_fix = dist_to(lambda x, y: (x, y) in special, cap=4)

objs = {}
occ_tree = []


def free(x, y):
    return inb(x, y) and (x, y) not in objs and ground[y][x] == G and (x, y) not in special


def near_tree(x, y, r):
    return any(max(abs(tx - x), abs(ty - y)) < r for (tx, ty) in occ_tree)


def put(x, y, oid, tree=False):
    objs[(x, y)] = oid
    if tree:
        occ_tree.append((x, y))


TREES = ['Forest Tree', 'Forest Oak', 'Forest Pine', 'Forest Pine Tall']
UNDER = ['Forest Bush', 'Forest Bush Wide', 'Forest Shrub', 'Forest Mushroom Red', 'Forest Mushroom Big', 'Forest Grass Tuft', 'Forest Flowers']
ROCKS = ['Forest Rock Cluster', 'Forest Small Rock', 'Forest Small Rock', 'Forest Rock Cluster']


def free_or_clearing(x, y, dirt_ok):
    # dirt_ok: dirt tiles count as free too, except a 3-row corridor (the E arm) through the middle of the clearing
    if free(x, y):
        return True
    return dirt_ok and inb(x, y) and ground[y][x] == D and (x, y) not in objs and (x, y) not in special and abs(y - Y(23.5)) >= 3


def scatter(cx, cy, rx, ry, count, pool, min_walk=2, min_water=1, min_edge=2, tree=False, gap=0, dirt_ok=False):
    placed, tries = 0, 0
    while placed < count and tries < count * 60:
        tries += 1
        a, r = rng.random() * 6.283, rng.random() ** 0.6
        x, y = int(round(cx + math.cos(a) * r * rx)), int(round(cy + math.sin(a) * r * ry))
        if not free_or_clearing(x, y, dirt_ok) or (not dirt_ok and d_walk[y][x] < min_walk) or d_water[y][x] < min_water or edge_dist[y][x] <= min_edge:
            continue
        if gap and near_tree(x, y, gap):
            continue
        put(x, y, rng.choice(pool) if isinstance(pool, list) else pool, tree=tree)
        placed += 1


# 1. tree wall round the island edge (thick, so the void is never visible from inside)
for y in range(H):
    for x in range(W):
        e = edge_dist[y][x]
        if e < 1 or e > 5 or not free(x, y) or d_walk[y][x] < 2 or d_water[y][x] < 1:
            continue
        p = {1: 1.0, 2: 1.0, 3: 0.85, 4: 0.55, 5: 0.3}[e]
        if rng.random() < p and not near_tree(x, y, 2 if e <= 3 else 3):
            put(x, y, rng.choice(TREES), tree=True)

# 2. north grove: dense trees + understory around the pond and clearing
scatter(X(31), Y(10), 15, 6, 46, TREES, min_walk=2, min_water=2, min_edge=3, tree=True, gap=3)
scatter(X(31), Y(10), 16, 7, 34, UNDER, min_walk=1, min_water=1, min_edge=2)
for _ in range(2):
    scatter(X(31), Y(10), 12, 5, 1, ['Forest Log', 'Forest Log Mossy'], min_walk=3, min_water=2, min_edge=3)

# 3. east: rocky clearing (rocks, small rocks, mossy logs, a few pines)
scatter(CLEAR_E[0], CLEAR_E[1], 7.5, 9.5, 36, ROCKS, min_walk=0, min_water=2, min_edge=2, dirt_ok=True)
scatter(CLEAR_E[0], CLEAR_E[1], 8, 10, 6, ['Forest Pine', 'Forest Pine Tall'], min_walk=3, min_water=2, min_edge=3, tree=True, gap=4)
scatter(CLEAR_E[0], CLEAR_E[1], 6, 8, 3, ['Forest Log Mossy', 'Forest Log'], min_walk=0, min_water=2, min_edge=3, dirt_ok=True)
scatter(CLEAR_E[0], CLEAR_E[1], 9, 11, 6, ['Forest Bush', 'Forest Shrub', 'Forest Grass Tuft'], min_walk=1, min_water=2, min_edge=2)

# 4. south camp: logs round a fire spot, mushrooms, rocks, a few trees
for k in range(6):
    a = k * math.pi / 3 + 0.3
    x, y = int(round(CLEAR_S[0] + math.cos(a) * 3.4)), int(round(CLEAR_S[1] + math.sin(a) * 2.5))
    if inb(x, y) and (x, y) not in objs and (x, y) not in special:
        objs[(x, y)] = 'Forest Log' if k % 2 == 0 else 'Forest Log Mossy'
scatter(CLEAR_S[0], CLEAR_S[1], 9, 6, 8, ['Forest Mushroom Red', 'Forest Mushroom Big', 'Forest Flowers', 'Forest Small Rock'], min_walk=1, min_water=1, min_edge=2)
scatter(CLEAR_S[0], CLEAR_S[1], 11, 7, 9, TREES, min_walk=3, min_water=2, min_edge=3, tree=True, gap=3)

# 5. west yard: rock markers on the corners and a few bushes on the rim
for (x, y) in ((YARD[0] + 2, YARD[1] + 1), (YARD[2] - 2, YARD[1] + 1), (YARD[0] + 2, YARD[3] - 1), (YARD[2] - 2, YARD[3] - 1)):
    if inb(x, y) and (x, y) not in objs and ground[y][x] == D and (x, y) not in special:
        objs[(x, y)] = 'Forest Rock Cluster'
scatter(X(8), Y(23), 10, 12, 8, ['Forest Bush', 'Forest Shrub', 'Forest Grass Tuft', 'Forest Small Rock'], min_walk=1, min_water=1, min_edge=3)

# 6. riverside reeds / rocks
for y in range(H):
    for x in range(W):
        if free(x, y) and d_water[y][x] == 1 and edge_dist[y][x] > 2:
            r = rng.random()
            if r < 0.30:
                put(x, y, 'Forest Reeds')
            elif r < 0.36:
                put(x, y, 'Forest Small Rock')

# 7. plaza rim dressing (never on dirt) and open-field accents
for y in range(H):
    for x in range(W):
        if not free(x, y):
            continue
        dw = d_walk[y][x]
        if dw == 1 and rng.random() < 0.12:
            put(x, y, rng.choice(['Forest Bush', 'Forest Shrub', 'Forest Bush Wide', 'Forest Grass Tuft', 'Forest Flowers']))
        elif dw == 2 and rng.random() < 0.05:
            put(x, y, rng.choice(['Forest Flowers', 'Forest Grass Tuft', 'Forest Mushroom Red']))
        elif dw >= 3 and edge_dist[y][x] > 3 and d_water[y][x] >= 2 and rng.random() < 0.03:
            put(x, y, rng.choice(['Forest Grass Tuft', 'Forest Small Rock', 'Forest Flowers']))

# ---------------------------------------------------------------- checks
BLOCKING = {'Forest Tree', 'Forest Oak', 'Forest Pine', 'Forest Pine Tall', 'Forest Log', 'Forest Log Mossy', 'Forest Rock Cluster'}
fixture_ids = dict(FIXTURES)
fixture_ids.update(DUMMIES)


def passable(x, y):
    if not (0 <= x < W and 0 <= y < H) or ground[y][x] in (None, WT):
        return False
    if (x, y) in fixture_ids:
        return False
    return objs.get((x, y)) not in BLOCKING


spawn0 = SPAWN_REGION[0]
seen = {spawn0}
dq = deque([spawn0])
while dq:
    x, y = dq.popleft()
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        n = (x + dx, y + dy)
        if n not in seen and passable(*n):
            seen.add(n)
            dq.append(n)
FIXTURES_GH = [k for k, v in FIXTURES.items() if v == 'Guild Hall Portal'][0]
goals = {
    'west yard (dummy row)': (X(8), Y(23)),
    'north grove clearing': CLEAR_N[:2],
    'south camp': CLEAR_S[:2],
    'east rocky clearing (across the bridge)': CLEAR_E[:2],
    'portal pad (realm portal spots)': REALM_REGION[0],
    'guild hall portal': (FIXTURES_GH[0] + 1, FIXTURES_GH[1]),
}
ok = True
for name, (gx, gy) in goals.items():
    gx, gy = int(gx), int(gy)
    reach = (gx, gy) in seen
    ok &= reach
    print('reachable from spawn:', name, reach)
assert ok, 'some zone is cut off from spawn'

# ---------------------------------------------------------------- encode
def tile_key(x, y):
    g = ground[y][x]
    if g is None:
        return ('', None, None)
    gname = {G: 'Woodland Grass', D: 'Woodland Dirt', WT: 'Woodland Water', BEW: 'Woodland Bridge EW', BNS: 'Woodland Bridge NS'}[g]
    reg = None
    if (x, y) in SPAWN_REGION:
        reg = [{'id': 'Spawn'}]
    elif (x, y) in REALM_REGION:
        reg = [{'id': 'Realm Portals'}]
    oid = fixture_ids.get((x, y)) or objs.get((x, y))
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
print('objects:', sum(c.values()), dict(c.most_common()))
print('ground:', dict(Counter(g for row in ground for g in row)))
print('dict entries:', len(dict_list))

# ---------------------------------------------------------------- preview (uses the real sheets, 16px tiles)
if len(sys.argv) > 2:
    from PIL import Image
    S = ROOT + 'Tools/Sheets/source_old/'      # the old (pre-2026-09-20) sheets, only used for this preview
    gsheet = Image.open(S + 'ForestGround.png').convert('RGBA')
    dsheet = Image.open(S + 'ForestDecor.png').convert('RGBA')
    fsheet = Image.open(S + 'ForestFlat.png').convert('RGBA')
    lofi3 = Image.open(S + 'LofiEnvironment3.png').convert('RGBA')
    T = 16
    prev = Image.new('RGBA', (W * T, H * T), (12, 14, 12, 255))
    tex_pool = {G: [0, 2, 0, 3, 0, 2, 3, 4], D: [1, 7, 8, 1, 9, 5, 7, 6, 8, 9], WT: [10, 11, 10, 12, 10, 11, 12, 13], BEW: [14], BNS: [15]}
    cols = gsheet.width // 16
    prng = random.Random(5)
    for y in range(H):
        for x in range(W):
            g = ground[y][x]
            if g is None:
                continue
            i = prng.choice(tex_pool[g])
            prev.paste(gsheet.crop(((i % cols) * 16, (i // cols) * 16, (i % cols) * 16 + 16, (i // cols) * 16 + 16)), (x * T, y * T))
    DECOR = {'Forest Tree': 0, 'Forest Pine': 1, 'Forest Bush': 2, 'Forest Shrub': 3, 'Forest Reeds': 5, 'Forest Grass Tuft': 6,
             'Forest Rock Cluster': 7, 'Forest Mushroom Red': 8, 'Forest Mushroom Big': 9, 'Forest Flowers': 10, 'Forest Oak': 11,
             'Forest Pine Tall': 12, 'Forest Bush Wide': 13, 'Forest Small Rock': 14}
    FLATI = {'Forest Log': 5, 'Forest Log Mossy': 6}
    SCALE = 16 / 22.0
    for (x, y), oid in sorted(objs.items(), key=lambda kv: kv[0][1]):
        if oid in FLATI:
            i = FLATI[oid]
            sp = fsheet.crop(((i % 4) * 48, (i // 4) * 48, (i % 4) * 48 + 48, (i // 4) * 48 + 48))
            bb = sp.getbbox()
            sp = sp.crop(bb)
            sp = sp.resize((max(1, int(sp.width * SCALE)), max(1, int(sp.height * SCALE))), Image.NEAREST)
            prev.alpha_composite(sp, (x * T + T // 2 - sp.width // 2, y * T + T // 2 - sp.height // 2))
            continue
        i = DECOR[oid]
        sp = dsheet.crop(((i % 4) * 80, (i // 4) * 120, (i % 4) * 80 + 80, (i // 4) * 120 + 120))
        bb = sp.getbbox()
        sp = sp.crop(bb)
        sp = sp.resize((max(1, int(sp.width * SCALE)), max(1, int(sp.height * SCALE))), Image.NEAREST)
        prev.alpha_composite(sp, (x * T + T // 2 - sp.width // 2, y * T + T - sp.height - 1))
    from PIL import ImageDraw
    dr = ImageDraw.Draw(prev)
    for (x, y), oid in fixture_ids.items():
        col = (255, 60, 60, 255) if 'Dummy' in oid or 'Target' in oid else (80, 160, 255, 255)
        dr.rectangle((x * T, y * T, x * T + T - 1, y * T + T - 1), outline=col, width=2)
    for (x, y) in SPAWN_REGION:
        dr.rectangle((x * T + 4, y * T + 4, x * T + T - 5, y * T + T - 5), outline=(255, 255, 0, 255))
    for (x, y) in REALM_REGION:
        dr.rectangle((x * T + 2, y * T + 2, x * T + T - 3, y * T + T - 3), outline=(255, 0, 255, 255), width=2)
    prev.convert('RGB').save(sys.argv[2])
    print('preview', sys.argv[2])
