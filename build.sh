#!/usr/bin/env bash
# Builds the mod with the C# 5 compiler that ships with Windows, against the game's own Mono assemblies.
#   GAME=/d/SteamLibrary/steamapps/common/"Probably Stolen" CSC=/path/to/csc.exe ./build.sh [--install]
set -euo pipefail
cd "$(dirname "$0")"
GAME="${GAME:-/d/SteamLibrary/steamapps/common/Probably Stolen}"
M="$GAME/Probably Stolen_Data/Managed"
CSC="${CSC:-/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"
mkdir -p out/texture_packs

refs=()
for r in mscorlib netstandard System System.Core Assembly-CSharp 0Harmony UnityEngine UnityEngine.CoreModule \
         UnityEngine.TextRenderingModule UnityEngine.UI UnityEngine.UIModule; do
  [ -f "$M/$r.dll" ] && refs+=("-r:$M/$r.dll")
done

# The .NET Framework csc cannot create its temp files under long paths, so compile in a short folder.
# It also reads "/" as an option prefix, so sources and output use backslashes.
WORK="${TEMP:-/tmp}/tp_build"
rm -rf "$WORK" && mkdir -p "$WORK/src"
cp src/*.cs "$WORK/src/"
srcs=()
for f in src/*.cs; do srcs+=("src\\$(basename "$f")"); done
( cd "$WORK" && "$CSC" -nologo -nostdlib -noconfig -target:library -optimize+ -warn:4 -nowarn:1701,1702 \
  "${refs[@]}" '-out:TexturePacks.dll' "${srcs[@]}" )
cp "$WORK/TexturePacks.dll" out/texture_packs/

cp manifest.xml out/texture_packs/
[ -f preview.png ] && cp preview.png out/texture_packs/
mkdir -p out/texture_packs/Localization
cp Localization/*.csv out/texture_packs/Localization/
echo "built out/texture_packs"

# The starting point for a pack: the empty IMod every pack must carry, a manifest and an empty Textures folder.
mkdir -p out/texture_pack_template/Textures
cp template/TexturePack.cs "$WORK/"
( cd "$WORK" && "$CSC" -nologo -nostdlib -noconfig -target:library -optimize+ -warn:4 \
  "${refs[@]}" -out:TexturePack.dll TexturePack.cs )
cp "$WORK/TexturePack.dll" template/manifest.xml out/texture_pack_template/
echo "built out/texture_pack_template"

if [ "${1:-}" = "--install" ]; then
  rm -rf "$GAME/Mods/texture_packs"
  cp -r out/texture_packs "$GAME/Mods/"
  echo "installed to $GAME/Mods/texture_packs"
fi
