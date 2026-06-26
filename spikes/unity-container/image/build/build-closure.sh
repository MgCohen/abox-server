#!/bin/sh
# Rung 4: compile an asmdef + its PROJECT-SOURCE closure from source, resolving
# only engine/package binaries. No reliance on the target's stale ScriptAssemblies.
set -eu

TARGET="${1:?target asmdef name}"
PROJECT="${PROJECT:-/project}"

WORK="/tmp/closure-$TARGET"
mkdir -p "$WORK/resolver" "$WORK/build"
cp /build/resolve-closure.cs "$WORK/resolver/resolve-closure.cs"
cp /build/closure.csproj "$WORK/build/$TARGET.csproj"

echo "[closure] resolving graph for $TARGET ..."
dotnet run "$WORK/resolver/resolve-closure.cs" -- "$PROJECT" "$TARGET" "$WORK/build/closure.props"

echo "[closure] emitted props (head):"
sed -n '1,40p' "$WORK/build/closure.props"

echo "[closure] building ..."
dotnet build "$WORK/build/$TARGET.csproj" \
  -p:AsmName="$TARGET" \
  -p:ExtraDefines="${EXTRA_DEFINES:-}" \
  -v minimal
