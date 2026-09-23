"""The 2026-09-21 asset rework: EVERY picture the game draws comes from three bought packs by one artist -
GrasslandAssets, DarkDungeonAssets and CharactersAssets (Desktop) - and nothing from the original source is left in Content/Sheets.

What it does (re-runnable; the packs are read, never written):
  1. Writes the new sheets into WaW-Client/WaWClient/Content/Sheets:
       Dungeon_16x16.png          the Dark Dungeon tileset as is (13 columns)
       DungeonDecor_16x16.png     first frames of the animated decorations: torch pole, wall torch, side torch, chest closed, chest open, spikes
       DungeonItems_16x16.png     the Dark Dungeon item sheet as is (5 columns)
       DungeonMonsters_16x16.png  the Dark Dungeon monsters as is (3 columns): skeleton, knight, goblin, slime, wraith, rat
       HudIcons_16x16.png         engine glyphs (blank, minimap arrow, dot, burst - drawn here) + a few pack pictures the cave battle uses as effects
       LargeObjects_80x120.png    Bug Board + Jukebox (our own art) and the two Vault chests (the bought Treasure Chests pack) - all four cells copied
                                  from source_old/ForestProps.png as they were (2026-09-22: the chests were pack art all along, not placeholders)
       Skins.png                  every CharactersAssets character in the game's 7x3 "Full" layout (the future wardrobe; Players.png stays)
     The grassland sheets (Grasslands_16x16, SmallPlants, MediumPlants, SmallTrees, MediumTrees, FlatProps) are already pure GrasslandAssets art and stay.
  2. Moves the retired sheets (GuildHall, GuildHallLarge, Icons, CaveMonsters, Npcs, EquipAndConsume) to Tools/Sheets/source_old.
  3. Rewrites Game.atlas and every <File>/<Index> in Content/Xmls that pointed at a retired sheet (see MAP below), halving <Size> where the
     art moved from an 8x8 cell to a 16x16 one so nothing changes size on screen.
  4. Writes WaW-Client/WaWClient/Game/Components/Admin/ArtPlaceholders.g.cs: the objects whose new picture is only a stand-in, so the admin
     dashboard keeps showing them as PLACEHOLDER (they still need their own art one day).
  5. (since 2026-09-22) Points every placeholder OBJECT (not the grounds: a floor made of potions is no floor) at ONE common stand-in picture,
     PLACEHOLDER_PICTURE = the Health Potion, so what still needs art is obvious in the game and no random pack picture is mistaken for final art.

Run from the repo root:  python Tools/Sheets/build_pack_sheets.py
Then: python Tools/Sheets/verify_sheets.py ; python Tools/Editor/build_palette.py ; build the client (grep the output for "Failed to add").
"""
import os
import re
import shutil
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
CONTENT = os.path.join(ROOT, 'WaW-Client', 'WaWClient', 'Content')
SHEETS = os.path.join(CONTENT, 'Sheets')
XMLS = os.path.join(CONTENT, 'Xmls')
OLD = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'source_old')
HOME = os.path.expanduser('~')
GRASS = os.path.join(HOME, 'Desktop', 'GrasslandAssets')
DUNGEON = os.path.join(HOME, 'Desktop', 'DarkDungeonAssets')
CHARS = os.path.join(HOME, 'Desktop', 'CharactersAssets')
PLACEHOLDERS_CS = os.path.join(ROOT, 'WaW-Client', 'WaWClient', 'Game', 'Components', 'Admin', 'ArtPlaceholders.g.cs')

RETIRED = {'guildHall': 'GuildHall_8x8.png', 'guildHallLarge': 'GuildHallLarge_16x16.png', 'icons': 'Icons_8x8.png',
           'caveMonsters': 'CaveMonsters_16x16.png', 'npcs': 'Npcs_8x8.png', 'equipAndConsume': 'EquipAndConsume_16x16.png'}
RETIRED_CELL = {'guildHall': 8, 'guildHallLarge': 16, 'icons': 8, 'caveMonsters': 16, 'npcs': 8, 'equipAndConsume': 16}

# ---- where every retired picture goes: object id -> (new sheet, new index). Grounds by id too. -------------------------------------------
# Dark Dungeon tileset cells: 0/1/13/14 dark stone floors, 5/18 brick wall, 11 wall with window, 19 column, 39 black, 45 altar, 52 barrel,
# 53 chest, 54 crate, 62 open door, 63 door, 64 rack. Decor: 0 torch pole, 1 wall torch, 2 side torch, 3 chest closed, 4 chest open, 5 spikes.
# Items: 0 silver ring, 2 arrows, 3 scroll, 4 apple, 8 red book, 9 coins, 11 helmet, 13 dagger, 14 blue potion, 15 leather armor, 16 plate,
# 18 sword, 19 red potion, 21 gem staff, 22 fire staff, 23 golden sword, 24 dark wand. Monsters: 0 skeleton, 1 knight, 2 goblin, 3 slime, 4 wraith, 5 rat.
GRASS_GRASS, GRASS_DIRT, GRASS_WATER = ('grasslands', 0x00), ('grasslands', 0x08), ('grasslands', 0x10)
MAP = {
    # ---- items (Equip.xml): the starter set + the potion on the small Dark Dungeon item sheet (the tiered Iron..Gold weapons were removed on
    # 2026-09-21 until there is a picture for each tier). The user's picks: sword 0x12, staff 0x16, helmet 0x0a, spell 0x08, armour 0x0f, ring 0x05.
    'Old Sword': ('dungeonItems', 18), 'Old Staff': ('dungeonItems', 22),
    'Old Spell': ('dungeonItems', 8), 'Old Helmet': ('dungeonItems', 10), 'Health Potion': ('dungeonItems', 19),
    'Old Armor': ('dungeonItems', 15), 'Old Robe': ('dungeonItems', 15), 'Old Ring': ('dungeonItems', 5),      # the robe shares the armour's picture on purpose (the user's pick, not a placeholder)
    # ---- guild hall grounds (Ground.xml)
    'Black Water': GRASS_WATER, 'Wood Panel Floor': ('dungeon', 1), 'Wood Plank Floor': ('dungeon', 0), 'Tutorial Floor': GRASS_DIRT,
    'Guild Rug Green': GRASS_GRASS, 'Wood Plank Floor Dark': ('dungeon', 13), 'Grass': GRASS_GRASS, 'Rock': ('dungeon', 14),
    'White Floor 1': GRASS_GRASS, 'White Floor 2': GRASS_GRASS, 'White Floor 3': GRASS_GRASS, 'White Floor 4': GRASS_GRASS, 'White Floor 5': GRASS_GRASS,
    'Pool': GRASS_WATER, 'Deep Pool': GRASS_WATER, 'Space': ('dungeon', 39), 'Black': ('dungeon', 39), 'Empty': ('dungeon', 39),
    # ---- NPCs / dummies
    'DpsDummy0def': ('dungeonMonsters', 1), 'DpsDummy40def': ('dungeonMonsters', 1), 'DpsDummy100def': ('dungeonMonsters', 1),
    'Pirate': ('dungeonMonsters', 2),
    # ---- portals and Nexus fixtures (Objects.xml)
    'Portal to Nexus': ('dungeonDecor', 0), 'Vault Portal': ('dungeonDecor', 3), 'Realm Portal': ('dungeonDecor', 0), 'Guild Hall Portal': ('dungeonDecor', 1),
    'White Fountain': ('dungeon', 45), 'Guild Hall Upgrade 1': ('dungeonDecor', 4), 'Guild Hall Upgrade 2': ('dungeonDecor', 4), 'Guild Hall Upgrade 3': ('dungeonDecor', 4),
    # ---- guild hall furniture (StaticObjects.xml)
    'Red Pillar': ('dungeon', 19), 'Guild Register': ('dungeonItems', 3), 'Weapon Rack': ('dungeon', 64), 'Wood Panel Banner': ('dungeonDecor', 1),
    'Wood Panel Window': ('dungeon', 11), 'Table': ('dungeon', 54), 'Wood Panel Wall': ('dungeon', 18), 'Guild Chronicle': ('dungeonItems', 8),
    'Guild Board': ('dungeon', 63), 'Target Strong': ('dungeonMonsters', 3), 'Dummy Strong': ('dungeonMonsters', 1),
    'Table Edge UL': ('dungeon', 54), 'Table Edge UR': ('dungeon', 54), 'Table Edge LR': ('dungeon', 54), 'Table Edge LL': ('dungeon', 54),
    'Armoire': ('dungeon', 62),
    # ---- containers and projectiles
    'Loot Bag 0': ('dungeonItems', 9), 'Loot Bag 1': ('dungeonItems', 9), 'Loot Bag 2': ('dungeonItems', 9), 'Loot Bag 3': ('dungeonItems', 9),
    'Loot Bag 4': ('dungeonItems', 9), 'Loot Bag 5': ('dungeonItems', 9),
    'Blade': ('hudIcons', 5), 'Grey Missile': ('hudIcons', 5), 'Fire Bolt': ('hudIcons', 2), 'Invisible': ('hudIcons', 0),
}

# Objects whose new picture is only a stand-in (still marked so in the admin dashboard). Real art from the packs: the grassland forest, the class
# sprites, ALL the starter items (the Old Robe shares the armour's picture by the user's choice - it is not a placeholder), and the two Vault
# chests (the bought Treasure Chests pack on largeObjects cells 2 and 3 - wrongly listed here until 2026-09-22).
PLACEHOLDERS = sorted(set(MAP) - {'Old Sword', 'Old Staff', 'Old Helmet', 'Old Spell', 'Old Armor', 'Old Robe', 'Old Ring', 'Health Potion', 'Invisible'})

# The one picture every placeholder object shows (2026-09-22, the user's call): the Health Potion (dungeonItems cell 19). Grounds keep their
# stand-in floor / water pictures (they are in PLACEHOLDERS too, so the dashboard still flags them). Remove an object from PLACEHOLDERS (and give it
# its own picture in the XML) once it has real art.
PLACEHOLDER_PICTURE = ('dungeonItems', 19)

NEW_ATLAS_LINES = [
    '    <Image name="dungeon" w="16" h="16">Dungeon_16x16.png</Image>',
    '    <Image name="dungeonDecor" w="16" h="16">DungeonDecor_16x16.png</Image>',
    '    <Image name="dungeonItems" w="16" h="16">DungeonItems_16x16.png</Image>',
    '    <Image name="dungeonMonsters" w="16" h="16">DungeonMonsters_16x16.png</Image>',
    '    <Image name="hudIcons" w="16" h="16">HudIcons_16x16.png</Image>',
    '    <Animated name="skins" w="32" h="32" group="Full">Skins.png</Animated>',
]


def load(path):
    return Image.open(path).convert('RGBA')


def cell(img, index, cw=16, ch=16):
    cols = img.width // cw
    x, y = (index % cols) * cw, (index // cols) * ch
    return img.crop((x, y, x + cw, y + ch))


# ---- 1. sheets --------------------------------------------------------------------------------------------------------------------------------
def build_sheets():
    tiles = load(os.path.join(DUNGEON, 'Tileset', 'Tileset.png'))
    anim = load(os.path.join(DUNGEON, 'Tileset', 'Animated_Decorations_sheet.png'))
    items = load(os.path.join(DUNGEON, 'Items', 'Items_Normal_Outline.png'))
    monsters = load(os.path.join(DUNGEON, 'Characters', 'Characters_Normal_Outline.png'))
    assert tiles.size == (208, 80) and anim.size == (64, 80) and items.size == (80, 80) and monsters.size == (48, 32), 'unexpected pack sheet sizes'

    tiles.save(os.path.join(SHEETS, 'Dungeon_16x16.png'))
    items.save(os.path.join(SHEETS, 'DungeonItems_16x16.png'))
    monsters.save(os.path.join(SHEETS, 'DungeonMonsters_16x16.png'))

    decor = Image.new('RGBA', (16 * 6, 16), (0, 0, 0, 0))
    for i, src in enumerate([0, 4, 8, 12, 15, 16]):          # torch pole, wall torch, side torch, chest closed, chest open, spikes
        decor.paste(cell(anim, src), (i * 16, 0))
    decor.save(os.path.join(SHEETS, 'DungeonDecor_16x16.png'))

    # HUD glyphs + effect stand-ins: 0 blank, 1 minimap arrow, 2 flame (the torch's own flame), 3 dot, 4 burst, 5 arrows, 6 sword, 7 blue potion,
    # 8 red potion, 9 apple
    hud = Image.new('RGBA', (16 * 10, 16), (0, 0, 0, 0))
    d = ImageDraw.Draw(hud)
    hud.putpixel((0, 0), (0, 0, 0, 1))          # cell 0 = "no picture": one all-but-invisible pixel so the atlas builder keeps the cell
    d.polygon([(16 + 8, 1), (16 + 13, 14), (16 + 8, 11), (16 + 3, 14)], fill=(255, 255, 255, 255))                     # arrow
    torch = cell(anim, 0)
    flame = torch.crop((0, 0, 16, 9))
    hud.paste(flame, (32, 4), flame)
    d.ellipse([48 + 5, 5, 48 + 10, 10], fill=(255, 255, 255, 255))                                                        # dot
    for a, b in [((64 + 2, 2), (64 + 13, 13)), ((64 + 13, 2), (64 + 2, 13))]:                                             # burst
        d.line([a, b], fill=(255, 190, 90, 255), width=2)
    for i, src in enumerate([2, 18, 14, 19, 4]):
        hud.paste(cell(items, src), (80 + i * 16, 0))
    hud.save(os.path.join(SHEETS, 'HudIcons_16x16.png'))

    # Large objects (80x120 cells, art standing on row 117): all four cells of source_old/ForestProps.png as the old Tools/_retired/ForestContent/draw_props.py
    # built them. Cells 0 and 1 are the Bug Board and the Jukebox, OUR OWN pixel art; cells 2 and 3 are the open Vault Chest and the padlocked Closed
    # Vault Chest from the bought "Treasure Chests" pack (Desktop/Extra Assets/Treasure Chests, style 6, sources in Tools/ForestContent/source/vault).
    # Until 2026-09-22 this pasted the Dark Dungeon chest over cells 2 and 3 by mistake (the user had bought that pack but not mentioned it).
    old_props = load(os.path.join(OLD, 'ForestProps.png'))
    assert old_props.size == (320, 120), 'source_old/ForestProps.png must be the 4-cell 80x120 sheet'
    large = old_props.copy()
    large.save(os.path.join(SHEETS, 'LargeObjects_80x120.png'))

    build_skins()


# The game's character block: 7 columns x 3 rows of 32x32 (row 0 facing down, 1 side, 2 up); columns 0-3 the walk, 4 = 0 again, 5-6 one 64-wide
# attack frame. Measured against Players.png (Mage-Cyan / Warrior-Red): game row -> pack row {0: 2, 1: 0, 2: 3}, walk = pack columns 0-3,
# attack = pack column 8 pasted 16 px in (centred in the double cell).
PACK_ROW = {0: 2, 1: 0, 2: 3}


def character_block(sheet):
    block = Image.new('RGBA', (224, 96), (0, 0, 0, 0))
    for g in range(3):
        r = PACK_ROW[g]
        for c in range(4):
            block.paste(cell(sheet, r * 24 + c, 32, 32), (c * 32, g * 32))
        block.paste(cell(sheet, r * 24 + 0, 32, 32), (4 * 32, g * 32))
        block.paste(cell(sheet, r * 24 + 8, 32, 32), (5 * 32 + 16, g * 32))
    return block


def build_skins():
    names = sorted(f for f in os.listdir(CHARS) if f.endswith('.png') and f != 'Slime.png')
    sheets = [(n, load(os.path.join(CHARS, n))) for n in names]
    sheets = [(n, s) for n, s in sheets if s.size == (768, 256)]
    skins = Image.new('RGBA', (224, 96 * len(sheets)), (0, 0, 0, 0))
    for i, (n, s) in enumerate(sheets):
        skins.paste(character_block(s), (0, i * 96))
    skins.save(os.path.join(SHEETS, 'Skins.png'))
    # sanity: the rule reproduces the shipped Players.png exactly
    players = load(os.path.join(SHEETS, 'Players.png'))
    wiz = character_block(load(os.path.join(CHARS, 'Mage-Cyan.png')))
    war = character_block(load(os.path.join(CHARS, 'Warrior-Red.png')))
    same = players.crop((0, 0, 224, 96)).tobytes() == wiz.tobytes() and players.crop((0, 96, 224, 192)).tobytes() == war.tobytes()
    print('Skins.png:', len(sheets), 'characters (index = row block):', ', '.join(n[:-4] for n, _ in sheets))
    print('Players.png reproduced from the pack by the same rule:', same)
    with open(os.path.join(SHEETS, 'Skins.txt'), 'w', encoding='utf-8') as f:
        f.write('skins sheet index -> CharactersAssets file (Animated, group Full, 32x32)\n')
        for i, (n, _) in enumerate(sheets):
            f.write(f'{i}\t{n[:-4]}\n')


# ---- 2. retire the old sheets ------------------------------------------------------------------------------------------------------------------
def retire_sheets():
    os.makedirs(OLD, exist_ok=True)
    for png in RETIRED.values():
        src = os.path.join(SHEETS, png)
        if os.path.exists(src):
            shutil.move(src, os.path.join(OLD, png))
            print('retired', png)


# ---- 3. Game.atlas + XMLs ----------------------------------------------------------------------------------------------------------------------
def rewrite_atlas():
    path = os.path.join(CONTENT, 'Game.atlas')
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig').replace('\r\n', '\n')
    lines = [l for l in text.split('\n') if not any(f'name="{n}"' in l for n in RETIRED) and not any(f'name="{n.split(chr(34))[1]}"' in l for n in NEW_ATLAS_LINES)]
    out = []
    for l in lines:
        if l.strip() == '</Atlas>':
            out.extend(NEW_ATLAS_LINES)
        out.append(l)
    open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + '\n'.join(out).encode('utf-8'))
    print('Game.atlas rewritten')


TEX_BLOCK = re.compile(r'<Texture>\s*<File>(\w+)</File>\s*<Index>([^<]+)</Index>\s*</Texture>', re.S)


def rewrite_xmls():
    changed = 0
    for name in sorted(os.listdir(XMLS)):
        if not name.endswith('.xml'):
            continue
        path = os.path.join(XMLS, name)
        raw = open(path, 'rb').read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig')
        crlf = '\r\n' in text
        text = text.replace('\r\n', '\n')
        # object by object: <Object ...> or <Ground ...> up to its closing tag
        def fix(m):
            nonlocal changed
            head, body, tail = m.group(1), m.group(2), m.group(3)
            oid = re.search(r'id="([^"]+)"', head).group(1)
            refs = TEX_BLOCK.findall(body)
            old_sheets = {s for s, _ in refs} | ({re.search(r'<AnimatedTexture>\s*<File>(\w+)</File>', body).group(1)} if '<AnimatedTexture>' in body else set())
            if not (old_sheets & set(RETIRED)):
                return m.group(0)
            if oid not in MAP:
                raise SystemExit(f'{name}: {oid} uses a retired sheet {old_sheets} but has no entry in MAP')
            sheet, index = MAP[oid]
            old_cell = max(RETIRED_CELL[s] for s in old_sheets & set(RETIRED))
            new_tex = f'<Texture>\n            <File>{sheet}</File>\n            <Index>0x{index:02x}</Index>\n        </Texture>'
            # drop every old texture (RandomTexture groups, AnimatedTexture) and put ONE texture where the first one was
            body2 = re.sub(r'\s*<RandomTexture>.*?</RandomTexture>', '', body, flags=re.S)
            body2 = re.sub(r'\s*<AnimatedTexture>.*?</AnimatedTexture>', '', body2, flags=re.S)
            body2 = TEX_BLOCK.sub('', body2)
            body2 = re.sub(r'\n\s*\n', '\n', body2)
            # insert after <Class>...</Class> if present, else at the start
            if '</Class>' in body2:
                body2 = body2.replace('</Class>', '</Class>\n        ' + new_tex, 1)
            else:
                body2 = '\n        ' + new_tex + body2
            # size: art that lived on an 8x8 cell and now sits on a 16x16 one draws twice as big unless Size halves
            if old_cell == 8 and head.startswith('<Object'):
                sm = re.search(r'<Size>(\d+)</Size>', body2)
                if sm:
                    body2 = body2.replace(sm.group(0), f'<Size>{max(1, int(sm.group(1)) // 2)}</Size>', 1)
                else:
                    body2 = body2.replace('</Texture>', '</Texture>\n        <Size>50</Size>', 1)
            changed += 1
            return head + body2 + tail
        text = re.sub(r'(<(?:Object|Ground)\b[^>]*>)(.*?)(</(?:Object|Ground)>)', fix, text, flags=re.S)
        if crlf:
            text = text.replace('\n', '\r\n')
        open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))
    print('XML objects re-pointed:', changed)


# ---- 3b. one common picture for every placeholder object ----------------------------------------------------------------------------------------
def apply_placeholder_picture():
    """Every <Object> in PLACEHOLDERS draws PLACEHOLDER_PICTURE: its first <Texture> is re-pointed, RandomTexture / AnimatedTexture groups are dropped.
    <Ground> entries are left alone. Sizes stay as they are (every placeholder object sits on a 16x16 cell already)."""
    sheet, index = PLACEHOLDER_PICTURE
    new_tex = f'<Texture>\n            <File>{sheet}</File>\n            <Index>0x{index:02x}</Index>\n        </Texture>'
    changed, seen = 0, set()
    for name in sorted(os.listdir(XMLS)):
        if not name.endswith('.xml'):
            continue
        path = os.path.join(XMLS, name)
        raw = open(path, 'rb').read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig')
        crlf = '\r\n' in text
        text = text.replace('\r\n', '\n')

        def fix(m):
            nonlocal changed
            head, body, tail = m.group(1), m.group(2), m.group(3)
            oid = re.search(r'id="([^"]+)"', head).group(1)
            if oid not in PLACEHOLDERS:
                return m.group(0)
            seen.add(oid)
            if TEX_BLOCK.findall(body) == [(sheet, f'0x{index:02x}')] and '<RandomTexture>' not in body and '<AnimatedTexture>' not in body:
                return m.group(0)
            body2 = re.sub(r'\s*<RandomTexture>.*?</RandomTexture>', '', body, flags=re.S)
            body2 = re.sub(r'\s*<AnimatedTexture>.*?</AnimatedTexture>', '', body2, flags=re.S)
            first = TEX_BLOCK.search(body2)
            if first:
                body2 = body2[:first.start()] + new_tex + TEX_BLOCK.sub('', body2[first.end():])
                body2 = re.sub(r'\n\s*\n', '\n', body2)
            elif '</Class>' in body2:
                body2 = body2.replace('</Class>', '</Class>\n        ' + new_tex, 1)
            else:
                body2 = '\n        ' + new_tex + body2
            changed += 1
            return head + body2 + tail
        text2 = re.sub(r'(<Object\b[^>]*>)(.*?)(</Object>)', fix, text, flags=re.S)
        if text2 != text:
            if crlf:
                text2 = text2.replace('\n', '\r\n')
            open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + text2.encode('utf-8'))
    print('placeholder objects pointed at', PLACEHOLDER_PICTURE, ':', changed, 'changed,', len(seen), 'found')


# ---- 4. the placeholder list for the admin dashboard ------------------------------------------------------------------------------------------
def write_placeholders():
    lines = ['// Generated by Tools/Sheets/build_pack_sheets.py - do not edit. Objects whose picture is a stand-in from the packs (see the tool\'s MAP):',
             '// the admin dashboard shows them as PLACEHOLDER until they get art of their own.',
             'namespace WaWClient.Game.Components.Admin;', '', 'public static class ArtPlaceholders {', '    public static readonly string[] Names = [']
    lines += [f'        "{n}",' for n in PLACEHOLDERS]
    lines += ['    ];', '}', '']
    open(PLACEHOLDERS_CS, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))
    print('placeholders:', len(PLACEHOLDERS))


if __name__ == '__main__':
    build_sheets()
    retire_sheets()
    rewrite_atlas()
    rewrite_xmls()
    apply_placeholder_picture()
    write_placeholders()
    print('done - now: verify_sheets.py, Tools/Editor/build_palette.py, build the client')
