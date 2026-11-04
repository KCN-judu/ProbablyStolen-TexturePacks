#!/usr/bin/env bash
# Builds the creator kit: the texture catalog, flat placeholders for every scene texture, the pack builder, the
# export script and the guide, in out/texture_pack_kit and zipped as out/TexturePackKit.zip.
# No game art goes into the kit: creators export the pictures from their own copy of the game.
#
# After a game update, refresh the inventory first (needs Python with tools/export/requirements.txt):
#   python tools/export/inventory.py "<game folder>" tools/Catalog/inventory.json
#   CSC=/path/to/csc.exe tools/kit.sh
set -euo pipefail
cd "$(dirname "$0")/.."
FW=/c/Windows/Microsoft.NET/Framework64/v4.0.30319
CSC="${CSC:-$FW/csc.exe}"
# The kit carries the empty TexturePack.dll that build.sh produces.
[ -f out/texture_pack_template/TexturePack.dll ] || ./build.sh >/dev/null

# The .NET Framework csc reads "/" as an option prefix and fails on long paths: work in a short folder.
WORK="${TEMP:-/tmp}/tp_kit"
rm -rf "${WORK:?}" && mkdir -p "$WORK"
cp tools/Catalog/Catalog.cs tools/PackBuilder/PackBuilder.cs src/PackIds.Pure.cs "$WORK/"
(
  cd "$WORK"
  "$CSC" -nologo -optimize+ -r:System.Drawing.dll -r:System.Web.Extensions.dll -out:Catalog.exe Catalog.cs
  # The builder writes pack ids with the mod's own rules, so it never writes one the game refuses.
  "$CSC" -nologo -optimize+ -out:PackBuilder.exe PackBuilder.cs PackIds.Pure.cs
)

# Layout of the kit: catalog.tsv, PackBuilder.exe and TexturePack.dll at the top, placeholders/ from Catalog.exe,
# export/ with the Python script, and the pack guide as README.md.
KIT=out/texture_pack_kit
rm -rf "$KIT" && mkdir -p "$KIT/export"
( cd "$WORK" && ./Catalog.exe "$(cygpath -w "$OLDPWD/tools/Catalog/inventory.json")" "$(cygpath -w "$OLDPWD/catalog.tsv")" \
    "$(cygpath -w "$OLDPWD/$KIT/placeholders")" ) | tail -2
cp catalog.tsv "$WORK/PackBuilder.exe" out/texture_pack_template/TexturePack.dll "$KIT/"
cp tools/export/export.py tools/export/unity_files.py tools/export/requirements.txt catalog.tsv "$KIT/export/"
cp doc/manual/making-a-pack.md "$KIT/README.md"
cp LICENSE "$KIT/"

rm -f out/TexturePackKit.zip
powershell -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$KIT")\\*' -DestinationPath '$(cygpath -w out/TexturePackKit.zip)'"
echo "built $KIT and out/TexturePackKit.zip"
