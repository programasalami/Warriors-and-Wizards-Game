# Archived enemy behaviours (moved out of the server build on 2026-09-22)

These are the original game's enemy AI scripts (`BehaviorLib.<area>.cs` partial classes from the upstream WaW-Server): Abyss, Avatar,
Ghost Ship, Hermit, Highland, Lost Halls, Lord of the Lost Lands, Lowland, Mad Lab, Midland, Mountain, Oryx, Oryx Castle, Shatters,
Shatters King, Sphinx, Toxic Sewers - about 14,900 lines. NONE of the enemies they drive exists in Warriors & Wizards' data (checked
against every `<Object id>` in the XMLs), so they only made the server harder to find your way around. They are kept here, renamed
`.cs.txt` so no project compiles them, as reference for building real enemies later.

To bring one back: rename it to `.cs`, move it to `WaW-Server/GameServer/Game/Systems/Behaviors/Library/`, give the enemy an
`<Object>` entry whose `id` matches the `[CharacterBehavior("...")]` name, and build.
