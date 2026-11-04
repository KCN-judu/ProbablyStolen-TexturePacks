"""The few parts of the game's Unity files the texture tools read, typed.

UnityPy decodes the files; the protocols below name the fields the tools use, so everything past `load` is checked.
"""

from __future__ import annotations

from collections.abc import Iterable, Iterator, Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Final, Protocol, TypeAlias, cast

import UnityPy
from PIL.Image import Image

DATA_FOLDER: Final[str] = "Probably Stolen_Data"
SCENE_FILES: Final[Mapping[str, str]] = {"sharedassets0.assets": "menu", "sharedassets1.assets": "game"}
RESOURCE_FILES: Final[tuple[str, ...]] = ("globalgamemanagers", "resources.assets")


class TypeInfo(Protocol):
    name: str


class Reader(Protocol):
    """One object in a Unity file, before it is decoded."""

    @property
    def type(self) -> TypeInfo: ...

    @property
    def path_id(self) -> int: ...

    def read(self) -> object: ...


class Pointer(Protocol):
    def deref(self) -> Reader: ...


class Rect(Protocol):
    width: float
    height: float


class Picture(Protocol):
    """A Texture2D or a Sprite, decoded."""

    m_Name: str

    @property
    def image(self) -> Image: ...


class Texture(Picture, Protocol):
    m_Width: int
    m_Height: int


class SpriteRenderData(Protocol):
    texture: Pointer


class Sprite(Picture, Protocol):
    m_Rect: Rect
    m_RD: SpriteRenderData


class ResourceManager(Protocol):
    m_Container: Sequence[tuple[str, Pointer]]


class Environment(Protocol):
    @property
    def objects(self) -> Sequence[Reader]: ...


def data_folder(game: Path) -> Path:
    """Accepts the game folder or its data folder."""
    return game if game.name == DATA_FOLDER else game / DATA_FOLDER


def load(files: Iterable[Path]) -> Environment:
    return cast(Environment, UnityPy.load(*(str(f) for f in files)))


def of_type(objects: Iterable[Reader], type_name: str) -> Iterator[Reader]:
    return (o for o in objects if o.type.name == type_name)


def deref(pointer: Pointer) -> Reader | None:
    """Entries can point into files that are not loaded (fonts, built-in shaders); those are not pictures."""
    try:
        return pointer.deref()
    except Exception:
        return None


def size_key(name: str, width: int, height: int) -> str:
    """How a pack names a scene texture: "wall.960x540"."""
    return f"{name}.{width}x{height}"


def texture_key(t: Texture) -> str:
    return size_key(t.m_Name, t.m_Width, t.m_Height)


@dataclass(frozen=True, slots=True)
class ResourceSprite:
    path: str          # the Resources path, as the game's container lists it ("items/items_tool")
    sprite: Sprite


def resource_sprites(env: Environment) -> tuple[ResourceSprite, ...]:
    """Every sprite the game can load from Resources. The environment must hold RESOURCE_FILES."""
    manager = cast(ResourceManager, next(of_type(env.objects, "ResourceManager")).read())
    targets = ((path, deref(pointer)) for path, pointer in manager.m_Container)
    return tuple(ResourceSprite(path, cast(Sprite, o.read())) for path, o in targets if o is not None and o.type.name == "Sprite")


TexturesByFile: TypeAlias = Mapping[str, tuple[Texture, ...]]


def scene_textures(data: Path) -> TexturesByFile:
    """The textures stored in each scene file."""
    return {f: tuple(cast(Texture, o.read()) for o in of_type(load([data / f]).objects, "Texture2D")) for f in SCENE_FILES}
