# Linux packaging

Both packages bundle the **self-contained** linux-x64 build, so the target machine needs no
.NET install. Build that binary first:

```bash
./build/publish-linux.sh        # -> publish/linux-x64/Carnelian
```

## .deb (Debian / Ubuntu)

```bash
./build/packaging/deb/build-deb.sh      # -> publish/carnelian_1.0.2_amd64.deb
sudo apt install ./publish/carnelian_1.0.2_amd64.deb
```

Needs `dpkg-deb` (Debian/Ubuntu, or Windows via WSL). Installs to `/usr/lib/carnelian` with a
`/usr/bin/carnelian` launcher, plus the `.desktop`, AppStream metainfo, and hicolor icons.

## Flatpak

```bash
./build/flatpak/build-flatpak.sh        # -> publish/io.github.Sagiv440.Carnelian.flatpak
flatpak install --user ./publish/io.github.Sagiv440.Carnelian.flatpak
```

**Linux only** — needs `flatpak` + `flatpak-builder` and the `org.freedesktop.Platform//24.08`
runtime (the script pulls it from Flathub). Cannot be built on Windows.

## Shared assets (used by both)

- `io.github.Sagiv440.Carnelian.desktop` — desktop entry
- `io.github.Sagiv440.Carnelian.metainfo.xml` — AppStream metadata
- `icons/carnelian-{64,128,256,512}.png` — app icons (from `Assets/app-logo.png`)

## Notes / TODO

- **App ID** `io.github.Sagiv440.Carnelian` follows the GitHub-owner convention; change it if the
  repo moves.
- **License**: the metainfo lists `LicenseRef-proprietary` as a placeholder — set a real SPDX
  license (and add a `LICENSE` file) before any public distribution (Flathub requires it).
- **Dependencies** in the `.deb` control file are a sensible default for Avalonia/.NET on
  Debian/Ubuntu; adjust per target distro if a library name differs.
- For a **Flathub** submission you'd additionally need screenshots in the metainfo and a
  build-from-source module (instead of bundling the prebuilt binary).
