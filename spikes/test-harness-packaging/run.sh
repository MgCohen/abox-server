#!/usr/bin/env bash
# Phase 4 proof: the test-harness (ParityGuard) pillar runs standalone under renamed seams, with the doc-engine
# as an OPTIONAL soft dependency. Run by hand:
#   bash spikes/test-harness-packaging/run.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

echo "== build the renamed harness engine (warnings-as-errors) =="
dotnet build "$here/Harness/Kit.Tests.Harness.csproj" -c Debug -v quiet --nologo

echo "== run the consumer suite (parity + marker seam + optional doc-engine edge) =="
dotnet test "$here/Sample/Kit.Sample.Tests.csproj" -c Debug -v quiet --nologo

echo
echo "PASS — parity holds off the ABox prefix/marker; doc-engine edge is optional."
