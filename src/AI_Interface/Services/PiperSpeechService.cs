using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Local, offline TTS using the <see href="https://github.com/rhasspy/piper">Piper</see> binary.
/// The engine path comes from settings (or the managed install), and the *voice* is chosen per call
/// from the text's language (via <see cref="ILanguageDetector"/> + the downloaded voice catalog), so
/// a Spanish reply is read by a Spanish voice when one is installed. Falls back to the configured
/// default voice, then to any installed voice.
/// </summary>
public sealed class PiperSpeechService : IPiperSpeechService
{
    private readonly ISettingsService _settings;
    private readonly IPiperInstaller _installer;
    private readonly IPiperVoiceCatalog _catalog;
    private readonly ILanguageDetector _detector;

    public PiperSpeechService(
        ISettingsService settings, IPiperInstaller installer,
        IPiperVoiceCatalog catalog, ILanguageDetector detector)
    {
        _settings = settings;
        _installer = installer;
        _catalog = catalog;
        _detector = detector;
    }

    public SpeechProvider Provider => SpeechProvider.Piper;

    public bool IsConfigured => ResolveExecutable() is not null && HasAnyVoice();

    public async Task<string> SynthesizeAsync(string text, CancellationToken ct)
    {
        var exe = ResolveExecutable()
                  ?? throw new InvalidOperationException(
                      "Piper isn't installed. Use “Download & install Piper” in Settings → Voice.");

        var modelPath = ChooseVoice(text)
                        ?? throw new InvalidOperationException(
                            "No Piper voice is available. Add one with “Browse voices” in Settings → Voice.");

        var outPath = Path.Combine(Path.GetTempPath(), $"ai_tts_{Guid.NewGuid():N}.wav");

        // Piper resolves its espeak-ng-data relative to the working directory, so run it from the
        // executable's own folder — otherwise it crashes (e.g. 0xC0000409 on Windows).
        var piperDir = Path.GetDirectoryName(exe) ?? "";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = piperDir,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(modelPath);
        psi.ArgumentList.Add("--output_file");
        psi.ArgumentList.Add(outPath);

        var espeakData = Path.Combine(piperDir, "espeak-ng-data");
        if (Directory.Exists(espeakData))
        {
            psi.ArgumentList.Add("--espeak_data");
            psi.ArgumentList.Add(espeakData);
        }

        // On Linux the bundled shared libraries sit next to the binary.
        if (!OperatingSystem.IsWindows() && piperDir.Length > 0)
        {
            var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            psi.Environment["LD_LIBRARY_PATH"] =
                string.IsNullOrEmpty(existing) ? piperDir : $"{piperDir}{Path.PathSeparator}{existing}";
        }

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        // Feed the text in, then close stdin so Piper starts synthesizing.
        await proc.StandardInput.WriteAsync(text.AsMemory(), ct).ConfigureAwait(false);
        proc.StandardInput.Close();

        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"Piper exited with code {proc.ExitCode}. {stderr}".Trim());

        return outPath;
    }

    /// <summary>Engine path: an explicit setting wins, else the managed install.</summary>
    private string? ResolveExecutable()
    {
        var configured = _settings.Current.PiperExecutablePath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;
        return _installer.ResolvedExecutablePath;
    }

    /// <summary>Pick the voice model for this text: by language, else default, else any installed.</summary>
    private string? ChooseVoice(string text)
    {
        var family = _detector.Detect(text);
        var byLanguage = _catalog.ResolveModelPathForLanguage(family);
        if (byLanguage is not null)
            return byLanguage;

        var configured = _settings.Current.PiperModelPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        return _catalog.AnyInstalledModelPath();
    }

    private bool HasAnyVoice()
    {
        if (_catalog.AnyInstalledModelPath() is not null)
            return true;
        var configured = _settings.Current.PiperModelPath;
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured);
    }
}
