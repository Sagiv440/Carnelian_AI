using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>Plays a local audio file. Cross-platform; one playback at a time.</summary>
public interface IAudioPlayer
{
    /// <summary>Play <paramref name="filePath"/> and complete when it finishes (or is cancelled/stopped).</summary>
    Task PlayAsync(string filePath, CancellationToken ct);

    /// <summary>Stop the current playback, if any.</summary>
    void Stop();
}
