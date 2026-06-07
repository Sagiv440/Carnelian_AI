#!/usr/bin/env bash
# Publishes a self-contained, single-file Linux x64 build.
# Output: publish/linux-x64/AI_Interface  (no .NET install needed on the target machine)
#
# Runs on Linux, or cross-publishes from Windows/macOS (the .NET SDK supports cross-RID publish).
# Usage (from repo root or anywhere):  ./build/publish-linux.sh

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/AI_Interface/AI_Interface.csproj"
OUT="$ROOT/publish/linux-x64"

dotnet publish "$PROJECT" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUT"

echo ""
echo "Published to $OUT"
echo "Run it with:  $OUT/AI_Interface"
