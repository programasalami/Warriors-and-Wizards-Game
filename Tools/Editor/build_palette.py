"""Builds Tools/Editor/palette.js - everything the map editor and the item maker need to know about the game's content:

  * every ground tile, object / entity and item in the client XMLs, with the picture (sheet + pixel rectangle) it is drawn with
  * the atlas sheets (cell size, image size) so the maker can show a picker
  * the region names a map can use (from Common/Resources/World/MapData.cs)
  * the type ids already taken, and the next free one per kind (so a new item never collides with an existing type)

Run it again whenever the XMLs, the atlas or the region list change (deploy / promote do not need it: the editor is a developer tool).
    python Tools/Editor/build_palette.py
"""
import glob
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
CONTENT = os.path.join(REPO, 'WaW-Client', 'WaWClient', 'Content')
XMLS = os.path.join(CONTENT, 'Xmls')
MAPDATA = os.path.join(REPO, 'WaW-Server', 'Common', 'Resources', 'World', 'MapData.cs')
OUT = os.path.join(HERE, 'palette.js')
SHEET_URL = '../../WaW-Client/WaWClient/Content/Sheets/'


def read(path):
    with open(path, encoding='utf-8-sig', errors='replace') as f:
        return f.read()


def png_size(path):
    with open(path, 'rb') as f:
        head = f.read(24)
    if head[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError(path + ' is not a PNG')
    return int.from_bytes(head[16:20], 'big'), int.from_bytes(head[20:24], 'big')


def parse_atlas():
    """name -> {file, w, h (cell size), sheetW, sheetH, animated}"""
    sheets = {}
    text = read(os.path.join(CONTENT, 'Game.atlas'))
    for m in re.finditer(r'<(Image|Animated)\s+name="([^"]+)"\s+w="(\d+)"\s+h="(\d+)"[^>]*>([^<]+)</\1>', text):
        kind, name, w, h, file = m.groups()
        path = os.path.join(CONTENT, 'Sheets', file.strip())
        if not os.path.exists(path):
            continue
        sw, sh = png_size(path)
        sheets[name] = {'file': SHEET_URL + file.strip(), 'w': int(w), 'h': int(h), 'sheetW': sw, 'sheetH': sh, 'animated': kind == 'Animated'}
    return sheets


def cell_rect(sheets, name, index):
    s = sheets.get(name)
    if s is None or s['animated']:
        return None
    cols = max(1, s['sheetW'] // s['w'])
    x = (index % cols) * s['w']
    y = (index // cols) * s['h']
    if y + s['h'] > s['sheetH']:
        return None
    return {'sheet': name, 'x': x, 'y': y, 'w': s['w'], 'h': s['h']}


def first_texture(block, sheets):
    """The first <File>/<Index> pair inside a definition (random / top textures use their first)."""
    m = re.search(r'<File>([^<]+)</File>\s*<Index>([^<]+)</Index>', block)
    if not m:
        return None
    name = m.group(1).strip()
    try:
        index = int(m.group(2).strip(), 0)
    except ValueError:
        return None
    return cell_rect(sheets, name, index)


def definitions(kind_tag):
    """Yield (file, type, id, block) for every <Object ...> / <Ground ...> in the client XMLs."""
    for path in sorted(glob.glob(os.path.join(XMLS, '*.xml'))):
        text = read(path)
        for m in re.finditer(r'<%s\s+type="([^"]+)"\s+id="([^"]*)"\s*>(.*?)</%s>' % (kind_tag, kind_tag), text, re.S):
            yield os.path.basename(path), int(m.group(1), 0), m.group(2), m.group(3)


def tag(block, name, default=None):
    m = re.search(r'<%s(?:\s[^>]*)?>([^<]*)</%s>' % (name, name), block)
    return m.group(1).strip() if m else default


def parse_regions():
    text = read(MAPDATA)
    m = re.search(r'enum TileRegion\s*\{(.*?)\}', text, re.S)
    regions = []
    for line in m.group(1).splitlines():
        line = line.split('//')[0].strip().rstrip(',')
        if not line or line.startswith('None'):
            continue
        name, _, value = line.partition('=')
        regions.append({'name': name.strip().replace('_', ' '), 'value': int(value.strip(), 0) if value.strip() else 0})
    return regions


def main():
    sheets = parse_atlas()

    tiles = []
    for file, t, ident, block in definitions('Ground'):
        tiles.append({'id': ident, 'type': t, 'file': file, 'walk': '<NoWalk' not in block, 'pic': first_texture(block, sheets)})

    objects, items = [], []
    projectiles = []
    for file, t, ident, block in definitions('Object'):
        entry = {'id': ident, 'type': t, 'file': file, 'cls': tag(block, 'Class', ''), 'pic': first_texture(block, sheets)}
        if '<Item' in block and re.search(r'<Item\s*/>', block):
            entry.update({'tier': int(tag(block, 'Tier', '-1')), 'slot': int(tag(block, 'SlotType', '0')), 'desc': tag(block, 'Description', ''),
                          'min': int(tag(block, 'MinDamage', '0')), 'max': int(tag(block, 'MaxDamage', '0'))})
            items.append(entry)
        elif file == 'Projectiles.xml':
            projectiles.append(entry['id'])
        else:
            entry.update({'enemy': '<Enemy' in block, 'static': '<Static' in block, 'occupy': '<OccupySquare' in block})
            objects.append(entry)

    used = {'ground': sorted(t['type'] for t in tiles), 'object': sorted(o['type'] for o in objects + items), 'all': sorted(o['type'] for o in objects + items)}

    def next_free(values, start):
        n = start
        taken = set(values)
        while n in taken:
            n += 1
        return n

    palette = {
        'sheets': sheets,
        'tiles': sorted(tiles, key=lambda t: t['id'].lower()),
        'objects': sorted(objects, key=lambda o: o['id'].lower()),
        'items': sorted(items, key=lambda i: (i['tier'], i['id'].lower())),
        'projectiles': sorted(projectiles),
        'regions': parse_regions(),
        'usedTypes': used['all'],
        'nextItemType': next_free(used['all'], 0xa00),
        'nextObjectType': next_free(used['all'], 0x9f00),
    }

    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('// GENERATED by Tools/Editor/build_palette.py - do not edit by hand.\n')
        f.write('window.PALETTE = ' + json.dumps(palette, separators=(',', ':')) + ';\n')

    print('palette.js: %d tiles, %d objects, %d items, %d projectiles, %d regions, %d sheets' % (
        len(tiles), len(objects), len(items), len(projectiles), len(palette['regions']), len(sheets)))
    return palette


if __name__ == '__main__':
    main()
    sys.exit(0)
