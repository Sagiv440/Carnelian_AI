using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Local, offline TTS using the <see href="https://github.com/rhasspy/piper">Piper</see> binary.
/// Reads the executable + voice-model paths from settings on every call (like the chat clients read
/// their config), runs <c>piper --model &lt;voice&gt; --output_file &lt;tmp.wav&gt;</c> with the text on
/// stdin, and returns the path to the generated WAV for <see cref="IAudioPlayer"/> to play.
/// </summary>
public sealed class PiperSpeechService : IPiperSpeechService
{
    private readonly ISettingsService _settings;

    public PiperSpeechService(ISettingsService settings) => _settings = settings;

    public SpeechProvider Provider => SpeechProvider.Piper;

    public bool IsConfigured
    {
        get
        {
            var s = _settings.Current;
            return !string.IsNullOrWhiteSpace(s.PiperExecutablePath)
                   && File.Exists(s.PiperExecutablePath)
                   && !string.IsNullOrWhiteSpace(s.PiperModelPath)
                   && File.Exists(s.PiperModelPath);
        }
    }

    public async Task<string> SynthesizeAsync(string text, CancellationToken ct)
    {
        var s = _settings.Current;
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Piper isn't configured. Set the Piper executable and a voice model in Settings → Voice.");

        var outPath = Path.Combine(Path.GetTempPath(), $"ai_tts_{Guid.NewGuid():N}.wav");

        var psi = new ProcessStartInfo
        {
            FileName = s.PiperExecutablePath,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(s.PiperModelPath);
        psi.ArgumentList.Add("--output_file");
        psi.ArgumentList.Add(outPath);

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
}
