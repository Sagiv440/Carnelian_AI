using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;

namespace AI_Interface.ViewModels;

// Lightweight stand-ins used only by the XAML previewer's design-time DataContext
// (MainWindowViewModel's parameterless constructor). They never run at runtime, where
// real services are injected via DI.

internal sealed class DesignOllamaClient : IOllamaClient
{
    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(new[] { "llama3:latest", "mistral:latest" });

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model, IEnumerable<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield return "This is a design-time preview response.";
    }

    public Task<string> CompleteAsync(
        string model, IEnumerable<ChatMessage> messages, CancellationToken ct = default) =>
        Task.FromResult("design-time");
}

internal sealed class DesignWebSearchService : IWebSearchService
{
    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SearchResult>>(new[]
        {
            new SearchResult { Title = "Example result", Url = "https://example.com", Snippet = "Sample snippet." }
        });

    public Task<string> FetchReadableTextAsync(string url, int maxChars, CancellationToken ct = default) =>
        Task.FromResult("Sample page text.");
}

internal sealed class DesignDeepResearchService : IDeepResearchService
{
    public Task<IReadOnlyList<SearchResult>> RunAsync(
        string question, string model, IProgress<string> status,
        Action<string> onAnswerDelta, CancellationToken ct = default)
    {
        onAnswerDelta("Design-time research answer.");
        return Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());
    }
}

internal sealed class DesignSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new();
    public void Save() { }
}
