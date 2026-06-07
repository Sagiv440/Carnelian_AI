namespace AI_Interface.Models;

/// <summary>A single web search hit, optionally enriched with fetched page text.</summary>
public sealed class SearchResult
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string Snippet { get; init; } = "";

    /// <summary>Readable page text, populated lazily by the page fetcher during deep research.</summary>
    public string? Content { get; set; }
}
