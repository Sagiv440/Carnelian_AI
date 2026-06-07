using System;
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

    /// <summary>True if an Ollama server responds at the given base URL. Used by the Settings probes
    /// (Quick setup / Test connection); bounded by a short timeout so a dead address fails fast.</summary>
    Task<bool> PingAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>Names of locally installed models (e.g. "llama3:latest").</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>Pulls (downloads) a model, reporting human-readable progress as it streams.</summary>
    Task PullModelAsync(string name, IProgress<string>? progress, CancellationToken ct = default);

    /// <summary>Deletes a locally installed model.</summary>
    Task DeleteModelAsync(string name, CancellationToken ct = default);

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
