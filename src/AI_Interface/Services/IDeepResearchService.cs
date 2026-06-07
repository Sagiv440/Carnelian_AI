using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Multi-step research: the model plans queries, the app searches the web and reads pages,
/// then the model synthesizes a cited answer.
/// </summary>
public interface IDeepResearchService
{
    /// <param name="client">The chat client serving the chosen model (resolved by the model router).</param>
    /// <param name="question">The user's research question.</param>
    /// <param name="model">Model id to use for planning and synthesis.</param>
    /// <param name="status">Receives human-readable progress lines ("Searching: …", "Reading: …").</param>
    /// <param name="onAnswerDelta">Receives the synthesized answer token-by-token.</param>
    /// <returns>The sources that were read and offered to the model, in citation order.</returns>
    Task<IReadOnlyList<SearchResult>> RunAsync(
        IChatClient client,
        string question,
        string model,
        IProgress<string> status,
        Action<string> onAnswerDelta,
        CancellationToken ct = default);
}
