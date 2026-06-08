using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// The single <see cref="ISpeechService"/> the app talks to. Picks the engine named in settings
/// (mirrors <see cref="ChatRouter.For"/>), synthesizes via that engine, plays through the shared
/// <see cref="IAudioPlayer"/>, and owns the one "currently speaking" cancellation so only one
/// utterance plays at a time.
/// </summary>
public sealed class SpeechRouter : ISpeechService
{
    private readonly ISettingsService _settings;
    private readonly IAudioPlayer _player;
    private readonly IReadOnlyDictionary<SpeechProvider, ITtsEngine> _engines;

    private CancellationTokenSource? _cts;

    public SpeechRouter(ISettingsService settings, IAudioPlayer player, IPiperSpeechService piper)
    {
        _settings = settings;
        _player = player;
        _engines = new Dictionary<SpeechProvider, ITtsEngine>
        {
            [SpeechProvider.Piper] = piper,
        };
    }

    public bool IsConfigured =>
        _engines.TryGetValue(_settings.Current.SpeechProvider, out var engine) && engine.IsConfigured;

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var provider = _settings.Current.SpeechProvider;
        if (!_engines.TryGetValue(provider, out var engine))
            throw new InvalidOperationException("No voice provider is selected. Choose one in Settings → Voice.");

        // Only one voice at a time: cancel whatever's playing before starting.
        await StopAsync().ConfigureAwait(false);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _cts = cts;

        string? file = null;
        try
        {
            file = await engine.SynthesizeAsync(text, cts.Token).ConfigureAwait(false);
            await _player.PlayAsync(file, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose — not an error.
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
                _cts = null;
            cts.Dispose();
            TryDelete(file);
        }
    }

    public Task StopAsync()
    {
        var cts = _cts;
        if (cts is not null)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* already finished */ }
        }
        _player.Stop();
        return Task.CompletedTask;
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }
}
