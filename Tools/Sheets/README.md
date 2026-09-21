# Art sheets (rebuilt 2026-09-20)

Every picture the game draws comes from a sheet listed in `AlloyClient/AlloyClient/Content/Game.atlas` (files in `Content/Sheets/`). Each sheet is ONE uniform grid of
equal cells; the atlas builder skips empty cells but keeps their numbers. Edit the PNGs (and the XMLs that point at them) directly - nothing generates them any more.
An XML picks a picture with `<File>sheetName</File><Index>n</Index>`, where n counts cells left to right, top to bottom (row * columns + column).

| sheet (atlas name) | file | cell | what is on it |
|---|---|---|---|
| grasslands | Grasslands_16x16.png (8 cols) | 16x16 | ground: row 0 grass, row 1 dirt, row 2 water, row 3 bridges, row 4 dirt/water edges + corners |
| smallPlants | SmallPlants_24x36.png | 24x36 | mushrooms, flowers, grass tuft |
| mediumPlants | MediumPlants_40x60.png | 40x60 | shrub, bushes, rocks, reeds (+ one log the title backdrop draws) |
| smallTrees | SmallTrees_56x84.png | 56x84 | the two pines |
| mediumTrees | MediumTrees_80x120.png | 80x120 | tree, oak |
| flatProps | FlatProps_48x48.png | 48x48 | the flat logs |
| largeObjects | LargeObjects_80x120.png | 80x120 | Bug Board, Jukebox, the two Vault chests |
| equipAndConsume | EquipAndConsume_16x16.png (12 cols) | 16x16 | items, see below |
| guildHall | GuildHall_8x8.png (8 cols) | 8x8 | guild hall floors/walls/furniture (one object or ground per row) - still original art |
| guildHallLarge | GuildHallLarge_16x16.png | 16x16 | the Armoire - still original art |
| icons | Icons_8x8.png (16 cols) | 8x8 | empty-slot icons, condition icons, loot bags, minimap marker, effect sprites; cell 0 is blank = "no picture" |
| caveMonsters | CaveMonsters_16x16.png (7 cols) | 16x16 | the monsters of the title screen's cave battle |
| npcs | Npcs_8x8.png | 8x8 | the Pirate (animated sheet) |
| players | Players.png | 32x32 | Warrior / Wizard |
| tileAlphaBlend | AlphaTileBlends.png | 8x8 | ground blending masks (engine data) |

**Decoration cells are always 2:3 (width:height).** The game scales a sprite from its cell: with a 2:3 cell, the art comes out the same size on screen whatever the cell size
is. A new size class = a new sheet with its own cell size (keep 2:3). `BottomInset` in the XML is a FRACTION OF THE CELL HEIGHT, so if art moves to a different cell
height, new inset = old inset x old height / new height.

**EquipAndConsume**: one column per kind, tier 0 on the top row and tiers going down (row = tier, 8 rows so far). Columns: 0 sword, 1 helmet, 2 armor, 3 staff, 4 spell,
5 robe, 6 ring, 7 (spacer), 8-11 potions / other consumables. cell = tier * 12 + column. Only swords and staffs have tiers 1-4 so far. The Health Potion (cell 8) is still
original art (doubled 8x8) - `ArtRules.StockCells` in `AdminRules.cs` says so; remove it there when the potion is replaced.

Adding art: paste it in an empty cell, point the XML at it. A new kind of picture that fits no sheet = new PNG + a line in Game.atlas (+ `ArtRules.NewSheets` if it is ours).
After changing sheets: `python Tools/Editor/build_palette.py`, then build.

Files here: `source_old/` = the sheets and Game.atlas as they were before the rework (the art library the new sheets were cut from - keep); `migrate_sheets.py` +
`migrate_sheets_part2.py` = the one-shot migration (already applied, do not re-run); `migration_map_2026-09-20.json` = old `sheet:cell` -> new `sheet:cell`;
`verify_sheets.py` = checks every XML reference and every moved picture; `source/item209.png` = the Broken Helmet art.
