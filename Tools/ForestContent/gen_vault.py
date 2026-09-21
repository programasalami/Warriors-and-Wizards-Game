"""Generates the forest VAULT map (alloy-server/Common/Resources/World/Data/Vault.jm): your personal storage glade.

    a round clearing of grass ringed by a wall of trees, a dirt plaza in the middle, a dirt path running south to the spawn, a small pond with reeds in the
    north-east, flowers / mushrooms / rocks scattered about. 16 chest spots are marked with the "Vault" region: the server puts an OPEN Vault Chest on as many
    as the account owns (nearest the middle first) and a Closed Vault Chest on the rest (see GameServer/Game/Worlds/Logic/Vault.cs).

Everything is the new forest tileset (Woodland* grounds, Forest* objects) - no original art - and a test enforces that. There is no exit portal (the only portal
art is original): leave with the Escape-to-Nexus key. The script checks every chest spot can be reached on foot from the spawn.

    python Tools/ForestContent/gen_vault.py [out.jm]
"""
import base64
import json
import math
import os
import random
import struct
import sys
import zlib
from collections import deque

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')).replace(os.sep, '/') + '/'
OUT = ROOT + 'alloy-server/Common/Resources/World/Data/Vault.jm'

W, H = 48, 40
CX, CY = 24, 20
RX, RY = 21.5, 17.0            # the whole grove, trees included
WALL = 0.80                    # inside this fraction of the radius is the open glade; from here out is the ring of trees
SPOTS_WANTED = 18

GRASS, DIRT, WATER = 'Woodland Grass', 'Woodland Dirt', 'Woodland Water'
BLOCKING = {'Forest Pine', 'Forest Pine Tall', 'Forest Oak', 'Forest Tree', 'Forest Rock Cluster', 'Forest Log', 'Forest Log Mossy'}
WALL_TREES = ['Forest Pine', 'Forest Oak', 'Forest Pine Tall', 'Forest Tree']
SOFT_DECOR = ['Forest Flowers', 'Forest Flowers', 'Forest Grass Tuft', 'Forest Grass Tuft', 'Forest Bush', 'Forest Shrub', 'Forest Mushroom Red', 'Forest Small Rock']

rng = random.Random(20260920)
ground = [[None] * W for _ in range(H)]
objs = {}
regions = {}


def ell(x, y, cx, cy, rx, ry):
    return ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2


# ---- ground: the grove (grass) with a plaza and a path of dirt --------------------------------------------------------------------------------------
for y in range(H):
    for x in range(W):
        if ell(x, y, CX, CY, RX, RY) < 1.0:
            ground[y][x] = GRASS
        if ell(x, y, CX, CY, 8.5, 6.5) < 1.0:
            ground[y][x] = DIRT
for y in range(CY, CY + 14):                       # the path south to the spawn
    for x in range(CX - 1, CX + 2):
        if ground[y][x]:
            ground[y][x] = DIRT

# the spawn: a small dirt square at the south end of the path
SPAWN = [(x, y) for y in range(CY + 12, CY + 14) for x in range(CX - 2, CX + 3)]
for x, y in SPAWN:
    ground[y][x] = DIRT
    regions[(x, y)] = 'Spawn'

# a small pond in the north-east
POND = (35.5, 12.5, 3.2, 2.2)
for y in range(H):
    for x in range(W):
        if ell(x, y, *POND) < 1.0 and ground[y][x]:
            ground[y][x] = WATER

# ---- the wall of trees ------------------------------------------------------------------------------------------------------------------------------
for y in range(H):
    for x in range(W):
        if ground[y][x] and ell(x, y, CX, CY, RX, RY) >= WALL ** 2 and ground[y][x] != WATER:
            objs[(x, y)] = rng.choice(WALL_TREES)

# ---- chest spots ------------------------------------------------------------------------------------------------------------------------------------
def near_path(x, y):
    return abs(x - CX) <= 3 and y >= CY - 1

spots = []
for ring, (rx, ry, n) in enumerate(((5.5, 3.8, 7), (11.5, 8.6, 13))):
    for k in range(n):
        a = math.pi * 2 * k / n - math.pi / 2
        x, y = int(round(CX + rx * math.cos(a) - 0.5)), int(round(CY + ry * math.sin(a) - 0.5))
        if near_path(x, y) or (x, y) in spots or ground[y][x] in (None, WATER) or (x, y) in objs:
            continue
        spots.append((x, y))
spots = spots[:SPOTS_WANTED]
for s in spots:
    regions[s] = 'Vault'

# ---- decor (never on a chest spot, the spawn, or the path) -----------------------------------------------------------------------------------------
protected = set(spots) | set(SPAWN)
for (sx, sy) in spots:
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            protected.add((sx + dx, sy + dy))
for y in range(H):
    for x in range(W):
        if abs(x - CX) <= 2 and y >= CY:
            protected.add((x, y))


def free(x, y):
    return ground[y][x] == GRASS and (x, y) not in objs and (x, y) not in protected


placed = 0
tries = 0
while placed < 70 and tries < 4000:
    tries += 1
    x, y = rng.randrange(W), rng.randrange(H)
    if free(x, y) and ell(x, y, CX, CY, RX, RY) < (WALL - 0.04) ** 2:
        objs[(x, y)] = rng.choice(SOFT_DECOR)
        placed += 1
for _ in range(5):                                  # a few solid rocks and a fallen log
    for t in range(200):
        x, y = rng.randrange(W), rng.randrange(H)
        if free(x, y) and ell(x, y, CX, CY, RX, RY) < (WALL - 0.1) ** 2:
            objs[(x, y)] = rng.choice(['Forest Rock Cluster', 'Forest Log', 'Forest Log Mossy'])
            break
for y in range(H):                                  # reeds round the pond
    for x in range(W):
        if ground[y][x] == GRASS and (x, y) not in objs and (x, y) not in protected and ell(x, y, POND[0], POND[1], POND[2] + 1.4, POND[3] + 1.4) < 1.0:
            if rng.random() < 0.5:
                objs[(x, y)] = 'Forest Reeds'


# ---- checks: every chest spot can be reached on foot from the spawn, and the counts are what the server expects -----------------------------------
def passable(x, y):
    return 0 <= x < W and 0 <= y < H and ground[y][x] in (GRASS, DIRT) and objs.get((x, y)) not in BLOCKING


start = SPAWN[0]
seen = {start}
queue = deque([start])
while queue:
    cx_, cy_ = queue.popleft()
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = cx_ + dx, cy_ + dy
        if (nx, ny) not in seen and passable(nx, ny):
            seen.add((nx, ny))
            queue.append((nx, ny))

for (sx, sy) in spots:
    if not any((sx + dx, sy + dy) in seen for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
        raise SystemExit('chest spot %s cannot be reached from the spawn' % ((sx, sy),))
if len(spots) < 16:
    raise SystemExit('only %d chest spots - the server hands out up to 16' % len(spots))

# ---- write the map ----------------------------------------------------------------------------------------------------------------------------------
dict_list, index, cells = [], {}, []
for y in range(H):
    for x in range(W):
        e = {}
        if ground[y][x]:
            e['ground'] = ground[y][x]
        if (x, y) in objs:
            e['objs'] = [{'id': objs[(x, y)]}]
        if (x, y) in regions:
            e['regions'] = [{'id': regions[(x, y)]}]
        key = json.dumps(e, sort_keys=True)
        if key not in index:
            index[key] = len(dict_list)
            dict_list.append(e)
        cells.append(index[key])

data = base64.b64encode(zlib.compress(struct.pack('>%dh' % (W * H), *cells), 9)).decode('ascii')
out = json.dumps({'width': W, 'data': data, 'height': H, 'dict': dict_list}, separators=(',', ':'))
target = sys.argv[1] if len(sys.argv) > 1 else OUT
open(target, 'w', encoding='utf-8', newline='').write(out)
print('wrote %s: %dx%d, %d dictionary entries, %d chest spots, %d objects, %d reachable tiles' % (target, W, H, len(dict_list), len(spots), len(objs), len(seen)))
