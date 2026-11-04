# Building

## Requirements

- *Probably Stolen*, game version 0.46D. The build compiles against the game's own assemblies in `Probably Stolen_Data/Managed`.
- Git Bash or another POSIX shell on Windows.
- The C# compiler that ships with Windows (`csc.exe` of .NET Framework 4).
- To refresh the catalog after a game update: [Python](https://www.python.org/downloads/) 3.10 or newer, with the packages in `tools/export/requirements.txt`.

## Build

```bash
./build.sh            # out/texture_packs and out/texture_pack_template
./build.sh --install  # also copies the mod to the game's Mods folder
```

Two environment variables override the defaults:

| Variable | Default |
| --- | --- |
| `GAME` | `/d/SteamLibrary/steamapps/common/Probably Stolen` |
| `CSC` | `/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe` |

`out/texture_pack_template` holds the empty `TexturePack.dll` and a manifest that every pack starts from.

## Checks

```bash
tests/run.sh
```

It runs the decisions in `src/*.Pure.cs` headless, once on examples and once against plain reference models on generated input. When the game is installed and the mod is built, it also runs the game's mod scanner on the build. The mod passes when the scanner reports `capabilities: none` and no `HARD` line.

## The creator kit

```bash
tools/kit.sh
```

This builds `Catalog.exe` and `PackBuilder.exe`, writes `catalog.tsv` and the placeholders, and zips the kit as `out/TexturePackKit.zip`. The kit holds no game art.

After a game update, refresh the inventory first:

```bash
python tools/export/inventory.py "<game folder>" tools/Catalog/inventory.json
tools/kit.sh
```

## Sprite Probe

`tools/SpriteProbe/build.sh` builds a development mod that writes every sprite the game shows to `Player.log`, once, with the node that showed it. It helps to find what a pack can replace. It is not meant for play. `tools/SpriteProbe/build.sh --install` copies it to the game's `Mods` folder.

## Install by hand

Copy the folder `out/texture_packs` into the game's `Mods` folder, start the game, and enable the mod in the Mods menu. Mods load when the game starts, so restart after enabling or updating one.

The mod declares no capabilities, so the game enables it without a permission prompt.
