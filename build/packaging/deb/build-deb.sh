#!/usr/bin/env bash
# Builds a Debian package (.deb) for Carnelian from the linux-x64 self-contained publish.
#
# Requires a Debian/Ubuntu toolchain (dpkg-deb) — works on Linux or via WSL on Windows.
# Run the Linux publish first so the binary exists:
#     ./build/publish-linux.sh
# then:
#     ./build/packaging/deb/build-deb.sh
# Output: publish/carnelian_<version>_amd64.deb
set -euo pipefail

APP_ID="io.github.Sagiv440.Carnelian"
PKG="carnelian"
VERSION="1.0.3"
ARCH="amd64"
MAINT="Sagiv Reuben <moshe@kiro-inc.com>"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
PKGDIR="$ROOT/build/packaging"
BIN="$ROOT/publish/linux-x64/Carnelian"
OUT="$ROOT/publish"

[ -f "$BIN" ] || { echo "ERROR: $BIN not found — run ./build/publish-linux.sh first." >&2; exit 1; }
command -v dpkg-deb >/dev/null || { echo "ERROR: dpkg-deb not found (need Debian/Ubuntu or WSL)." >&2; exit 1; }

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
chmod 755 "$STAGE"   # package root (./) should be world-readable, not the mktemp default 700

# --- file layout under the package root ---------------------------------------------------
# The self-contained single-file binary lives in /usr/lib/carnelian; /usr/bin/carnelian links to it.
install -Dm755 "$BIN" "$STAGE/usr/lib/$PKG/Carnelian"
mkdir -p "$STAGE/usr/bin"
ln -sf "../lib/$PKG/Carnelian" "$STAGE/usr/bin/$PKG"

install -Dm644 "$PKGDIR/$APP_ID.desktop"      "$STAGE/usr/share/applications/$APP_ID.desktop"
install -Dm644 "$PKGDIR/$APP_ID.metainfo.xml" "$STAGE/usr/share/metainfo/$APP_ID.metainfo.xml"
for s in 512 256 128 64; do
  install -Dm644 "$PKGDIR/icons/carnelian-$s.png" "$STAGE/usr/share/icons/hicolor/${s}x${s}/apps/$APP_ID.png"
done

# Minimal copyright stub (Debian policy expects /usr/share/doc/<pkg>/copyright).
install -Dm644 /dev/stdin "$STAGE/usr/share/doc/$PKG/copyright" <<EOF
Upstream-Name: Carnelian
Source: https://github.com/Sagiv440/Carnelian_AI
Files: *
Copyright: $(date +%Y) Sagiv Reuben
License: see the project repository.
EOF

INSTALLED_KB="$(du -ks "$STAGE/usr" | cut -f1)"

# --- control metadata ---------------------------------------------------------------------
mkdir -p "$STAGE/DEBIAN"
cat > "$STAGE/DEBIAN/control" <<EOF
Package: $PKG
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Maintainer: $MAINT
Installed-Size: $INSTALLED_KB
Depends: libc6, libstdc++6, zlib1g, libicu76 | libicu74 | libicu72 | libicu70, libssl3t64 | libssl3, libx11-6, libice6, libsm6, libfontconfig1, libgl1
Recommends: pulseaudio-utils | alsa-utils
Homepage: https://github.com/Sagiv440/Carnelian_AI
Description: Local-first AI workbench
 Carnelian is a cross-platform desktop AI client that runs models locally
 through Ollama and optionally through cloud providers, with web search,
 deep research, and a tool-using project agent.
EOF

# md5sums (Debian best practice).
( cd "$STAGE" && find usr -type f -print0 | xargs -0 md5sum > DEBIAN/md5sums )

DEB="$OUT/${PKG}_${VERSION}_${ARCH}.deb"
# --root-owner-group forces root:root inside the package without needing fakeroot.
dpkg-deb --root-owner-group --build "$STAGE" "$DEB"

echo ""
echo "Built: $DEB"
echo "Install with:  sudo apt install \"$DEB\"   (resolves dependencies)"
