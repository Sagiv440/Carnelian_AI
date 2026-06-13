#!/usr/bin/env bash
# Builds and bundles the Carnelian Flatpak.  LINUX ONLY — needs flatpak + flatpak-builder
# (not available on Windows; under WSL you must install them first and have the runtime).
#
# Run the Linux publish first:   ./build/publish-linux.sh
# then:                          ./build/flatpak/build-flatpak.sh
# Output: publish/io.github.Sagiv440.Carnelian.flatpak  (+ a local repo under build/flatpak/repo)
set -euo pipefail

APP_ID="io.github.Sagiv440.Carnelian"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FP="$ROOT/build/flatpak"
MANIFEST="$FP/$APP_ID.yml"

[ -f "$ROOT/publish/linux-x64/Carnelian" ] || { echo "Run ./build/publish-linux.sh first." >&2; exit 1; }
command -v flatpak >/dev/null && command -v flatpak-builder >/dev/null || {
  echo "ERROR: flatpak / flatpak-builder not found. Install them, e.g.:" >&2
  echo "  sudo apt install flatpak flatpak-builder" >&2
  exit 1
}

# Runtime + SDK (one-time, from Flathub).
flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install --user -y flathub org.freedesktop.Platform//24.08 org.freedesktop.Sdk//24.08 || true

flatpak-builder --force-clean --user --install-deps-from=flathub \
  --repo="$FP/repo" "$FP/build-dir" "$MANIFEST"

flatpak build-bundle "$FP/repo" "$ROOT/publish/${APP_ID}.flatpak" "$APP_ID"
echo ""
echo "Built: $ROOT/publish/${APP_ID}.flatpak"
echo "Install with:  flatpak install --user \"$ROOT/publish/${APP_ID}.flatpak\""
