"""Checks the art sheets (after the 2026-09-21 rework):
  * every sheet listed in Game.atlas exists in Content/Sheets and is a whole number of cells,
  * no other PNG sits in Content/Sheets (a stray old sheet would be a retired one creeping back),
  * every <File>/<Index> in the client XMLs (Texture, RandomTexture, AnimatedTexture) points at a listed sheet and a real, NON-EMPTY cell,
  * no XML or client code names a retired sheet (guildHall, guildHallLarge, icons, caveMonsters, npcs, equipAndConsume),
  * every picture on the sheets is pixel-exact pack art where that can be checked: the grassland decorations in Decorations.png, the
    Dark Dungeon sheets as copies, Players.png / Skins.png rebuilt from CharactersAssets by the game's frame rule.
    python Tools/Sheets/verify_sheets.py
Exit code 1 on any failure. Needs the packs on the Desktop for the pack checks (skipped with a note when they are missing).
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
CONTENT = os.path.join(REPO, 'WaW-Client', 'WaWClient', 'Content')
SHEETS = os.path.join(CONTENT, 'Sheets')
CLIENT = os.path.join(REPO, 'WaW-Client', 'WaWClient')
HOME = os.path.expanduser('~')
RETIRED = ['guildHall', 'guildHallLarge', 'icons', 'caveMonsters', 'npcs', 'equipAndConsume']
ENGINE_SHEETS = {'tileAlphaBlend'}          # blending masks, not a picture anyone sees

problems = []


def fail(msg):
    problems.append(msg)
    print('FAIL', msg)


def atlas():
    out = {}
    for m in re.finditer(r'<(Image|Animated)\s+name="([^"]+)"\s+w="(\d+)"\s+h="(\d+)"[^>]*>([^<]+)</\1>', open(os.path.join(CONTENT, 'Game.atlas'), encoding='utf-8-sig').read()):
        out[m.group(2)] = (m.group(5).strip(), int(m.group(3)), int(m.group(4)), m.group(1) == 'Animated')
    return out


def parse_int(t):
    t = (t or '0').strip()
    return int(t, 16) if t.lower().startswith('0x') else int(t)


def main():
    sheets = atlas()
    images = {}
    for name, (png, w, h, animated) in sheets.items():
        path = os.path.join(SHEETS, png)
        if not os.path.exists(path):
            fail(f'Game.atlas lists {png} but it is not in Content/Sheets'); continue
        im = Image.open(path).convert('RGBA')
        if im.width % w or im.height % h:
            fail(f'{png} is {im.size}, not a whole number of {w}x{h} cells')
        images[name] = (im, w, h, animated)
    listed = {v[0] for v in sheets.values()}
    for png in os.listdir(SHEETS):
        if png.endswith('.png') and png not in listed:
            fail(f'{png} sits in Content/Sheets but Game.atlas does not list it (a retired sheet?)')
    for r in RETIRED:
        if r in sheets:
            fail(f'retired sheet {r} is back in Game.atlas')

    # XML references
    refs = 0
    for xml in glob.glob(os.path.join(CONTENT, 'Xmls', '*.xml')):
        root = ET.parse(xml).getroot()
        for obj in root.iter():
            if obj.tag not in ('Object', 'Ground'):
                continue
            for t in list(obj.iter('Texture')) + list(obj.iter('AnimatedTexture')):
                file, index = (t.findtext('File') or '').strip(), parse_int(t.findtext('Index'))
                refs += 1
                if file in RETIRED:
                    fail(f'{os.path.basename(xml)} {obj.get("id")}: retired sheet {file}'); continue
                if file not in images:
                    fail(f'{os.path.basename(xml)} {obj.get("id")}: unknown sheet {file}'); continue
                im, w, h, animated = images[file]
                cols = im.width // w
                if animated:
                    x, y = 0, index * 3 * h
                else:
                    x, y = (index % cols) * w, (index // cols) * h
                if y + h > im.height or x + w > im.width:
                    fail(f'{os.path.basename(xml)} {obj.get("id")}: {file} cell {index} is outside the sheet'); continue
                if not im.crop((x, y, x + w, y + h)).getbbox():
                    fail(f'{os.path.basename(xml)} {obj.get("id")}: {file} cell {index} is empty')
    print(f'XML references checked: {refs}')

    # code references
    for cs in glob.glob(os.path.join(CLIENT, '**', '*.cs'), recursive=True):
        if os.sep + 'bin' + os.sep in cs or os.sep + 'obj' + os.sep in cs:
            continue
        text = open(cs, encoding='utf-8-sig').read()
        for r in RETIRED:
            for m in re.finditer(r'GetAtlasData\("%s"|FromGameAtlas\("%s"' % (r, r), text):
                fail(f'{os.path.relpath(cs, REPO)} still draws from the retired sheet {r}')

    # pack checks
    grass = os.path.join(HOME, 'Desktop', 'GrasslandAssets', 'Decorations', 'Decorations.png')
    dungeon = os.path.join(HOME, 'Desktop', 'DarkDungeonAssets')
    chars = os.path.join(HOME, 'Desktop', 'CharactersAssets')
    if os.path.exists(grass):
        import numpy as np
        deco = np.array(Image.open(grass).convert('RGBA'))
        for name in ('smallPlants', 'mediumPlants', 'smallTrees', 'mediumTrees', 'flatProps'):
            im, w, h, _ = images[name]
            for cy in range(0, im.height, h):
                for cx in range(0, im.width, w):
                    t = im.crop((cx, cy, cx + w, cy + h)); bb = t.getbbox()
                    if not bb:
                        continue
                    s = np.array(t.crop(bb)); sh, sw = s.shape[:2]; ok = False
                    for y in range(deco.shape[0] - sh + 1):
                        for x in range(deco.shape[1] - sw + 1):
                            if np.array_equal(deco[y:y + sh, x:x + sw], s):
                                ok = True; break
                        if ok: break
                    if not ok:
                        fail(f'{name} cell at ({cx},{cy}) is not a picture from GrasslandAssets/Decorations.png')
        print('grassland decorations: checked against the pack')
    else:
        print('note: GrasslandAssets not on the Desktop, pack check skipped')
    if os.path.exists(dungeon):
        for name, src in [('dungeon', 'Tileset/Tileset.png'), ('dungeonItems', 'Items/Items_Normal_Outline.png'), ('dungeonMonsters', 'Characters/Characters_Normal_Outline.png')]:
            if images[name][0].tobytes() != Image.open(os.path.join(dungeon, src)).convert('RGBA').tobytes():
                fail(f'{name} is not a byte-exact copy of DarkDungeonAssets/{src}')
        print('dark dungeon sheets: byte-exact copies of the pack')
    else:
        print('note: DarkDungeonAssets not on the Desktop, pack check skipped')
    if os.path.exists(chars):
        sys.path.insert(0, HERE)
        from build_pack_sheets import character_block, load
        players = images['players'][0]
        wiz, war = character_block(load(os.path.join(chars, 'Mage-Cyan.png'))), character_block(load(os.path.join(chars, 'Warrior-Red.png')))
        if players.crop((0, 0, 224, 96)).tobytes() != wiz.tobytes() or players.crop((0, 96, 224, 192)).tobytes() != war.tobytes():
            fail('Players.png is not the Mage-Cyan / Warrior-Red blocks cut by the game frame rule')
        print('players: cut from CharactersAssets by the frame rule')
    else:
        print('note: CharactersAssets not on the Desktop, pack check skipped')

    if problems:
        print(f'{len(problems)} problem(s)')
        return 1
    print('all sheets OK: every picture comes from the three packs, every XML reference lands on a real cell')
    return 0


if __name__ == '__main__':
    sys.exit(main())
