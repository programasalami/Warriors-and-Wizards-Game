# Retired tools (moved here on 2026-09-22)

Scripts that built the art sheets and object XML BEFORE the three bought packs replaced everything (see `Tools/Sheets/README.md`).
Each one is guarded so it refuses to run; they are kept only as a record of how the old sheets were made.

- `ForestContent/build_forest_sheets.py`, `ForestContent/draw_props.py` (it composed the Vault chests, whose sources still live in
  `Tools/ForestContent/source/vault`), `ForestContent/gen_xml.py` (NEVER run against Objects.xml: it rewrites everything after its
  marker), `Weapons/build_weapons_sheet.py`.

The current art pipeline is `Tools/Sheets/build_pack_sheets.py` + `verify_sheets.py`.
