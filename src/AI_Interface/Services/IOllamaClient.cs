using System;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>
/// Thin client over the local Ollama HTTP API. Implements the provider-agnostic <see cref="IChatClient"/>
/// (chat / tools / model list) and adds Ollama-only model management used by Model Config.
/// </summary>
public interface IOllamaClient : IChatClient
{
    /// <summary>True if the configured Ollama server responds.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>True if an Ollama server responds at the given base URL. Used by the Settings probes
    /// (Quick setup / Test connection); bounded by a short timeout so a dead address fails fast.</summary>
    Task<bool> PingAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>Pulls (downloads) a model, reporting human-readable progress as it streams.</summary>
    Task PullModelAsync(string name, IProgress<string>? progress, CancellationToken ct = default);

    /// <summary>Deletes a locally installed model.</summary>
    Task DeleteModelAsync(string name, CancellationToken ct = default);
}
