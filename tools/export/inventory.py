"""Lists what the game ships, for building the texture catalog after a game update.

    python inventory.py "<game folder>" inventory.json

Facts only, no categories: every Resources sprite with its size and whether it is blank, and every texture in the
menu and shop scene files with its size, whether it is blank, how much of it is opaque, and whether it is really a
Resources atlas stored in a scene file. tools/Catalog turns this into catalog.tsv.
"""

from __future__ import annotations

import json
import sys
from collections.abc import Iterable, Mapping, Sequence
from dataclasses import asdict, dataclass
from itertools import groupby
from pathlib import Path
from typing import Final, cast

from PIL.Image import Image

from unity_files import (RESOURCE_FILES, SCENE_FILES, ResourceSprite, Texture, data_folder, deref, load,
                         resource_sprites, scene_textures, texture_key)

BLANK_ALPHA: Final[int] = 8        # no pixel more opaque than this: nothing to see
OPAQUE_ALPHA: Final[int] = 32      # a pixel counts towards coverage from this opacity
LARGEST_SHEET: Final[int] = 2048   # bigger textures are font atlases; their pixels are not measured


@dataclass(frozen=True, slots=True)
class Pixels:
    blank: bool
    coverage: float


@dataclass(frozen=True, slots=True)
class SpriteFacts:
    name: str
    w: int
    h: int
    empty: bool


@dataclass(frozen=True, slots=True)
class Atlas:
    path: str
    sprites: tuple[SpriteFacts, ...]


@dataclass(frozen=True, slots=True)
class TextureFacts:
    name: str
    w: int
    h: int
    empty: bool
    cov: float | None
    dup: bool


@dataclass(frozen=True, slots=True)
class SceneFile:
    file: str
    label: str
    textures: tuple[TextureFacts, ...]


def pixels(image: Image) -> Pixels:
    """Whether the picture is blank, and the share of its area that is opaque, from the alpha histogram."""
    counts: Sequence[int] = image.convert("RGBA").getchannel("A").histogram()
    return Pixels(blank=max(i for i, n in enumerate(counts) if n) <= BLANK_ALPHA,
                  coverage=round(sum(counts[OPAQUE_ALPHA:]) / max(1, image.width * image.height), 3))


def sprite_facts(r: ResourceSprite) -> SpriteFacts:
    """Name, size and blankness of one Resources sprite."""
    return SpriteFacts(name=r.sprite.m_Name, w=round(r.sprite.m_Rect.width), h=round(r.sprite.m_Rect.height),
                       empty=pixels(r.sprite.image).blank)


def atlases(sprites: Iterable[ResourceSprite]) -> tuple[Atlas, ...]:
    """The sprites grouped by Resources path, sorted so the inventory file does not change between runs."""
    ordered = sorted(sprites, key=lambda r: (r.path, r.sprite.m_Name))
    return tuple(Atlas(path, tuple(sprite_facts(r) for r in group)) for path, group in groupby(ordered, key=lambda r: r.path))


def resource_texture_keys(sprites: Iterable[ResourceSprite]) -> frozenset[str]:
    """The textures Resources sprites are cut from; some of them are stored in a scene file."""
    readers = (deref(r.sprite.m_RD.texture) for r in sprites)
    return frozenset(texture_key(cast(Texture, o.read())) for o in readers if o is not None and o.type.name == "Texture2D")


def texture_facts(t: Texture, resources: frozenset[str]) -> TextureFacts:
    """Facts of one scene texture; dup marks one that is really a Resources atlas, listed under resources."""
    measured = 0 < max(t.m_Width, t.m_Height) <= LARGEST_SHEET and min(t.m_Width, t.m_Height) > 0
    p = pixels(t.image) if measured else None
    return TextureFacts(name=t.m_Name, w=t.m_Width, h=t.m_Height, empty=p.blank if p else False,
                        cov=p.coverage if p else None, dup=texture_key(t) in resources)


def inventory(data: Path) -> Mapping[str, object]:
    """The whole inventory as plain data, ready for json.dumps: {"resources": [...], "scenes": [...]}."""
    # The scene files are loaded with Resources so that sprites can reach atlases stored in them.
    sprites = resource_sprites(load(data / f for f in (*RESOURCE_FILES, *SCENE_FILES)))
    resources = resource_texture_keys(sprites)
    scenes = tuple(SceneFile(f, SCENE_FILES[f], tuple(texture_facts(t, resources) for t in ts))
                   for f, ts in scene_textures(data).items())
    return {"resources": [asdict(a) for a in atlases(sprites)], "scenes": [asdict(s) for s in scenes]}


def main(argv: Sequence[str]) -> int:
    """Writes the inventory file. Needed only after a game update, to refresh the catalog."""
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    data, out = data_folder(Path(argv[0])), Path(argv[1])
    if not (data / next(iter(SCENE_FILES))).is_file():
        print(f"no game data in {data}", file=sys.stderr)
        return 2
    out.write_text(json.dumps(inventory(data), ensure_ascii=False), encoding="utf-8")
    print(f"wrote {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
