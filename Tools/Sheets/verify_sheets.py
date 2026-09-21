"""Checks a finished sheet migration against the archive: every (old sheet, old cell) in migration_map_*.json must appear at its new place with the same picture
(decor cells are compared by their visible art, because they were re-seated in a smaller cell; the doubled potion and the replaced helmet are expected exceptions).
Also checks that every <File>/<Index> in the client XMLs points at a real, non-empty cell of a sheet listed in Game.atlas.
    python Tools/Sheets/verify_sheets.py
"""
import glob
import json
import os
import re

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
CONTENT = os.path.join(REPO, 'AlloyClient', 'AlloyClient', 'Content')
OLD = os.path.join(HERE, 'source_old')


def meta(path):
    out = {}
    for m in re.finditer(r'<(Image|Animated)\s+name="([^"]+)"\s+w="(\d+)"\s+h="(\d+)"[^>]*>([^<]+)</\1>', open(path, encoding='utf-8-sig').read()):
        out[m.group(2)] = (m.group(5).strip(), int(m.group(3)), int(m.group(4)), m.group(1) == 'Animated')
    return out


old_meta, new_meta = meta(os.path.join(OLD, 'Game.atlas.old')), meta(os.path.join(CONTENT, 'Game.atlas'))
cache = {}


def cell(base, meta_, sheet, idx):
    f, cw, ch, _ = meta_[sheet]
    if (base, f) not in cache:
        cache[(base, f)] = Image.open(os.path.join(base, f)).convert('RGBA')
    im = cache[(base, f)]
    cols = im.width // cw
    x, y = (idx % cols) * cw, (idx // cols) * ch
    return im.crop((x, y, x + cw, y + ch))


bad = 0
for k, v in json.load(open(os.path.join(HERE, 'migration_map_2026-09-20.json'))).items():
    os_, oi = k.split(':')
    ns, ni = v.split(':')
    a = cell(OLD, old_meta, os_, int(oi))
    b = cell(os.path.join(CONTENT, 'Sheets'), new_meta, ns, int(ni))
    if a.size == b.size:
        same = list(a.getdata()) == list(b.getdata())
    else:
        ba, bb = a.getbbox(), b.getbbox()
        if ba is None or bb is None:
            same = ba == bb
        else:
            same = list(a.crop(ba).getdata()) == list(b.crop(bb).getdata()) if a.crop(ba).size == b.crop(bb).size else False
        if os_ == 'lofiObj2' and oi == '50' or os_ == 'lofiObj4' and False:
            same = True                        # the doubled Health Potion
    if not same and not (os_ == 'weapons' and oi == '10'):          # weapons:10 = the Broken Helmet, replaced on purpose by item209.png
        bad += 1
        print('DIFFERENT', k, '->', v)

empty = 0
for path in glob.glob(os.path.join(CONTENT, 'Xmls', '*.xml')):
    t = open(path, encoding='utf-8-sig', errors='replace').read()
    for m in re.finditer(r'<File>([^<]+)</File>\s*<Index>([^<]+)</Index>', t):
        sheet, idx = m.group(1).strip(), int(m.group(2).strip(), 0)
        if sheet not in new_meta:
            print('UNKNOWN SHEET', os.path.basename(path), sheet)
            bad += 1
            continue
        if new_meta[sheet][3]:
            continue
        if cell(os.path.join(CONTENT, 'Sheets'), new_meta, sheet, idx).getbbox() is None:
            print('EMPTY CELL', os.path.basename(path), sheet, idx)
            empty += 1
print('problems:', bad, ' empty cells referenced:', empty)
