---
name: publish-app
description: Produce self-contained, single-file distributables of the AI Interface app for Windows and/or Linux. Use when asked to build a release, package, bundle, or publish the app for distribution.
---

# Publish the AI Interface app

Builds standalone binaries that bundle the .NET runtime, so the target machine needs **no** .NET
install. Output goes to `publish/<rid>/`.

## Commands

```bash
# Windows x64  ->  publish/win-x64/AI_Interface.exe
pwsh ./build/publish-windows.ps1

# Linux x64    ->  publish/linux-x64/AI_Interface
./build/publish-linux.sh
```

Both scripts cross-publish from the other OS (the .NET SDK supports cross-RID publish), so you can
produce the Linux build from Windows and vice-versa.

## What the scripts do

`dotnet publish` in Release, `--self-contained true`, `-p:PublishSingleFile=true`,
`-p:IncludeNativeLibrariesForSelfExtract=true`, targeting `win-x64` / `linux-x64`. The single-file
output is large (~100 MB) because it embeds the runtime — this is expected.

## Adding another target

To support a new platform/architecture (e.g. `linux-arm64`, `osx-arm64`), copy one of the scripts in
`build/` and change the `-r <rid>` and output path. Avalonia supports macOS too if a target is needed.
