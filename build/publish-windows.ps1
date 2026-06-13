# Publishes a self-contained, single-file Windows x64 build.
# Output: publish/win-x64/Carnelian.exe  (no .NET install needed on the target machine)
#
# Usage (from repo root or anywhere):  pwsh ./build/publish-windows.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/AI_Interface/Carnelian.csproj'
$out = Join-Path $root 'publish/win-x64'

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out

Write-Host "`nPublished to $out" -ForegroundColor Green
