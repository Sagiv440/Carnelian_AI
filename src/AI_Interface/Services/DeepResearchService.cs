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
/// cited answer. All model work runs through the local Ollama model; nothing leaves the machine
/// except the web searches and page fetches themselves.
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
        CancellationToken ct = default)
    {
        var cfg = _settings.Current;

        // 1. Plan: let the model decompose the question into focused search queries.
        status.Report("Planning search queries…");
        var queries = await PlanQueriesAsync(client, question, model, cfg.ResearchQueryCount, ct).ConfigureAwait(false);
        status.Report($"Planned {queries.Count} queries: {string.Join(" | ", queries)}");

        // 2. Search each query, collecting unique results across all of them.
        var byUrl = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();
            status.Report($"Searching: {query}");
            var hits = await _search.SearchAsync(query, cfg.SearchResultsPerQuery, ct).ConfigureAwait(false);
            foreach (var hit in hits)
                byUrl.TryAdd(hit.Url, hit);
        }

        if (byUrl.Count == 0)
        {
            onAnswerDelta("I couldn't find any web results for this question. " +
                          "Check your internet connection, or try rephrasing.");
            return Array.Empty<SearchResult>();
        }

        // 3. Read the top unique pages so the model works from full text, not just snippets.
        var toRead = byUrl.Values.Take(cfg.MaxPagesToRead).ToList();
        foreach (var source in toRead)
        {
            ct.ThrowIfCancellationRequested();
            status.Report($"Reading: {Trim(source.Title, 80)}");
            source.Content = await _search
                .FetchReadableTextAsync(source.Url, cfg.MaxCharsPerPage, ct)
                .ConfigureAwait(false);
        }

        // 4. Synthesize a cited answer, streamed back to the caller (in the active agent's voice).
        status.Report("Synthesizing answer…");
        var messages = BuildSynthesisPrompt(question, toRead, personaPrefix);
        await foreach (var delta in client.ChatStreamAsync(model, messages, think: false, ct).ConfigureAwait(false))
            onAnswerDelta(delta);

        return toRead;
    }

    private async Task<List<string>> PlanQueriesAsync(
        IChatClient client, string question, string model, int count, CancellationToken ct)
    {
        var messages = new[]
        {
            ChatMessage.System(
                "You are a research planner. Break the user's question into focused, diverse web-search " +
                $"queries that together would answer it. Respond with ONLY a JSON array of at most {count} " +
                "short query strings — no prose, no code fences."),
            ChatMessage.User(question)
        };

        try
        {
            var raw = await client.CompleteAsync(model, messages, ct).ConfigureAwait(false);
            var parsed = ParseJsonStringArray(raw, count);
            if (parsed.Count > 0)
                return parsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Planning is best-effort; fall through to the raw question.
        }

        return new List<string> { question };
    }

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
