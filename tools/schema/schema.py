"""Writes the schema of a mod: every picture of the mod that a texture pack can replace.

    python schema.py "<mod folder>" [--out FILE]

The schema is `textures.schema.tsv`, written into the mod folder unless --out says otherwise. It has the first eight
columns of the game's catalog.tsv (file kind category group width height source name), then origin, container and
version, so the export script and the pack builder treat a mod's pictures like the game's. `file` is the name a pack
gives the picture, `<origin>+<container>~<name>.png`: the same spelling as `AssetIdCodec` in src/AssetIdCodec.Pure.cs.
Run it before a release and ship the file with the mod. The game never reads it.

It reads the mod's files and never runs its code. What it lists:

  - the PNG and JPG files in `Items/` and `Paper/`, by file name without extension, with their size;
  - the pictures in `textures.extra.tsv` next to `manifest.xml`, for pictures the mod makes in code. Its first line is
    `container<TAB>name<TAB>width<TAB>height`; empty lines and lines starting with `#` are skipped;
  - minus the pictures named in `textures.exclude`, one `container/name` per line.

It does NOT read asset bundles in this version: list the pictures that live in a bundle in `textures.extra.tsv`.

The origin is the mod's UUID, from `<AssetOrigin>` in `manifest.xml` and from the marker picture
`Textures/origin.<uuid>.png`; when both exist they must agree. Without a UUID the mod id is the origin, with a warning.
Exit code 0 on success, 1 when the mod cannot be described (the message says why).
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from collections.abc import Iterable, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Final, cast

SCHEMA_FILE: Final[str] = "textures.schema.tsv"
EXTRA_FILE: Final[str] = "textures.extra.tsv"
EXCLUDE_FILE: Final[str] = "textures.exclude"
FOLDERS: Final[tuple[str, ...]] = ("Items", "Paper")
PICTURE_SUFFIXES: Final[tuple[str, ...]] = (".png", ".jpg", ".jpeg")
COLUMNS: Final[tuple[str, ...]] = ("file", "kind", "category", "group", "width", "height", "source", "name",
                                   "origin", "container", "version")

MAX_ID_LENGTH: Final[int] = 64
_MOD_ID = re.compile(r"[A-Za-z0-9_\-.]+")
_UUID = re.compile(r"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")
_CONTAINER = re.compile(r"[A-Za-z0-9_\-]+")
_MARKER = "origin."
# What no Windows file name can hold. A pack file is named after the picture, so the picture's name cannot hold them.
_FORBIDDEN_IN_NAME = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


class SchemaError(Exception):
    """The mod cannot be described. The message says what to fix."""


# ---------------------------------------------------------------------------------------------- the file name

def is_mod_id(text: str) -> bool:
    """A mod id as the game accepts it (ModLoader.IsValidModId, PackIds.IsValid): 1 to 64 characters, ASCII letters,
    digits, '_', '-' and '.', not starting with '.'."""
    return 0 < len(text) <= MAX_ID_LENGTH and text[0] != "." and _MOD_ID.fullmatch(text) is not None


def is_uuid(text: str) -> bool:
    """A hyphenated UUID of 36 characters, in any case."""
    return _UUID.fullmatch(text) is not None


def is_container(text: str) -> bool:
    """One word of the file name: ASCII letters, digits, '_' and '-'."""
    return _CONTAINER.fullmatch(text) is not None


def origin_token(text: str) -> str:
    """How a mod's origin is written in a file name: a UUID in lower case, or the mod id as it is."""
    if is_uuid(text):
        return text.lower()
    if not is_mod_id(text):
        raise SchemaError(f"'{text}' is not a mod id the game accepts: 1 to {MAX_ID_LENGTH} ASCII letters, digits, "
                          "'_', '-' and '.', not starting with '.'")
    return text


def check_name(container: str, name: str) -> None:
    """Raises when AssetIdCodec could not spell the picture, or its file could not exist."""
    if not is_container(container):
        raise SchemaError(f"the container '{container}' must be one word of letters, digits, '_' and '-'")
    if not name:
        raise SchemaError(f"a picture in {container} has an empty name")
    if _FORBIDDEN_IN_NAME.search(name) or name != name.rstrip(" ."):
        raise SchemaError(f"the name '{name}' ({container}) cannot be a file name: no '<>:\"/\\|?*', "
                          "no control characters, no space or dot at the end")


def spell(origin: str, container: str, name: str) -> str:
    """The pack file name of a mod's picture, equal to AssetIdCodec.Format in C#: `<origin>+<container>~<name>.png`."""
    check_name(container, name)
    return f"{origin_token(origin)}+{container}~{name}.png"


# ---------------------------------------------------------------------------------------------- what is read

@dataclass(frozen=True, slots=True)
class Manifest:
    """The fields of manifest.xml this script uses."""

    mod_id: str
    version: str
    asset_origin: str | None


@dataclass(frozen=True, slots=True)
class Found:
    """One picture: where the mod keeps it, and its size."""

    container: str
    name: str
    width: int
    height: int


@dataclass(frozen=True, slots=True)
class ModFiles:
    """Everything the schema is made from, read from the mod folder."""

    folder_name: str
    manifest_text: str
    marker_keys: tuple[str, ...]            # file names in Textures/ without ".png", every one
    pictures: tuple[Found, ...]             # Items/ and Paper/
    extra_text: str | None
    exclude_text: str | None


@dataclass(frozen=True, slots=True)
class Row:
    """One line of textures.schema.tsv."""

    file: str
    group: str
    width: int
    height: int
    source: str
    name: str
    origin: str
    container: str
    version: str

    def cells(self) -> tuple[str, ...]:
        return (self.file, "mod", "mod", self.group, str(self.width), str(self.height), self.source, self.name,
                self.origin, self.container, self.version)


@dataclass(frozen=True, slots=True)
class Schema:
    rows: tuple[Row, ...]
    warnings: tuple[str, ...]


def parse_manifest(text: str) -> Manifest:
    """ID, ModVersion and the optional AssetOrigin of a manifest.xml."""
    try:
        root = ET.fromstring(text)
    except ET.ParseError as e:
        raise SchemaError(f"manifest.xml is not valid XML: {e}") from e

    def field(tag: str) -> str | None:
        element = root.find(tag)
        return element.text.strip() if element is not None and element.text is not None else None

    mod_id = field("ID")
    if not mod_id:
        raise SchemaError("manifest.xml has no <ID>")
    return Manifest(mod_id=mod_id, version=field("ModVersion") or "", asset_origin=field("AssetOrigin") or None)


def markers_in(keys: Iterable[str]) -> tuple[str, ...]:
    """The UUIDs of the marker pictures `origin.<uuid>` among the Textures keys, lower case, sorted, without repeats."""
    found = {k[len(_MARKER):].lower() for k in keys if k.startswith(_MARKER) and is_uuid(k[len(_MARKER):])}
    return tuple(sorted(found))


def resolve_origin(manifest: Manifest, markers: Sequence[str]) -> tuple[str, tuple[str, ...]]:
    """The origin token for the file names, and the warnings. Raises when the declarations disagree."""
    if len(markers) > 1:
        raise SchemaError("more than one origin marker picture in Textures/: " + ", ".join(markers))
    declared = manifest.asset_origin
    if declared is not None and not is_uuid(declared):
        raise SchemaError(f"<AssetOrigin> '{declared}' is not a hyphenated UUID")
    if declared is not None and markers and declared.lower() != markers[0]:
        raise SchemaError(f"<AssetOrigin> {declared.lower()} and the marker picture Textures/{_MARKER}{markers[0]}.png "
                          "name different UUIDs")
    if declared is not None:
        if not markers:
            return declared.lower(), ("<AssetOrigin> has no marker picture Textures/origin.<uuid>.png next to it: "
                                      "the game does not read the manifest element, so Texture Packs cannot "
                                      "learn the UUID unless the mod's code declares it",)
        return declared.lower(), ()
    if markers:
        return markers[0], ("the UUID is only in the marker picture; add <AssetOrigin> to manifest.xml too, "
                            "so tools can read it",)
    return origin_token(manifest.mod_id), (
        f"no UUID: packs will name this mod by its id '{manifest.mod_id}', which cannot tell it apart from another "
        "mod with the same id. Add <AssetOrigin> and Textures/origin.<uuid>.png (a 1x1 picture)",)


def _positive(text: str, where: str) -> int:
    if not text.isascii() or not text.isdigit() or int(text) <= 0:
        raise SchemaError(f"{where}: '{text}' is not a positive whole number")
    return int(text)


def parse_extra(text: str) -> tuple[Found, ...]:
    """The pictures of textures.extra.tsv: a header line, then container, name, width and height per line."""
    lines = [(n, line) for n, line in enumerate(text.splitlines(), 1) if line.strip() and not line.startswith("#")]
    if not lines or lines[0][1].split("\t") != ["container", "name", "width", "height"]:
        raise SchemaError(f"{EXTRA_FILE}: the first line must be: container<TAB>name<TAB>width<TAB>height")
    found: list[Found] = []
    for n, line in lines[1:]:
        cells = line.split("\t")
        if len(cells) != 4:
            raise SchemaError(f"{EXTRA_FILE} line {n}: expected 4 columns separated by tabs, found {len(cells)}")
        where = f"{EXTRA_FILE} line {n}"
        found.append(Found(cells[0], cells[1], _positive(cells[2], where), _positive(cells[3], where)))
    return tuple(found)


def parse_exclude(text: str) -> tuple[tuple[str, str], ...]:
    """The pictures of textures.exclude as (container, name): one `container/name` per line."""
    pairs: list[tuple[str, str]] = []
    for n, line in enumerate(text.splitlines(), 1):
        if not line.strip() or line.startswith("#"):
            continue
        container, slash, name = line.strip().partition("/")
        if not slash or not container or not name:
            raise SchemaError(f"{EXCLUDE_FILE} line {n}: expected container/name, found '{line}'")
        pairs.append((container, name))
    return tuple(pairs)


def merge_pictures(folders: Sequence[Found], extra: Sequence[Found]) -> tuple[Found, ...]:
    """The mod's pictures by container and name. A picture in both places must have one size."""
    merged: dict[tuple[str, str], Found] = {}
    for f in folders:
        merged[(f.container, f.name)] = f
    seen: set[tuple[str, str]] = set()
    for f in extra:
        key = (f.container, f.name)
        if key in seen:
            raise SchemaError(f"{EXTRA_FILE} lists {f.container}/{f.name} twice")
        seen.add(key)
        have = merged.get(key)
        if have is not None and (have.width, have.height) != (f.width, f.height):
            raise SchemaError(f"{f.container}/{f.name} is {have.width}x{have.height} in the folder but "
                              f"{f.width}x{f.height} in {EXTRA_FILE}")
        merged[key] = have or f
    return tuple(merged.values())


def make_schema(files: ModFiles) -> Schema:
    """The rows of the schema, sorted by file name, and the warnings. Pure: everything it needs is in `files`."""
    manifest = parse_manifest(files.manifest_text)
    origin, warnings = resolve_origin(manifest, markers_in(files.marker_keys))
    extra = parse_extra(files.extra_text) if files.extra_text is not None else ()
    pictures = merge_pictures(files.pictures, extra)
    excluded = set(parse_exclude(files.exclude_text)) if files.exclude_text is not None else set()
    names = {(p.container, p.name) for p in pictures}
    unused = tuple(f"{EXCLUDE_FILE}: {c}/{n} matches no picture" for c, n in sorted(excluded - names))
    rows = tuple(sorted(
        (Row(file=spell(origin, p.container, p.name), group=manifest.mod_id, width=p.width, height=p.height,
             source=files.folder_name, name=p.name, origin=origin_token(origin), container=p.container,
             version=manifest.version) for p in pictures if (p.container, p.name) not in excluded),
        key=lambda r: r.file))
    return Schema(rows=rows, warnings=(*warnings, *unused))


def render(rows: Sequence[Row]) -> str:
    """The text of textures.schema.tsv: a header line, then a line per row, tab separated, ending in a newline."""
    return "".join("\t".join(cells) + "\n" for cells in (COLUMNS, *(r.cells() for r in rows)))


# ---------------------------------------------------------------------------------------------- reading the folder

PNG_SIGNATURE: Final[bytes] = b"\x89PNG\r\n\x1a\n"


def png_size(head: bytes) -> tuple[int, int] | None:
    """The size from the first 24 bytes of a PNG (the IHDR chunk), or None when it is not a PNG."""
    if len(head) < 24 or head[:8] != PNG_SIGNATURE or head[12:16] != b"IHDR":
        return None
    return int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")


def image_size(path: Path) -> tuple[int, int]:
    """The size of a picture file. A PNG is read from its header; a JPG with Pillow, which only a mod with JPGs needs."""
    with path.open("rb") as f:
        head = f.read(24)
    size = png_size(head)
    if size is not None:
        return size
    if path.suffix.lower() == ".png":
        raise SchemaError(f"{path.name} is not a PNG file")
    from PIL import Image  # imported here: a mod of PNG files does not need Pillow
    with Image.open(path) as image:
        return image.size


def read_pictures(folder: Path) -> tuple[Found, ...]:
    """The pictures in `Items/` and `Paper/`. A name used by two files (a.png and a.jpg) is an error."""
    found: dict[tuple[str, str], Found] = {}
    for container in FOLDERS:
        directory = folder / container
        if not directory.is_dir():
            continue
        for path in sorted(directory.iterdir()):
            if not path.is_file() or path.suffix.lower() not in PICTURE_SUFFIXES:
                continue
            key = (container, path.stem)
            if key in found:
                raise SchemaError(f"{container}/{path.stem} is in two files with different extensions")
            width, height = image_size(path)
            found[key] = Found(container, path.stem, width, height)
    return tuple(found.values())


def _read_text(path: Path) -> str | None:
    return path.read_bytes().decode("utf-8-sig") if path.is_file() else None


def read_mod(folder: Path) -> ModFiles:
    """Reads what the schema is made from. The only place that touches the mod folder."""
    manifest = _read_text(folder / "manifest.xml")
    if manifest is None:
        raise SchemaError(f"no manifest.xml in {folder}")
    textures = folder / "Textures"
    keys = tuple(sorted(p.stem for p in textures.glob("*.png"))) if textures.is_dir() else ()
    return ModFiles(folder_name=folder.name, manifest_text=manifest, marker_keys=keys, pictures=read_pictures(folder),
                    extra_text=_read_text(folder / EXTRA_FILE), exclude_text=_read_text(folder / EXCLUDE_FILE))


def write_schema(path: Path, rows: Sequence[Row]) -> None:
    """UTF-8 without a byte order mark, LF line ends, whatever the platform."""
    path.write_bytes(render(rows).encode("utf-8"))


# ---------------------------------------------------------------------------------------------- command line

@dataclass(frozen=True, slots=True)
class Options:
    folder: Path
    out: Path


def parse(argv: Sequence[str]) -> Options:
    p = argparse.ArgumentParser(description="Write the schema of a mod: the pictures a texture pack can replace.")
    p.add_argument("folder", type=Path, help="the mod folder, the one with manifest.xml")
    p.add_argument("--out", type=Path, help=f"where to write the schema (default: {SCHEMA_FILE} in the mod folder)")
    a = p.parse_args(argv)
    folder = cast(Path, a.folder).resolve()
    out = cast("Path | None", a.out)
    return Options(folder=folder, out=out if out is not None else folder / SCHEMA_FILE)


def main(argv: Sequence[str]) -> int:
    opts = parse(argv)
    try:
        if not opts.folder.is_dir():
            raise SchemaError(f"{opts.folder} is not a folder")
        schema = make_schema(read_mod(opts.folder))
    except SchemaError as e:
        print(f"error: {e}", file=sys.stderr)
        return 1
    for warning in schema.warnings:
        print(f"warning: {warning}", file=sys.stderr)
    write_schema(opts.out, schema.rows)
    print(f"{len(schema.rows)} picture(s) -> {opts.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
