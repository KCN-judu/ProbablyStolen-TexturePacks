"""Exports the game's own pictures from your copy of the game, for making a texture pack.

    python export.py "<game folder>" [--catalog catalog.tsv] [--out export] [--only CATEGORY] [--overwrite]

Every picture listed in catalog.tsv is written as a PNG into <out>/<category>/<group>/, named exactly as a pack
must name it. Files that already exist are kept unless --overwrite is given. The pictures are the game's art:
use them as a reference for your own and do not share them.
"""

from __future__ import annotations

import argparse
import csv
import sys
from collections import Counter
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Final, Literal, TypeAlias, cast

from PIL.Image import Image

from unity_files import (RESOURCE_FILES, SCENE_FILES, Picture, Texture, data_folder, load, resource_sprites,
                         scene_textures, texture_key)

# ---------------------------------------------------------------------------------------------- the catalog

Kind: TypeAlias = Literal["resources", "scene"]
KINDS: Final[Mapping[str, Kind]] = {"resources": "resources", "scene": "scene"}


@dataclass(frozen=True, slots=True)
class Entry:
    """One row of catalog.tsv: a picture a pack can replace."""

    file: str
    kind: Kind
    category: str
    group: str
    width: int
    height: int
    source: str
    name: str

    def target(self, out: Path) -> Path:
        return out / self.category / self.group / self.file


def _entry(row: Mapping[str, str]) -> Entry:
    kind = KINDS.get(row["kind"])
    if kind is None:
        raise ValueError(f"unknown kind {row['kind']!r} for {row['file']}")
    return Entry(file=row["file"], kind=kind, category=row["category"], group=row["group"],
                 width=int(row["width"]), height=int(row["height"]), source=row["source"], name=row["name"])


def read_catalog(path: Path) -> tuple[Entry, ...]:
    with path.open(encoding="utf-8", newline="") as f:
        return tuple(_entry(row) for row in csv.DictReader(f, delimiter="\t"))


# ---------------------------------------------------------------------------------------------- the game's pictures

SceneTextures: TypeAlias = Mapping[str, Texture]
ResourceSprites: TypeAlias = Mapping[tuple[str, str], Picture]


def scene_pictures(data: Path) -> SceneTextures:
    """By pack file name ("wall.960x540.png")."""
    return {f"{texture_key(t)}.png": t for ts in scene_textures(data).values() for t in ts}


def resource_pictures(data: Path) -> ResourceSprites:
    """By (Resources path, sprite name)."""
    return {(r.path, r.sprite.m_Name): r.sprite for r in resource_sprites(load(data / f for f in (*RESOURCE_FILES, *SCENE_FILES)))}


def picture(entry: Entry, scene: SceneTextures, sprites: ResourceSprites) -> Image | None:
    match entry.kind:
        case "scene":
            texture = scene.get(entry.file)
            return texture.image if texture is not None else None
        case "resources":
            sprite = sprites.get((entry.source, entry.name))
            return sprite.image if sprite is not None else None


# ---------------------------------------------------------------------------------------------- export

Status: TypeAlias = Literal["written", "kept", "missing", "wrong size"]
STATUSES: Final[tuple[Status, ...]] = ("written", "kept", "missing", "wrong size")


def export_one(entry: Entry, out: Path, overwrite: bool, find: Callable[[Entry], Image | None]) -> Status:
    target = entry.target(out)
    if target.exists() and not overwrite:
        return "kept"
    image = find(entry)
    if image is None:
        return "missing"
    if image.size != (entry.width, entry.height):
        return "wrong size"
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target)
    return "written"


def export(entries: Sequence[Entry], data: Path, out: Path, overwrite: bool) -> Mapping[Entry, Status]:
    kinds = frozenset(e.kind for e in entries)
    scene: SceneTextures = scene_pictures(data) if "scene" in kinds else {}
    sprites: ResourceSprites = resource_pictures(data) if "resources" in kinds else {}
    return {e: export_one(e, out, overwrite, lambda x: picture(x, scene, sprites)) for e in entries}


# ---------------------------------------------------------------------------------------------- command line

@dataclass(frozen=True, slots=True)
class Options:
    data: Path
    catalog: Path
    out: Path
    only: str | None
    overwrite: bool


def parse(argv: Sequence[str]) -> Options:
    here = Path(__file__).resolve().parent
    p = argparse.ArgumentParser(description="Export the game's pictures for texture-pack work.")
    p.add_argument("game", type=Path, help='the game folder, e.g. "...\\steamapps\\common\\Probably Stolen"')
    p.add_argument("--catalog", type=Path, default=here / "catalog.tsv", help="catalog.tsv from the kit")
    p.add_argument("--out", type=Path, default=Path("export"), help="where to write the pictures (default: ./export)")
    p.add_argument("--only", help="one category: item, ui, scene, character or new-game")
    p.add_argument("--overwrite", action="store_true", help="write files that already exist again")
    a = p.parse_args(argv)
    return Options(data=data_folder(cast(Path, a.game)), catalog=cast(Path, a.catalog), out=cast(Path, a.out),
                   only=cast("str | None", a.only), overwrite=cast(bool, a.overwrite))


def report(results: Mapping[Entry, Status], out: Path) -> str:
    counts = Counter(results.values())
    problems = sorted(f"  {status}: {e.file}" for e, status in results.items() if status in ("missing", "wrong size"))
    summary = ", ".join(f"{counts[s]} {s}" for s in STATUSES if counts[s])
    return "\n".join([f"{summary or 'nothing to export'} -> {out.resolve()}", *problems])


def main(argv: Sequence[str]) -> int:
    opts = parse(argv)
    if not (opts.data / next(iter(SCENE_FILES))).is_file():
        print(f"no game data in {opts.data}", file=sys.stderr)
        return 2
    entries = tuple(e for e in read_catalog(opts.catalog) if opts.only in (None, e.category))
    results = export(entries, opts.data, opts.out, opts.overwrite)
    print(report(results, opts.out))
    return 0 if all(s in ("written", "kept") for s in results.values()) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
