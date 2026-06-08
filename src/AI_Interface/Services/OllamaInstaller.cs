using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>The host operating system, decoupled from <c>OperatingSystem.Is*</c> so the pure path/URL
/// helpers can be exercised for every branch from a unit test on any host.</summary>
internal enum OSKind { Windows, Linux, MacOS, Other }

/// <summary>
/// Installs the local Ollama runtime with a single confirmed click, mirroring <see cref="PiperInstaller"/>.
/// <list type="bullet">
/// <item><b>Windows</b> — downloads the official <c>OllamaSetup.exe</c> (an Inno Setup installer) and runs it
/// silently. The installer may prompt for elevation (UAC); that is expected and handled by the installer
/// itself. The binary lands at <c>%LOCALAPPDATA%\Programs\Ollama\ollama.exe</c>.</item>
/// <item><b>Linux</b> — runs the official <c>curl -fsSL https://ollama.com/install.sh | sh</c> script
/// (requires <c>curl</c> and may prompt for <c>sudo</c>).</item>
/// <item><b>macOS</b> — not auto-installed; reports a graceful "install from ollama.com" message.</item>
/// </list>
/// After install the server is started best-effort so the configured URL becomes reachable.
/// </summary>
public sealed class OllamaInstaller : IOllamaInstaller
{
    // Pinned official endpoints (fixed constants — never interpolated with untrusted input).
    private const string WindowsInstallerUrl = "https://ollama.com/download/OllamaSetup.exe";
    private const string LinuxInstallScriptUrl = "https://ollama.com/install.sh";

    // Inno Setup silent-install switches: no UI, no message boxes, no reboot.
    private const string WindowsSilentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";

    private readonly HttpClient _http;

    public OllamaInstaller(HttpClient http)
    {
        _http = http;
    }

    public bool IsOllamaInstalled => ResolveInstalledExecutable() is not null;

    public async Task InstallAsync(IProgress<string>? progress, CancellationToken ct)
    {
        // Idempotent: if Ollama is already on this machine, skip the download/installer entirely and just
        // (best-effort) start the server so the URL becomes reachable — don't re-download on every click.
        if (IsOllamaInstalled)
        {
            progress?.Report("Ollama is already installed — starting the server…");
            await StartServerBestEffortAsync(progress, ct).ConfigureAwait(false);
            return;
        }

        switch (CurrentOs())
        {
            case OSKind.Windows:
                await InstallWindowsAsync(progress, ct).ConfigureAwait(false);
                return;

            case OSKind.Linux:
                await InstallLinuxAsync(progress, ct).ConfigureAwait(false);
                return;

            case OSKind.MacOS:
                // No silent installer is published for macOS; guide the user instead of failing hard.
                throw new InvalidOperationException(
                    "Automatic install isn't supported on macOS. Download Ollama from https://ollama.com/download.");

            default:
                throw new InvalidOperationException(
                    "Automatic install isn't supported on this platform. Download Ollama from https://ollama.com/download.");
        }
    }

    // --- Windows -------------------------------------------------------------------------------

    private async Task InstallWindowsAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var installerPath = Path.Combine(Path.GetTempPath(), $"OllamaSetup_{Guid.NewGuid():N}.exe");
        try
        {
            progress?.Report("Downloading Ollama…");
            await HttpDownloads.ToFileAsync(_http, WindowsInstallerUrl, installerPath, "Downloading Ollama", progress, ct)
                .ConfigureAwait(false);

            // Inno Setup silent install. The installer may show a UAC prompt (it requests elevation
            // itself); that's expected and handled by the installer, not us.
            // useShell: true is required so ShellExecute can surface the installer's UAC elevation prompt
            // (a non-shell launch would fail to elevate and the install would silently do nothing).
            progress?.Report("Installing… (you may see a Windows permission prompt)");
            await RunProcessAsync(installerPath, WindowsSilentArgs, useShell: true, workingDirectory: null, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException($"Ollama install failed: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(installerPath);
        }

        await StartServerBestEffortAsync(progress, ct).ConfigureAwait(false);
        progress?.Report("Ollama installed.");
    }

    // --- Linux ---------------------------------------------------------------------------------

    private async Task InstallLinuxAsync(IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            progress?.Report("Installing Ollama (this may ask for your password)…");
            // The official one-liner. `curl` (and possibly sudo) must be available; the URL is a fixed
            // constant, so there's no shell-injection surface here. `set -o pipefail` (bash-only — we
            // invoke /bin/bash, not /bin/sh) makes a curl failure fail the pipeline instead of being
            // masked by sh's exit code.
            await RunProcessAsync("/bin/bash", $"-c \"set -o pipefail; curl -fsSL {LinuxInstallScriptUrl} | sh\"",
                useShell: false, workingDirectory: null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Ollama install failed: {ex.Message}. Ensure 'curl' is installed and try again.", ex);
        }

        await StartServerBestEffortAsync(progress, ct).ConfigureAwait(false);
        progress?.Report("Ollama installed.");
    }

    // --- Start the server ----------------------------------------------------------------------

    /// <summary>
    /// Best-effort: launch the Ollama server so the configured URL becomes reachable. Failures here are
    /// swallowed — the install itself succeeded; the user can start the server manually.
    /// </summary>
    private async Task StartServerBestEffortAsync(IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            var exe = ResolveInstalledExecutable();
            if (exe is null)
                return;

            progress?.Report("Starting the Ollama server…");
            // `ollama serve` runs the daemon; on Windows the installer normally starts the tray app, but
            // starting serve directly is harmless and ensures the URL answers a probe.
            // The daemon is *deliberately* meant to outlive this call: we neither wait for nor dispose
            // the wrapper (no `using`, no Kill) — disposing/exiting here must not tear down the server.
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();

            // Give the daemon a moment to bind its port before the caller re-probes.
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-fatal: the install succeeded; the user can run `ollama serve` themselves.
            // A real cancellation (OperationCanceledException) is allowed to propagate rather than be
            // silently swallowed into apparent success.
        }
    }

    // --- Process helper ------------------------------------------------------------------------

    private static async Task RunProcessAsync(
        string fileName, string arguments, bool useShell, string? workingDirectory, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = useShell,
                CreateNoWindow = !useShell,
                WorkingDirectory = workingDirectory ?? string.Empty
            }
        };

        if (!process.Start())
            throw new InvalidOperationException($"Could not start the installer process ({fileName}).");

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"The installer exited with code {process.ExitCode}.");
    }

    // --- Detection -----------------------------------------------------------------------------

    /// <summary>Best-effort path of an installed <c>ollama</c> binary, or null if none is found.</summary>
    private static string? ResolveInstalledExecutable()
    {
        var os = CurrentOs();
        foreach (var candidate in CandidateExecutablePaths(os))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        // Fall back to PATH (covers Linux/macOS installs and a Windows install added to PATH).
        return FindOnPath(os == OSKind.Windows ? "ollama.exe" : "ollama");
    }

    /// <summary>The host OS, mapped to the testable <see cref="OSKind"/>. The only place the live
    /// <c>OperatingSystem.Is*</c> checks live — the pure helpers below take the result as a parameter.</summary>
    internal static OSKind CurrentOs()
    {
        if (OperatingSystem.IsWindows()) return OSKind.Windows;
        if (OperatingSystem.IsLinux()) return OSKind.Linux;
        if (OperatingSystem.IsMacOS()) return OSKind.MacOS;
        return OSKind.Other;
    }

    /// <summary>
    /// The well-known install locations for the given OS, in priority order. Pure (no I/O, no
    /// <c>OperatingSystem.Is*</c>) so it can be unit-tested on any host without an install present.
    /// </summary>
    internal static string[] CandidateExecutablePaths(OSKind os)
    {
        if (os == OSKind.Windows)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return new[]
            {
                Path.Combine(local, "Programs", "Ollama", "ollama.exe"),
                Path.Combine(programFiles, "Ollama", "ollama.exe")
            };
        }

        if (os == OSKind.MacOS)
        {
            return new[]
            {
                "/usr/local/bin/ollama",
                "/opt/homebrew/bin/ollama",
                "/Applications/Ollama.app/Contents/Resources/ollama"
            };
        }

        // Linux (and any other Unix): the official script installs to /usr/local/bin (or /usr/bin).
        return new[]
        {
            "/usr/local/bin/ollama",
            "/usr/bin/ollama"
        };
    }

    /// <summary>The official download URL / install endpoint for the given OS, or null on macOS/other.
    /// Pure selection (no <c>OperatingSystem.Is*</c>) so it can be asserted in a unit test without
    /// touching the network.</summary>
    internal static string? InstallSourceUrl(OSKind os) => os switch
    {
        OSKind.Windows => WindowsInstallerUrl,
        OSKind.Linux => LinuxInstallScriptUrl,
        _ => null // macOS / unsupported
    };

    private static string? FindOnPath(string exeName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // Skip malformed PATH entries.
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort temp cleanup */ }
    }
}
