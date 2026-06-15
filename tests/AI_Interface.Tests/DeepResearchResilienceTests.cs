using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Verifies a Deep Research run is best-effort: a single failing search query or unreadable page is
/// skipped, not fatal — the run still reaches synthesis. (Regression for "the agent fails mid search".)
/// </summary>
public sealed class DeepResearchResilienceTests
{
    [Fact]
    public async Task RunAsync_FailingQueryAndPage_AreSkipped_AndSynthesisStillRuns()
    {
        var svc = new DeepResearchService(new FlakySearch(), new FakeSettings());
        var answer = new StringBuilder();

        var sources = await svc.RunAsync(
            new FakeChat(), "question", "model", personaPrefix: "",
            new Progress<string>(_ => { }), d => answer.Append(d));

        // q1 threw → skipped; q2 succeeded → one source. The page fetch threw but was swallowed,
        // and synthesis still produced an answer.
        Assert.Single(sources);
        Assert.Contains("Synthesized answer.", answer.ToString());
    }

    [Fact]
    public async Task RunAsync_AllSearchesFail_ReportsNoResults_WithoutThrowing()
    {
        var svc = new DeepResearchService(new AlwaysFailSearch(), new FakeSettings());
        var answer = new StringBuilder();

        var sources = await svc.RunAsync(
            new FakeChat(), "question", "model", personaPrefix: "",
            new Progress<string>(_ => { }), d => answer.Append(d));

        Assert.Empty(sources);
        Assert.Contains("couldn't find any web results", answer.ToString());
    }

    // --- test doubles ------------------------------------------------------------------------

    private sealed class FlakySearch : IWebSearchService
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default)
        {
            if (query == "q1")
                throw new HttpRequestException("search boom");
            return Task.FromResult<IReadOnlyList<SearchResult>>(
                new[] { new SearchResult { Title = "T", Url = "https://example.com", Snippet = "snip" } });
        }

        public Task<string> FetchReadableTextAsync(string url, int maxChars, CancellationToken ct = default) =>
            throw new HttpRequestException("page boom");
    }

    private sealed class AlwaysFailSearch : IWebSearchService
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default) =>
            throw new TaskCanceledException("timeout"); // a timeout (ct not requested) must not abort the run

        public Task<string> FetchReadableTextAsync(string url, int maxChars, CancellationToken ct = default) =>
            Task.FromResult("");
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public void Save() { }
    }

    private sealed class FakeChat : IChatClient
    {
        public AiProvider Provider => AiProvider.Ollama;

        public async IAsyncEnumerable<string> ChatStreamAsync(
            string model, IEnumerable<ChatMessage> messages, bool think,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return "Synthesized answer.";
        }

        public Task<string> CompleteAsync(string model, IEnumerable<ChatMessage> messages, CancellationToken ct = default) =>
            Task.FromResult("[\"q1\", \"q2\"]");

        public Task<AgentTurn> ChatWithToolsAsync(
            string model, IEnumerable<ChatMessage> messages, IReadOnlyList<AgentTool> tools, CancellationToken ct = default) =>
            Task.FromResult(new AgentTurn("", Array.Empty<AgentToolCall>()));

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<bool> IsConfiguredAndReachableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
