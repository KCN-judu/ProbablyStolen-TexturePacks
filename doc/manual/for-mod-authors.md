# For mod authors

If your mod adds items, papers or sprites, texture packs can replace them without any change to your mod's behaviour. This page says what you have to do, which is often nothing, and what you can do to make it work better.

## Items and Paper need nothing

The game decodes the PNG files in your mod's `Items` and `Paper` folders. A pack can replace each of them, with no code and no change in your mod. A pack file for `Items/crusher_body.png` in the mod `machine_mod` is named `machine_mod+Items~crusher_body.png`. The picture must have the size of yours. The shape of an item always comes from your picture, so a pack never changes which cells an item takes.

## Add a UUID

Without a UUID, packs name your mod by its id. That works, but it cannot tell your mod from another mod with the same id, and a pack breaks if you rename the mod. A UUID is recommended.

1. Pick a UUID once and keep it for good, for example with `python -c "import uuid; print(uuid.uuid4())"`.
2. Add a 1x1 picture named `Textures/origin.<uuid>.png` to your mod. The game decodes it, and Texture Packs reads the UUID from its name.
3. Add the same UUID to `manifest.xml`, for example `<AssetOrigin>3f2a9c1e-5b7d-4e2a-9c1e-0a1b2c3d4e5f</AssetOrigin>`. The game ignores this element. The schema script reads it and checks that it matches the marker picture.

A mod with code can declare the UUID with `TexturePacksClient.DeclareOrigin` (below) instead of the marker picture. A pack then names your mod as `3f2a9c1e-5b7d-4e2a-9c1e-0a1b2c3d4e5f+Items~crusher_body.png`.

## Pictures you make in code

A sprite from `Sprite.Create` or an asset bundle has no file in `Items` or `Paper`. To let packs replace it:

1. Copy `TexturePacksClient.cs` into your mod. It is in `template/` in this repository and in `for-mod-authors/` in the creator kit. It needs no reference to Texture Packs.
2. Call `Offer` for each picture in `OnEnable`, and `Withdraw` in `OnDisable`:

```csharp
void OnEnable()
{
    TexturePacksClient.DeclareOrigin("machine_mod", "3f2a9c1e-5b7d-4e2a-9c1e-0a1b2c3d4e5f");
    TexturePacksClient.Offer("machine_mod", "Code", "spark", sparkSprite, s => current = s);
}
void OnDisable() { TexturePacksClient.Withdraw("machine_mod"); }
```

`DeclareOrigin` is optional. `Offer` takes your mod id, a container, the picture's name, your original sprite and a function, `apply`. The container is a word of letters, digits, `_` and `-`. The game decodes only `Items` and `Paper`, so use any other word for what you make in code, such as `Code`. If you offer a picture with the same container and name as a file in your `Items` folder, the offered one is used. A pack names the picture `<UUID or mod id>+<container>~<name>.png`, here `machine_mod+Code~spark.png`, and its file must have the size of your sprite.

Texture Packs calls `apply` with the pack's sprite, and later with your original sprite to put it back. You keep owning your objects. Texture Packs reads your list on every rebuild and again whenever your mod finishes loading, so it does not matter which mod starts first. Without Texture Packs, nothing reads what `Offer` publishes and your mod behaves as if the file were not there.

Texture Packs keeps `apply` and the sprites until its next rebuild or until your mod is disabled. The project's code standard forbids keeping objects of the store (`GameItem`, `PixelWindow`, `TreeNodeRender`, scene objects) after the player leaves it, so `apply` must not capture one. Keep the current sprite in a static field and let your own drawing code read it, as `current` above, or call `Withdraw` when the store is left.

## The schema

The schema lists every picture of your mod that a pack can replace, with its file name, size and your mod's version. Pack creators need it to export your pictures and to check their packs. Write it before each release and ship it in your mod's folder as `textures.schema.tsv`. The game never reads it.

```
python schema.py "<your mod folder>"
```

The script is `schema/schema.py` in the creator kit and `tools/schema/schema.py` in this repository. It needs [Python](https://www.python.org/downloads/) 3.10 or newer, and Pillow (`requirements.txt` next to it) only if your pictures are JPG. It reads your files and never runs your code. `--out FILE` writes the schema somewhere else.

It lists:

- the PNG and JPG files in `Items` and `Paper`, with their sizes;
- the pictures in `textures.extra.tsv`, a file you write next to `manifest.xml` for pictures made in code. Its first line names the columns, and each following line is one picture, with the columns separated by tabs: `container`, `name`, `width`, `height`;
- except the pictures in `textures.exclude`, a file with one `container/name` per line.

```
container	name	width	height
Code	spark	16	16
```

The script takes the UUID from `<AssetOrigin>` and from the marker picture, and stops with an error when they differ. A mod without a UUID gets a warning, and the mod id is used. It also stops with an error for a name that no file name can spell, such as a container with a space or an empty name.

Pictures that live in an asset bundle are not read. List them in `textures.extra.tsv`.

## Try it with the sample provider

`tools/SampleProvider` is a small mod that makes a 16x16 yellow square in code and offers it. It shows what a provider does and tests the whole path in the game.

1. Run `tools/SampleProvider/build.sh --install` and enable **Sample Provider (development)**. `Player.log` says `[SampleProvider] showing the yellow square`. Nothing else happens, and nothing fails, because Texture Packs is not there to read the offer.
2. Enable Texture Packs and a pack with a 16x16 picture named `4b6f1f6c-2d6e-4a46-9f7e-5c1d2a7b8e90+Code~square.png`. Restart the game. `Player.log` now says `[SampleProvider] showing the pack's sprite`.

Switching the pack off in the pack manager and clicking Apply brings the yellow square back.

## What is not supported

- The schema script does not read asset bundles. List their pictures in `textures.extra.tsv`.
- You can offer sprites, not textures (`Texture2D`).
- A pack cannot change an item's shape, only how it looks.
