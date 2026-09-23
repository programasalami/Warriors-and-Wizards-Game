"""ONE-SHOT migration (2026-09-20): rebuilds every game art sheet from only the pictures that something really uses, grouped and ordered neatly, and rewrites every
`<File>` / `<Index>` reference in the client XMLs (and the few code references) to the new positions.

  python Tools/Sheets/migrate_sheets.py            # dry run: prints the plan, writes nothing
  python Tools/Sheets/migrate_sheets.py --apply    # does it

The OLD sheets are archived in Tools/Sheets/source_old (kept forever: it is the art library the new sheets were cut from). After this run the new sheets are
the source of truth: edit them (and Game.atlas / the XMLs) directly. See Tools/Sheets/README.md for the sheet list and how to add art.

Why the sheets are split by SIZE: the atlas builder cuts each sheet into ONE uniform grid, and the game scales a sprite from its cell's width/height ratio, so every
decoration sheet keeps the old 2:3 cell ratio (the sprites then look exactly as before) and the BottomInset of the moved sprites is recomputed so they still stand
on the same spot.
"""
import collections
import glob
import json
import os
import re
import shutil
import sys

from PIL import Image

APPLY = '--apply' in sys.argv
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
CONTENT = os.path.join(REPO, 'WaW-Client', 'WaWClient', 'Content')
SHEETS = os.path.join(CONTENT, 'Sheets')
XMLS = os.path.join(CONTENT, 'Xmls')
OLD = os.path.join(HERE, 'source_old')
DESKTOP = r'C:\Users\cbart\Desktop'
CODE = os.path.join(REPO, 'WaW-Client', 'WaWClient')

# ------------------------------------------------------------------------------------------------------------------------------------------------------------
# 0. the old sheets (archived on the first run)
# ------------------------------------------------------------------------------------------------------------------------------------------------------------
if not os.path.isdir(OLD):
    if APPLY:
        os.makedirs(OLD)
        for f in glob.glob(os.path.join(SHEETS, '*.png')):
            shutil.copy2(f, OLD)
        shutil.copy2(os.path.join(CONTENT, 'Game.atlas'), os.path.join(OLD, 'Game.atlas.old'))
    else:
        OLD = SHEETS
if APPLY and not os.path.exists(os.path.join(OLD, 'Game.atlas.old')):
    raise SystemExit('archive is incomplete')
ATLAS_OLD = os.path.join(OLD, 'Game.atlas.old') if os.path.exists(os.path.join(OLD, 'Game.atlas.old')) else os.path.join(CONTENT, 'Game.atlas')

old_meta = {}       # atlas name -> (file, cw, ch, animated)
for m in re.finditer(r'<(Image|Animated)\s+name="([^"]+)"\s+w="(\d+)"\s+h="(\d+)"[^>]*>([^<]+)</\1>', open(ATLAS_OLD, encoding='utf-8-sig').read()):
    kind, name, w, h, f = m.groups()
    old_meta[name] = (f.strip(), int(w), int(h), kind == 'Animated')
_images = {}


def old_cell(sheet, idx):
    f, cw, ch, _ = old_meta[sheet]
    if f not in _images:
        _images[f] = Image.open(os.path.join(OLD, f)).convert('RGBA')
    img = _images[f]
    cols = img.width // cw
    x, y = (idx % cols) * cw, (idx // cols) * ch
    return img.crop((x, y, x + cw, y + ch))


def read(p):
    return open(p, 'rb').read().decode('latin-1')


def write(p, t):
    open(p, 'wb').write(t.encode('latin-1'))


# ------------------------------------------------------------------------------------------------------------------------------------------------------------
# 1. who uses which cell (XML definitions, in file order)
# ------------------------------------------------------------------------------------------------------------------------------------------------------------
PAIR = re.compile(r'<File>([^<]+)</File>\s*<Index>([^<]+)</Index>')
owners = []         # (xml file, kind, id, [(sheet, idx)])
for path in sorted(glob.glob(os.path.join(XMLS, '*.xml'))):
    t = read(path)
    for m in re.finditer(r'<(Object|Ground)\b[^>]*?\bid="([^"]*)"[^>]*>(.*?)</\1>', t, re.S):
        kind, oid, body = m.groups()
        cells = []
        for pm in PAIR.finditer(body):
            sheet, ix = pm.group(1).strip(), pm.group(2).strip()
            cells.append((sheet, int(ix, 0)))
        owners.append((os.path.basename(path), kind, oid, cells))
ORDER = {'Ground.xml': 0, 'Objects.xml': 1, 'StaticObjects.xml': 2, 'NPCs.xml': 3}


def owner_cells(oid, sheets=None):
    out = []
    for f, k, i, cells in owners:
        if i == oid:
            for c in cells:
                if (sheets is None or c[0] in sheets) and c not in out:
                    out.append(c)
    return out


# ------------------------------------------------------------------------------------------------------------------------------------------------------------
# 2. the new sheets
# ------------------------------------------------------------------------------------------------------------------------------------------------------------
class Sheet:
    def __init__(self, name, file, cw, ch, cols, rows, animated=False):
        self.name, self.file, self.cw, self.ch, self.cols, self.rows, self.animated = name, file, cw, ch, cols, rows, animated
        self.img = Image.new('RGBA', (cols * cw, rows * ch), (0, 0, 0, 0))
        self.count = 0

    def put(self, row, col, pic):
        assert row < self.rows and col < self.cols, (self.name, row, col)
        assert pic.size == (self.cw, self.ch), (self.name, pic.size)
        self.img.paste(pic, (col * self.cw, row * self.ch))
        self.count += 1
        return row * self.cols + col


NEW = collections.OrderedDict()
MAP = {}            # (old sheet, old index) -> (new sheet, new index)
INSET_H = {}        # new sheet -> cell height (for the BottomInset rescale of decor that moved)
STOCK = []          # (new sheet, index): original (stock) art that lives on a "new art" sheet


def add(sheet):
    NEW[sheet.name] = sheet
    return sheet


def move(old, new_sheet, new_idx):
    assert old not in MAP, ('cell used twice', old)
    MAP[old] = (new_sheet, new_idx)


# -- Grasslands: ground and edges (16x16), one row per ground type ------------------------------------------------------------------------------------------
g = add(Sheet('grasslands', 'Grasslands_16x16.png', 16, 16, 8, 6))
row = 0
for gid in ('Woodland Grass', 'Woodland Dirt', 'Woodland Water'):
    cells = [c for c in owner_cells(gid, ('forestGround',))]
    for col, c in enumerate(cells):
        move(c, 'grasslands', g.put(row, col, old_cell(*c)))
    row += 1
bridges = [c for gid in ('Woodland Bridge EW', 'Woodland Bridge NS') for c in owner_cells(gid, ('forestGround',))]
for col, c in enumerate(bridges):
    move(c, 'grasslands', g.put(row, col, old_cell(*c)))
row += 1
edges = []
for gid in ('Woodland Dirt', 'Woodland Water'):
    for c in owner_cells(gid, ('forestGroundEdge',)):
        if c not in edges:
            edges.append(c)
for col, c in enumerate(edges):
    move(c, 'grasslands', g.put(row, col, old_cell(*c)))


# -- decoration sheets: cells keep the old 2:3 ratio; the art is re-seated in the middle, 3 px above the cell's bottom (as the old sheet did) --------------------
def decor_sheet(name, file, cw, ch, cols, rows, sprites):
    """sprites: object ids (their forestDecor cell) or a raw ('forestDecor', n) cell that only the backdrop uses. The art keeps its exact pixel distance from
    the cell's bottom edge and from the cell's centre line, so BottomInset (recomputed below) puts it on the same spot as before."""
    s = add(Sheet(name, file, cw, ch, cols, rows))
    INSET_H[name] = ch
    for i, oid in enumerate(sprites):
        old = [oid] if isinstance(oid, tuple) else owner_cells(oid, ('forestDecor',))
        assert len(old) == 1, (oid, old)
        cell = old_cell(*old[0])
        bb = cell.getbbox()
        art = cell.crop(bb)
        margin = cell.height - bb[3]
        x = cw // 2 + ((bb[0] + bb[2]) // 2 - cell.width // 2) - art.width // 2
        assert 0 <= x and x + art.width <= cw and art.height + margin <= ch, ('does not fit', oid, art.size, bb, (cw, ch))
        pic = Image.new('RGBA', (cw, ch), (0, 0, 0, 0))
        pic.paste(art, (x, ch - margin - art.height), art)
        move(old[0], name, s.put(i // cols, i % cols, pic))
    return s


decor_sheet('smallPlants', 'SmallPlants_24x36.png', 24, 36, 4, 2, ['Forest Mushroom Red', 'Forest Mushroom Big', 'Forest Flowers', 'Forest Grass Tuft'])
decor_sheet('mediumPlants', 'MediumPlants_40x60.png', 40, 60, 4, 2, ['Forest Shrub', 'Forest Bush', 'Forest Bush Wide', 'Forest Small Rock', 'Forest Rock Cluster', 'Forest Reeds', ('forestDecor', 15)])   # the last one only the title backdrop draws
decor_sheet('smallTrees', 'SmallTrees_56x84.png', 56, 84, 4, 1, ['Forest Pine', 'Forest Pine Tall'])
decor_sheet('mediumTrees', 'MediumTrees_80x120.png', 80, 120, 4, 1, ['Forest Tree', 'Forest Oak'])

fl = add(Sheet('flatProps', 'FlatProps_48x48.png', 48, 48, 4, 1))
for i, oid in enumerate(['Forest Log', 'Forest Log Mossy']):
    old = owner_cells(oid, ('forestFlat',))
    move(old[0], 'flatProps', fl.put(0, i, old_cell(*old[0])))

lo = add(Sheet('largeObjects', 'LargeObjects_80x120.png', 80, 120, 4, 1))
for i, oid in enumerate(['Bug Board', 'Jukebox', 'Vault Chest', 'Closed Vault Chest']):
    old = owner_cells(oid, ('forestProps',))
    move(old[0], 'largeObjects', lo.put(0, i, old_cell(*old[0])))

# -- Equip and consumables (16x16): one COLUMN per kind, tier 0 (the starter set) on the top row, tiers going down --------------------------------------------
COLS = 12
eq = add(Sheet('equipAndConsume', 'EquipAndConsume_16x16.png', 16, 16, COLS, 8))
KINDS = ['sword', 'helmet', 'armor', 'staff', 'spell', 'robe', 'ring']          # columns 0..6; column 7 is a spacer; consumables start at column 8
sword_cells = [('weapons', i) for i in range(0, 5)]
staff_cells = [('weapons', i) for i in range(5, 10)]
for tier, c in enumerate(sword_cells):
    move(c, 'equipAndConsume', eq.put(tier, KINDS.index('sword'), old_cell(*c)))
for tier, c in enumerate(staff_cells):
    move(c, 'equipAndConsume', eq.put(tier, KINDS.index('staff'), old_cell(*c)))
helmet = Image.open(os.path.join(HERE, 'source', 'item209.png') if os.path.exists(os.path.join(HERE, 'source', 'item209.png')) else os.path.join(DESKTOP, 'item209.png')).convert('RGBA')
move(('weapons', 10), 'equipAndConsume', eq.put(0, KINDS.index('helmet'), helmet))         # Broken Helmet = the user's item209.png
move(('weapons', 11), 'equipAndConsume', eq.put(0, KINDS.index('spell'), old_cell('weapons', 11)))       # Old Spell
potion = old_cell('lofiObj2', 50).resize((16, 16), Image.NEAREST)              # the stock 8x8 Health Potion, doubled (nearest neighbour keeps the pixels square)
pidx = eq.put(0, 8, potion)
move(('lofiObj2', 50), 'equipAndConsume', pidx)
STOCK.append(('equipAndConsume', pidx))

# -- Guild hall (8x8) + the one 16x16 piece -------------------------------------------------------------------------------------------------------------------
STOCK8 = ('lofiEnvironment', 'lofiEnvironment2', 'lofiEnvironment3')
gh_owners = [o for o in owners if any(c[0] in STOCK8 for c in o[3])]
gh_owners.sort(key=lambda o: (0 if o[1] == 'Ground' else 1, ORDER.get(o[0], 9)))
seen = set()
rows_needed = 0
plan = []
for f, k, oid, cells in gh_owners:
    fresh = [c for c in cells if c[0] in STOCK8 and c not in seen]
    if fresh:
        plan.append((oid, fresh))
        seen.update(fresh)
gh = add(Sheet('guildHall', 'GuildHall_8x8.png', 8, 8, 8, len(plan)))
for r, (oid, cells) in enumerate(plan):
    assert len(cells) <= 8, oid
    for col, c in enumerate(cells):
        move(c, 'guildHall', gh.put(r, col, old_cell(*c)))
ghl = add(Sheet('guildHallLarge', 'GuildHallLarge_16x16.png', 16, 16, 4, 1))
move(('lofiObjBig', 157), 'guildHallLarge', ghl.put(0, 0, old_cell('lofiObjBig', 157)))         # the Armoire

# -- Small icons and effects (8x8) -----------------------------------------------------------------------------------------------------------------------------
IC = 16
ic = add(Sheet('icons', 'Icons_8x8.png', 8, 8, IC, 0 + 12))
move(('invisible', 0), 'icons', 0)                     # cell 0 stays blank: "no picture" (the empty item slot, the Invisible projectile)
SLOT_ICONS = [('SwordType', 'lofiObj5', 48), ('HelmType', 'lofiObj6', 96), ('PlateType', 'lofiObj5', 32), ('RingType', 'lofiObj', 44),
              ('StaffType', 'lofiObj5', 112), ('SpellType', 'lofiObj6', 64), ('RobeType', 'lofiObj5', 16)]
slot_new = {}
for i, (t, s, ix) in enumerate(SLOT_ICONS):
    slot_new[t] = ic.put(0, 1 + i, old_cell(s, ix))
    move((s, ix), 'icons', slot_new[t])
for i, c in enumerate([('lofiObj', 246), ('lofiObj', 154), ('lofiObj2', 156)]):       # Blade, Fire Bolt, Grey Missile
    move(c, 'icons', ic.put(1, i, old_cell(*c)))
for i, c in enumerate([('lofiObj4', n) for n in (208, 209, 210, 211, 213, 214)]):      # the six loot bags
    move(c, 'icons', ic.put(2, i, old_cell(*c)))
move(('lofiInterface', 54), 'icons', ic.put(3, 0, old_cell('lofiInterface', 54)))       # the minimap's player marker
cond_src = read(os.path.join(CODE, 'Game', 'ConditionEffect.cs'))
cond_old = []
for m in re.finditer(r'new\("[^"]+",\s*ConditionEffect\.\w+,\s*\[([\d,\s]+)\]\)', cond_src):
    for n in re.findall(r'\d+', m.group(1)):
        if int(n) not in cond_old:
            cond_old.append(int(n))
cond_new = {}
for i, n in enumerate(cond_old):
    cond_new[n] = ic.put(4 + i // IC, i % IC, old_cell('lofiInterface2', n))
    move(('lofiInterface2', n), 'icons', cond_new[n])

# -- NPCs (animated 8x8): only the Pirate's row ---------------------------------------------------------------------------------------------------------------------
pf, pcw, pch, _ = old_meta['chars8x8rBeach']
pim = Image.open(os.path.join(OLD, pf)).convert('RGBA')
row_img = pim.crop((0, 1 * pch, pim.width, 2 * pch))
npc = add(Sheet('npcs', 'Npcs_8x8.png', 8, 8, pim.width // 8, 1, animated=True))
npc.img = row_img
move(('chars8x8rBeach', 1), 'npcs', 0)

KEEP = {'tileAlphaBlend': 'AlphaTileBlends.png', 'players': 'Players.png'}      # untouched


# ------------------------------------------------------------------------------------------------------------------------------------------------------------
# 3. rewrite the client XMLs
# ------------------------------------------------------------------------------------------------------------------------------------------------------------
def fmt_index(old_text, n):
    return ('0x%02x' % n) if old_text.strip().lower().startswith('0x') else str(n)


unresolved = []
rewritten = {}
for path in sorted(glob.glob(os.path.join(XMLS, '*.xml'))):
    t = read(path)

    def sub(m):
        sheet, ix = m.group(1).strip(), m.group(2)
        key = (sheet, int(ix.strip(), 0))
        if sheet in KEEP:
            return m.group(0)
        if key not in MAP:
            unresolved.append((os.path.basename(path), key))
            return m.group(0)
        ns, ni = MAP[key]
        return '<File>%s</File>\n            <Index>%s</Index>' % (ns, fmt_index(ix, ni)) if False else m.group(0).replace(sheet, ns, 1).replace('<Index>' + ix + '</Index>', '<Index>' + fmt_index(ix, ni) + '</Index>')

    t2 = PAIR.sub(sub, t)

    # decoration that moved to a shorter cell: keep the same pixel margin under the sprite (BottomInset is a FRACTION of the cell height)
    def fix_block(m):
        block = m.group(0)
        pm = PAIR.search(block)
        if not pm or pm.group(1).strip() not in INSET_H:
            return block
        h = INSET_H[pm.group(1).strip()]
        if h == 120:
            return block
        return re.sub(r'<BottomInset>([^<]+)</BottomInset>', lambda bm: '<BottomInset>%s</BottomInset>' % ('%.3f' % (float(bm.group(1)) * 120.0 / h)).rstrip('0').rstrip('.'), block)

    t2 = re.sub(r'    <Object type="[^"]*" id="[^"]*">.*?\n    </Object>', fix_block, t2, flags=re.S)
    if t2 != t:
        rewritten[path] = t2
if unresolved:
    print('UNRESOLVED references (no new home):')
    for u in unresolved:
        print('   ', u)
    raise SystemExit(1)

# ------------------------------------------------------------------------------------------------------------------------------------------------------------
# 4. code that names sheets directly
# ------------------------------------------------------------------------------------------------------------------------------------------------------------
code_edits = {}
p = os.path.join(CODE, 'Ui', 'ItemConstants.cs')
t = read(p)
a = t.index('    public static TextureInfo GetSlot(int slotType) {')
new_get = '''    // The faint picture drawn in an EMPTY equipment slot, by slot type. Only the kinds the two classes have (sword, helm, armor, staff, spell, robe, ring) exist;
    // everything else shows nothing.
    public static TextureInfo GetSlot(int slotType) {
        switch (slotType) {
%s        }

        return TextureHelper.FromGameAtlas(0x0096);
    }
}
''' % ''.join('            case %s:\n                return TextureHelper.FromGameAtlas("icons", %d);\n' % (t_, slot_new[t_]) for t_, _, _ in SLOT_ICONS)
code_edits[p] = t[:a] + new_get
p = os.path.join(CODE, 'Game', 'Components', 'Hud', 'Minimap.cs')
t = read(p)
assert 'FromGameAtlas("lofiInterface", 54, false)' in t
code_edits[p] = t.replace('FromGameAtlas("lofiInterface", 54, false)', 'FromGameAtlas("icons", %d, false)' % MAP[('lofiInterface', 54)][1])
p = os.path.join(CODE, 'Utils', 'Texture.cs')
t = read(p)
assert 'FromGameAtlas("invisible", 0)' in t
code_edits[p] = t.replace('FromGameAtlas("invisible", 0)', 'FromGameAtlas("icons", 0)')
p = os.path.join(CODE, 'Game', 'ConditionEffect.cs')
t = read(p)
t = re.sub(r'(new\("[^"]+",\s*ConditionEffect\.\w+,\s*\[)([\d,\s]+)(\]\))', lambda m: m.group(1) + ', '.join(str(cond_new[int(n)]) for n in re.findall(r'\d+', m.group(2))) + m.group(3), t)
assert '"lofiInterface2"' in t
code_edits[p] = t.replace('"lofiInterface2"', '"icons"')
DEAD = [os.path.join(CODE, 'Ui', 'Components', 'Buttons', 'MusicButton.cs'), os.path.join(CODE, 'Screens', 'Components', 'CaveBattle.cs')]

# ------------------------------------------------------------------------------------------------------------------------------------------------------------
# 5. Game.atlas
# ------------------------------------------------------------------------------------------------------------------------------------------------------------
lines = ['<Atlas source="Sheets">', '',
         '    <!-- Every sheet the game uses (rebuilt 2026-09-20 by Tools/Sheets/migrate_sheets.py; see Tools/Sheets/README.md). Each sheet is ONE uniform grid, split by',
         '         cell size. Add new sheets here as they are made. Decoration cells keep the 2:3 width:height ratio on purpose (the game scales a sprite from it). -->',
         '    <Image name="tileAlphaBlend"   w="8"  h="8" >AlphaTileBlends.png</Image>']
for s in NEW.values():
    if s.animated:
        continue
    lines.append('    <Image name="%-15s w="%-3d h="%-3d>%s</Image>' % (s.name + '"', s.cw, s.ch, s.file))
lines.append('    <Animated name="npcs"                    w="8"  h="8" >Npcs_8x8.png</Animated>')
lines.append('    <Animated name="players"                 w="32" h="32" group="Full">Players.png</Animated>')
lines.append('</Atlas>')
atlas_text = '\n'.join(lines) + '\n'

# ------------------------------------------------------------------------------------------------------------------------------------------------------------
# report / apply
# ------------------------------------------------------------------------------------------------------------------------------------------------------------
print('NEW SHEETS')
for s in NEW.values():
    print('  %-16s %-28s cell %3dx%-3d grid %2dx%-2d image %4dx%-4d  %d pictures' % (s.name, s.file, s.cw, s.ch, s.cols, s.rows, s.img.width, s.img.height, s.count))
print('cells moved:', len(MAP), '| XML files changed:', len(rewritten), '| code files changed:', len(code_edits), '| dead code deleted:', [os.path.basename(d) for d in DEAD])
if not APPLY:
    print('\n(dry run - nothing written; run with --apply)')
    raise SystemExit(0)

for s in NEW.values():
    s.img.save(os.path.join(SHEETS, s.file))
for path, t in rewritten.items():
    write(path, t)
for path, t in code_edits.items():
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    crlf = b'\r\n' in raw
    body = t.replace('\r\n', '\n')
    if bom and body.startswith('\xef\xbb\xbf'):
        body = body[3:]
    data = body.replace('\n', '\r\n') if crlf else body
    open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + data.encode('latin-1', 'ignore') if False else (b'\xef\xbb\xbf' if bom else b'') + data.encode('latin-1'))
for d in DEAD:
    if os.path.exists(d):
        os.remove(d)
raw = open(os.path.join(CONTENT, 'Game.atlas'), 'rb').read()
open(os.path.join(CONTENT, 'Game.atlas'), 'wb').write((b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'') + atlas_text.replace('\n', '\r\n' if b'\r\n' in raw else '\n').encode('utf-8'))
keep_files = {s.file for s in NEW.values()} | set(KEEP.values())
for f in glob.glob(os.path.join(SHEETS, '*.png')):
    if os.path.basename(f) not in keep_files:
        os.remove(f)
json.dump({'%s:%d' % k: '%s:%d' % v for k, v in MAP.items()}, open(os.path.join(HERE, 'migration_map_2026-09-20.json'), 'w'), indent=1)
print('applied.')
