# Making a texture pack

A texture pack is a small mod that holds pictures and nothing else. Texture Packs puts them in place of the game's own: items, windows and buttons, the shop and its fixtures, the customers, the new-game screens.

This page walks through making one with the creator kit (`TexturePackKit.zip`).

## What is in the kit

| File | What it is for |
| --- | --- |
| `catalog.tsv` | Every picture a pack can replace: its file name, size and category. Open it in a spreadsheet. |
| `placeholders/` | One flat rectangle of the right size for every scene picture, sorted into the category folders. |
| `export/` | A script that saves the game's own pictures from your copy of the game. |
| `PackBuilder.exe` | Turns your folder of pictures into a pack the game can load, and checks names and sizes on the way. |
| `TexturePack.dll` | The empty file every pack carries (see below). |

The kit holds no game art. You get the original pictures from your own game with the export script, and you should not share them: only your own pictures go into a pack.

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

   Everything in the catalog is written, about 1,800 pictures, in a few seconds. `--only item` (or `ui`, `scene`, `character`, `new-game`) exports one category. Files already there are kept; `--overwrite` writes them again, for example after a game update.

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
- **Keep the name.** The file name says what is replaced. Item and window pictures look like `Items~items_tool@wrench.png`; scene pictures like `wall.960x540.png`.
- **Pictures with several frames** (walking people, customer clothing, icon sheets) are replaced as a whole. Keep each frame where it was.
- **Perk icons are white.** The game colours them itself, so draw them in white and change the shape.
- **Item shapes do not change.** Which cells an item takes comes from the game's picture, whatever your pack draws.
- **Appraisal close-ups go with the item.** If you redraw an item that can be appraised, redraw its close-ups too, or the magnifier shows the old one.

You can keep your work in the category folders; the pack builder flattens them.

## 3. Build the pack

```
PackBuilder.exe <your folder> "<game>\Mods\my_pack" my_pack "My Pack"
```

The builder copies every PNG under your folder into the pack's `Textures` folder, and writes `TexturePack.dll` and a `manifest.xml` if the pack has none. It stops if two files share a name or a size is wrong, and notes any name that is not in the catalog.

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

Enable **Texture Packs** and your pack in the Mods menu and restart. The game's log (`Player.log`) has a line for each scene, with the pictures that were replaced and any that were skipped and why.
