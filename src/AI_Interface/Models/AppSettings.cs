using System.Text.Json.Serialization;

namespace AI_Interface.Models;

/// <summary>User-configurable settings, persisted as JSON by <see cref="Services.ISettingsService"/>.</summary>
public sealed class AppSettings
{
    /// <summary>Base URL of the local Ollama server.</summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    // --- Theme ---

    /// <summary>Light / dark / follow-system appearance. Defaults to Dark — the site's native look.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;

    /// <summary>Primary accent color (hex), e.g. "#804DEE".</summary>
    public string AccentColor { get; set; } = ThemeDefaults.Accent;

    /// <summary>User message bubble color (hex, may include alpha).</summary>
    public string UserBubbleColor { get; set; } = ThemeDefaults.UserBubble;

    /// <summary>Assistant message bubble color (hex, may include alpha).</summary>
    public string AssistantBubbleColor { get; set; } = ThemeDefaults.AssistantBubble;

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

    // --- Project agent ---

    /// <summary>How the project agent's tool calls are gated before they run.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentApprovalMode AgentApproval { get; set; } = AgentApprovalMode.ConfirmDestructive;

    /// <summary>
    /// Whether the project agent may install software on the machine (package managers / system-wide
    /// installs). Off by default; when off, system install commands are refused. Still gated by
    /// <see cref="AgentApproval"/> when on.
    /// </summary>
    public bool AllowSoftwareInstall { get; set; }

    // --- Thinking ---

    /// <summary>
    /// How much the model is asked to plan/reason before acting when the Thinking toggle is on
    /// (0 = minimal, 100 = maximum effort).
    /// </summary>
    public int ThinkingEffort { get; set; } = 50;

    // --- Web search ---

    /// <summary>Which web search backend to use. Defaults to keyless DuckDuckGo.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SearchProvider SearchProvider { get; set; } = SearchProvider.DuckDuckGo;

    /// <summary>SearXNG instance base URL, e.g. "https://searx.example.org".</summary>
    public string SearxngUrl { get; set; } = "";

    /// <summary>Brave Search API subscription token.</summary>
    public string BraveApiKey { get; set; } = "";

    /// <summary>Tavily Search API key.</summary>
    public string TavilyApiKey { get; set; } = "";

    /// <summary>Google Custom Search JSON API key.</summary>
    public string GoogleApiKey { get; set; } = "";

    /// <summary>Google Programmable Search engine ID (the "cx" parameter).</summary>
    public string GoogleSearchEngineId { get; set; } = "";
}
