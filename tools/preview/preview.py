"""Layered preview of the shop view, with a texture pack applied, to see a pack before the game starts.

    python tools/preview/preview.py "<game folder>" [--pack FOLDER] [--out FILE] [--scale N] [--compare]
                                    [--hide NAME[,NAME...]] [--list]

The shop is a stack of sprites in the game's scene (`level1`): the store's background layers, the wall the customer
stands behind, the desk, the counter, the fixtures on it, the window and the door. Each is a SpriteRenderer with a
sorting order and a position; this script reads them from the game files and draws them in that order on the 960x540
view, as the game's camera does. Nothing is started and nothing is written to the game.

--pack takes a folder of PNG files named as a pack names them (a pack's Textures folder, or a folder of drawings):
a scene texture `store_mid.960x540.png` replaces the whole texture, so every sprite cut from it changes; a Resources
sprite `items~items_vending@vending0.png` replaces that sprite. Files of another size than the game's are left out
and listed. --compare writes the game's own picture above the pack's. --hide drops layers by their object name, and
--list prints the layers in drawing order instead of rendering.

What it does not draw: the customer (animated in code), text and the lit windows of the UI, and the glow sprites'
exact blending (they are added to the picture, as the game does, but without its post-processing). It is a way to
look at a pack, not a replica of the game's screen.
"""

from __future__ import annotations

import argparse
import sys
from collections.abc import Iterable, Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Final, Protocol, TypeAlias, cast

import UnityPy
from PIL import Image, ImageChops

DATA_FOLDER: Final = "Probably Stolen_Data"
SCENE_FILE: Final = "level1"
SCENE_ASSETS: Final = "sharedassets1.assets"
SUPPORT_FILES: Final = ("globalgamemanagers", "globalgamemanagers.assets", "resources.assets")
VIEW: Final = (960, 540)
PIXELS_PER_UNIT: Final = 100.0
# Objects of the scene file that are not part of the shop view: other scenes the file holds, the customer and the
# security guards (animated in code), the shooting and magnifier overlays, and test objects.
SKIP: Final = frozenset({
    "inventor_scene", "Commissary", "DumpingGround", "TestClientObject", "_ClientGameObject", "_BodyGameObject",
    "_SecGameObject1", "_SecGameObject2", "_ShootingAnimation", "_ShootingWound", "_Arm", "_MuzzleFlash", "_Shotgun",
    "_MagnifyingWindow", "_MagnifyingWindowCollapsed", "test3", "test3 (1)", "Walker1", "GameObject",
    "prefabFarBackpack", "prefabFarGasmask", "prefabFarPuncho", "prefabFarBandana", "prefabFarTrenchCoat",
    "prefabMidTrenchCoat", "prefabMidGasmask", "prefabMidPuncho", "prefabMidBackpack", "prefabMidBandana",
})
ADDITIVE: Final = frozenset({"halo_glow"})

Json: TypeAlias = Mapping[str, object]


class Entry(Protocol):
    """One object of a Unity file, as UnityPy gives it: enough to read its fields and find the file it sits in."""

    path_id: int
    assets_file: Any

    @property
    def type(self) -> Any: ...

    def read(self) -> Any: ...

    def read_typetree(self) -> Json: ...


@dataclass(frozen=True, slots=True)
class SpriteRef:
    """A sprite as the scene refers to it: where it is cut from its texture, and where its pivot is."""

    texture: str
    name: str
    rect: tuple[int, int, int, int]       # x, y, width, height in the texture, from its bottom left corner
    pivot: tuple[float, float]            # in pixels from the bottom left corner of the sprite
    texture_size: tuple[int, int]


@dataclass(frozen=True, slots=True)
class Layer:
    """One drawn sprite: the object's name, its sprite, and where and how it is put on the view."""

    name: str
    sprite: SpriteRef
    order: int
    x: float
    y: float
    scale: tuple[float, float]
    flip: tuple[bool, bool]
    color: tuple[float, float, float, float]

    @property
    def additive(self) -> bool:
        return self.sprite.name.split("_")[0] in ADDITIVE or self.sprite.texture in ADDITIVE


# ---------------------------------------------------------------------------------------------- the pack's names


@dataclass(frozen=True, slots=True)
class PackKey:
    """What a pack file name replaces, for the game's own pictures (the spelling is AssetIdCodec's in the mod)."""

    texture: str            # a scene texture's name, or the last segment of a Resources path
    size: tuple[int, int] | None   # the scene texture's size
    sprite: str | None      # the sprite inside a Resources atlas


def parse_pack_name(stem: str) -> PackKey | None:
    """The game's names: `wall.960x540`, `items~items_vending@vending0`, `window~machinery~bevel_frame`."""
    if "+" in stem:
        return None
    dot = stem.rfind(".")
    if dot > 0:
        w, _, h = stem[dot + 1:].partition("x")
        if w.isdigit() and h.isdigit() and 0 < int(w) < 100000 and 0 < int(h) < 100000:
            return PackKey(stem[:dot], (int(w), int(h)), None)
    if "~" not in stem:
        return None
    path, at, sprite = stem.partition("@")
    return PackKey(path.split("~")[-1], None, sprite if at else None)


def read_pack(folder: Path) -> Mapping[PackKey, Image.Image]:
    return {k: Image.open(p).convert("RGBA") for p in sorted(folder.glob("*.png"))
            if (k := parse_pack_name(p.stem)) is not None}


# ---------------------------------------------------------------------------------------------- the scene


def num(value: object) -> float:
    return float(cast("float", value))


def vec(value: object, *keys: str) -> tuple[float, ...]:
    table = cast("Json", value)
    return tuple(num(table[k]) for k in keys)


def load_scene(game: Path) -> tuple[tuple[Layer, ...], Mapping[str, Image.Image]]:
    """The shop's layers in drawing order, and the images of the textures they use, by texture name."""
    data = game / DATA_FOLDER
    env = UnityPy.load(*(str(data / f) for f in (SCENE_FILE, SCENE_ASSETS, *SUPPORT_FILES) if (data / f).exists()))
    files = {Path(k).name: v for k, v in env.files.items()}
    scene = [o for o in env.objects if o.assets_file.name == SCENE_FILE]
    trees = {o.path_id: o.read_typetree() for o in scene if o.type.name in ("GameObject", "Transform", "SpriteRenderer")}
    kind = {o.path_id: o.type.name for o in scene}

    def resolve(owner: Entry, pointer: Json) -> Entry | None:
        """The object a PPtr names, which may sit in another asset file."""
        af = owner.assets_file
        file_id, path_id = int(cast("int", pointer["m_FileID"])), int(cast("int", pointer["m_PathID"]))
        target = af if file_id == 0 else files.get(Path(af.externals[file_id - 1].path).name)
        return cast("Entry | None", getattr(target, "objects", {}).get(path_id)) if path_id else None

    transform_of: dict[int, int] = {}
    for pid, tree in trees.items():
        if kind[pid] == "GameObject":
            for c in cast("Sequence[Json]", tree["m_Component"]):
                cp = int(cast("int", cast("Json", c["component"])["m_PathID"]))
                if kind.get(cp) == "Transform":
                    transform_of[pid] = cp
    game_object_of = {v: k for k, v in transform_of.items()}

    def chain(tid: int) -> list[int]:
        out: list[int] = []
        while tid in trees:
            out.append(tid)
            tid = int(cast("int", cast("Json", trees[tid]["m_Father"])["m_PathID"]))
        return out

    def world(tid: int) -> tuple[float, float, float, float]:
        """Position and scale in the scene: each parent's scale applies to its children's offsets."""
        x = y = 0.0
        sx = sy = 1.0
        for t in reversed(chain(tid)):
            px, py, _ = vec(trees[t]["m_LocalPosition"], "x", "y", "z")
            lx, ly, _ = vec(trees[t]["m_LocalScale"], "x", "y", "z")
            x, y = x + px * sx, y + py * sy
            sx, sy = sx * lx, sy * ly
        return x, y, sx, sy

    def active(tid: int) -> bool:
        return all(bool(trees[game_object_of[t]]["m_IsActive"]) for t in chain(tid) if t in game_object_of)

    sprite_cache: dict[tuple[int, int], tuple[SpriteRef, Image.Image]] = {}
    textures: dict[str, Image.Image] = {}

    def sprite_of(owner: Entry, pointer: Json) -> SpriteRef | None:
        target = resolve(owner, pointer)
        if target is None:
            return None
        key = (id(target.assets_file), target.path_id)
        if key not in sprite_cache:
            sprite = target.read_typetree()
            rd = cast("Json", sprite["m_RD"])
            texture_object = resolve(target, cast("Json", rd["texture"]))
            if texture_object is None:
                return None
            texture = texture_object.read()
            tex_name = str(texture.m_Name)
            textures.setdefault(tex_name, texture.image.convert("RGBA"))
            x, y, w, h = vec(rd["textureRect"], "x", "y", "width", "height")
            px, py = vec(sprite["m_Pivot"], "x", "y")
            rect = (int(round(x)), int(round(y)), int(round(w)), int(round(h)))
            ref = SpriteRef(tex_name, str(sprite["m_Name"]), rect, (px * rect[2], py * rect[3]), textures[tex_name].size)
            sprite_cache[key] = (ref, textures[tex_name])
        return sprite_cache[key][0]

    layers: list[Layer] = []
    for o in scene:
        if o.type.name != "SpriteRenderer":
            continue
        sr = trees[o.path_id]
        gid = int(cast("int", cast("Json", sr["m_GameObject"])["m_PathID"]))
        tid = transform_of.get(gid)
        name = str(trees[gid]["m_Name"])
        if tid is None or name in SKIP or not sr["m_Enabled"] or not active(tid):
            continue
        ref = sprite_of(o, cast("Json", sr["m_Sprite"]))
        if ref is None:
            continue
        x, y, sx, sy = world(tid)
        layers.append(Layer(name, ref, int(cast("int", sr["m_SortingOrder"])), x, y, (sx, sy),
                            (bool(sr["m_FlipX"]), bool(sr["m_FlipY"])), cast("tuple[float, float, float, float]", vec(sr["m_Color"], "r", "g", "b", "a"))))
    ordered = tuple(sorted(layers, key=lambda layer: layer.order))   # stable: equal orders keep the scene's order
    return ordered, textures


# ---------------------------------------------------------------------------------------------- drawing


def crop_sprite(texture: Image.Image, ref: SpriteRef) -> Image.Image:
    x, y, w, h = ref.rect
    _, height = texture.size
    return texture.crop((x, height - y - h, x + w, height - y))


def sprite_image(layer: Layer, textures: Mapping[str, Image.Image], pack: Mapping[PackKey, Image.Image],
                 skipped: list[str]) -> Image.Image:
    """The layer's picture: the pack's if it has one that fits, the game's otherwise."""
    ref = layer.sprite
    own = pack.get(PackKey(ref.texture, ref.texture_size, None))        # the whole scene texture
    if own is not None:
        return crop_sprite(own, ref)
    single = pack.get(PackKey(ref.texture, None, ref.name))             # one sprite of a Resources atlas
    if single is not None:
        if single.size == ref.rect[2:]:
            return single
        skipped.append(f"{ref.texture}@{ref.name} is {single.width}x{single.height}, the game's is {ref.rect[2]}x{ref.rect[3]}")
    return crop_sprite(textures[ref.texture], ref)


def tint(image: Image.Image, color: tuple[float, float, float, float]) -> Image.Image:
    if color == (1.0, 1.0, 1.0, 1.0):
        return image
    r, g, b, a = image.split()
    scale = [lambda v, k=k: int(v * k) for k in color]
    return Image.merge("RGBA", (r.point(scale[0]), g.point(scale[1]), b.point(scale[2]), a.point(scale[3])))


def place(canvas: Image.Image, layer: Layer, image: Image.Image) -> None:
    """Puts a sprite with its pivot at the layer's position (the view's centre is (0, 0), y up)."""
    sx, sy = layer.scale
    image = tint(image, layer.color)
    if layer.flip[0] != (sx < 0):
        image = image.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    if layer.flip[1] != (sy < 0):
        image = image.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    w, h = image.size
    if (abs(sx), abs(sy)) != (1.0, 1.0):
        w, h = max(1, round(w * abs(sx))), max(1, round(h * abs(sy)))
        image = image.resize((w, h), Image.Resampling.NEAREST)
    px, py = layer.sprite.pivot
    left = round(VIEW[0] / 2 + layer.x * PIXELS_PER_UNIT - px * abs(sx))
    top = round(VIEW[1] / 2 - layer.y * PIXELS_PER_UNIT - (layer.sprite.rect[3] - py) * abs(sy))
    if layer.additive:
        patch = canvas.crop((left, top, left + w, top + h))
        added = ImageChops.add(patch.convert("RGB"), Image.composite(image.convert("RGB"), Image.new("RGB", (w, h)), image.getchannel("A")))
        canvas.paste(added, (left, top))
    else:
        canvas.alpha_composite(image, (left, top)) if left >= 0 and top >= 0 and left + w <= VIEW[0] and top + h <= VIEW[1] \
            else paste_clipped(canvas, image, left, top)


def paste_clipped(canvas: Image.Image, image: Image.Image, left: int, top: int) -> None:
    """alpha_composite needs the sprite inside the canvas; a sprite that overhangs the view is cropped first."""
    x0, y0 = max(0, -left), max(0, -top)
    x1, y1 = min(image.width, VIEW[0] - left), min(image.height, VIEW[1] - top)
    if x1 > x0 and y1 > y0:
        canvas.alpha_composite(image.crop((x0, y0, x1, y1)), (left + x0, top + y0))


def render(layers: Iterable[Layer], textures: Mapping[str, Image.Image], pack: Mapping[PackKey, Image.Image],
           hidden: frozenset[str]) -> tuple[Image.Image, list[str]]:
    canvas = Image.new("RGBA", VIEW, (0, 0, 0, 255))
    skipped: list[str] = []
    for layer in layers:
        if layer.name not in hidden:
            place(canvas, layer, sprite_image(layer, textures, pack, skipped))
    return canvas, sorted(set(skipped))


def stack(top: Image.Image, bottom: Image.Image) -> Image.Image:
    both = Image.new("RGBA", (top.width, top.height * 2 + 4), (40, 40, 48, 255))
    both.paste(top, (0, 0))
    both.paste(bottom, (0, top.height + 4))
    return both


# ---------------------------------------------------------------------------------------------- command line


@dataclass(frozen=True, slots=True)
class Options:
    game: Path
    pack: Path | None
    out: Path
    scale: int
    compare: bool
    hide: frozenset[str]
    list_layers: bool


def parse(argv: Sequence[str]) -> Options:
    p = argparse.ArgumentParser(description="Layered preview of the shop view with a texture pack applied.")
    p.add_argument("game", type=Path, help="the game folder or its data folder")
    p.add_argument("--pack", type=Path, help="a folder of PNG files named as a pack names them")
    p.add_argument("--out", type=Path, default=Path("preview.png"), help="where to write the picture (default: preview.png)")
    p.add_argument("--scale", type=int, default=1, help="whole-number enlargement, nearest neighbour (default 1)")
    p.add_argument("--compare", action="store_true", help="the game's picture above the pack's")
    p.add_argument("--hide", default="", help="object names to leave out, separated by commas")
    p.add_argument("--list", action="store_true", dest="list_layers", help="print the layers in drawing order and stop")
    a = p.parse_args(argv)
    game = cast(Path, a.game)
    return Options(game.parent if game.name == DATA_FOLDER else game, cast("Path | None", a.pack), cast(Path, a.out),
                   max(1, cast(int, a.scale)), cast(bool, a.compare), frozenset(s for s in cast(str, a.hide).split(",") if s),
                   cast(bool, a.list_layers))


def main(argv: Sequence[str]) -> int:
    opts = parse(argv)
    layers, textures = load_scene(opts.game)
    if opts.list_layers:
        for layer in layers:
            print(f"{layer.order:4d}  {layer.name:28s} {layer.sprite.texture}@{layer.sprite.name}  at ({layer.x:.2f}, {layer.y:.2f})")
        return 0
    pack = read_pack(opts.pack) if opts.pack is not None else {}
    picture, skipped = render(layers, textures, pack, opts.hide)
    if opts.compare:
        picture = stack(render(layers, textures, {}, opts.hide)[0], picture)
    if opts.scale > 1:
        picture = picture.resize((picture.width * opts.scale, picture.height * opts.scale), Image.Resampling.NEAREST)
    picture.convert("RGB").save(opts.out)
    print(f"{opts.out}: {len(layers)} layers, {len(pack)} pack picture(s)")
    for line in skipped:
        print("  left out:", line)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
