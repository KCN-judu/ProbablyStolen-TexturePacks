# Texture Packs (WIP)

![Texture Packs](preview.png)

A mod for *Probably Stolen* that replaces the game's pictures with the ones in texture packs. A texture pack is a small mod that holds pictures. Texture Packs puts them in place of the game's own: items, windows, the shop and its fixtures, the customers and the new-game screens. A pack manager lists the installed packs in priority order. Each pack can be switched on or off, and the pack at the top wins where two replace the same picture. A pack can also replace the pictures that other mods add, such as the items of a gameplay mod, without any change to that mod.

**Work in progress.** Not released yet. Things will change.

Part of the [Probably Stolen mods collection](https://github.com/KCN-judu/ProbablyStolen-Mods).

## Making a pack

See [Making a texture pack](doc/manual/making-a-pack.md). The creator kit (`tools/kit.sh`) holds the catalog of every picture a pack can replace, placeholders of the right sizes, a script that exports the game's pictures from your own copy of the game, and a tool that builds the pack. It holds no game art. [How it works](doc/manual/how-it-works.md) describes what the mod patches. Authors of other mods: see [For mod authors](doc/manual/for-mod-authors.md).

## Build

```bash
./build.sh            # out/texture_packs and out/texture_pack_template
./build.sh --install  # also copies the mod to the game's Mods folder
tests/run.sh          # the pure-logic tests, the schema script's tests and the game's own mod scanner
tools/kit.sh          # the creator kit, out/TexturePackKit.zip
```

`GAME` and `CSC` override the game folder and the C# compiler, as in the collection's other mods. After a game update, refresh the catalog first with `tools/export/inventory.py` (Python, see `tools/export/requirements.txt`), then run `tools/kit.sh`.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/` | The mod's source |
| `Localization/` | In-game text, one file per game language |
| `template/` | The empty `TexturePack.dll` every pack carries, a pack manifest, and `TexturePacksClient.cs`, which a mod that makes sprites in code copies to offer them to packs |
| `catalog.tsv` | Every picture a pack can replace, with its file name, size and category |
| `tools/Catalog/` | Builds `catalog.tsv` and the placeholders from `inventory.json` |
| `tools/export/` | Reads the game's files: the inventory for the catalog, and the export for creators |
| `tools/PackBuilder/` | Turns a folder of pictures into a pack, and checks names and sizes against the catalog and the schemas |
| `tools/schema/` | Writes `textures.schema.tsv`, the list of a mod's replaceable pictures |
| `tools/preview/` | Lays out the shop view from the game's scene with a pack's pictures in it, to look at a pack without starting the game |
| `tools/SampleProvider/` | Development mod that offers one sprite made in code, to test the provider path |
| `tools/SpriteProbe/` | Development mod that logs every sprite the game shows |
| `tests/` | Headless tests of `src/*.Pure.cs` and of the schema script, and the run of the game's mod scanner |
| `doc/manual/` | Documentation, in English |
| `manifest.xml`, `preview.png` | What the game reads, next to the DLL |
| `CHANGELOG.md` | Changes by version |

## License

[Apache License 2.0](LICENSE).
