namespace AI_Interface.Models;

/// <summary>User-configurable settings, persisted as JSON by <see cref="Services.ISettingsService"/>.</summary>
public sealed class AppSettings
{
    /// <summary>Base URL of the local Ollama server.</summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model selected last time, restored on startup if still installed.</summary>
    public string? DefaultModel { get; set; }

    /// <summary>Results requested per web search query.</summary>
    public int SearchResultsPerQuery { get; set; } = 5;

    /// <summary>Pages actually fetched and read during a deep-research run.</summary>
    public int MaxPagesToRead { get; set; } = 5;

    /// <summary>Search queries the planner is asked to generate for a deep-research run.</summary>
    public int ResearchQueryCount { get; set; } = 4;

    /// <summary>Characters of page text kept per source when building the synthesis prompt.</summary>
    public int MaxCharsPerPage { get; set; } = 4000;
}
