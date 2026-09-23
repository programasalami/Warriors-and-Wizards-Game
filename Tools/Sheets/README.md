# Art sheets (rebuilt 2026-09-20, all-pack since 2026-09-21)

Every picture the game draws comes from a sheet listed in `AlloyClient/AlloyClient/Content/Game.atlas` (files in `Content/Sheets/`), and since
2026-09-21 every sheet is cut from three bought packs by one artist, all on the Desktop: **GrasslandAssets**, **DarkDungeonAssets**,
**CharactersAssets** (plus the two Vault chests from the bought **Treasure Chests** pack, `Desktop/Extra Assets/Treasure Chests`). Nothing from the original source is left (the retired sheets are archived in `source_old/`). Each sheet is ONE uniform grid of
equal cells; the atlas builder skips empty cells but keeps their numbers. An XML picks a picture with `<File>sheetName</File><Index>n</Index>`,
where n counts cells left to right, top to bottom (row * columns + column). `Tools/Sheets/build_pack_sheets.py` rebuilds the pack sheets and
re-points the XMLs (re-runnable); `verify_sheets.py` proves every picture is pack art and every reference lands on a real cell.

| sheet (atlas name) | file | cell | pack | what is on it |
|---|---|---|---|---|
| grasslands | Grasslands_16x16.png (8 cols) | 16x16 | Grassland | ground: row 0 grass, row 1 dirt, row 2 water, row 3 bridges, row 4 dirt/water edges + corners |
| smallPlants | SmallPlants_24x36.png | 24x36 | Grassland | mushrooms, flowers, grass tuft |
| mediumPlants | MediumPlants_40x60.png | 40x60 | Grassland | shrub, bushes, rocks, reeds (+ one log the title backdrop draws) |
| smallTrees | SmallTrees_56x84.png | 56x84 | Grassland | the two pines |
| mediumTrees | MediumTrees_80x120.png | 80x120 | Grassland | tree, oak |
| flatProps | FlatProps_48x48.png | 48x48 | Grassland | the flat logs |
| largeObjects | LargeObjects_80x120.png | 80x120 | ours + Treasure Chests | all four cells = source_old/ForestProps.png: 0 Bug Board, 1 Jukebox (our own drawn art), 2 Vault Chest, 3 Closed Vault Chest (the bought Treasure Chests pack, `Desktop/Extra Assets/Treasure Chests`; sources in Tools/ForestContent/source/vault) - real art, not placeholders (fixed 2026-09-22) |
| dungeon | Dungeon_16x16.png (13 cols) | 16x16 | Dark Dungeon | the whole tileset: floors, walls, column, altar, barrel, chest, crate, doors, rack |
| dungeonDecor | DungeonDecor_16x16.png | 16x16 | Dark Dungeon | 0 torch pole, 1 wall torch, 2 side torch, 3 chest closed, 4 chest open, 5 spikes (first frames of the animations) |
| dungeonItems | DungeonItems_16x16.png (5 cols) | 16x16 | Dark Dungeon | the 25 items: rings, necklaces, arrows, scroll, apple, bow, book, coins, hood, helmet, crown, dagger, potions, armours, sword, shield, staffs, gold sword, wand |
| dungeonMonsters | DungeonMonsters_16x16.png (3 cols) | 16x16 | Dark Dungeon | 0 skeleton, 1 knight, 2 goblin, 3 slime, 4 wraith, 5 rat |
| hudIcons | HudIcons_16x16.png (10 cols) | 16x16 | drawn + Dark Dungeon | 0 blank ("no picture", one faint pixel), 1 minimap arrow, 2 flame, 3 dot, 4 burst, 5 arrows, 6 sword, 7 blue potion, 8 red potion, 9 apple |
| players | Players.png | 32x32 (Animated, Full) | Characters | 0 Wizard = Mage-Cyan, 1 Warrior = Warrior-Red |
| skins | Skins.png | 32x32 (Animated, Full) | Characters | all 19 characters, one block each (`Skins.txt` lists index -> file) - for the future wardrobe, nothing uses it yet |
| tileAlphaBlend | AlphaTileBlends.png | 8x8 | engine | ground blending masks (engine data, not a picture) |

**Who uses what (2026-09-21):** the Nexus / Vault / Realm maps are grassland only. The GUILD HALL maps keep their object and ground NAMES but every
one of them now draws a Dark Dungeon or grassland picture (floors = dark stone / grass / dirt, walls = bricks, tables = crates, banners = wall
torches...). Portals are torches / a chest, the fountain an altar, the DPS dummies the knight, the Pirate the goblin, loot bags a pile of coins,
projectiles arrows / the torch flame, condition icons items from the item sheet (`ConditionEffect.cs`), the title screen's cave monsters the six
dungeon characters. Items: swords 13 / 18 / 18 / 23 / 23 and staffs 24 / 21 / 21 / 22 / 22 for tiers 0-4 (the repeats are placeholders), Old Helmet 11,
Old Spell 8 (book), Health Potion 19. All of these stand-ins are listed in the generated `ArtPlaceholders.g.cs` and show as PLACEHOLDER in the admin
dashboard; the goal is to buy / draw real art for each and remove it from the tool's placeholder list. **Since 2026-09-22 every placeholder OBJECT draws
the same picture, the Health Potion (`PLACEHOLDER_PICTURE`, step 3b of the tool), so a stand-in is never mistaken for final art; placeholder grounds keep their
floor / water stand-ins.**

**Decoration cells are always 2:3 (width:height).** The game scales a sprite from its cell: with a 2:3 cell, the art comes out the same size on screen
whatever the cell size is. A new size class = a new sheet with its own cell size (keep 2:3). `BottomInset` in the XML is a FRACTION OF THE CELL HEIGHT.
Square 16x16 cells draw one tile wide at `<Size>50</Size>` (the engine's base unit is 8 px): the objects moved from 8x8 cells got their Size halved.

**Characters** (`build_pack_sheets.character_block`): a CharactersAssets sheet is 24 x 8 cells of 32x32; the game block is 7 x 3 (rows: down, side, up =
pack rows 2, 0, 3; columns 0-3 the walk, 4 = column 0 again, 5-6 one 64-wide attack frame = pack column 8 pasted 16 px in). Players.png is exactly
that rule applied to Mage-Cyan and Warrior-Red (verified).

Adding art: paste it in an empty cell, point the XML at it. A new kind of picture that fits no sheet = new PNG + a line in Game.atlas (+ `ArtRules.NewSheets`
if it is ours). After changing sheets: `python Tools/Sheets/verify_sheets.py`, `python Tools/Editor/build_palette.py`, then build the client and
grep its output for "Failed to add" (the UI atlas is a fixed 4096x4096; see CLAUDE.md).

The original source's FBX meshes (Pillar, Sign, Table, TableEdge, Tower, CandyColBroken - the Red Pillar, Guild Board, Table, Table Edges and White Fountain drew
them) are retired too (`source_old/fbx/`): those objects are plain upright sprites of their placeholder art now. The Bug Board and the Jukebox keep their
code-built meshes (`ModelData.Props.cs`), which are ours.

Files here: `build_pack_sheets.py` (the 2026-09-21 rework, re-runnable), `verify_sheets.py`, `source_old/` = every retired sheet (the pre-2026-09-20
originals and the six retired on 2026-09-21: GuildHall, GuildHallLarge, Icons, CaveMonsters, Npcs, EquipAndConsume) - keep as reference, nothing reads
them; `migrate_sheets*.py` + `migration_map_2026-09-20.json` = the earlier one-shot migration (history).
