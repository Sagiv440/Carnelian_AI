using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>
/// Provider-agnostic text-to-speech surface — the spine of the Voice feature, mirroring how
/// <see cref="IChatClient"/>/<see cref="IModelRouter"/> abstract chat. The concrete engine
/// (Piper, …) is chosen from settings by <see cref="SpeechRouter"/>.
/// </summary>
public interface ISpeechService
{
    /// <summary>True when the selected provider is configured and ready to speak.</summary>
    bool IsConfigured { get; }

    /// <summary>Synthesize <paramref name="text"/> and play it. Any current playback is stopped first.</summary>
    /// <param name="text">Plain text; callers are responsible for stripping markdown before calling.</param>
    Task SpeakAsync(string text, CancellationToken ct = default);

    /// <summary>Stop any current playback.</summary>
    Task StopAsync();
}
