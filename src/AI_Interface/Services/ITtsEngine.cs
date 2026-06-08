using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// A single TTS engine: turns text into an audio file. Playback is handled separately by
/// <see cref="IAudioPlayer"/> so every engine shares one stop/cancel path (see <see cref="SpeechRouter"/>).
/// </summary>
public interface ITtsEngine
{
    /// <summary>The engine this implementation provides.</summary>
    SpeechProvider Provider { get; }

    /// <summary>True when the engine has everything it needs (binary present, voice model set, …).</summary>
    bool IsConfigured { get; }

    /// <summary>Synthesize <paramref name="text"/> to a temporary audio file and return its path.</summary>
    Task<string> SynthesizeAsync(string text, CancellationToken ct);
}

/// <summary>Marker interface so DI can supply the Piper engine as its own typed dependency.</summary>
public interface IPiperSpeechService : ITtsEngine
{
}
