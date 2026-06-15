using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Orchestrates a research run: plan queries → search the web → read top pages → synthesize a
/// cited answer. By default both LLM steps use the chat model; the "Use Multiple LLMs" setting can
/// route planning and synthesis to their own models (the <c>planner</c>/<c>synthesizer</c> overrides).
/// Nothing leaves the machine except the web searches/page fetches — unless an override targets a
/// cloud provider, in which case that step's prompt is sent to it (synthesis includes page contents).
/// </summary>
public sealed class DeepResearchService : IDeepResearchService
{
    private readonly IWebSearchService _search;
    private readonly ISettingsService _settings;

    public DeepResearchService(IWebSearchService search, ISettingsService settings)
    {
        _search = search;
        _settings = settings;
    }

    public async Task<IReadOnlyList<SearchResult>> RunAsync(
        IChatClient client,
        string question,
        string model,
        string personaPrefix,
        IProgress<string> status,
        Action<string> onAnswerDelta,
        ModelEndpoint? planner = null,
        ModelEndpoint? synthesizer = null,
        CancellationToken ct = default)
    {
        var cfg = _settings.Current;

        // Resolve the per-step models: each override falls back to the chat client/model.
        var planClient = planner?.Client ?? client;
        var planModel = planner?.Model ?? model;
        var synthClient = synthesizer?.Client ?? client;
        var synthModel = synthesizer?.Model ?? model;

        // 1. Plan: let the (planning) model decompose the question into focused search queries.
        var plannerDistinct = IsDistinct(planClient, planModel, client, model);
        status.Report(plannerDistinct ? $"Planning with {planModel}…" : "Planning search queries…");
        var queries = await PlanQueriesAsync(
            planClient, planModel, client, model, question, cfg.ResearchQueryCount, ct).ConfigureAwait(false);
        status.Report($"Planned {queries.Count} queries: {string.Join(" | ", queries)}");

        // 2. Search each query, collecting unique results across all of them. Each query is best-effort:
        // a single failing/timed-out search must not abort the whole run — only a genuine user cancel does.
        var byUrl = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();
            status.Report($"Searching: {query}");
            try
            {
                var hits = await _search.SearchAsync(query, cfg.SearchResultsPerQuery, ct).ConfigureAwait(false);
                foreach (var hit in hits)
                    byUrl.TryAdd(hit.Url, hit);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                status.Report($"Search failed for \"{Trim(query, 60)}\" — skipping.");
            }
        }

        if (byUrl.Count == 0)
        {
            onAnswerDelta("I couldn't find any web results for this question. " +
                          "Check your internet connection, or try rephrasing.");
            return Array.Empty<SearchResult>();
        }

        // 3. Read the top unique pages so the model works from full text, not just snippets. Per-page
        // best-effort too: an unreadable page leaves Content empty (synthesis falls back to its snippet).
        var toRead = byUrl.Values.Take(cfg.MaxPagesToRead).ToList();
        foreach (var source in toRead)
        {
            ct.ThrowIfCancellationRequested();
            status.Report($"Reading: {Trim(source.Title, 80)}");
            try
            {
                source.Content = await _search
                    .FetchReadableTextAsync(source.Url, cfg.MaxCharsPerPage, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Leave Content empty — the snippet is used instead.
            }
        }

        // 4. Synthesize a cited answer, streamed back to the caller (in the active agent's voice).
        var synthDistinct = IsDistinct(synthClient, synthModel, client, model);
        status.Report(synthDistinct ? $"Synthesizing with {synthModel}…" : "Synthesizing answer…");
        var messages = BuildSynthesisPrompt(question, toRead, personaPrefix);
        await SynthesizeAsync(
            synthClient, synthModel, client, model, synthDistinct, messages, onAnswerDelta, ct).ConfigureAwait(false);

        return toRead;
    }

    /// <summary>
    /// Streams the synthesis answer. If a <i>distinct</i> synthesizer throws before emitting any token,
    /// restarts the stream on the chat client/model; a failure after tokens were already emitted stops
    /// gracefully with a short note (no double-emit). Cancellation is rethrown.
    /// </summary>
    private static async Task SynthesizeAsync(
        IChatClient synthClient, string synthModel,
        IChatClient chatClient, string chatModel, bool synthDistinct,
        IReadOnlyList<ChatMessage> messages, Action<string> onAnswerDelta, CancellationToken ct)
    {
        var emittedAny = false;
        try
        {
            await foreach (var delta in synthClient.ChatStreamAsync(synthModel, messages, think: false, ct).ConfigureAwait(false))
            {
                emittedAny = true;
                onAnswerDelta(delta);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A distinct synthesizer that failed before producing anything: retry on the chat model.
            // The retry itself must degrade gracefully too — if the fallback model also fails, surface a
            // note rather than letting the exception crash the whole research run (cancellation rethrows).
            if (synthDistinct && !emittedAny)
            {
                try
                {
                    await foreach (var delta in chatClient.ChatStreamAsync(chatModel, messages, think: false, ct).ConfigureAwait(false))
                        onAnswerDelta(delta);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Fall through to the graceful note below.
                }
            }

            // Failed mid-stream (or on the chat-model fallback itself): stop gracefully without crashing.
            onAnswerDelta("\n\n_(Synthesis was interrupted by an error; the answer above may be incomplete.)_");
        }
    }

    /// <summary>
    /// Plans search queries on the planning model. A <i>distinct</i> planner that throws is retried once on
    /// the chat client/model before falling back to the raw question. Cancellation is rethrown.
    /// </summary>
    private async Task<List<string>> PlanQueriesAsync(
        IChatClient planClient, string planModel,
        IChatClient chatClient, string chatModel,
        string question, int count, CancellationToken ct)
    {
        var messages = new[]
        {
            ChatMessage.System(
                "You are a research planner. Break the user's question into focused, diverse web-search " +
                $"queries that together would answer it. Respond with ONLY a JSON array of at most {count} " +
                "short query strings — no prose, no code fences."),
            ChatMessage.User(question)
        };

        var plannerDistinct = IsDistinct(planClient, planModel, chatClient, chatModel);
        try
        {
            var parsed = await CompletePlanAsync(planClient, planModel, messages, count, ct).ConfigureAwait(false);
            if (parsed.Count > 0)
                return parsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A distinct planner failed: retry once on the chat model before giving up.
            if (plannerDistinct)
            {
                try
                {
                    var parsed = await CompletePlanAsync(chatClient, chatModel, messages, count, ct).ConfigureAwait(false);
                    if (parsed.Count > 0)
                        return parsed;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Fall through to the raw question.
                }
            }
            // Planning is best-effort; fall through to the raw question.
        }

        return new List<string> { question };
    }

    private static async Task<List<string>> CompletePlanAsync(
        IChatClient client, string model, IReadOnlyList<ChatMessage> messages, int count, CancellationToken ct)
    {
        var raw = await client.CompleteAsync(model, messages, ct).ConfigureAwait(false);
        return ParseJsonStringArray(raw, count);
    }

    /// <summary>
    /// "Distinct" = a different client instance OR a different model id from the chat client/model.
    /// Used to decide status wording and whether to attempt a chat-model retry.
    /// </summary>
    private static bool IsDistinct(IChatClient client, string model, IChatClient chatClient, string chatModel) =>
        !ReferenceEquals(client, chatClient) ||
        !string.Equals(model, chatModel, StringComparison.Ordinal);

    private static List<string> ParseJsonStringArray(string raw, int max)
    {
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start < 0 || end <= start)
            return new List<string>();

        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(raw[start..(end + 1)]);
            return arr?
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Select(q => q.Trim())
                .Take(max)
                .ToList() ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static IReadOnlyList<ChatMessage> BuildSynthesisPrompt(
        string question, IReadOnlyList<SearchResult> sources, string personaPrefix)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sources:");
        for (var i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            var body = string.IsNullOrWhiteSpace(s.Content) ? s.Snippet : s.Content;
            sb.AppendLine($"[{i + 1}] {s.Title}");
            sb.AppendLine($"URL: {s.Url}");
            sb.AppendLine(body);
            sb.AppendLine();
        }

        sb.AppendLine($"Question: {question}");

        return new[]
        {
            ChatMessage.System(
                personaPrefix +
                "You are a research assistant. Answer the question using ONLY the numbered sources provided. " +
                "Cite claims inline with bracketed numbers like [1] or [2][3]. Be specific and structured. " +
                "If the sources do not contain the answer, say so plainly rather than guessing."),
            ChatMessage.User(sb.ToString())
        };
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
