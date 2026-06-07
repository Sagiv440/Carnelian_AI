using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>Web search plus readable-text extraction, used by chat web-search and deep research.</summary>
public interface IWebSearchService
{
    /// <summary>Runs a single web search and returns up to <paramref name="maxResults"/> hits.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken ct = default);

    /// <summary>Downloads a page and extracts its readable text, truncated to <paramref name="maxChars"/>.</summary>
    Task<string> FetchReadableTextAsync(
        string url, int maxChars, CancellationToken ct = default);
}
