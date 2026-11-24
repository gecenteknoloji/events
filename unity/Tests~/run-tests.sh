#!/bin/sh
# Builds GtEventsCore.cs + CoreTests.cs with the .NET that ships inside Unity and runs them.
# Usage: Tests~/run-tests.sh [--live]   (UNITY_SCRIPTING overrides the Unity install path)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
S="${UNITY_SCRIPTING:-$(ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll | tail -1 | xargs dirname | xargs dirname)}"
RT="$(ls -d "$S"/NetCoreRuntime/shared/Microsoft.NETCore.App/* | tail -1)"
OUT="${TMPDIR:-/tmp}/gt-events-unity-tests"
mkdir -p "$OUT"
REFS=""
for f in "$RT"/*.dll; do
  case "$(basename "$f")" in Microsoft.VisualBasic*|*Native*|mscorlib.dll|netstandard.dll) ;; *) REFS="$REFS -r:$f" ;; esac
done
"$S/NetCoreRuntime/dotnet" "$S/DotNetSdkRoslyn/csc.dll" -nologo -langversion:9 -nostdlib -out:"$OUT/CoreTests.dll" $REFS \
  "$HERE/../Runtime/GtEventsCore.cs" "$HERE/CoreTests.cs"
cat > "$OUT/CoreTests.runtimeconfig.json" <<JSON
{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "$(basename "$RT")" } } }
JSON
"$S/NetCoreRuntime/dotnet" exec "$OUT/CoreTests.dll" "$@"
