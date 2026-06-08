using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>
/// Installs the Piper engine into <c>%LOCALAPPDATA%\AI_Interface\piper</c> (or the Linux/macOS
/// equivalent): downloads the matching release archive, extracts it, marks the binary executable on
/// Unix, and writes the resolved path into settings so <see cref="PiperSpeechService"/> picks it up.
/// </summary>
public sealed class PiperInstaller : IPiperInstaller
{
    // Pinned to a known-good Piper release with prebuilt binaries for every desktop platform.
    private const string ReleaseTag = "2023.11.14-2";
    private const string ReleaseBase = "https://github.com/rhasspy/piper/releases/download/" + ReleaseTag + "/";

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;

    public PiperInstaller(HttpClient http, ISettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    public string EngineDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI_Interface", "piper");

    public string VoicesDirectory => Path.Combine(EngineDirectory, "voices");

    public string? ResolvedExecutablePath
    {
        get
        {
            if (!Directory.Exists(EngineDirectory))
                return null;
            var exeName = OperatingSystem.IsWindows() ? "piper.exe" : "piper";
            return Directory
                .EnumerateFiles(EngineDirectory, exeName, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
    }

    public bool IsEngineInstalled => ResolvedExecutablePath is not null;

    public async Task<string> InstallEngineAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var asset = AssetName();
        var url = ReleaseBase + asset;

        Directory.CreateDirectory(EngineDirectory);
        var archivePath = Path.Combine(Path.GetTempPath(), $"piper_{Guid.NewGuid():N}_{asset}");

        try
        {
            progress?.Report("Downloading Piper…");
            await HttpDownloads.ToFileAsync(_http, url, archivePath, "Downloading Piper", progress, ct)
                .ConfigureAwait(false);

            progress?.Report("Extracting…");
            await ExtractAsync(archivePath, EngineDirectory, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(archivePath);
        }

        var exe = ResolvedExecutablePath
                  ?? throw new InvalidOperationException("Piper was downloaded but its executable wasn't found.");

        if (!OperatingSystem.IsWindows())
            MakeExecutable(exe);

        // Persist so the speech service (which reads settings) uses the freshly installed engine.
        _settings.Current.PiperExecutablePath = exe;
        _settings.Save();

        progress?.Report("Piper installed.");
        return exe;
    }

    /// <summary>The release asset for the current OS and CPU architecture.</summary>
    private static string AssetName()
    {
        var arm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows())
            return "piper_windows_amd64.zip";
        if (OperatingSystem.IsMacOS())
            return arm64 ? "piper_macos_aarch64.tar.gz" : "piper_macos_x64.tar.gz";
        return arm64 ? "piper_linux_aarch64.tar.gz" : "piper_linux_x86_64.tar.gz";
    }

    private static async Task ExtractAsync(string archivePath, string destDir, CancellationToken ct)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true), ct)
                .ConfigureAwait(false);
            return;
        }

        // .tar.gz — gunzip then untar (preserves Unix exec bits).
        await Task.Run(() =>
        {
            using var file = File.OpenRead(archivePath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, destDir, overwriteFiles: true);
        }, ct).ConfigureAwait(false);
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path,
                mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch
        {
            // Non-fatal: on some filesystems chmod isn't supported; the binary may already be +x.
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort temp cleanup */ }
    }
}
