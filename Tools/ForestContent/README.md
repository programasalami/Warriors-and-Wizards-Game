# Forest content generators

Run from the repo root, in this order, then rebuild `AlloyClient/AlloyTk.sln` and `alloy-server/RealmServer.sln`:

1. `python Tools/ForestContent/build_forest_sheets.py` - builds `ForestDecor.png`, `ForestGround.png`, `ForestGroundEdge.png`
   from `Desktop/TopDownFantasy-Forest` (needs Pillow).
2. `python Tools/ForestContent/gen_xml.py` - rewrites the forest `<Object>`/`<Ground>` blocks in both copies of
   `Objects.xml`/`Ground.xml` (sizes, `BottomInset`, RandomTexture variants).
3. `python Tools/ForestContent/gen_nexus.py [out.jm]` - regenerates the Nexus layout. It reads the map at `HEAD` for the
   island silhouette, regions and functional fixtures (all of which the generated map preserves, so re-running after
   committing a generated Nexus still works). Layout knobs are the constants near the top (SPAWN, MARKET, GROVES, ...).
   With no argument it overwrites `alloy-server/Common/Resources/World/Data/Nexus.jm`.
