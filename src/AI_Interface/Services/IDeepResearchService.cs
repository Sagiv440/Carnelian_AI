using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// A model to call for one of Deep Research's two LLM steps: a resolved <see cref="IChatClient"/>
/// plus the model id to run on it. Used to give planning and synthesis their own models (the
/// "Use Multiple LLMs" Deep Research setting). "Distinct" from the chat client/model means a
/// different client instance or a different model id.
/// </summary>
public readonly record struct ModelEndpoint(IChatClient Client, string Model);

/// <summary>
/// Multi-step research: the model plans queries, the app searches the web and reads pages,
/// then the model synthesizes a cited answer.
/// </summary>
public interface IDeepResearchService
{
    /// <param name="client">The chat client serving the chosen model (resolved by the model router).</param>
    /// <param name="question">The user's research question.</param>
    /// <param name="model">Model id to use for planning and synthesis — the fallback when no override is given.</param>
    /// <param name="personaPrefix">The active agent's persona, prepended to the synthesis prompt (empty = none).</param>
    /// <param name="status">Receives human-readable progress lines ("Searching: …", "Reading: …").</param>
    /// <param name="onAnswerDelta">Receives the synthesized answer token-by-token.</param>
    /// <param name="planner">
    /// Optional override for the query-planning step. <c>null</c> ⇒ use <paramref name="client"/>/<paramref name="model"/>.
    /// Only the user's question is sent here. If a distinct planner fails, the run retries once on the chat
    /// client/model before falling back to the raw question.
    /// </param>
    /// <param name="synthesizer">
    /// Optional override for the report-synthesis step. <c>null</c> ⇒ use <paramref name="client"/>/<paramref name="model"/>.
    /// <b>Web-page contents are sent here.</b> If a distinct synthesizer fails before emitting any token, the
    /// run restarts the stream on the chat client/model; a failure after tokens were emitted stops gracefully.
    /// </param>
    /// <returns>The sources that were read and offered to the model, in citation order.</returns>
    Task<IReadOnlyList<SearchResult>> RunAsync(
        IChatClient client,
        string question,
        string model,
        string personaPrefix,
        IProgress<string> status,
        Action<string> onAnswerDelta,
        ModelEndpoint? planner = null,
        ModelEndpoint? synthesizer = null,
        CancellationToken ct = default);
}
