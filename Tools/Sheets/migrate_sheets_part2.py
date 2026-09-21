"""Part 2 of the 2026-09-20 sheet migration (run once, after migrate_sheets.py --apply): the title screen's cave battle animation (CaveBattle.cs) draws
21 monsters from the old LofiChar2 sheet and a dozen effect sprites from the old LofiObj sheet, which part 1 did not know about. This adds them:
  CaveMonsters_16x16.png  (sheet "caveMonsters", 21 monsters in the order of the roster in CaveBattle.cs)
  Icons_8x8.png           (the effect sprites are added to the end of the "icons" sheet)
and rewrites the sheet/index numbers in CaveBattle.cs and Game.atlas.
"""
import json
import os
import re

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
CONTENT = os.path.join(REPO, 'AlloyClient', 'AlloyClient', 'Content')
SHEETS = os.path.join(CONTENT, 'Sheets')
OLD = os.path.join(HERE, 'source_old')
CB = os.path.join(REPO, 'AlloyClient', 'AlloyClient', 'Screens', 'Components', 'CaveBattle.cs')
MAPF = os.path.join(HERE, 'migration_map_2026-09-20.json')
MAP = json.load(open(MAPF))

src = open(CB, encoding='utf-8-sig').read()

# monsters ----------------------------------------------------------------------------------------------------------------------------------------------
cells = [int(n) for n in re.findall(r'^\s+new\((\d+), [\d.]+, [\d.]+, Glow\w+, ', src, re.M)]
assert len(cells) == 21, cells
old2 = Image.open(os.path.join(OLD, 'LofiChar2.png')).convert('RGBA')
cols_old = old2.width // 16
cols = 7
sheet = Image.new('RGBA', (cols * 16, 3 * 16), (0, 0, 0, 0))
new_of = {}
for i, c in enumerate(cells):
    x, y = (c % cols_old) * 16, (c // cols_old) * 16
    sheet.paste(old2.crop((x, y, x + 16, y + 16)), ((i % cols) * 16, (i // cols) * 16))
    new_of[c] = i
    MAP['lofiChar216x16:%d' % c] = 'caveMonsters:%d' % i
sheet.save(os.path.join(SHEETS, 'CaveMonsters_16x16.png'))

# effects -----------------------------------------------------------------------------------------------------------------------------------------------
fx_old = sorted({int(x, 16) for x in re.findall(r'(?:Fx\w+ = |\[)?0x([0-9A-Fa-f]{2})\b', src)})
fx_old = [n for n in fx_old if n in (0xEA, 0xF6, 0x69, 0x6B, 0x72, 0x98, 0x88, 0x62, 0x65, 0x75, 0x78, 0x68)]
icons = Image.open(os.path.join(SHEETS, 'Icons_8x8.png')).convert('RGBA')
lofi = Image.open(os.path.join(OLD, 'LofiObj.png')).convert('RGBA')
icols = icons.width // 8
used = {int(v.split(':')[1]) for v in MAP.values() if v.startswith('icons:')}
free = [i for i in range(icols * (icons.height // 8)) if i not in used]
fx_new = {}
for n in fx_old:
    k = 'lofiObj:%d' % n
    if k in MAP:                                             # already moved (the Blade)
        fx_new[n] = int(MAP[k].split(':')[1])
        continue
    slot = free.pop(0)
    x, y = (n % 16) * 8, (n // 16) * 8
    icons.paste(lofi.crop((x, y, x + 8, y + 8)), ((slot % icols) * 8, (slot // icols) * 8))
    MAP[k] = 'icons:%d' % slot
    fx_new[n] = slot
icons.save(os.path.join(SHEETS, 'Icons_8x8.png'))

# CaveBattle.cs -------------------------------------------------------------------------------------------------------------------------------------------
def fx_sub(m):
    return m.group(1) + str(fx_new[int(m.group(2), 16)])


t = re.sub(r'(private const int Fx\w+ = )0x([0-9A-Fa-f]{2})', lambda m: m.group(1) + str(fx_new[int(m.group(2), 16)]), src)
t = re.sub(r'(SparkByGlow = \[)([^\]]+)(\])', lambda m: m.group(1) + ', '.join(str(fx_new[int(x, 16)]) for x in re.findall(r'0x([0-9A-Fa-f]{2})', m.group(2))) + m.group(3), t)
t = re.sub(r'^(\s+new\()(\d+)(, [\d.]+, [\d.]+, Glow\w+, )', lambda m: m.group(1) + str(new_of[int(m.group(2))]) + m.group(3), t, flags=re.M)
t = t.replace('"lofiChar216x16"', '"caveMonsters"').replace('FromGameAtlas("lofiObj", index)', 'FromGameAtlas("icons", index)')
t = t.replace('(lofiChar216x16)', '(the "caveMonsters" sheet)').replace('in lofiObj', 'in the "icons" sheet').replace('cell in lofiChar216x16', 'cell in caveMonsters').replace('lofiChar216x16', 'caveMonsters')
t = t.replace('// ---- Effect sprite indices in the "icons" sheet (8x8 art; see the sheet)', '// ---- Effect sprite indices in the "icons" sheet (8x8 art; see the sheet; moved there 2026-09-20)')
open(CB, 'w', encoding='utf-8-sig', newline='').write(t)

# Game.atlas ----------------------------------------------------------------------------------------------------------------------------------------------
lines = ['<Atlas source="Sheets">', '',
         '    <!-- Every sheet the game uses (rebuilt 2026-09-20 by Tools/Sheets/migrate_sheets.py; see Tools/Sheets/README.md). Each sheet is ONE uniform grid, split by',
         '         cell size. Add new sheets here as they are made. Decoration cells keep the 2:3 width:height ratio on purpose (the game scales a sprite from it). -->']
ENTRIES = [('tileAlphaBlend', 8, 8, 'AlphaTileBlends.png'), ('grasslands', 16, 16, 'Grasslands_16x16.png'), ('smallPlants', 24, 36, 'SmallPlants_24x36.png'),
           ('mediumPlants', 40, 60, 'MediumPlants_40x60.png'), ('smallTrees', 56, 84, 'SmallTrees_56x84.png'), ('mediumTrees', 80, 120, 'MediumTrees_80x120.png'),
           ('flatProps', 48, 48, 'FlatProps_48x48.png'), ('largeObjects', 80, 120, 'LargeObjects_80x120.png'),
           ('equipAndConsume', 16, 16, 'EquipAndConsume_16x16.png'), ('guildHall', 8, 8, 'GuildHall_8x8.png'),
           ('guildHallLarge', 16, 16, 'GuildHallLarge_16x16.png'), ('icons', 8, 8, 'Icons_8x8.png'), ('caveMonsters', 16, 16, 'CaveMonsters_16x16.png')]
for name, w, h, f in ENTRIES:
    lines.append('    <Image name="%s" w="%d" h="%d">%s</Image>' % (name, w, h, f))
lines.append('    <Animated name="npcs" w="8" h="8">Npcs_8x8.png</Animated>')
lines.append('    <Animated name="players" w="32" h="32" group="Full">Players.png</Animated>')
lines.append('</Atlas>')
p = os.path.join(CONTENT, 'Game.atlas')
raw = open(p, 'rb').read()
open(p, 'wb').write((b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'') + '\r\n'.join(lines).encode('utf-8') + b'\r\n')
json.dump(MAP, open(MAPF, 'w'), indent=1)
print('monsters', len(cells), 'effects', fx_new)
