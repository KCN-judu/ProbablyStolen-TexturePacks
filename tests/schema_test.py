"""Tests of tools/schema/schema.py. Plain functions and asserts: python tests/schema_test.py

The spelling examples are the ones of `Identity` in tests/PureTest.cs, so the Python and C# codecs stay equal.
The mod folders are built in a temporary folder, with PNG files written by hand (no Pillow needed).
"""

from __future__ import annotations

import struct
import sys
import tempfile
import zlib
from collections.abc import Callable
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "tools" / "schema"))
import schema  # noqa: E402

UUID = "3f2a9c1e-5b7d-4e2a-9c1e-0a1b2c3d4e5f"
OTHER = "4b6f1f6c-2d6e-4a46-9f7e-5c1d2a7b8e90"


def png(width: int, height: int) -> bytes:
    """A valid grey PNG of the given size."""
    def chunk(kind: bytes, data: bytes) -> bytes:
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))
    raw = b"".join(b"\x00" + b"\x80" * width for _ in range(height))
    return (schema.PNG_SIGNATURE + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 0, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


def manifest(mod_id: str = "machine_mod", version: str = "1.2.0", origin: str | None = None) -> str:
    extra = f"  <AssetOrigin>{origin}</AssetOrigin>\n" if origin else ""
    return (f'<?xml version="1.0" encoding="utf-8"?>\n<Manifest>\n  <ID>{mod_id}</ID>\n'
            f"  <ModVersion>{version}</ModVersion>\n{extra}</Manifest>\n")


def put(root: Path, rel: str, data: bytes | str) -> None:
    path = root / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data if isinstance(data, bytes) else data.encode("utf-8"))


def raises(action: Callable[[], object], text: str) -> None:
    try:
        action()
    except schema.SchemaError as e:
        assert text in str(e), f"expected '{text}' in '{e}'"
        return
    raise AssertionError(f"no error, expected one with '{text}'")


# ---------------------------------------------------------------------------------------------- spelling

def test_spelling() -> None:
    assert schema.spell("machine_mod", "Items", "crusher_body") == "machine_mod+Items~crusher_body.png"
    assert schema.spell(UUID, "Paper", "note") == UUID + "+Paper~note.png"
    assert schema.spell(UUID.upper(), "Paper", "note") == UUID + "+Paper~note.png"      # read in any case, kept lower
    assert schema.spell("a.b-c_d", "Items", "x~y") == "a.b-c_d+Items~x~y.png"           # the name starts at the first "~"
    assert schema.spell("m", "Code", "a+b") == "m+Code~a+b.png"                         # "+" may be part of the name
    assert schema.spell("m", "Items", "two words") == "m+Items~two words.png"
    raises(lambda: schema.spell("m", "It ems", "a"), "container")                       # a container is one word
    raises(lambda: schema.spell("m", "", "a"), "container")                             # no container
    raises(lambda: schema.spell("m", "Items", ""), "empty name")                        # no name
    raises(lambda: schema.spell("bad id", "Items", "a"), "mod id")                      # a space is not in a mod id
    raises(lambda: schema.spell(".hidden", "Items", "a"), "mod id")                     # a leading dot
    raises(lambda: schema.spell("", "Items", "a"), "mod id")                            # no origin
    raises(lambda: schema.spell("m" * 65, "Items", "a"), "mod id")                      # too long
    raises(lambda: schema.spell("m", "Items", "a/b"), "file name")
    raises(lambda: schema.spell("m", "Items", "a."), "file name")
    raises(lambda: schema.spell("m", "Itéms", "a"), "container")                        # ASCII only, as in C#


def test_markers() -> None:
    assert schema.markers_in(["origin." + UUID.upper(), "pack", "origin.zzz", "origin." + UUID]) == (UUID,)
    assert schema.markers_in(["origin." + UUID, "origin." + OTHER]) == tuple(sorted((UUID, OTHER)))
    assert schema.markers_in(["origin"]) == ()


def test_png_size() -> None:
    assert schema.png_size(png(48, 64)[:24]) == (48, 64)
    assert schema.png_size(b"not a png at all, not at all") is None


# ---------------------------------------------------------------------------------------------- mod folders

def schema_of(root: Path) -> schema.Schema:
    return schema.make_schema(schema.read_mod(root))


def test_mod_with_uuid(tmp: Path) -> None:
    root = tmp / "machine_mod"
    put(root, "manifest.xml", manifest(origin=UUID.upper()))
    put(root, "Items/crusher_body.png", png(48, 64))
    put(root, "Items/belt.png", png(32, 8))
    put(root, "Items/readme.txt", "not a picture")
    put(root, "Paper/note.png", png(100, 150))
    put(root, f"Textures/origin.{UUID}.png", png(1, 1))
    put(root, "textures.extra.tsv", "# made in code\ncontainer\tname\twidth\theight\nCode\tspark\t16\t16\nItems\tbelt\t32\t8\n")
    put(root, "textures.exclude", "Paper/note\nItems/missing\n")
    result = schema_of(root)
    expected = (
        schema.Row(f"{UUID}+Code~spark.png", "machine_mod", 16, 16, "machine_mod", "spark", UUID, "Code", "1.2.0"),
        schema.Row(f"{UUID}+Items~belt.png", "machine_mod", 32, 8, "machine_mod", "belt", UUID, "Items", "1.2.0"),
        schema.Row(f"{UUID}+Items~crusher_body.png", "machine_mod", 48, 64, "machine_mod", "crusher_body", UUID,
                   "Items", "1.2.0"),
    )
    assert result.rows == expected, result.rows
    assert result.warnings == ("textures.exclude: Items/missing matches no picture",), result.warnings
    text = schema.render(result.rows)
    lines = text.split("\n")
    assert lines[0] == "file\tkind\tcategory\tgroup\twidth\theight\tsource\tname\torigin\tcontainer\tversion"
    assert lines[1] == f"{UUID}+Code~spark.png\tmod\tmod\tmachine_mod\t16\t16\tmachine_mod\tspark\t{UUID}\tCode\t1.2.0"
    assert text.endswith("\n") and "\r" not in text and len(lines) == 5


def test_written_file(tmp: Path) -> None:
    root = tmp / "written"
    put(root, "manifest.xml", manifest("w"))
    put(root, "Items/a.png", png(2, 3))
    assert schema.main([str(root)]) == 0
    data = (root / schema.SCHEMA_FILE).read_bytes()
    assert not data.startswith(b"\xef\xbb\xbf") and b"\r" not in data
    assert data.decode("utf-8").split("\n")[1].startswith("w+Items~a.png\tmod\tmod\tw\t2\t3\twritten\ta\tw\tItems\t")


def test_mod_id_fallback(tmp: Path) -> None:
    root = tmp / "plain"
    put(root, "manifest.xml", manifest("plain_mod"))
    put(root, "Items/a.png", png(4, 4))
    result = schema_of(root)
    assert [r.file for r in result.rows] == ["plain_mod+Items~a.png"]
    assert len(result.warnings) == 1 and "no UUID" in result.warnings[0]


def test_marker_only(tmp: Path) -> None:
    root = tmp / "marker"
    put(root, "manifest.xml", manifest("m"))
    put(root, "Items/a.png", png(4, 4))
    put(root, f"Textures/origin.{UUID}.png", png(1, 1))
    result = schema_of(root)
    assert result.rows[0].file == f"{UUID}+Items~a.png"
    assert len(result.warnings) == 1 and "AssetOrigin" in result.warnings[0]


def test_disagreeing_origin(tmp: Path) -> None:
    root = tmp / "bad_origin"
    put(root, "manifest.xml", manifest("m", origin=UUID))
    put(root, f"Textures/origin.{OTHER}.png", png(1, 1))
    raises(lambda: schema_of(root), "different UUIDs")
    root = tmp / "bad_uuid"
    put(root, "manifest.xml", manifest("m", origin="not-a-uuid"))
    raises(lambda: schema_of(root), "not a hyphenated UUID")
    root = tmp / "two_markers"
    put(root, "manifest.xml", manifest("m"))
    put(root, f"Textures/origin.{UUID}.png", png(1, 1))
    put(root, f"Textures/origin.{OTHER}.png", png(1, 1))
    raises(lambda: schema_of(root), "more than one origin marker")


def test_bad_inputs(tmp: Path) -> None:
    root = tmp / "bad_extra"
    put(root, "manifest.xml", manifest("m", origin=UUID))
    put(root, "textures.extra.tsv", "container\tname\twidth\theight\nCode bad\tx\t1\t1\n")
    raises(lambda: schema_of(root), "container")
    put(root, "textures.extra.tsv", "container\tname\twidth\theight\nCode\t\t1\t1\n")
    raises(lambda: schema_of(root), "empty name")
    put(root, "textures.extra.tsv", "container\tname\twidth\theight\nCode\tx\t0\t1\n")
    raises(lambda: schema_of(root), "positive")
    put(root, "textures.extra.tsv", "name\twidth\n")
    raises(lambda: schema_of(root), "first line")
    put(root, "textures.extra.tsv", "container\tname\twidth\theight\nCode\tx\t1\t1\nCode\tx\t1\t1\n")
    raises(lambda: schema_of(root), "twice")
    root = tmp / "conflict"
    put(root, "manifest.xml", manifest("m", origin=UUID))
    put(root, "Items/a.png", png(4, 4))
    put(root, "textures.extra.tsv", "container\tname\twidth\theight\nItems\ta\t5\t4\n")
    raises(lambda: schema_of(root), "in the folder")
    root = tmp / "no_manifest"
    root.mkdir()
    raises(lambda: schema.read_mod(root), "manifest.xml")
    root = tmp / "bad_id"
    put(root, "manifest.xml", manifest("bad id"))
    put(root, "Items/a.png", png(1, 1))
    raises(lambda: schema_of(root), "mod id")
    root = tmp / "fake_png"
    put(root, "manifest.xml", manifest("m", origin=UUID))
    put(root, "Items/a.png", b"this is text")
    raises(lambda: schema_of(root), "not a PNG")


def main() -> int:
    test_spelling()
    test_markers()
    test_png_size()
    with tempfile.TemporaryDirectory() as t:
        tmp = Path(t)
        for test in (test_mod_with_uuid, test_written_file, test_mod_id_fallback, test_marker_only,
                     test_disagreeing_origin, test_bad_inputs):
            test(tmp)
    print("schema tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
