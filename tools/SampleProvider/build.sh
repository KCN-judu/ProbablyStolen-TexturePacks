#!/usr/bin/env bash
# Builds the sample provider against the game's assemblies; --install copies it to the game's Mods folder.
# It compiles template/TexturePacksClient.cs into the mod, as a real provider does.
#   GAME=/d/SteamLibrary/steamapps/common/"Probably Stolen" CSC=/path/to/csc.exe tools/SampleProvider/build.sh [--install]
set -euo pipefail
cd "$(dirname "$0")"
GAME="${GAME:-/d/SteamLibrary/steamapps/common/Probably Stolen}"
M="$GAME/Probably Stolen_Data/Managed"
CSC="${CSC:-/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"
# Written next to the mod's own build output (out/ is ignored by git).
OUT=../../out/sample_provider
mkdir -p "$OUT"
refs=()
for r in mscorlib netstandard System System.Core Assembly-CSharp UnityEngine UnityEngine.CoreModule; do
  [ -f "$M/$r.dll" ] && refs+=("-r:$M/$r.dll")
done
# The .NET Framework csc reads "/" as an option prefix and fails on long paths: compile in a short folder.
WORK="${TEMP:-/tmp}/sprov_build"
rm -rf "$WORK" && mkdir -p "$WORK"
cp SampleProvider.cs ../../template/TexturePacksClient.cs "$WORK/"
( cd "$WORK" && "$CSC" -nologo -nostdlib -noconfig -target:library -optimize+ -warn:4 -nowarn:1701,1702 \
  "${refs[@]}" -out:SampleProvider.dll SampleProvider.cs TexturePacksClient.cs )
cp "$WORK/SampleProvider.dll" manifest.xml "$OUT/"
echo "built out/sample_provider"
if [ "${1:-}" = "--install" ]; then
  rm -rf "$GAME/Mods/sample_provider"
  cp -r "$OUT" "$GAME/Mods/"
  echo "installed to $GAME/Mods/sample_provider"
fi
