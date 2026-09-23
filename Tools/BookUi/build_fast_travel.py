"""Art for the Character Book's FAST TRAVEL page:

    Content/Ui/BookGems/Maps/<World>.png   a small preview of each destination map (one pixel per tile, painted with the ground art's
                                           own average colour, objects a shade darker), fitted into PreviewSize x PreviewSize
    Content/Ui/BookGems/Icon/FastTravel.png  the page's 16x16 tab icon (a signpost), drawn here
    Content/Ui/BookGems/Tab7/{Idle,Active}.png  a seventh side tab: the pack has six, so the sixth tab's art is reused one slot lower
                                           (BookFrameData.g.cs carries its frame by hand)

Run from the repo root:  python Tools/BookUi/build_fast_travel.py
Reads the real map files (alloy-server/Common/Resources/World/Data/*.jm), Ground.xml and the ground sheets; never edits them.
"""
import base64
import json
import os
import shutil
import struct
import xml.etree.ElementTree as ET
import zlib

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
CONTENT = os.path.join(ROOT, 'AlloyClient', 'AlloyClient', 'Content')
MAPS = os.path.join(ROOT, 'alloy-server', 'Common', 'Resources', 'World', 'Data')
OUT = os.path.join(CONTENT, 'Ui', 'BookGems')
PreviewSize = 96

# destination -> map file (the Guild Hall shows its first map; the others have one map each)
WORLDS = {'Nexus': 'Nexus.jm', 'Vault': 'Vault.jm', 'GuildHall': 'Guild0.jm'}


def parse_int(t):
    t = (t or '0').strip()
    return int(t, 16) if t.lower().startswith('0x') else int(t)


def sheets():
    out = {}
    for e in ET.parse(os.path.join(CONTENT, 'Game.atlas')).getroot().iter():
        if e.tag in ('Image', 'Animated') and e.get('name'):
            out[e.get('name')] = (e.text.strip(), int(e.get('w')), int(e.get('h')))
    return out


_imgs = {}


def cell_colour(sheet_table, file, index):
    """Average colour of one cell of a ground sheet (opaque pixels only)."""
    png, w, h = sheet_table[file]
    if png not in _imgs:
        _imgs[png] = Image.open(os.path.join(CONTENT, 'Sheets', png)).convert('RGBA')
    img = _imgs[png]
    cols = img.width // w
    x, y = (index % cols) * w, (index // cols) * h
    cell = img.crop((x, y, x + w, y + h))
    r = g = b = n = 0
    for px in cell.getdata():
        if px[3] > 0:
            r += px[0]; g += px[1]; b += px[2]; n += 1
    return (r // n, g // n, b // n, 255) if n else (0, 0, 0, 0)


def ground_colours():
    table = sheets()
    colours = {}
    for ground in ET.parse(os.path.join(CONTENT, 'Xmls', 'Ground.xml')).getroot().iter('Ground'):
        tex = ground.find('Texture')
        if tex is None:
            rnd = ground.find('RandomTexture')
            tex = rnd.find('Texture') if rnd is not None else None
        if tex is None:
            continue
        colours[ground.get('id')] = cell_colour(table, tex.findtext('File').strip(), parse_int(tex.findtext('Index')))
    return colours


def load_map(name):
    j = json.load(open(os.path.join(MAPS, name), encoding='utf-8-sig'))
    w, h = j['width'], j['height']
    idx = struct.unpack('>%dh' % (w * h), zlib.decompress(base64.b64decode(j['data'])))
    return w, h, j['dict'], idx


def preview(name, colours):
    w, h, d, idx = load_map(name)
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    px = img.load()
    for y in range(h):
        for x in range(w):
            e = d[idx[y * w + x]]
            g = e.get('ground')
            if not g:
                continue
            c = colours.get(g, (90, 70, 50, 255))
            if e.get('objs'):
                c = (int(c[0] * 0.55), int(c[1] * 0.55), int(c[2] * 0.55), 255)
            px[x, y] = c
    scale = max(1, PreviewSize // max(w, h))
    img = img.resize((w * scale, h * scale), Image.NEAREST)
    canvas = Image.new('RGBA', (PreviewSize, PreviewSize), (0, 0, 0, 0))
    canvas.paste(img, ((PreviewSize - img.width) // 2, (PreviewSize - img.height) // 2))
    return canvas


def signpost_icon():
    """16x16: a wooden post with two arrow boards, in the same brown/cream palette as the other tab icons' outlines."""
    im = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    p = im.load()
    post, wood, edge, cream = (92, 58, 30, 255), (171, 118, 63, 255), (54, 33, 16, 255), (232, 214, 170, 255)
    for y in range(3, 16):
        p[7, y] = post; p[8, y] = post
    p[7, 15] = edge; p[8, 15] = edge
    # upper board pointing right
    for x in range(4, 13):
        for y in (3, 4, 5):
            p[x, y] = wood
    p[13, 4] = wood
    for x in range(4, 13):
        p[x, 2] = edge; p[x, 6] = edge
    p[13, 3] = edge; p[14, 4] = edge; p[13, 5] = edge; p[3, 3] = edge; p[3, 5] = edge; p[3, 4] = edge
    # lower board pointing left
    for x in range(3, 12):
        for y in (8, 9, 10):
            p[x, y] = wood
    p[2, 9] = wood
    for x in range(3, 12):
        p[x, 7] = edge; p[x, 11] = edge
    p[2, 8] = edge; p[1, 9] = edge; p[2, 10] = edge; p[12, 8] = edge; p[12, 10] = edge; p[12, 9] = edge
    # a cream letter mark on each board
    p[6, 4] = cream; p[7, 4] = cream; p[9, 4] = cream; p[10, 4] = cream
    p[5, 9] = cream; p[6, 9] = cream; p[8, 9] = cream; p[9, 9] = cream
    return im


def main():
    colours = ground_colours()
    os.makedirs(os.path.join(OUT, 'Maps'), exist_ok=True)
    for world, file in WORLDS.items():
        im = preview(file, colours)
        im.save(os.path.join(OUT, 'Maps', world + '.png'))
        print('preview', world, im.size)
    signpost_icon().save(os.path.join(OUT, 'Icon', 'FastTravel.png'))
    os.makedirs(os.path.join(OUT, 'Tab7'), exist_ok=True)
    for f in ('Idle.png', 'Active.png'):
        shutil.copyfile(os.path.join(OUT, 'Tab6', f), os.path.join(OUT, 'Tab7', f))
    print('icon + Tab7 written')


if __name__ == '__main__':
    main()
