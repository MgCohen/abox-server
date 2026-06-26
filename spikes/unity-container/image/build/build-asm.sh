#!/bin/sh
# Compile one Unity asmdef's sources with `dotnet build` against baked/mounted
# Unity reference DLLs. No Unity editor involved. (Rung 1: U3/U4)
set -eu

ASM_NAME="${1:?asm name}"
SRC_GLOB="${2:?src glob}"
UNITY_MANAGED="${UNITY_MANAGED:-/unity-refs}"

WORK="/tmp/build-$ASM_NAME"
mkdir -p "$WORK"
cp /build/unity-asm.csproj "$WORK/$ASM_NAME.csproj"

echo "[build] asm=$ASM_NAME"
echo "[build] unity refs: $UNITY_MANAGED/UnityEngine ($(ls "$UNITY_MANAGED/UnityEngine"/*.dll | wc -l) dlls)"
echo "[build] unity editor present? $([ -d /opt/UnityEditor ] || command -v Unity >/dev/null 2>&1 && echo YES || echo no)"
echo "[build] src files:"; ls $SRC_GLOB 2>/dev/null || true

dotnet build "$WORK/$ASM_NAME.csproj" \
  -p:AsmName="$ASM_NAME" \
  -p:SrcGlob="$SRC_GLOB" \
  -p:UnityManaged="$UNITY_MANAGED" \
  -p:ProjectLibrary="${PROJECT_LIBRARY:-/project/Library}" \
  -p:ExtraDefines="${EXTRA_DEFINES:-}" \
  -v minimal
