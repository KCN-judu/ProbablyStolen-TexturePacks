# How it works

![How the loader and the creator kit fit together: the game's loader decodes a pack's files, the file name tells what each picture replaces, the manager's order decides which pack wins, and each picture is checked before it is used](../attachments/texture-packs-architecture.png)

The left side of the diagram is the mod in the game. The right side is the [creator kit](making-a-pack.md).

## Finding the packs

A pack is an enabled mod that lists `texture_packs` among its prerequisites. The game's loader has already decoded every PNG in the pack's `Textures` folder, so Texture Packs reads nothing from disk. It reads the packs again each time a pack's assets finish loading, each time a mod finishes loading that a pack has pictures for (they exist from then on, whichever of the two finished first), after Apply, and when you enable the mod from the Mods menu after start-up.

## What a file name means

The game reads only the top of the `Textures` folder, so the file name says what a picture replaces:

| Name | Replaces |
| --- | --- |
| `items~items_tool2@blender.png` | The sprite `blender` in the Resources atlas `items/items_tool2` (`~` stands for `/`) |
| `window~machinery~bevel_frame.png` | The single Resources sprite `window/machinery/bevel_frame` |
| `store_mid.960x540.png` | The whole scene texture `store_mid` of that size |
| `machine_mod+Items~crusher_body.png` | The picture `crusher_body` in the `Items` folder of the mod with the id `machine_mod` |
| `3f2a9c1e-5b7d-4e2a-9c1e-0a1b2c3d4e5f+Paper~note.png` | The picture `note` in the `Paper` folder of the mod that declared that UUID |

Scene textures carry their size in the name because several share a name. `wall`, for example, is 960x540 in the shop and 640x360 in the run summary. A PNG whose name is none of these is not a replacement, and the game keeps its own picture. A pack may hold such files.

A picture of a mod is written as origin, `+`, container, `~`, name:

- The origin is the mod's UUID or, for a mod without one, its mod id. A UUID may be written in any case and is read as lower case.
- The container is one word of letters, digits, `_` and `-`. `Items` and `Paper` are the two folders the game decodes. A mod that makes sprites in code may use any other word.
- The name is everything after the first `~`: the file name in the mod's folder without its extension.

No mod id, UUID or game picture contains a `+`, so the first `+` always ends the origin, and a name with a `+` in it is never a picture of the game.

## Identity

Every replaceable picture has an identity of two values. The origin says where the picture comes from: the game, or one mod. The locator says where it is inside the origin: for the game a sprite in a Resources atlas, a single Resources sprite or a scene texture, for a mod an asset in a container. An identity pairs only what exists: the game has no mod assets, and a mod has no Resources sprites.

The identity is not one string, because everything that decides something compares identities: the order of the packs, the check for two files with the same target, and the numbers in the pack manager. With a string, each of those would have to take the name apart again, and the spelling of the file name would become a format they all depend on. The file name is only how an identity is written on disk. One class, `AssetIdCodec`, reads and writes it. The pack builder is compiled with the same class, so the builder and the game read a name the same way. The codec never gives two identities one name, and reading a name it wrote gives back the same identity.

## The origin of a mod

Texture Packs reads no files of other mods. It learns a mod's origin in this order:

1. The mod's code declares a UUID (`TexturePacksClient.DeclareOrigin`, see [For mod authors](for-mod-authors.md)).
2. The mod carries a 1x1 marker picture `Textures/origin.<uuid>.png`. The game decodes it into the mod's `LoadedMod.Textures` under the key `origin.<uuid>`, and Texture Packs looks for that key.
3. The mod's id.

A declaration from code wins over a marker. If they name different UUIDs, or a mod has several markers, `Player.log` gets one warning. If two enabled mods end up with the same origin, the first is used and the second gets a warning. `manifest.xml` may also carry the UUID as `<AssetOrigin>`. The game keeps only its own fixed fields and ignores that element, so Texture Packs cannot read it. The [schema script](for-mod-authors.md#the-schema) does, and checks that it agrees with the marker.

The mod id works, but it is a weaker name. Two mods can have the same id, and a mod can be renamed. A UUID the author picks once survives both, and a local copy of the mod and its Workshop copy share it. A pack written for the UUID keeps working through all of these.

## Pictures of other mods

The game decodes the PNG files in a mod's `Items` folder into `LoadedMod.Sprites` and the files in `Paper` into `LoadedMod.PaperSprites`, under the file name without `.png`. Whoever needs the sprite looks it up there each time: `GameItemElement.ResolveSpriteByName` for items, `CustomUIManager.GetModSprite` for icons, `PaperAssets.FromMod` for papers. Texture Packs replaces the entry with a sprite made from the pack's picture. Every reader gets it, and none of them needs a patch.

The new sprite has the original's size, pivot, pixels per unit and border, so the picture must have exactly the original's size. The original goes back into the dictionary when the pack is switched off, when a rebuild no longer picks the picture, and when Texture Packs is disabled.

An item's shape stays as it was, for the same reason as for the game's own items. It comes from the sprite's transparency, which `GameItem.SetSpriteAndShapeFromMod` reads from the dictionary when the item is made. That method is patched: while it runs, the original is back in the dictionary, and afterwards the pack's sprite returns. A pack changes how a mod's item looks and never which cells it takes.

## The provider bridge

A mod that makes sprites in code (`Sprite.Create`, an asset bundle) has no file in `Items` for Texture Packs to replace. Such a mod, a provider, publishes a list of its pictures in two slots of the `AppDomain`, which every mod's assembly shares:

| Slot | Holds |
| --- | --- |
| `texture_packs.v1.sprites.<mod id>` | A function that returns one row per picture: container, name, the original sprite and an `apply` function |
| `texture_packs.v1.origin.<mod id>` | The mod's UUID, if it declares one |

Texture Packs reads the slots on every rebuild and writes nothing back. It also rebuilds when a provider finishes loading. So it does not matter which of the two mods starts first, and a provider works the same when Texture Packs is not installed: nothing reads its slots. `apply` receives the pack's sprite, and later the original, to put it back. The provider keeps owning its sprites. If a picture is offered from code and also exists in the mod's `Items` folder, the one from code is used.

The slots stand in for an API assembly that every provider would share. The game resolves a mod's assembly references only inside that mod's own folder, so another mod would not find a `TexturePacks.Api.dll` that ships with Texture Packs. Instead, a provider copies `template/TexturePacksClient.cs` into its own code. It has three calls, `DeclareOrigin`, `Offer` and `Withdraw`, and needs no reference to Texture Packs. Only sprites can be offered, not textures.

Texture Packs keeps each `apply` function and the sprites it was given until the next rebuild or until the provider is disabled. The code standard forbids keeping objects of the store (`GameItem`, `PixelWindow`, `TreeNodeRender`, scene objects) after the player leaves it, so `apply` must not capture one. Keep the current sprite in a static field that the drawing code reads, or call `Withdraw` when the store is left.

## Which pack wins

For each picture, every switched-on pack that has a file for it is a candidate, the highest in the list first. A candidate the game cannot use is rejected before the order is considered: an unknown name, a mod that is not enabled or has no such picture, or a wrong size. The first usable candidate wins. The usable ones below it count as covered.

## Sprites

Nearly every sprite reaches the game through `RenderHandler`. A postfix on `RenderHandler.LoadFromAtlasRaw` (which `LoadFromAtlas` and `TryLoadFromAtlas` call) and on `RenderHandler.LoadFromFile` swaps the game's sprite for the pack's. The new sprite has the original's size, pivot, pixels per unit and nine-slice border, so the game lays it out as before.

`RenderHandler` keeps the window frame and grid sprites in fields it fills once. Texture Packs sets those fields again after every apply and after `RenderHandler.Awake`.

Item shapes come from the transparency of the item's sprite. `GameItem.BuildShapeFromSprite` is patched to see the game's original, so a pack changes how an item looks and never which cells it takes.

## Scene textures

Backgrounds, fixtures, portraits and most of the interface are textures stored in the scene files. They do not pass through `RenderHandler`. After every scene load, Texture Packs copies the pack's pixels into the game's texture on the graphics card. Every sprite, animation frame and portrait cut from that texture changes at once. The original is kept aside and put back when the packs change or the mod is disabled.

The sizes must match exactly. A texture the game stores compressed as DXT1 or DXT5 is compressed again at run time to match. Other compressed formats cannot be replaced, and a graphics device that cannot copy textures keeps the game's pictures. Each case writes one warning to `Player.log`.

## Tests

`tests/run.sh` checks the identity, the file-name codec and the choice of a mod's origin without the game. `tests/PureTest.cs` holds examples. `tests/DiffTest.cs` generates names and checks that each reads back as the identity it was written from and that no two identities share a name. `tests/schema_test.py` builds a small mod in a temporary folder and checks that the schema script spells file names as the C# codec does, using the same examples. The run ends with the game's own mod scanner on the build.

## The saved order

The order is one line in the config, top first, each pack marked `+` (on) or `-` (off), for example `+hd_items;-old_ui;+dark_shop`. A `%` or `;` inside a pack id is written as `%25` or `%3B`.
