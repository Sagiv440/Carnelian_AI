using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Talks to the published Piper voice catalog (download list) and to the locally downloaded voices
/// (install/remove + language lookup). Used by the voice browser UI and by the speech service to
/// pick the right voice for the language being spoken.
/// </summary>
public interface IPiperVoiceCatalog
{
    /// <summary>Fetch the full catalog, each entry flagged with whether it's already downloaded.</summary>
    Task<IReadOnlyList<PiperVoiceInfo>> ListAvailableAsync(CancellationToken ct);

    /// <summary>Download a voice's model + config into the local voices folder.</summary>
    Task DownloadAsync(PiperVoiceInfo voice, IProgress<string>? progress, CancellationToken ct);

    /// <summary>Delete a downloaded voice's local files.</summary>
    void Delete(PiperVoiceInfo voice);

    /// <summary>True when the voice's model + config are present locally.</summary>
    bool IsDownloaded(PiperVoiceInfo voice);

    /// <summary>
    /// Local <c>.onnx</c> path of a downloaded voice matching the given language family
    /// (e.g. <c>en</c>), preferring <c>medium</c> quality; null if none is downloaded for it.
    /// </summary>
    string? ResolveModelPathForLanguage(string languageFamily);

    /// <summary>Local <c>.onnx</c> path of any downloaded voice, or null if none are installed.</summary>
    string? AnyInstalledModelPath();
}
