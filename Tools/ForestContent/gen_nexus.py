"""Generates the organized forest Nexus (alloy-server/Common/Resources/World/Data/Nexus.jm).

Layout ("compass village"): dirt market+spawn plaza in the middle, four dirt path arms
(N -> realm portals plaza, W -> training yard, E -> loop over two bridges to the guild plaza,
S -> campsite clearing), a river with a north pond and a south lake, a SW pond, tree groves,
and a dense tree wall along the island edge. Functional regions/fixtures keep their coordinates.
"""
import base64
import json
import math
import random
import struct
import subprocess
import sys
import zlib
from collections import Counter

ROOT = 'C:/Users/cbart/Desktop/Repos/Warriors-and-Wizards-Game/'
OUT = ROOT + 'alloy-server/Common/Resources/World/Data/Nexus.jm'
REL = 'alloy-server/Common/Resources/World/Data/Nexus.jm'

# ---------------------------------------------------------------- load original (mask + fixtures)
raw = subprocess.check_output(['git', '-C', ROOT, 'show', 'HEAD:' + REL]).decode('utf-8')
m = json.loads(raw)
W, H = m['width'], m['height']
idx = struct.unpack('>%dh' % (W * H), zlib.decompress(base64.b64decode(m['data'])))
orig = [[m['dict'][idx[y * W + x]] for x in range(W)] for y in range(H)]

FIXTURES = {'Guild Register', 'Armoire', 'Seer of the Stars', 'Guild Hall Portal', 'Vault Portal',
            'DpsDummy0def', 'DpsDummy40def', 'DpsDummy100def'}
mask = [[orig[y][x].get('ground') is not None for x in range(W)] for y in range(H)]
# The old buildings' wall tiles carry no ground, which leaves building-shaped holes inside the
# island. Flood-fill the real outside from the array border and treat every other empty tile as land.
outside = [[False] * W for _ in range(H)]
stack = [(x, y) for x in range(W) for y in (0, H - 1)] + [(x, y) for y in range(H) for x in (0, W - 1)]
while stack:
    x, y = stack.pop()
    if 0 <= x < W and 0 <= y < H and not outside[y][x] and not mask[y][x]:
        outside[y][x] = True
        stack += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
for y in range(H):
    for x in range(W):
        if not mask[y][x] and not outside[y][x]:
            mask[y][x] = True
fixture_objs = {}
regions = {}
for y in range(H):
    for x in range(W):
        l = orig[y][x]
        if l.get('regions'):
            regions[(x, y)] = l['regions']
        if l.get('objs') and l['objs'][0]['id'] in FIXTURES:
            fixture_objs[(x, y)] = l['objs']

rng = random.Random(20260918)

# inward distance from the void (4-neighbour BFS)
INF = 10 ** 6
edge_dist = [[INF] * W for _ in range(H)]
q = []
for y in range(H):
    for x in range(W):
        if not mask[y][x]:
            edge_dist[y][x] = 0
            q.append((x, y))
qi = 0
while qi < len(q):
    x, y = q[qi]
    qi += 1
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < W and 0 <= ny < H and edge_dist[ny][nx] > edge_dist[y][x] + 1:
            edge_dist[ny][nx] = edge_dist[y][x] + 1
            q.append((nx, ny))
for x in range(W):          # the array border counts as void
    for y in (0, H - 1):
        edge_dist[y][x] = min(edge_dist[y][x], 1)
for y in range(H):
    for x in (0, W - 1):
        edge_dist[y][x] = min(edge_dist[y][x], 1)

# ---------------------------------------------------------------- helpers
PH = [rng.random() * 6.28 for _ in range(6)]


def noise(x, y):
    return (math.sin(x * 0.31 + PH[0]) * math.cos(y * 0.27 + PH[1])
            + 0.6 * math.sin((x + y) * 0.19 + PH[2])
            + 0.4 * math.sin(x * 0.53 - y * 0.47 + PH[3])) / 2.0  # ~[-1, 1]


G, D, WT, BEW, BNS = 'grass', 'dirt', 'water', 'bridge_ew', 'bridge_ns'
ground = [[(G if mask[y][x] else None) for x in range(W)] for y in range(H)]


def inb(x, y):
    return 0 <= x < W and 0 <= y < H and ground[y][x] is not None


def paint_ellipse(cx, cy, rx, ry, kind, wobble=0.16, only_over=None):
    for y in range(int(cy - ry - 3), int(cy + ry + 4)):
        for x in range(int(cx - rx - 3), int(cx + rx + 4)):
            if not inb(x, y):
                continue
            d = ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2
            if d + noise(x, y) * wobble < 1.0 and (only_over is None or ground[y][x] in only_over):
                ground[y][x] = kind


def paint_rrect(x0, y0, x1, y1, r, kind, wobble=0.9):
    """Rounded rectangle [x0,x1]x[y0,y1] with corner radius r, edge jittered by noise."""
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


def stroke(pts, width, kind, meander=0.0, freq=0.35, skip_water=False, only_over=None):
    """Thick polyline; `meander` swings the centreline sideways."""
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
                        if only_over is not None and ground[yy][xx] not in only_over:
                            continue
                        ground[yy][xx] = kind


# ---------------------------------------------------------------- layout constants
SPAWN = (77.5, 74.5)
MARKET = (64, 52, 91, 69)          # x0,y0,x1,y1 (stores span x 67..88, y 54..67)
PORTALS = (78.5, 40.5)             # realm-portal plaza centre (region x 74..83, y 39..42)
GUILD = (109.5, 41.5)              # guild register plaza centre
YARD = (48, 70, 62, 84)            # training yard (dummies at x=56, y=73/76/79)
CAMP = (78.0, 101.0)               # southern campsite clearing

RIVER_X = lambda y: 100 + 3.0 * math.sin(y * 0.08)
RIVER_Y0, RIVER_Y1 = 28, 106
NPOND = (101.0, 27.0, 9.0, 6.0)
SLAKE = (97.0, 106.0, 14.0, 9.0)
SWPOND = (62.0, 103.0, 10.0, 6.5)

# ---------------------------------------------------------------- water
paint_ellipse(*NPOND[:2], NPOND[2], NPOND[3], WT, wobble=0.25)
paint_ellipse(*SLAKE[:2], SLAKE[2], SLAKE[3], WT, wobble=0.25)
paint_ellipse(*SWPOND[:2], SWPOND[2], SWPOND[3], WT, wobble=0.25)
river_pts = [(RIVER_X(y), y) for y in range(RIVER_Y0, RIVER_Y1 + 1, 4)]
stroke(river_pts, 5.0, WT, meander=0.0)

# ---------------------------------------------------------------- dirt: village + plazas + paths
paint_rrect(*MARKET, 5, D)
paint_ellipse(SPAWN[0], SPAWN[1], 11.0, 7.5, D)
paint_ellipse(PORTALS[0], PORTALS[1], 10.5, 6.5, D)
paint_ellipse(GUILD[0], GUILD[1], 6.5, 5.5, D)
paint_rrect(*YARD, 3, D)
paint_ellipse(CAMP[0], CAMP[1], 6.5, 5.0, D)

skip = dict(skip_water=True)
stroke([(78.5, 46.5), (78.5, 53.0)], 3.2, D, **skip)                     # N arm: market -> portals plaza
stroke([(66.0, 76.0), (61.0, 76.0)], 3.2, D, **skip)                     # W arm: spawn -> training yard
stroke([(77.5, 80.0), (78.0, 96.0)], 3.2, D, meander=0.5, freq=0.4, **skip)  # S arm: spawn -> campsite
stroke([(88.0, 41.0), (112.0, 41.0)], 3.2, D, **skip)                    # E loop: portals plaza -> guild (bridge 1)
stroke([(109.5, 43.0), (109.5, 60.0)], 3.2, D, **skip)                   # E loop: guild -> south
stroke([(109.5, 60.0), (90.0, 60.0)], 3.2, D, **skip)                    # E loop: -> market (bridge 2)

# bridges: any water tile within 1 tile of the straight crossing rows becomes plank
for (row, x0, x1) in ((41, 88, 112), (60, 90, 110)):
    for y in range(row - 1, row + 2):
        for x in range(x0, x1 + 1):
            if inb(x, y) and ground[y][x] == WT:
                ground[y][x] = BEW
# planks need a dry approach: make sure every bridge row ends on dirt on both banks
for (row, xa, xb) in ((41, 88, 112), (60, 90, 110)):
    for y in range(row - 1, row + 2):
        xs = [x for x in range(xa, xb + 1) if ground[y][x] == BEW]
        if xs:
            for x in (min(xs) - 1, min(xs) - 2, max(xs) + 1, max(xs) + 2):
                if inb(x, y) and ground[y][x] == G:
                    ground[y][x] = D

# region + fixture tiles are always dirt (never water/grass)
for (x, y) in list(regions) + list(fixture_objs):
    if inb(x, y):
        ground[y][x] = D

# ---------------------------------------------------------------- reserved-tile maps
walk = [[False] * W for _ in range(H)]
for y in range(H):
    for x in range(W):
        if ground[y][x] in (D, BEW, BNS):
            walk[y][x] = True
for (x, y) in list(regions) + list(fixture_objs):
    walk[y][x] = True


def dist_to(pred, cap=8):
    """Chebyshev distance to the nearest tile satisfying pred (capped)."""
    src = [(x, y) for y in range(H) for x in range(W) if pred(x, y)]
    out = [[cap] * W for _ in range(H)]
    for (sx, sy) in src:
        for y in range(max(0, sy - cap + 1), min(H, sy + cap)):
            for x in range(max(0, sx - cap + 1), min(W, sx + cap)):
                d = max(abs(x - sx), abs(y - sy))
                if d < out[y][x]:
                    out[y][x] = d
    return out


d_walk = dist_to(lambda x, y: walk[y][x])
d_water = dist_to(lambda x, y: ground[y][x] == WT)
# fixtures + any object tile need extra clearance
d_fix = dist_to(lambda x, y: (x, y) in fixture_objs, cap=4)

objs = {}          # (x,y) -> id
occ_tree = []      # tree positions for spacing checks


def free(x, y):
    return inb(x, y) and (x, y) not in objs and ground[y][x] == G and (x, y) not in regions and (x, y) not in fixture_objs


def near_tree(x, y, r):
    for (tx, ty) in occ_tree:
        if max(abs(tx - x), abs(ty - y)) < r:
            return True
    return False


def put(x, y, oid, tree=False):
    objs[(x, y)] = oid
    if tree:
        occ_tree.append((x, y))


TREES = ['Forest Tree', 'Forest Oak', 'Forest Pine', 'Forest Pine Tall']


def pick_tree():
    return rng.choice(TREES)


# ---------------------------------------------------------------- 1. tree wall along the island edge
for y in range(H):
    for x in range(W):
        e = edge_dist[y][x]
        if e < 1 or e > 7 or not free(x, y) or d_walk[y][x] < 2 or d_water[y][x] < 1:
            continue
        p = {1: 1.0, 2: 1.0, 3: 0.9, 4: 0.7, 5: 0.5, 6: 0.32, 7: 0.16}[e]
        if rng.random() < p and not near_tree(x, y, 2 if e <= 3 else 3):
            put(x, y, pick_tree(), tree=True)

# ---------------------------------------------------------------- 2. path avenues (pines/oaks every few tiles)
def avenue(x0, y0, x1, y1, off, step=5, tree_pool=('Forest Pine', 'Forest Pine Tall', 'Forest Oak')):
    L = math.hypot(x1 - x0, y1 - y0)
    n = int(L // step)
    ux, uy = (x1 - x0) / L, (y1 - y0) / L
    px, py = -uy, ux
    for i in range(1, n):
        cx, cy = x0 + ux * i * step, y0 + uy * i * step
        for side in (-1, 1):
            x = int(round(cx + px * off * side + rng.uniform(-0.6, 0.6)))
            y = int(round(cy + py * off * side + rng.uniform(-0.6, 0.6)))
            if free(x, y) and d_walk[y][x] >= 2 and not near_tree(x, y, 3):
                put(x, y, rng.choice(tree_pool), tree=True)


avenue(78.5, 46, 78.5, 52, 5, step=4)
avenue(61, 76, 48, 76, 5, step=5)
avenue(78, 82, 78, 96, 5, step=5)
avenue(88, 41, 108, 41, 5, step=5)
avenue(109.5, 45, 109.5, 60, 5, step=5)
avenue(109, 60, 91, 60, 5, step=5)

# ---------------------------------------------------------------- 3. groves (deliberate clusters)
GROVES = [(30, 45, 7), (48, 26, 6), (22, 72, 6), (20, 100, 5), (48, 105, 7), (62, 112, 6),
          (118, 28, 6), (124, 55, 7), (122, 80, 6), (112, 100, 6), (70, 22, 6), (92, 90, 5),
          (56, 92, 5), (40, 62, 5), (68, 97, 4), (116, 70, 4)]
for (gx, gy, gr) in GROVES:
    tries = 0
    placed = 0
    want = int(gr * gr * 0.55)
    while placed < want and tries < 400:
        tries += 1
        a, r = rng.random() * 6.283, rng.random() ** 0.6 * gr
        x, y = int(round(gx + math.cos(a) * r)), int(round(gy + math.sin(a) * r * 0.85))
        if free(x, y) and d_walk[y][x] >= 3 and d_water[y][x] >= 2 and edge_dist[y][x] > 3 and not near_tree(x, y, 3):
            put(x, y, pick_tree(), tree=True)
            placed += 1
    # understory: bushes/mushrooms/logs/rocks around and inside the grove
    for _ in range(int(gr * 3)):
        a, r = rng.random() * 6.283, rng.random() * (gr + 2)
        x, y = int(round(gx + math.cos(a) * r)), int(round(gy + math.sin(a) * r * 0.85))
        if free(x, y) and d_walk[y][x] >= 2 and d_water[y][x] >= 1 and edge_dist[y][x] > 2:
            put(x, y, rng.choice(['Forest Bush', 'Forest Bush Wide', 'Forest Shrub', 'Forest Mushroom Red',
                                  'Forest Mushroom Big', 'Forest Mushroom Red', 'Forest Small Rock', 'Forest Grass Tuft']))
    for _ in range(2):
        a, r = rng.random() * 6.283, rng.random() * gr
        x, y = int(round(gx + math.cos(a) * r)), int(round(gy + math.sin(a) * r * 0.85))
        if free(x, y) and free(x + 1, y) and d_walk[y][x] >= 3 and d_water[y][x] >= 2 and edge_dist[y][x] > 3:
            put(x, y, rng.choice(['Forest Log', 'Forest Log Mossy', 'Forest Rock Cluster']))

# ---------------------------------------------------------------- 4. waterside: reeds, rocks
for y in range(H):
    for x in range(W):
        if free(x, y) and d_water[y][x] == 1 and edge_dist[y][x] > 2 and d_walk[y][x] >= 1:
            r = rng.random()
            if r < 0.30:
                put(x, y, 'Forest Reeds')
            elif r < 0.36:
                put(x, y, 'Forest Small Rock')
            elif r < 0.39:
                put(x, y, 'Forest Rock Cluster')
# a few oaks reaching over the ponds' far banks
for (cx, cy) in ((SWPOND[0] - 12, SWPOND[1] - 2), (SWPOND[0] + 13, SWPOND[1] + 3), (NPOND[0] + 11, NPOND[1] + 1),
                 (SLAKE[0] - 16, SLAKE[1] - 4)):
    for _ in range(20):
        x, y = int(cx + rng.randint(-2, 2)), int(cy + rng.randint(-2, 2))
        if free(x, y) and d_walk[y][x] >= 3 and d_water[y][x] >= 2 and not near_tree(x, y, 4):
            put(x, y, 'Forest Oak', tree=True)
            break

# ---------------------------------------------------------------- 5. village dressing (non-blocking)
for y in range(H):
    for x in range(W):
        if not free(x, y):
            continue
        dw = d_walk[y][x]
        if dw == 1 and rng.random() < 0.13:
            put(x, y, rng.choice(['Forest Bush', 'Forest Shrub', 'Forest Bush Wide', 'Forest Grass Tuft', 'Forest Flowers']))
        elif dw == 2 and rng.random() < 0.05:
            put(x, y, rng.choice(['Forest Flowers', 'Forest Grass Tuft', 'Forest Mushroom Red']))

# campsite: logs around a centre, rocks, mushrooms
for k in range(6):
    a = k * math.pi / 3 + 0.3
    x, y = int(round(CAMP[0] + math.cos(a) * 3.6)), int(round(CAMP[1] + math.sin(a) * 2.8))
    if inb(x, y) and (x, y) not in objs and (x, y) not in regions:
        objs[(x, y)] = 'Forest Log' if k % 2 == 0 else 'Forest Log Mossy'
# training yard: rock markers at the corners
for (x, y) in ((YARD[0] + 1, YARD[1] + 1), (YARD[2] - 1, YARD[1] + 1), (YARD[0] + 1, YARD[3] - 1), (YARD[2] - 1, YARD[3] - 1)):
    if inb(x, y) and (x, y) not in objs and ground[y][x] == D:
        objs[(x, y)] = 'Forest Rock Cluster'
# spawn plaza: ring of small rocks/tufts on its grass rim (never on dirt)
for k in range(14):
    a = k * 2 * math.pi / 14
    x, y = int(round(SPAWN[0] + math.cos(a) * 12.5)), int(round(SPAWN[1] + math.sin(a) * 9.0))
    if free(x, y):
        put(x, y, rng.choice(['Forest Small Rock', 'Forest Grass Tuft', 'Forest Flowers']))

# ---------------------------------------------------------------- 6. open-field accents
for y in range(H):
    for x in range(W):
        if free(x, y) and edge_dist[y][x] > 3 and d_walk[y][x] >= 3 and d_water[y][x] >= 2:
            if rng.random() < 0.035:
                put(x, y, rng.choice(['Forest Grass Tuft', 'Forest Grass Tuft', 'Forest Small Rock', 'Forest Bush']))
# flower / mushroom patches
for _ in range(26):
    cx, cy = rng.randint(8, W - 8), rng.randint(8, H - 8)
    kind = rng.choice(['Forest Flowers', 'Forest Flowers', 'Forest Mushroom Red', 'Forest Mushroom Big'])
    if not (inb(cx, cy) and edge_dist[cy][cx] > 4 and d_walk[cy][cx] >= 3):
        continue
    for _ in range(rng.randint(3, 6)):
        x, y = cx + rng.randint(-2, 2), cy + rng.randint(-2, 2)
        if free(x, y) and d_walk[y][x] >= 2 and d_water[y][x] >= 2:
            put(x, y, kind)

# ---------------------------------------------------------------- encode
def tile_key(x, y):
    g = ground[y][x]
    o = fixture_objs.get((x, y))
    r = regions.get((x, y))
    if g is None:
        return ('', None, None)
    gname = {G: 'Woodland Grass', D: 'Woodland Dirt', WT: 'Woodland Water',
             BEW: 'Woodland Bridge EW', BNS: 'Woodland Bridge NS'}[g]
    if o:
        return (gname, json.dumps(o, sort_keys=True), None)
    if (x, y) in objs:
        return (gname, json.dumps([{'id': objs[(x, y)]}], sort_keys=True), json.dumps(r, sort_keys=True) if r else None)
    return (gname, None, json.dumps(r, sort_keys=True) if r else None)


dict_list = []
key_idx = {}
buf = []
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
assert len(dict_list) < 32000
data = base64.b64encode(zlib.compress(struct.pack('>%dh' % (W * H), *buf))).decode('ascii')
out = json.dumps({'width': W, 'data': data, 'height': H, 'dict': dict_list}, separators=(',', ':'))
open(sys.argv[1] if len(sys.argv) > 1 else OUT, 'w', encoding='utf-8', newline='').write(out)

c = Counter(objs.values())
print('objects:', sum(c.values()), dict(c.most_common()))
print('ground:', Counter(g for row in ground for g in row))
print('dict entries:', len(dict_list))
