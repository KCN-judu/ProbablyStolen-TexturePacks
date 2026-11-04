# Making a texture pack

A texture pack is a small mod that holds pictures and no code. Texture Packs puts them in place of the game's own: items, windows and buttons, the shop and its fixtures, the customers, the new-game screens.

The creator kit (`TexturePackKit.zip`) has what you need. This page goes through it step by step.

## What is in the kit

| File | What it is for |
| --- | --- |
| `catalog.tsv` | Every picture a pack can replace: its file name, size and category. Open it in a spreadsheet. |
| `placeholders/` | One flat rectangle of the right size for every scene picture, sorted into the category folders. |
| `export/` | A script that saves the game's own pictures from your copy of the game. |
| `PackBuilder.exe` | Turns your folder of pictures into a pack the game can load, and checks names and sizes on the way. |
| `TexturePack.dll` | The empty file every pack carries (see below). |
| `schemas/` | Where you put the schemas of mods whose pictures you replace. `PackBuilder.exe` reads every `*.schema.tsv` in it. |
| `schema/` | A script that writes a mod's schema, for mods that do not ship one. |
| `for-mod-authors/` | A file for mod authors who make sprites in code. You do not need it. |

The kit holds no game art. You get the original pictures from your own game with the export script, and you should not share them: only your own pictures go into a pack. The same goes for the pictures of other mods.

## 1. Export the game's pictures

The export script reads the game's files directly; the game does not need to run. It needs [Python](https://www.python.org/downloads/) 3.10 or newer.

1. Open a terminal in the kit's `export` folder and install what the script uses:

   ```
   python -m pip install -r requirements.txt
   ```

2. Export, giving the game folder:

   ```
   python export.py "C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen" --out ..\originals
   ```

   Everything in the catalog is written: about 1,800 pictures. `--only item` (or `ui`, `scene`, `character`, `new-game`) exports one category. Files already there are kept; `--overwrite` writes them again, for example after a game update.

The pictures land sorted the same way as the placeholders:

| Folder | What is in it |
| --- | --- |
| `item/atlases` | Every item, one file per item picture |
| `item/appraisal-closeups` | What the magnifier shows when appraising: letters, seals, chips, burger layers, prosthetic hands, and the magnifier itself |
| `item/appraisal-references` | Check cards and the guide pictures that show an item's details |
| `item/opened-tools` | The full-screen body of a tool when it is open: scanners, the labeler |
| `ui/window-elements` | Window frames and grid cells |
| `ui/main-menu`, `ui/in-game` | Buttons, panels, papers, the phone and tablet screens |
| `scene/backgrounds` | Whole backgrounds |
| `scene/illustrations` | The main menu backgrounds and the run-summary picture |
| `scene/shop-structure` | The shop's walls, doors, windows, floor and lights, as layers |
| `scene/fixtures` | What stands in the shop: phone, radio, calendar, display cases, the trash can, vending machines, the wires that light up |
| `scene/animation` | People walking past, desk and tape animations |
| `character/people` | Every customer portrait and clothing layer |
| `new-game/...` | Specializations, perk icons and the new-game screens |

## 2. Draw

Change any picture you like and leave the rest out: a pack only needs the files it replaces.

- **Keep the size.** A picture must be exactly as wide and as tall as the game's. The size is in `catalog.tsv` and in every scene file name (`store_mid.960x540.png`). A picture of another size is left out.
- **Keep the name.** The file name says what is replaced. Item and window pictures look like `items~items_tool2@blender.png`; scene pictures like `wall.960x540.png`. Use the names in `catalog.tsv` exactly, including lower case.
- **Pictures with several frames** (walking people, customer clothing, icon sheets) are replaced as a whole. Keep each frame where it was.
- **Perk icons are white.** The game colours them itself, so draw them in white and change the shape.
- **Item shapes do not change.** Which cells an item takes comes from the game's picture, whatever your pack draws.
- **Appraisal close-ups go with the item.** If you redraw an item that can be appraised, redraw its close-ups too, or the magnifier shows the old one.
- **A pack icon is optional.** A file named `pack.png`, of any size, is shown next to your pack in the pack manager. It replaces no picture of the game.

You can keep your work in the category folders; the pack builder flattens them.

## 3. Build the pack

```
PackBuilder.exe <your folder> "<game>\Mods\my_pack" my_pack "My Pack"
```

The builder copies every PNG under your folder into the pack's `Textures` folder. It first deletes the PNG files already in that folder, so the pack holds exactly what is in your folder. It writes `TexturePack.dll` if the pack has none, and a `manifest.xml` if the pack has none. It stops without writing anything if two files share a name or a size is wrong, and notes any name that is not in the catalog.

Open `manifest.xml` and fill in the name, author and description. Two lines must stay as they are:

```xml
<Prerequisites>
  <Mod>texture_packs</Mod>
</Prerequisites>
<Assemblies>
  <Assembly>TexturePack.dll</Assembly>
</Assemblies>
```

The game only loads a mod that has code, so every pack carries `TexturePack.dll`, which does nothing. Every pack can use the same copy.

## 4. Try it

Enable **Texture Packs** and your pack in the Mods menu and restart. To change the order of several packs, or to switch one off, use the [pack manager](index.md#using-packs). The game's log (`Player.log`) has a `[TexturePacks]` line that lists each pack with the number of pictures used, covered and skipped. Every skipped picture has its own warning that says why. When a scene loads, another line lists the scene pictures that were replaced. The pack manager shows the same numbers.

## Looking at a pack without the game

The repository has a preview that lays out the shop as the game does and puts a folder of your pictures into it, so a picture can be judged in the scene before the game starts. The shop is a stack of sprites, each with a sorting order and a position; the preview reads them from your copy of the game and draws them in that order.

```
python tools/preview/preview.py "<game folder>" --pack <your Textures folder> --compare --out preview.png
```

`--compare` puts the game's own picture above yours. `--hide ShutterDoor` leaves a layer out (here, to see the room behind the serving window), `--list` prints the layers in drawing order, and `--scale 2` enlarges the result. A picture of a size the game does not use is left out and listed. The preview shows the shop view only. It does not draw the customer, the text or the game's lighting effects, so it is for looking at a pack, not a copy of the game's screen. It needs [Python](https://www.python.org/downloads/) and the packages in `tools/export/requirements.txt`.

## Pictures of other mods

A pack can also replace the pictures that other mods add: items, papers, and sprites a mod makes in code. The mod does not have to know about your pack, but you need its schema to name and size your pictures.

### The schema

A schema is a file, `textures.schema.tsv`, that lists every picture of one mod that a pack can replace. It has the first eight columns of `catalog.tsv` (`file`, `kind`, `category`, `group`, `width`, `height`, `source`, `name`), then `origin`, `container` and `version`. For a mod picture, `file` is the name your file must have, `group` is the mod id and `version` is the mod's version.

- If the mod's author ships a schema, it is in the mod's folder.
- If not, write one. Run the script from the kit on the mod's folder; it reads the files and does not run the mod: `python schema\schema.py "<game>\Mods\machine_mod"`. It lists the pictures in the mod's `Items` and `Paper` folders. Pictures that the mod makes in code, or keeps in an asset bundle, are not found. Ask the author to list them.
- Put the schema in the kit's `schemas` folder, or pass it to the tools with `--schema` as below.

### The file name

A mod's picture is named `<origin>+<container>~<name>.png`. Copy it from the `file` column of the schema, for example:

```
3f2a9c1e-5b7d-4e2a-9c1e-0a1b2c3d4e5f+Items~crusher_body.png
```

The origin is the mod's UUID. A mod without a UUID is named by its mod id, as in `machine_mod+Items~crusher_body.png`. The picture must have exactly the size in the schema. [How it works](how-it-works.md#what-a-file-name-means) explains the parts of the name.

### Export the mod's pictures

Give the export script the schemas. `--schema FILE` adds one mod and can be repeated. `--mods DIR` adds every `*/textures.schema.tsv` in a Mods folder:

```
python export.py "<game folder>" --out ..\originals --mods "<game folder>\Mods"
```

The pictures go into `mod/<mod id>/` in the output folder. The script copies each picture from the mod's own `Items` or `Paper` folder, next to the schema. For a picture with no file there, it writes a flat grey rectangle of the right size. `--only mod` exports only the mods' pictures. The game's export is the same as without these options.

### Build the pack

Give PackBuilder the same schemas:

```
PackBuilder.exe <your folder> "<game>\Mods\my_pack" my_pack "My Pack" --schema machine_mod\textures.schema.tsv
```

`--schema` can be repeated. PackBuilder also reads every `*.schema.tsv` in the `schemas` folder next to it. It checks your mod pictures by what they replace, not by how the name is spelled, and reports:

| Message | What it means |
| --- | --- |
| `error: ... is 40x64, the picture is 48x64 in machine_mod 1.2.0` | The size is wrong. The message gives the size in the schema and the mod version the schema was written for. The build stops. |
| `warning: ... names the mod by its id, and the mod has the UUID ...` | The mod has a UUID, and the file names it by its id. The message shows the file name to use. |
| `note: ... is not in any schema` | No schema lists this picture. The mod may be newer or older than the schema, or you gave no schema for it. The file is still copied. |
| `error: ... and ... name the same picture` | Two files are one picture, for example the same UUID written in upper and lower case. The build stops. |

When a mod's update changes a picture's size, rewrite its schema and run PackBuilder again: it lists the pictures to redraw.

### In the game

The picture takes effect only while the mod it names is enabled. If no enabled mod has the origin, or the mod has no such picture, or the size differs from the mod's picture, the picture is skipped. The pack manager counts it, and `Player.log` says why.
