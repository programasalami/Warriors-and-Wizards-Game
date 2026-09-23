# OBSOLETE since 2026-09-20: the game's art sheets are now edited directly (see Tools/Sheets/README.md), so this script would recreate sheets that no longer
# exist. Kept as the record of how the old art was made / where it came from. Run with --force only if you know why.
import sys
if '--force' not in sys.argv:
    raise SystemExit('obsolete: see Tools/Sheets/README.md (run with --force to run it anyway)')
"""Cuts the first four weapon tiers out of the four metal sheets (Tools/Weapons/source, 16x16 cells, 24 columns) into
WaW-Client/WaWClient/Content/Sheets/Weapons.png - one row of 16x16 cells, atlas name "weapons" (Game.atlas).

Cell index (Equip.xml <Index>):   0 Used Sword   1 Iron Sword   2 Steel Sword   3 Bronze Sword   4 Gold Sword
                                  5 Used Staff   6 Iron Staff   7 Steel Staff   8 Bronze Staff   9 Gold Staff
                                 10 Broken Helmet (the starter helm, item "Broken Helmet")   11 Old Spell (the starter spell, item "Old Spell")
Cells 10 and 11 are the user's own 16x16 pictures (Tools/Weapons/source/BrokenHelmet.png, OldSpell.png), copied in whole.
Picks are (column, row) counted from the top-left cell 0,0 of a source sheet.
The art is licensed for commercial use without credit (user's own download, 2026-09-20).
"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'WaW-Client', 'WaWClient', 'Content', 'Sheets', 'Weapons.png')
CELL = 16
SWORD = (7, 0)          # the broad sword, top row
USED_SWORD = (7, 1)     # the same sword one row down
STAFF = (20, 0)         # the lightning staff, top row
USED_STAFF = (20, 1)    # the crooked wooden staff, one row down

CELLS = [
    ('iron',   USED_SWORD), ('iron',   SWORD), ('steel',  SWORD), ('bronze', SWORD), ('gold',   SWORD),
    ('iron',   USED_STAFF), ('iron',   STAFF), ('steel',  STAFF), ('bronze', STAFF), ('gold',   STAFF),
]

SINGLES = ['BrokenHelmet.png', 'OldSpell.png']          # cells 10, 11

sheets = {}
out = Image.new('RGBA', (CELL * (len(CELLS) + len(SINGLES)), CELL), (0, 0, 0, 0))
for i, (metal, (col, row)) in enumerate(CELLS):
    if metal not in sheets:
        sheets[metal] = Image.open(os.path.join(HERE, 'source', metal + '-weapons.png')).convert('RGBA')
    cell = sheets[metal].crop((col * CELL, row * CELL, col * CELL + CELL, row * CELL + CELL))
    if cell.getchannel('A').getbbox() is None:
        raise SystemExit('cell %d (%s %d,%d) is empty' % (i, metal, col, row))
    out.paste(cell, (i * CELL, 0))
for j, name in enumerate(SINGLES):
    pic = Image.open(os.path.join(HERE, 'source', name)).convert('RGBA')
    if pic.size != (CELL, CELL) or pic.getchannel('A').getbbox() is None:
        raise SystemExit('%s must be a non-empty %dx%d picture' % (name, CELL, CELL))
    out.paste(pic, ((len(CELLS) + j) * CELL, 0))
out.save(OUT)
print('wrote', os.path.normpath(OUT), out.size)
