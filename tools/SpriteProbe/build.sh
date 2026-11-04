#!/usr/bin/env bash
# Builds the sprite probe against the game's assemblies; --install copies it to the game's Mods folder.
#   GAME=/d/SteamLibrary/steamapps/common/"Probably Stolen" CSC=/path/to/csc.exe tools/SpriteProbe/build.sh [--install]
set -euo pipefail
cd "$(dirname "$0")"
GAME="${GAME:-/d/SteamLibrary/steamapps/common/Probably Stolen}"
M="$GAME/Probably Stolen_Data/Managed"
CSC="${CSC:-/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"
OUT=../../out/sprite_probe
mkdir -p "$OUT"
refs=()
for r in mscorlib netstandard System System.Core Assembly-CSharp UnityEngine UnityEngine.CoreModule UnityEngine.UI UnityEngine.UIModule; do
  [ -f "$M/$r.dll" ] && refs+=("-r:$M/$r.dll")
done
# The .NET Framework csc reads "/" as an option prefix and fails on long paths: compile in a short folder.
WORK="${TEMP:-/tmp}/sp_build"
rm -rf "$WORK" && mkdir -p "$WORK"
cp SpriteProbe.cs "$WORK/"
( cd "$WORK" && "$CSC" -nologo -nostdlib -noconfig -target:library -optimize+ -warn:4 -nowarn:1701,1702 \
  "${refs[@]}" -out:SpriteProbe.dll SpriteProbe.cs )
cp "$WORK/SpriteProbe.dll" manifest.xml "$OUT/"
echo "built out/sprite_probe"
if [ "${1:-}" = "--install" ]; then
  rm -rf "$GAME/Mods/sprite_probe"
  cp -r "$OUT" "$GAME/Mods/"
  echo "installed to $GAME/Mods/sprite_probe"
fi
