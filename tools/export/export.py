"""Exports the game's own pictures from your copy of the game, for making a texture pack.

    python export.py "<game folder>" [--catalog catalog.tsv] [--out export] [--only CATEGORY] [--overwrite]
                     [--schema FILE]... [--mods DIR]

Every picture listed in catalog.tsv is written as a PNG into <out>/<category>/<group>/, named exactly as a pack
must name it. Files that already exist are kept unless --overwrite is given. The pictures are the game's art:
use them as a reference for your own and do not share them.

--schema FILE adds the pictures of one mod, from its textures.schema.tsv (written by tools/schema/schema.py);
--mods DIR adds every <DIR>/*/textures.schema.tsv, DIR being a Mods folder. They go into <out>/mod/<mod id>/.
A mod's picture is copied from the Items or Paper folder next to its schema; a picture with no file there (one the
mod makes in code) is written as a flat grey placeholder of the right size.
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

from PIL import Image as PilImage
from PIL.Image import Image

from unity_files import (RESOURCE_FILES, SCENE_FILES, Picture, Texture, data_folder, load, resource_sprites,
                         scene_textures, texture_key)

# ---------------------------------------------------------------------------------------------- the catalog

Kind: TypeAlias = Literal["resources", "scene", "mod"]
KINDS: Final[Mapping[str, Kind]] = {"resources": "resources", "scene": "scene", "mod": "mod"}
SCHEMA_NAME: Final[str] = "textures.schema.tsv"
PICTURE_SUFFIXES: Final[tuple[str, ...]] = (".png", ".jpg", ".jpeg")
PLACEHOLDER: Final[tuple[int, int, int, int]] = (128, 128, 128, 255)


@dataclass(frozen=True, slots=True)
class Entry:
    """One row of catalog.tsv or of a mod's schema: a picture a pack can replace.

    A mod's picture also has its container (Items, Paper, ...) and `folder`, the mod folder its schema sits in."""

    file: str
    kind: Kind
    category: str
    group: str
    width: int
    height: int
    source: str
    name: str
    container: str = ""
    folder: Path | None = None

    def target(self, out: Path) -> Path:
        """Where the picture goes: the same category and group folders the placeholders use."""
        return out / self.category / self.group / self.file


def _entry(row: Mapping[str, str], folder: Path | None = None) -> Entry:
    """Turns one catalog.tsv row (columns as written by tools/Catalog), or a schema row, into an Entry."""
    kind = KINDS.get(row["kind"])
    if kind is None:
        raise ValueError(f"unknown kind {row['kind']!r} for {row['file']}")
    return Entry(file=row["file"], kind=kind, category=row["category"], group=row["group"],
                 width=int(row["width"]), height=int(row["height"]), source=row["source"], name=row["name"],
                 container=row.get("container", ""), folder=folder)


def read_catalog(path: Path) -> tuple[Entry, ...]:
    """Every row of catalog.tsv, in file order."""
    with path.open(encoding="utf-8", newline="") as f:
        return tuple(_entry(row) for row in csv.DictReader(f, delimiter="\t"))


def read_schema(path: Path) -> tuple[Entry, ...]:
    """Every row of one mod's textures.schema.tsv. The mod's files are looked for in the schema's own folder."""
    with path.open(encoding="utf-8", newline="") as f:
        return tuple(_entry(row, path.resolve().parent) for row in csv.DictReader(f, delimiter="	"))


def schema_files(schemas: Sequence[Path], mods: Path | None) -> tuple[Path, ...]:
    """The schema files asked for: those given, then the ones in a Mods folder, sorted."""
    found = sorted(mods.glob(f"*/{SCHEMA_NAME}")) if mods is not None else []
    return (*schemas, *found)


def merge(catalog: Sequence[Entry], schemas: Sequence[Entry]) -> tuple[Entry, ...]:
    """The game's rows, then the mods'. Two rows for one target file (a schema given twice) count once, the first."""
    seen: set[tuple[str, str, str]] = set()
    merged: list[Entry] = []
    for e in (*catalog, *schemas):
        key = (e.category, e.group, e.file)
        if key not in seen:
            seen.add(key)
            merged.append(e)
    return tuple(merged)


# ---------------------------------------------------------------------------------------------- the game's pictures

SceneTextures: TypeAlias = Mapping[str, Texture]
ResourceSprites: TypeAlias = Mapping[tuple[str, str], Picture]


def scene_pictures(data: Path) -> SceneTextures:
    """By pack file name ("wall.960x540.png")."""
    return {f"{texture_key(t)}.png": t for ts in scene_textures(data).values() for t in ts}


def resource_pictures(data: Path) -> ResourceSprites:
    """By (Resources path, sprite name)."""
    return {(r.path, r.sprite.m_Name): r.sprite for r in resource_sprites(load(data / f for f in (*RESOURCE_FILES, *SCENE_FILES)))}


def mod_picture(entry: Entry) -> Image:
    """A mod's picture from its own folder (Items/<name>.png, Paper/<name>.png), or a flat placeholder of the schema's
    size when the folder has no file for it, as for a picture the mod makes in code."""
    if entry.folder is not None:
        for suffix in PICTURE_SUFFIXES:
            path = entry.folder / entry.container / f"{entry.name}{suffix}"
            if path.is_file():
                image = PilImage.open(path)
                image.load()
                return image
    return PilImage.new("RGBA", (entry.width, entry.height), PLACEHOLDER)


def picture(entry: Entry, scene: SceneTextures, sprites: ResourceSprites) -> Image | None:
    """The picture for a row, or None when this game version no longer has it (the game's pictures only)."""
    match entry.kind:
        case "mod":
            return mod_picture(entry)
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
    """Writes one picture. A size that differs from the catalog is not written: a pack could not use it."""
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
    """Exports every entry. A kind of picture is read from the game's files only when an entry needs it."""
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
    schemas: tuple[Path, ...]
    mods: Path | None


def parse(argv: Sequence[str]) -> Options:
    """Reads the command line. The catalog defaults to the one next to this script, as in the creator kit."""
    here = Path(__file__).resolve().parent
    p = argparse.ArgumentParser(description="Export the game's pictures for texture-pack work.")
    p.add_argument("game", type=Path, help='the game folder, e.g. "...\\steamapps\\common\\Probably Stolen"')
    p.add_argument("--catalog", type=Path, default=here / "catalog.tsv", help="catalog.tsv from the kit")
    p.add_argument("--out", type=Path, default=Path("export"), help="where to write the pictures (default: ./export)")
    p.add_argument("--only", help="one category: item, ui, scene, character, new-game or mod")
    p.add_argument("--overwrite", action="store_true", help="write files that already exist again")
    p.add_argument("--schema", type=Path, action="append", default=[], help="textures.schema.tsv of a mod (repeatable)")
    p.add_argument("--mods", type=Path, help="a Mods folder: every */textures.schema.tsv in it is used")
    a = p.parse_args(argv)
    return Options(data=data_folder(cast(Path, a.game)), catalog=cast(Path, a.catalog), out=cast(Path, a.out),
                   only=cast("str | None", a.only), overwrite=cast(bool, a.overwrite),
                   schemas=tuple(cast("list[Path]", a.schema)), mods=cast("Path | None", a.mods))


def report(results: Mapping[Entry, Status], out: Path) -> str:
    """One line of counts per status, then every picture that is missing or has the wrong size."""
    counts = Counter(results.values())
    problems = sorted(f"  {status}: {e.file}" for e, status in results.items() if status in ("missing", "wrong size"))
    summary = ", ".join(f"{counts[s]} {s}" for s in STATUSES if counts[s])
    return "\n".join([f"{summary or 'nothing to export'} -> {out.resolve()}", *problems])


def main(argv: Sequence[str]) -> int:
    """Exit code 0 when every picture was written or kept, 1 when some are missing, 2 without game data."""
    opts = parse(argv)
    if not (opts.data / next(iter(SCENE_FILES))).is_file():
        print(f"no game data in {opts.data}", file=sys.stderr)
        return 2
    mods = tuple(e for f in schema_files(opts.schemas, opts.mods) for e in read_schema(f))
    entries = tuple(e for e in merge(read_catalog(opts.catalog), mods) if opts.only in (None, e.category))
    results = export(entries, opts.data, opts.out, opts.overwrite)
    print(report(results, opts.out))
    return 0 if all(s in ("written", "kept") for s in results.values()) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
