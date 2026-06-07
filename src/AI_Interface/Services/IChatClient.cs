using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Provider-agnostic chat surface. Every backend (local Ollama and the cloud providers OpenAI,
/// Gemini, Anthropic) implements this, so the view models and the agent/research services can route
/// uniformly through <see cref="IModelRouter"/> regardless of which provider serves the chosen model.
/// The method shapes mirror <see cref="IOllamaClient"/> exactly so existing call sites map over directly.
/// </summary>
public interface IChatClient
{
    /// <summary>Which provider this client serves.</summary>
    AiProvider Provider { get; }

    /// <summary>
    /// Streams the assistant reply token-by-token. Each yielded string is a delta. When
    /// <paramref name="think"/> is true, native reasoning is requested where supported and streamed
    /// inline wrapped in <c>&lt;think&gt;…&lt;/think&gt;</c>; providers without exposed reasoning ignore it.
    /// </summary>
    IAsyncEnumerable<string> ChatStreamAsync(
        string model, IEnumerable<ChatMessage> messages, bool think, CancellationToken ct = default);

    /// <summary>Runs a chat to completion and returns the full reply. Used for internal research steps.</summary>
    Task<string> CompleteAsync(
        string model, IEnumerable<ChatMessage> messages, CancellationToken ct = default);

    /// <summary>
    /// Runs one non-streaming chat turn with tools available. Returns the model's text plus any tool
    /// calls it requested. Used by the project agent's tool-use loop.
    /// </summary>
    Task<AgentTurn> ChatWithToolsAsync(
        string model, IEnumerable<ChatMessage> messages,
        IReadOnlyList<AgentTool> tools, CancellationToken ct = default);

    /// <summary>Ids of models this provider exposes (empty when the provider isn't configured/reachable).</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// True only if this provider is configured (cloud providers need an API key) and its API responds.
    /// Best-effort and fail-fast — used to decide whether to surface the provider's models in the picker.
    /// </summary>
    Task<bool> IsConfiguredAndReachableAsync(CancellationToken ct = default);
}
