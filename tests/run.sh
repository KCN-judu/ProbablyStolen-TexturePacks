#!/usr/bin/env bash
# Runs the checks. Always: the mod's decisions (src/*.Pure.cs), headless, with no game or Unity assembly.
# With the game installed: also the game's own mod scanner on the built mod (run ./build.sh first).
#   GAME=/d/SteamLibrary/steamapps/common/"Probably Stolen" CSC=/path/to/csc.exe tests/run.sh
set -euo pipefail
cd "$(dirname "$0")/.."
GAME="${GAME:-/d/SteamLibrary/steamapps/common/Probably Stolen}"
CSC="${CSC:-/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"
M="$GAME/Probably Stolen_Data/Managed"
# The .NET Framework csc reads "/" as an option prefix and fails on long paths: work in a short folder.
WORK="${TEMP:-/tmp}/tp_tests"
rm -rf "${WORK:?}" && mkdir -p "$WORK"
cp tests/PureTest.cs tests/DiffTest.cs tests/ScanTest.cs src/*.Pure.cs "$WORK/"

pure=()
for f in src/*.Pure.cs; do pure+=("$(basename "$f")"); done
( cd "$WORK" && "$CSC" -nologo -out:PureTest.exe PureTest.cs "${pure[@]}" >/dev/null && ./PureTest.exe | grep -v '^ok' )
# The same modules against plain reference models, on many generated inputs.
echo "-- differential tests"
( cd "$WORK" && "$CSC" -nologo -optimize+ -out:DiffTest.exe DiffTest.cs "${pure[@]}" >/dev/null && ./DiffTest.exe )

if [ -f "$M/Assembly-CSharp.dll" ] && [ -f out/texture_packs/TexturePacks.dll ]; then
  refs=()
  for r in mscorlib netstandard System System.Core Assembly-CSharp; do refs+=("-r:$M/$r.dll"); done
  ( cd "$WORK" && "$CSC" -nologo -nostdlib -noconfig "${refs[@]}" -out:ScanTest.exe ScanTest.cs >/dev/null )
  echo "-- the game's mod scanner"
  # Outside the game the scanner reports a load error for netstandard types; the capabilities and the hard flags
  # count. A hard flag (HARD line) makes the game refuse the mod outright, whatever the capabilities say.
  MOD="${SCAN_DIR:-$PWD/out/texture_packs}"
  ( cd "$WORK" && ./ScanTest.exe "$(cygpath -w "$M")" "$(cygpath -w "$MOD")" | sed -n '2,$p' | grep -v '^FIND' || true ) | tee "$WORK/scan.txt"
  tr -d '\r' < "$WORK/scan.txt" | grep -qx 'capabilities: none'
  ! tr -d '\r' < "$WORK/scan.txt" | grep -q '^HARD'
else
  echo "-- game or build not found: scanner skipped"
fi
