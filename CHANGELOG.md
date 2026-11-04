# Changelog

## 0.1.0 (work in progress)

Not released.

- Texture packs: mods that list Texture Packs as a prerequisite and carry `TexturePack.dll` and a `Textures` folder of PNG files.
- Item and window pictures the game loads from its atlases are replaced sprite by sprite; pictures stored in the menu and shop scenes are replaced in place each time the scene loads. A picture must be exactly the size of the game's.
- Item shapes always come from the game's own pictures, whatever a pack draws.
- The pack manager, opened from the Texture Packs settings button in the Mods menu: packs in priority order, the top one winning, each switched on or off and moved up or down. Apply puts the new order in place at once.
- The creator kit: the catalog of every replaceable picture by category, placeholders, the export script and the pack builder.
