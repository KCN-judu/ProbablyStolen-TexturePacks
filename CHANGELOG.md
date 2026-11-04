# Changelog

## 0.1.0 (early access)

First public release, for testing. Discussion during development: https://discord.com/channels/1315752321975845004/1567431485391372339

- Texture packs: mods that list Texture Packs as a prerequisite and carry `TexturePack.dll` and a `Textures` folder of PNG files.
- Item and window pictures the game loads from its atlases are replaced sprite by sprite; pictures stored in the menu and shop scenes are replaced in place each time the scene loads. A picture must be exactly the size of the game's.
- Item shapes always come from the game's own pictures, whatever a pack draws.
- The pack manager, opened from the Texture Packs settings button in the Mods menu: packs in priority order, the top one winning, each switched on or off and moved up or down. Apply puts the new order in place at once.
- The creator kit: the catalog of every replaceable picture by category, placeholders, the export script and the pack builder.
- A preview of the shop view (`tools/preview/preview.py`) that draws the game's scene layers in the game's order with a pack's pictures in place, and can show the game's own picture above the pack's.
- Pictures of other mods: a pack replaces the pictures in another mod's `Items` and `Paper` folders by naming them `<origin>+<container>~<name>.png`. The mod needs no change, and a picture takes effect only while that mod is enabled.
- A mod's origin is its UUID, from a marker picture `Textures/origin.<uuid>.png` or from its code. A mod without a UUID is named by its id.
- Schema script (`tools/schema/schema.py`): writes `textures.schema.tsv`, the list of a mod's replaceable pictures with their sizes and the mod's version. The export script reads schemas with `--schema` and `--mods`, and the pack builder checks names and sizes against them.
- Provider helper (`template/TexturePacksClient.cs`): a mod that makes sprites in code copies it to let packs replace them. It works without Texture Packs installed. Only sprites can be offered.
