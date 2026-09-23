# OBSOLETE since 2026-09-20: the game's art sheets are now edited directly (see Tools/Sheets/README.md), so this script would recreate sheets that no longer
# exist. Kept as the record of how the old art was made / where it came from. Run with --force only if you know why.
import sys
if '--force' not in sys.argv:
    raise SystemExit('obsolete: see Tools/Sheets/README.md (run with --force to run it anyway)')
"""Regenerates the forest <Object> block in both Objects.xml copies and the forest <Ground> block in
both Ground.xml copies. Idempotent: replaces from its own start marker to the end of the file's
container element."""
import re
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')).replace(os.sep, '/') + '/'   # the repo root, wherever the folder lives
# The server links the client's Xmls folder at build time (the duplicate copies under WaW-Server were removed), so the client copy is the only one.
OBJ_FILES = [ROOT + 'WaW-Client/WaWClient/Content/Xmls/Objects.xml']
GND_FILES = [ROOT + 'WaW-Client/WaWClient/Content/Xmls/Ground.xml']

# (type, id, sheet index, size spec, inset, kind)
#   size spec: int -> <Size>, (min, max) -> Min/MaxSize step 2
#   kind: 'tree' blocking+sight, 'block' blocking, 'deco' walk-through
S = 50  # ~0.031 screen px per art px per Size point (tile ~33px), so Size 48-50 matches the characters' ~1.5 px per art px
OBJECTS = [
    (0x9f00, 'Forest Tree',          0, (44, 52), 0.067, 'tree'),
    (0x9f01, 'Forest Pine',          1, (44, 52), 0.062, 'tree'),
    (0x9f02, 'Forest Bush',          2, 48, 0.035, 'deco'),
    (0x9f03, 'Forest Shrub',         3, 50, 0.030, 'deco'),
    (0x9f04, 'Forest Log',           4, 48, 0.040, 'block_rock'),
    (0x9f05, 'Forest Reeds',         5, 50, 0.030, 'deco'),
    (0x9f06, 'Forest Grass Tuft',    6, 52, 0.028, 'deco'),
    (0x9f07, 'Forest Rock Cluster',  7, 50, 0.035, 'block_rock'),
    (0x9f08, 'Forest Mushroom Red',  8, 54, 0.030, 'deco'),
    (0x9f09, 'Forest Mushroom Big',  9, 54, 0.030, 'deco'),
    (0x9f0a, 'Forest Flowers',      10, 54, 0.028, 'deco'),
    (0x9f0b, 'Forest Oak',          11, (44, 52), 0.067, 'tree'),
    (0x9f0c, 'Forest Pine Tall',    12, (44, 52), 0.062, 'tree'),
    (0x9f0d, 'Forest Bush Wide',    13, 48, 0.035, 'deco'),
    (0x9f0e, 'Forest Small Rock',   14, 52, 0.030, 'deco'),
    (0x9f0f, 'Forest Log Mossy',    15, 48, 0.040, 'block_rock'),
]


# Objects drawn flat on the ground (they lie in the ground plane and turn WITH the terrain when the camera rotates):
# id -> index in forestFlat (a 48x48 sheet with the art centred - flat sprites pivot on their centre; see build_forest_sheets.py).
# Logs are the props that read wrong as upright cards (long and low, they seemed to spin with the camera). Everything else stays
# an upright billboard. TypeGameObject.DrawFlat had the camera-angle sign backwards (fixed 2026-09-19), which is likely why
# flat props looked wrong when they were first tried.
FLAT = {'Forest Log': 5, 'Forest Log Mossy': 6}
# (Bushes and rocks were tried flat on 2026-09-20 and looked worse: they stay upright. NOTE: never run this script against Objects.xml - it rewrites everything after
# its marker and would cut off the hand-added objects such as the Bug Board.)


def obj_xml(t, oid, idx, size, inset, kind):
    flat = oid in FLAT
    if flat:
        idx = FLAT[oid]
    lines = [f'    <Object type="0x{t:x}" id="{oid}">',
             '        <Class>GameObject</Class>',
             '        <Texture>',
             f'            <File>{"forestFlat" if flat else "forestDecor"}</File>',
             f'            <Index>0x{idx:02x}</Index>',
             '        </Texture>']
    if kind == 'tree':
        lines += ['        <HitSound>monster/trees_hit</HitSound>', '        <DeathSound>monster/trees_death</DeathSound>']
    elif kind == 'block_rock':
        lines += ['        <HitSound>monster/rocks_hit</HitSound>', '        <DeathSound>monster/rocks_death</DeathSound>']
    if isinstance(size, tuple):
        lines += [f'        <MinSize>{size[0]}</MinSize>', f'        <MaxSize>{size[1]}</MaxSize>', '        <SizeStep>2</SizeStep>']
    else:
        lines += [f'        <RealSize>{size}</RealSize>']   # the client ignores <Size>; only RealSize/MinSize+MaxSize take effect
    if flat:
        lines += ['        <FlatOnGround/>', '        <Static/>']
    else:
        lines += [f'        <BottomInset>{inset}</BottomInset>', '        <Static/>']
    if kind in ('tree', 'block_rock'):
        lines += ['        <OccupySquare/>']
    if kind == 'tree':
        lines += ['        <BlocksSight/>']
    lines += ['    </Object>']
    return '\n'.join(lines)


OBJ_MARK = '    <!-- Forest (TopDownFantasy-Forest pack)'
obj_block = (OBJ_MARK + ' - Nexus reskin. All on the shared "forestDecor" 80x120\n'
             '         sheet cell (see Game.atlas). Size ~40 gives every sprite the same on-screen pixel scale as the\n'
             '         characters (Size is linear in per-art-pixel scale; see RenderBase.SetTexture). BottomInset is the\n'
             '         fraction of cell height to push the sprite down so its visual base sits on its shadow. -->\n'
             + '\n'.join(obj_xml(*o) for o in OBJECTS) + '\n</Objects>')


def tex(idx, indent='        '):
    return (f'{indent}<Texture>\n{indent}    <File>forestGround</File>\n{indent}    <Index>0x{idx:02x}</Index>\n{indent}</Texture>')


def rand_tex(idxs):
    return '\n'.join(f'        <RandomTexture>\n{tex(i, "            ")}\n        </RandomTexture>' for i in idxs)


GND_MARK = '    <!-- Forest (TopDownFantasy-Forest pack) - Nexus reskin. Named "Woodland'
gnd_block = GND_MARK + ''' *" rather than
         "Forest *" - a pre-existing "Forest Dirt" (0x5f) already used that exact id, which threw
         a duplicate-key exception out of AssetParser.ParseGround at startup (it keys ground types
         by this string) and corrupted ground rendering for the whole session, not just this tile.
         One <RandomTexture> element per variant (TextureData reads a single <Texture> from each);
         the pick is per-tile and repeated entries weight it. -->
    <Ground type="0xfa00" id="Woodland Grass">
''' + rand_tex([0, 2, 0, 3, 0, 2, 3, 4]) + '''
    </Ground>
    <Ground type="0xfa01" id="Woodland Dirt">
        <SameTypeEdgeMode/>
''' + rand_tex([1, 7, 8, 1, 9, 5, 7, 6, 8, 9]) + '''
        <BlendPriority>1</BlendPriority>
        <Edge>
            <Texture>
                <File>forestGroundEdge</File>
                <Index>0x00</Index>
            </Texture>
        </Edge>
        <Corner>
            <Texture>
                <File>forestGroundEdge</File>
                <Index>0x01</Index>
            </Texture>
        </Corner>
    </Ground>
    <Ground type="0xfa02" id="Woodland Water">
        <SameTypeEdgeMode/>
        <NoWalk/>
''' + rand_tex([10, 11, 10, 12, 10, 11, 12, 13]) + '''
        <Edge>
            <Texture>
                <File>forestGroundEdge</File>
                <Index>0x02</Index>
            </Texture>
        </Edge>
        <Corner>
            <Texture>
                <File>forestGroundEdge</File>
                <Index>0x03</Index>
            </Texture>
        </Corner>
    </Ground>
    <Ground type="0xfa03" id="Woodland Bridge EW">
''' + tex(14) + '''
    </Ground>
    <Ground type="0xfa04" id="Woodland Bridge NS">
''' + tex(15) + '''
    </Ground>
</GroundTypes>'''


def rewrite(path, mark, block):
    raw = open(path, 'rb').read().decode('utf-8')
    crlf = '\r\n' in raw
    t = raw.replace('\r\n', '\n')
    i = t.index(mark)
    t = t[:i] + block + ('\n' if t.endswith('\n') else '')
    if crlf:
        t = t.replace('\n', '\r\n')
    open(path, 'wb').write(t.encode('utf-8'))


for p in OBJ_FILES:
    rewrite(p, OBJ_MARK, obj_block)
for p in GND_FILES:
    rewrite(p, GND_MARK, gnd_block)
print('xml regenerated')
