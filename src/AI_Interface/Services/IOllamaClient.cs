using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>Thin client over the local Ollama HTTP API.</summary>
public interface IOllamaClient
{
    /// <summary>True if the configured Ollama server responds.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Names of locally installed models (e.g. "llama3:latest").</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>Streams the assistant reply token-by-token. Each yielded string is a content delta.</summary>
    IAsyncEnumerable<string> ChatStreamAsync(
        string model, IEnumerable<ChatMessage> messages, CancellationToken ct = default);

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
}
