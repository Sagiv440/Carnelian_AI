using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Aggregates the configured AI providers (local Ollama + the cloud clients) so callers can list every
/// available model and resolve the right <see cref="IChatClient"/> for a chosen one.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Lists models from every provider that is configured and reachable, in parallel and best-effort
    /// (a provider that errors contributes nothing and never throws). Ollama models come first, then
    /// cloud models, each tagged with its provider.
    /// </summary>
    Task<IReadOnlyList<ChatModel>> ListAllModelsAsync(CancellationToken ct = default);

    /// <summary>Resolves the client for a given provider.</summary>
    IChatClient For(AiProvider provider);
}
