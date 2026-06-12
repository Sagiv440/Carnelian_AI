using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AI_Interface.Models;

/// <summary>User-configurable settings, persisted as JSON by <see cref="Services.ISettingsService"/>.</summary>
public sealed class AppSettings
{
    /// <summary>Base URL of the local Ollama server.</summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    // --- Cloud AI providers (Settings → AI Model → Web Models) ---

    /// <summary>OpenAI (ChatGPT) API key. Blank = provider disabled. Plaintext (matches the search keys).</summary>
    public string OpenAiApiKey { get; set; } = "";

    /// <summary>Google Gemini API key. Blank = provider disabled.</summary>
    public string GeminiApiKey { get; set; } = "";

    /// <summary>Anthropic (Claude) API key. Blank = provider disabled.</summary>
    public string AnthropicApiKey { get; set; } = "";

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

    /// <summary>Font family for the app. "Poppins" maps to the embedded font; others use the system font.</summary>
    public string FontFamily { get; set; } = ThemeDefaults.FontFamily;

    /// <summary>Base font size for the app.</summary>
    public double FontSize { get; set; } = ThemeDefaults.FontSize;

    /// <summary>Model selected last time, restored on startup if still installed.</summary>
    public string? DefaultModel { get; set; }

    /// <summary>The agent selected last time (its <c>Id</c>), restored on startup. Defaults to the built-in "assistant".</summary>
    public string ActiveAgentId { get; set; } = "assistant";

    /// <summary>Results requested per web search query.</summary>
    public int SearchResultsPerQuery { get; set; } = 5;

    /// <summary>Pages actually fetched and read during a deep-research run.</summary>
    public int MaxPagesToRead { get; set; } = 5;

    /// <summary>Search queries the planner is asked to generate for a deep-research run.</summary>
    public int ResearchQueryCount { get; set; } = 4;

    /// <summary>Characters of page text kept per source when building the synthesis prompt.</summary>
    public int MaxCharsPerPage { get; set; } = 4000;

    /// <summary>
    /// When on, Deep Research can use a separate model for each of its two LLM steps — query
    /// planning and report synthesis — instead of the chat/selected model. Off = both steps use
    /// the chat model (today's behavior).
    /// </summary>
    public bool DeepResearchUseMultipleModels { get; set; }

    /// <summary>
    /// Model used for Deep Research's query-planning step when <see cref="DeepResearchUseMultipleModels"/>
    /// is on. Stored as "{provider}:{id}" (same convention as <see cref="DefaultModel"/>); blank/unknown
    /// falls back to the chat model. Only the user's question is sent to this model.
    /// </summary>
    public string? DeepResearchPlanningModel { get; set; }

    /// <summary>
    /// Model used for Deep Research's report-synthesis step when <see cref="DeepResearchUseMultipleModels"/>
    /// is on. Stored as "{provider}:{id}"; blank/unknown falls back to the chat model. <b>Web-page contents
    /// are sent to this model</b> — keep it local (Ollama) if the sources may be sensitive.
    /// </summary>
    public string? DeepResearchSynthesisModel { get; set; }

    // --- Project agent ---

    /// <summary>How the project agent's tool calls are gated before they run.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentApprovalMode AgentApproval { get; set; } = AgentApprovalMode.ConfirmDestructive;

    /// <summary>
    /// Whether/how the project agent may install software on the machine (package managers / system-wide
    /// installs). Defaults to <see cref="SoftwareInstallPermission.Never"/>; when Never, system install
    /// commands are refused. <see cref="SoftwareInstallPermission.Ask"/> always confirms each install;
    /// <see cref="SoftwareInstallPermission.Allow"/> follows <see cref="AgentApproval"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SoftwareInstallPermission SoftwareInstall { get; set; } = SoftwareInstallPermission.Never;

    /// <summary>
    /// When the project agent works in named phases, whether it advances automatically (true) or pauses at
    /// each phase boundary for the user's OK (false). Independent of <see cref="AgentApproval"/>. Defaults to
    /// auto-advance so phases are non-disruptive visibility out of the box; the pause is opt-in.
    /// </summary>
    public bool AutoFlowPhases { get; set; } = true;

    /// <summary>Recently opened projects (most-recent-first), shown in the startup launcher. Capped on write.</summary>
    public List<RecentProject> RecentProjects { get; set; } = new();

    // --- MCP (Model Context Protocol) servers ---

    /// <summary>
    /// Configured MCP servers (Settings → AI Features → MCP Servers). Each enabled server's discovered tools
    /// are offered to the Project-mode agent, namespaced <c>mcp__&lt;id&gt;__&lt;tool&gt;</c>. Empty by default.
    /// </summary>
    public List<McpServerConfig> McpServers { get; set; } = new();

    // --- Memory ---

    /// <summary>
    /// Master switch for persistent memory (Settings → Autonomy &amp; Memory). When off, no memory block
    /// is injected into prompts and the <c>remember</c> tool is withheld. Per-agent
    /// <see cref="Agent.MemoryEnabled"/> can still opt an individual agent out while this is on.
    /// </summary>
    public bool GlobalMemoryEnabled { get; set; } = true;

    // --- Agents (Project mode) ---

    /// <summary>
    /// When on, the Project-mode agent picker offers <b>only</b> orchestrator ("team") agents — single
    /// agents are hidden while a project is active (the Lead still delegates to them behind the scenes,
    /// since the delegation roster comes from the registry, not the picker). Off ⇒ the picker still
    /// auto-selects the Lead and badges orchestrators, but single agents stay selectable. Default off.
    /// </summary>
    public bool ProjectTeamAgentsOnly { get; set; } = false;

    // --- Thinking ---

    /// <summary>
    /// How much the model is asked to plan/reason before acting when the Thinking toggle is on
    /// (0 = minimal, 100 = maximum effort).
    /// </summary>
    public int ThinkingEffort { get; set; } = 50;

    // --- Voice (text-to-speech) ---

    /// <summary>Which engine reads replies aloud. <see cref="SpeechProvider.None"/> = voice off.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SpeechProvider SpeechProvider { get; set; } = SpeechProvider.None;

    /// <summary>Path to the Piper executable (used when <see cref="SpeechProvider"/> is Piper).</summary>
    public string PiperExecutablePath { get; set; } = "";

    /// <summary>Path to a Piper voice model (<c>.onnx</c>); its <c>.onnx.json</c> sits beside it.</summary>
    public string PiperModelPath { get; set; } = "";

    /// <summary>When on, each assistant reply is read aloud automatically (composer 🔊 toggle).</summary>
    public bool AutoSpeakReplies { get; set; } = false;

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
