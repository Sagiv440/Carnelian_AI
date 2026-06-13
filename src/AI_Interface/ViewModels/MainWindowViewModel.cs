using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Interface.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// Base (mode-neutral) chat instructions. The active agent's persona is layered on top via
    /// <see cref="AgentPromptBuilder"/>, so this is no longer the whole system prompt.
    /// </summary>
    private const string ChatBaseInstructions =
        "You are running locally via Ollama. Be accurate and concise.";

    private readonly IModelRouter _router;
    private readonly IWebSearchService _search;
    private readonly IDeepResearchService _research;
    private readonly ISettingsService _settings;
    private readonly IAttachmentService _attachments;
    private readonly IChatHistoryService _history;
    private readonly IProjectAgentService _agent;
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IProjectSkillService _skills;
    private readonly IProjectDocsService _projectDocs;
    private readonly ISpeechService _speech;
    private readonly IAgentService _agents;
    private readonly IMemoryService _memory;
    private readonly IMcpService _mcp;
    private readonly IOllamaClient _ollama;
    private readonly IOllamaInstaller _ollamaInstaller;
    private readonly IPiperInstaller _piperInstaller;
    private readonly IUsageTracker _usage;

    /// <summary>Fallback when no Ollama URL is configured (matches the AppSettings default).</summary>
    private const string DefaultOllamaUrl = "http://localhost:11434";

    /// <summary>Skill files loaded from the active project (empty when not in a project).</summary>
    private IReadOnlyList<ProjectSkill> _projectSkills = Array.Empty<ProjectSkill>();

    /// <summary>The active project's AI_DOCS.md handbook text ("" when absent or not in a project).</summary>
    private string _projectDocsText = "";

    private CancellationTokenSource? _cts;

    /// <summary>The conversation currently shown in the transcript.</summary>
    private ChatSession _currentSession = new();

    /// <summary>Raised when the view should scroll the transcript to the bottom.</summary>
    public event EventHandler? ScrollToEndRequested;

    /// <summary>Raised when the view should open the Settings window (a view-only concern).</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the view should open a file picker for the given attachment kind.</summary>
    public event EventHandler<AttachmentKind>? AttachFilesRequested;

    /// <summary>Raised when the view should open the MCP resource browser (composer 📎 menu → "From MCP server…").</summary>
    public event EventHandler? McpResourcesRequested;

    /// <summary>Raised when the view should open the New Project window (a view-only concern).</summary>
    public event EventHandler? ProjectRequested;

    /// <summary>Raised when the project agent needs the user to approve a tool call.</summary>
    public event EventHandler<ToolApprovalEventArgs>? ToolApprovalRequested;

    /// <summary>Raised when the project agent reaches a phase boundary and needs the user's OK to continue.</summary>
    public event EventHandler<PhaseGateEventArgs>? PhaseGateRequested;

    /// <summary>Raised when the project agent asks a clarifying question (the <c>ask_user</c> tool).</summary>
    public event EventHandler<ClarifyEventArgs>? ClarificationRequested;

    /// <summary>Raised from the sidebar Tools menu to open the hardware-aware Model Config tool.</summary>
    public event EventHandler? ModelConfigRequested;

    /// <summary>Raised from the sidebar Tools menu to open the Piper Voice browser.</summary>
    public event EventHandler? VoiceBrowserRequested;

    /// <summary>Raised before installing Ollama so the view can confirm; the decision returns via the TCS.</summary>
    public event EventHandler<TaskCompletionSource<bool>>? InstallOllamaConfirmationRequested;

    /// <summary>Raised before installing Piper so the view can confirm; the decision returns via the TCS.</summary>
    public event EventHandler<TaskCompletionSource<bool>>? InstallPiperConfirmationRequested;

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    /// <summary>Models offered in the top-bar picker, across every configured provider.</summary>
    public ObservableCollection<ChatModel> Models { get; } = new();

    /// <summary>Agents offered in the top-bar picker (built-in + global + the active project's customs).</summary>
    public ObservableCollection<Agent> Agents { get; } = new();

    /// <summary>
    /// The agent that was selected before entering a project, when Project mode auto-switched to the Lead.
    /// Restored on project exit so entering a project never permanently changes the global agent preference.
    /// Null when no override is pending (already an orchestrator on entry, or not in a project).
    /// </summary>
    private string? _preProjectAgentId;

    public IReadOnlyList<ModeOption> Modes { get; }

    /// <summary>The composer's search-scope dropdown options (Local / Web / Deep), a subset of <see cref="Modes"/>.</summary>
    public IReadOnlyList<ModeOption> SearchModes { get; }

    /// <summary>Saved conversations, newest first (the sidebar "Chat Log").</summary>
    public ObservableCollection<ChatSession> ChatLog { get; } = new();

    /// <summary>Files staged for the next prompt (shown as chips in the composer).</summary>
    public ObservableCollection<Attachment> Attachments { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _hasAttachments;

    /// <summary>MCP resources fetched and staged for the next prompt (shown as chips in the composer).</summary>
    public ObservableCollection<McpAttachedResource> McpResources { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _hasMcpResources;

    /// <summary>Per-prompt toggle: when on, the model is asked to plan before answering (depth = Effort setting).</summary>
    [ObservableProperty]
    private bool _thinkingEnabled;

    /// <summary>When on, each completed reply is read aloud automatically. Persisted; composer 🔊 toggle.</summary>
    [ObservableProperty]
    private bool _autoSpeakEnabled;

    /// <summary>True when a voice is set up — gates the composer's Auto-read toggle's visibility.</summary>
    public bool IsVoiceConfigured => _speech.IsConfigured;

    /// <summary>Re-evaluate <see cref="IsVoiceConfigured"/> (e.g. after Settings → Voice may have changed).</summary>
    public void RefreshVoiceAvailability() => OnPropertyChanged(nameof(IsVoiceConfigured));

    // ---- Sidebar Tools menu (Model Config / Voice Browser) -------------------------------------

    /// <summary>True when the local Ollama server is reachable — gates the Tools ▸ Model Config entry.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenModelConfigCommand))]
    private bool _isOllamaReady;

    /// <summary>True when the Piper engine is installed — gates the Tools ▸ Voice Browser entry.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenVoiceBrowserCommand))]
    private bool _isPiperReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallOllamaCommand))]
    private bool _isInstallingOllama;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallPiperCommand))]
    private bool _isInstallingPiper;

    /// <summary>Transient status shown under the Model Config entry while/after an Ollama install.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOllamaInstallStatus))]
    private string _ollamaInstallStatus = "";

    /// <summary>Transient status shown under the Voice Browser entry while/after a Piper install.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPiperInstallStatus))]
    private string _piperInstallStatus = "";

    public bool HasOllamaInstallStatus => !string.IsNullOrEmpty(OllamaInstallStatus);
    public bool HasPiperInstallStatus => !string.IsNullOrEmpty(PiperInstallStatus);

    /// <summary>
    /// Re-evaluate which Tools entries are available — Ollama reachable (ping) and the Piper engine
    /// installed. Called when the Tools menu opens so the entries reflect reality. Best-effort: a failed
    /// probe just means "not ready" (the entry stays grayed out with its install option offered).
    /// </summary>
    public async Task RefreshToolsAvailabilityAsync()
    {
        // Best-effort throughout (this is called fire-and-forget from the Tools click): a throwing
        // filesystem scan or ping must not surface as an unobserved task fault — just mean "not ready".
        try { IsPiperReady = _piperInstaller.IsEngineInstalled; }
        catch { IsPiperReady = false; }

        var url = string.IsNullOrWhiteSpace(OllamaBaseUrl) ? DefaultOllamaUrl : OllamaBaseUrl.Trim();
        try { IsOllamaReady = await _ollama.PingAsync(url).ConfigureAwait(true); }
        catch { IsOllamaReady = false; }
    }

    /// <summary>Open the hardware-aware Model Config tool (enabled only when Ollama is reachable).</summary>
    [RelayCommand(CanExecute = nameof(IsOllamaReady))]
    private void OpenModelConfig() => ModelConfigRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Open the Piper Voice browser (enabled only when the Piper engine is installed).</summary>
    [RelayCommand(CanExecute = nameof(IsPiperReady))]
    private void OpenVoiceBrowser() => VoiceBrowserRequested?.Invoke(this, EventArgs.Empty);

    private bool CanInstallOllama => !IsInstallingOllama;

    /// <summary>Download and install the local Ollama runtime, then reload the model list + re-probe.</summary>
    [RelayCommand(CanExecute = nameof(CanInstallOllama))]
    private async Task InstallOllama()
    {
        // Confirm first — this downloads and installs additional software onto the user's machine.
        if (InstallOllamaConfirmationRequested is not null)
        {
            var tcs = new TaskCompletionSource<bool>();
            InstallOllamaConfirmationRequested.Invoke(this, tcs);
            if (!await tcs.Task)
                return;
        }

        IsInstallingOllama = true;
        // Idempotent: when Ollama is already present, InstallAsync just starts the server.
        OllamaInstallStatus = _ollamaInstaller.IsOllamaInstalled
            ? "Ollama already installed — starting…"
            : "Downloading Ollama…";
        var progress = new Progress<string>(s => OllamaInstallStatus = s);
        try
        {
            await _ollamaInstaller.InstallAsync(progress, CancellationToken.None);
            await RefreshAsync();                  // reload the model picker now Ollama may be up
            await RefreshToolsAvailabilityAsync(); // re-evaluate the Model Config gate
            OllamaInstallStatus = IsOllamaReady ? "Ollama ready." : "Installed — start Ollama, then reopen this menu.";
        }
        catch (Exception ex)
        {
            OllamaInstallStatus = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsInstallingOllama = false;
        }
    }

    private bool CanInstallPiper => !IsInstallingPiper;

    /// <summary>Download and install the Piper voice engine, then switch the voice provider on.</summary>
    [RelayCommand(CanExecute = nameof(CanInstallPiper))]
    private async Task InstallPiper()
    {
        // Confirm first — this downloads and installs additional software onto the user's machine.
        if (InstallPiperConfirmationRequested is not null)
        {
            var tcs = new TaskCompletionSource<bool>();
            InstallPiperConfirmationRequested.Invoke(this, tcs);
            if (!await tcs.Task)
                return;
        }

        IsInstallingPiper = true;
        PiperInstallStatus = "Downloading Piper…";
        var progress = new Progress<string>(s => PiperInstallStatus = s);
        try
        {
            // InstallEngineAsync persists the resolved executable path to settings; make Piper the active
            // engine so a voice picked in the browser is used right away (a voice is still needed to speak).
            await _piperInstaller.InstallEngineAsync(progress, CancellationToken.None);
            _settings.Current.SpeechProvider = SpeechProvider.Piper;
            _settings.Save();
            IsPiperReady = _piperInstaller.IsEngineInstalled;
            RefreshVoiceAvailability();
            PiperInstallStatus = "Piper installed — open Voice Browser to add a voice.";
        }
        catch (Exception ex)
        {
            PiperInstallStatus = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsInstallingPiper = false;
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = "";

    // ---- slash (/) command palette -------------------------------------------------------------

    /// <summary>The commands currently shown in the slash palette (filtered + context-aware).</summary>
    public ObservableCollection<SlashCommand> SlashCommands { get; } = new();

    /// <summary>The full command set, built lazily once (its actions reference this VM's commands).</summary>
    private IReadOnlyList<SlashCommand>? _allSlashCommands;

    /// <summary>MCP prompt slash-commands, discovered per project (rebuilt by <see cref="RefreshMcpPromptCommandsAsync"/>).</summary>
    private IReadOnlyList<SlashCommand> _mcpPromptCommands = Array.Empty<SlashCommand>();

    /// <summary>True while the slash palette is showing (the composer starts with "/" and matches commands).</summary>
    [ObservableProperty]
    private bool _isSlashMenuOpen;

    /// <summary>Highlighted row in the palette (driven by ↑/↓; two-way bound to the menu ListBox).</summary>
    [ObservableProperty]
    private int _selectedSlashIndex;

    /// <summary>Re-evaluates the slash palette whenever the composer text changes.</summary>
    partial void OnInputTextChanged(string value) => UpdateSlashMenu(value);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private ChatModel? _selectedModel;

    /// <summary>The active agent whose persona is applied to every mode's system prompt.</summary>
    [ObservableProperty]
    private Agent? _selectedAgent;

    [ObservableProperty]
    private ModeOption _selectedMode;

    [ObservableProperty]
    private string _ollamaBaseUrl;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>The active project, or null when not in project mode. Single active project per session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveProject))]
    [NotifyPropertyChangedFor(nameof(ActiveProjectName))]
    [NotifyPropertyChangedFor(nameof(ActiveProjectDirectory))]
    private Project? _activeProject;

    public bool HasActiveProject => ActiveProject is not null;
    public string ActiveProjectName => ActiveProject?.Name ?? "";
    public string ActiveProjectDirectory => ActiveProject?.Directory ?? "";

    /// <summary>Number of skill files loaded from the active project (shown in the project card).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjectSkills))]
    [NotifyPropertyChangedFor(nameof(ProjectSkillSummary))]
    private int _projectSkillCount;

    public bool HasProjectSkills => ProjectSkillCount > 0;
    public string ProjectSkillSummary => ProjectSkillCount == 1 ? "1 skill loaded" : $"{ProjectSkillCount} skills loaded";

    /// <summary>True when the active project ships an AI_DOCS.md handbook (shown in the project card).</summary>
    [ObservableProperty]
    private bool _hasProjectDocs;

    /// <summary>Sidebar tab state (only meaningful while a project is loaded): false = Chat Log, true = Files.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatLogTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsFilesTabSelected))]
    private bool _showProjectFiles;

    public bool IsChatLogTabSelected => !ShowProjectFiles;
    public bool IsFilesTabSelected => ShowProjectFiles;

    /// <summary>Root of the project file tree (one node = the project directory) for the Files tab.</summary>
    public ObservableCollection<FileNode> FileTree { get; } = new();

    // --- Startup launcher (in-place: the window opens on this, then switches to the chat UI) ---

    /// <summary>True while the in-place startup launcher overlay is shown (recent projects + Local Chat).</summary>
    [ObservableProperty]
    private bool _showStartupLauncher = true;

    /// <summary>How many recent projects the launcher shows.</summary>
    private const int MaxShownRecentProjects = 8;

    /// <summary>Recent projects shown on the launcher (most-recent-first, pruned to folders that still exist).</summary>
    public ObservableCollection<RecentProject> StartupRecentProjects { get; } = new();

    /// <summary>True when there's at least one recent project (toggles the launcher's empty-state hint).</summary>
    public bool HasStartupRecentProjects { get; private set; }

    /// <summary>Dismisses the launcher and stays in the global (no-project) chat.</summary>
    [RelayCommand]
    private void StartLocalChat() => ShowStartupLauncher = false;

    /// <summary>Opens a recent project from the launcher (ActivateProjectAsync dismisses the launcher).</summary>
    [RelayCommand]
    private async Task OpenRecentProject(RecentProject? recent)
    {
        if (recent is not null && !string.IsNullOrWhiteSpace(recent.Directory))
            await ActivateProjectAsync(new Project(recent.Name, recent.Directory));
    }

    // Live file-tree updates. A FileSystemWatcher runs only while the Files tab is open (switching to it
    // already rebuilds the tree, so staleness only matters while it stays open). Bursty FS events are
    // coalesced through a short debounce, then each touched directory node is reconciled in place.
    private FileSystemWatcher? _fileWatcher;
    private System.Threading.Timer? _fileDebounce;
    private readonly HashSet<string> _pendingDirRefresh = new(PathComparer);
    private readonly object _pendingLock = new();

    /// <summary>Case rules for path identity match the host filesystem (Windows is case-insensitive).</summary>
    private static readonly StringComparison PathCmp =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public MainWindowViewModel(
        IModelRouter router,
        IWebSearchService search,
        IDeepResearchService research,
        ISettingsService settings,
        IAttachmentService attachments,
        IChatHistoryService history,
        IProjectAgentService agent,
        IAgentOrchestrator orchestrator,
        IProjectSkillService skills,
        IProjectDocsService projectDocs,
        ISpeechService speech,
        IAgentService agents,
        IMemoryService memory,
        IMcpService mcp,
        IOllamaClient ollama,
        IOllamaInstaller ollamaInstaller,
        IPiperInstaller piperInstaller,
        IUsageTracker usage)
    {
        _router = router;
        _search = search;
        _research = research;
        _settings = settings;
        _attachments = attachments;
        _history = history;
        _agent = agent;
        _orchestrator = orchestrator;
        _skills = skills;
        _projectDocs = projectDocs;
        _speech = speech;
        _agents = agents;
        _memory = memory;
        _mcp = mcp;
        _ollama = ollama;
        _ollamaInstaller = ollamaInstaller;
        _piperInstaller = piperInstaller;
        _usage = usage;
        _autoSpeakEnabled = settings.Current.AutoSpeakReplies;

        Modes = new[]
        {
            new ModeOption(AppMode.Chat, "Chat", "Talk directly to the local model."),
            new ModeOption(AppMode.WebSearch, "Web Search", "Search the web once, then answer with citations."),
            new ModeOption(AppMode.DeepResearch, "Deep Research", "Plan queries, read pages, synthesize a cited report."),
            new ModeOption(AppMode.Project, "Project", "Use tools to edit files and run commands in a project directory.")
        };
        SearchModes = new[]
        {
            new ModeOption(AppMode.Chat, "💬  Local search", "Answer with the selected model only — no web."),
            new ModeOption(AppMode.WebSearch, "🌐  Web search", "Search the web once, then answer with citations."),
            new ModeOption(AppMode.DeepResearch, "🔎  Deep search", "Plan queries, read pages, then synthesize a cited report.")
        };
        _selectedMode = Modes[0];
        _ollamaBaseUrl = settings.Current.OllamaBaseUrl;
        // The saved selection is restored after the model list loads (see RefreshAsync).

        LoadAgents(); // built-in roster + global customs (project customs join on project activation)

        foreach (var session in _history.Load())
            ChatLog.Add(session);

        // The window starts on the in-place startup launcher (recent projects + Local Chat).
        foreach (var r in PrunedRecent(settings.Current.RecentProjects, MaxShownRecentProjects))
            StartupRecentProjects.Add(r);
        HasStartupRecentProjects = StartupRecentProjects.Count > 0;
    }

    // Design-time / fallback constructor so the XAML previewer can instantiate the window.
    public MainWindowViewModel()
        : this(new DesignModelRouter(), new DesignWebSearchService(),
               new DesignDeepResearchService(), new DesignSettingsService(),
               new DesignAttachmentService(), new DesignChatHistoryService(),
               new DesignProjectAgentService(), new DesignAgentOrchestrator(),
               new DesignProjectSkillService(), new DesignProjectDocsService(),
               new DesignSpeechService(), new DesignAgentService(), new DesignMemoryService(),
               new DesignMcpService(), new DesignOllamaClient(), new DesignOllamaInstaller(),
               new DesignPiperInstaller(), new DesignUsageTracker())
    {
    }

    partial void OnOllamaBaseUrlChanged(string value)
    {
        // Keep the live setting in sync so the Ollama client picks up the new URL immediately.
        _settings.Current.OllamaBaseUrl = value.Trim();
    }

    partial void OnSelectedModelChanged(ChatModel? value)
    {
        // Persist as "{provider}:{id}" so a cloud model round-trips by provider, not just by id.
        _settings.Current.DefaultModel = value is null ? null : $"{value.Provider}:{value.Id}";
        _settings.Save();
    }

    partial void OnSelectedAgentChanged(Agent? value)
    {
        // Persist the chosen agent's id so it's restored next launch (like DefaultModel) — but ONLY outside
        // a project. In Project mode the selection is transient: entering a project auto-selects the Lead,
        // and persisting that (or any in-project switch) would silently overwrite the user's global agent
        // preference if the app closed mid-project. The pre-project pick is restored on ExitProject.
        if (value is not null && ActiveProject is null)
        {
            _settings.Current.ActiveAgentId = value.Id;
            _settings.Save();
        }
    }

    /// <summary>The active agent's persona + skills + memory block, prepended to every mode's system prompt (empty = none).</summary>
    private string PersonaPrefix() => AgentPromptBuilder.PersonaPrefix(SelectedAgent, MemoryBlock());

    /// <summary>True when persistent memory should apply this turn: the global switch on AND the active agent opted in.</summary>
    private bool MemoryActive() =>
        _settings.Current.GlobalMemoryEnabled && (SelectedAgent?.MemoryEnabled ?? true);

    /// <summary>The persistent-memory block for the current turn (global + active-project facts), or "" when off/empty.</summary>
    private string MemoryBlock() =>
        MemoryActive() ? _memory.BuildContextBlock(ActiveProject?.Directory) : "";

    /// <summary>
    /// If memory is on and the prompt is an explicit "remember …" instruction, persists the fact before the
    /// turn runs (so the model sees it and can acknowledge it). Project facts go to project memory when a
    /// project is active; otherwise to global (about the user). Returns silently when it's not a remember.
    /// </summary>
    private void MaybeRememberFromPrompt(string prompt)
    {
        if (!MemoryActive())
            return;
        var fact = ExtractRememberFact(prompt);
        if (string.IsNullOrEmpty(fact))
            return;
        var scope = ActiveProject is not null ? MemoryScope.Project : MemoryScope.Global;
        _memory.Add(scope, fact, "you", ActiveProject?.Directory);
    }

    /// <summary>Extracts the fact from an explicit "remember …" prompt, or null if the prompt isn't one.</summary>
    private static string? ExtractRememberFact(string prompt)
    {
        var p = prompt.Trim();
        if (p.StartsWith("please ", StringComparison.OrdinalIgnoreCase))
            p = p[7..].TrimStart();
        if (!p.StartsWith("remember", StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = p["remember".Length..].TrimStart();
        if (rest.StartsWith("that ", StringComparison.OrdinalIgnoreCase))
            rest = rest[5..];
        else if (rest.StartsWith(":") || rest.StartsWith(","))
            rest = rest[1..];
        rest = rest.Trim().TrimEnd('.', '!', ' ');

        return rest.Length >= 3 ? rest : null; // ignore a bare "remember"
    }

    /// <summary>
    /// Phase 5 (proactive): asks the model for a few short follow-up actions and attaches them to the
    /// assistant message as clickable chips. Best-effort — any failure (or no useful suggestions, or a
    /// user stop) just yields none and never disturbs the completed turn.
    /// </summary>
    private async Task GenerateSuggestionsAsync(
        IChatClient client, string model, string prompt, MessageViewModel assistant, CancellationToken ct)
    {
        try
        {
            var answer = assistant.Text.Length > 2000 ? assistant.Text[..2000] : assistant.Text;

            var messages = new List<ChatMessage>
            {
                ChatMessage.System(
                    "You propose concise next steps. Given the user's request and the assistant's answer, " +
                    "suggest 2 to 4 short follow-up actions the user might take next. Each is a brief imperative " +
                    "phrase of 3-8 words (e.g. \"Add unit tests\"). Output one per line — no numbering, bullets, " +
                    "or extra commentary. If there are no useful follow-ups, reply with exactly NONE."),
                new ChatMessage(ChatRole.User, $"Request:\n{prompt}\n\nAnswer:\n{answer}")
            };

            var raw = await client.CompleteAsync(model, messages, ct);
            var suggestions = ParseSuggestions(raw);
            if (suggestions.Count > 0)
                assistant.SetSuggestions(suggestions);
        }
        catch (OperationCanceledException)
        {
            // user stopped — no suggestions
        }
        catch
        {
            // best-effort: never let suggestion generation break the completed turn
        }
    }

    /// <summary>Parses the model's reply into a short, de-duplicated list of next-step phrases (max 4).</summary>
    private static List<string> ParseSuggestions(string raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return result;

        foreach (var line in raw.Replace("\r\n", "\n").Split('\n'))
        {
            // Strip any leading bullet/number marker ("- ", "* ", "• ", "1.", "2)") the model may add.
            var s = System.Text.RegularExpressions.Regex.Replace(line.Trim(), @"^\s*([-*•]|\d+[.)])\s+", "");
            s = s.Trim().Trim('"', '\'', '.', ' ');

            if (s.Length < 2 || s.Length > 80 || s.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!result.Any(x => x.Equals(s, StringComparison.OrdinalIgnoreCase)))
                result.Add(s);
            if (result.Count == 4)
                break;
        }
        return result;
    }

    /// <summary>Drops a clicked suggestion chip into the composer for the user to review/edit, then send.</summary>
    [RelayCommand]
    private void UseSuggestion(string? suggestion)
    {
        if (!string.IsNullOrWhiteSpace(suggestion))
            InputText = suggestion!;
    }

    /// <summary>
    /// Rebuilds the agent picker from the registry (built-in + global + the active project's customs)
    /// and restores the saved selection. Called on construction, on project enter/exit, and after the
    /// Settings → Agents panel may have changed the roster.
    /// </summary>
    public void LoadAgents()
    {
        var previousId = SelectedAgent?.Id ?? _settings.Current.ActiveAgentId;

        var roster = _agents.ListAgents(ActiveProject?.Directory);

        // In a project, lead with the "team" experience: orchestrators sorted first, and (when the user
        // opted in) single agents hidden entirely. Outside a project the picker order is untouched.
        // Hiding single agents here doesn't affect delegation — the Lead's roster comes from the registry.
        IEnumerable<Agent> picker = ActiveProject is not null
            ? ProjectAgentPicker.Arrange(roster, _settings.Current.ProjectTeamAgentsOnly)
            : roster;

        Agents.Clear();
        foreach (var agent in picker)
            Agents.Add(agent);

        SelectedAgent =
            Agents.FirstOrDefault(a => string.Equals(a.Id, previousId, StringComparison.OrdinalIgnoreCase))
            ?? Agents.FirstOrDefault(a => string.Equals(a.Id, _agents.Default.Id, StringComparison.OrdinalIgnoreCase))
            ?? Agents.FirstOrDefault();
    }

    /// <summary>Parses a persisted "{provider}:{id}" selection back into a <see cref="ChatModel"/>.</summary>
    private static ChatModel? ParseSavedModel(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
            return null;

        var sep = saved.IndexOf(':');
        if (sep > 0 && Enum.TryParse<AiProvider>(saved[..sep], out var provider))
        {
            var id = saved[(sep + 1)..];
            if (!string.IsNullOrEmpty(id))
                return new ChatModel(provider, id);
        }

        // Legacy value (a bare Ollama model name with no provider prefix) — treat as Ollama.
        return new ChatModel(AiProvider.Ollama, saved);
    }

    /// <summary>
    /// Resolves a saved "{provider}:{id}" Deep Research model override against the live model list.
    /// Returns the parsed model only if it's currently <paramref name="available"/> (compared by
    /// Provider+Id) — membership is the reachability/fallback guarantee — otherwise <c>null</c>.
    /// </summary>
    internal static ChatModel? ResolveModelOverride(string? setting, IReadOnlyList<ChatModel> available)
    {
        var parsed = ParseSavedModel(setting);
        if (parsed is null)
            return null;
        return available.Any(m => m.Provider == parsed.Provider && m.Id == parsed.Id) ? parsed : null;
    }

    /// <summary>
    /// The composer's Local / Web / Deep search dropdown selection, mapped to the active
    /// <see cref="AppMode"/>. (In Project mode the dropdown is hidden; the getter falls back to the first option.)
    /// </summary>
    public ModeOption SelectedSearchMode
    {
        get => SearchModes.FirstOrDefault(o => o.Mode == SelectedMode.Mode) ?? SearchModes[0];
        set { if (value is not null) SetMode(value.Mode); }
    }

    partial void OnSelectedModeChanged(ModeOption value) =>
        OnPropertyChanged(nameof(SelectedSearchMode));

    partial void OnAutoSpeakEnabledChanged(bool value)
    {
        _settings.Current.AutoSpeakReplies = value;
        _settings.Save();
    }

    private void SetMode(AppMode mode)
    {
        var option = Modes.FirstOrDefault(o => o.Mode == mode);
        if (option is not null)
            SelectedMode = option;
    }

    /// <summary>Called once after the view loads: connect and load the model list.</summary>
    public async Task InitializeAsync()
    {
        RefreshVoiceAvailability(); // reflect a previously-installed voice in the composer toggle
        await RefreshAsync().ConfigureAwait(true);
        await RefreshToolsAvailabilityAsync().ConfigureAwait(true); // prime the Tools menu's entries
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _settings.Current.OllamaBaseUrl = OllamaBaseUrl.Trim();
        _settings.Save();

        ConnectionStatus = "Connecting…";

        IReadOnlyList<ChatModel> models;
        try
        {
            // The router queries every configured provider best-effort and aggregates the results.
            models = await _router.ListAllModelsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = $"Error listing models: {ex.Message}";
            Models.Clear();
            return;
        }

        Models.Clear();
        foreach (var m in models)
            Models.Add(m);

        // Connected if ANY provider returned at least one model.
        IsConnected = Models.Count > 0;
        if (!IsConnected)
        {
            ConnectionStatus =
                $"Offline — is Ollama running at {OllamaBaseUrl}, or add a cloud API key in Settings → AI Model?";
            SelectedModel = null;
            return;
        }

        var providerCount = Models.Select(m => m.Provider).Distinct().Count();
        ConnectionStatus = providerCount == 1
            ? $"Connected — {Models.Count} model(s)"
            : $"Connected — {Models.Count} model(s) across {providerCount} provider(s)";

        // Restore the saved selection if still present, otherwise pick the first model.
        var saved = ParseSavedModel(_settings.Current.DefaultModel);
        SelectedModel = saved is not null && Models.Contains(saved)
            ? saved
            : Models.FirstOrDefault();
    }

    private bool CanSend =>
        !IsBusy && (!string.IsNullOrWhiteSpace(InputText) || HasAttachments || HasMcpResources) && SelectedModel is not null;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var prompt = InputText.Trim();
        var selected = SelectedModel!;
        var client = _router.For(selected.Provider);
        var model = selected.Id;
        InputText = "";

        // Explicit "remember …" prompts persist a fact before the turn runs, so the reply can acknowledge it.
        MaybeRememberFromPrompt(prompt);

        // Snapshot and clear the staged attachments.
        var attachments = Attachments.ToList();
        Attachments.Clear();
        HasAttachments = false;

        // Snapshot and clear any staged MCP resources (already-fetched text, folded into the prompt context).
        var mcpResources = McpResources.ToList();
        McpResources.Clear();
        HasMcpResources = false;

        // The per-prompt toggle upgrades a plain chat into a web-searched answer.
        var mode = SelectedMode.Mode;

        var user = new MessageViewModel(ChatRole.User, prompt);
        user.SetAttachments(attachments);
        Messages.Add(user);
        // Stamp the reply with the active agent's identity (glyph + name show in the header; the model id
        // moves to a tooltip). The agent's persona is layered into the system prompt for every mode below.
        var agent = SelectedAgent;
        var assistant = new MessageViewModel(ChatRole.Assistant)
        {
            IsStreaming = true,
            ModelName = model,
            AgentGlyph = agent?.Glyph,
            AgentName = agent?.Name
        };
        Messages.Add(assistant);
        RequestScroll();

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            if (attachments.Count > 0)
            {
                StatusText = "Reading attachments…";
                await ProcessAttachmentsAsync(attachments, user, ct);
            }

            // Fold any staged MCP resources into the user message's attached context (same channel as documents).
            if (mcpResources.Count > 0)
            {
                var block = string.Join("\n\n", mcpResources.Select(r => $"--- {r.Label} ---\n{r.Text}"));
                user.AttachedContext = string.IsNullOrEmpty(user.AttachedContext)
                    ? block
                    : user.AttachedContext + "\n\n" + block;
            }

            switch (mode)
            {
                case AppMode.Chat:
                    await RunChatAsync(client, model, assistant, ct);
                    break;
                case AppMode.WebSearch:
                    await RunWebSearchAsync(client, model, prompt, user, assistant, ct);
                    break;
                case AppMode.DeepResearch:
                    await RunDeepResearchAsync(client, model, prompt, user, assistant, ct);
                    break;
                case AppMode.Project:
                    await RunProjectAgentAsync(client, model, assistant, ct);
                    break;
            }

            if (assistant.Text.Length == 0 && assistant.Work.Length == 0)
                assistant.Text = "_(no response)_";

            // Count this reply's estimated cost against the provider's budget (no-op for Ollama / providers
            // the user hasn't added). Input is approximated by the prompt + any attached context.
            if (!ct.IsCancellationRequested && assistant.Text.Length > 0 && assistant.Text != "_(no response)_")
            {
                var inputText = string.IsNullOrEmpty(user.AttachedContext) ? prompt : prompt + "\n" + user.AttachedContext;
                _usage.RecordEstimatedUsage(selected.Provider, model, inputText, assistant.Text);
            }

            // Phase 5: a proactive agent ends its turn with suggested next steps (best-effort, gated).
            if (SelectedAgent?.Proactive == true && !ct.IsCancellationRequested
                && assistant.Text.Length > 0 && assistant.Text != "_(no response)_")
            {
                StatusText = "Thinking of next steps…";
                await GenerateSuggestionsAsync(client, model, prompt, assistant, ct);
            }

            // Auto-read the finished reply aloud when enabled and a voice is configured (fire-and-forget).
            if (AutoSpeakEnabled && _speech.IsConfigured && !ct.IsCancellationRequested
                && assistant.Text.Length > 0 && assistant.Text != "_(no response)_")
            {
                SpeakMessageCommand.Execute(assistant);
            }
        }
        catch (OperationCanceledException)
        {
            assistant.Append(assistant.Text.Length == 0 ? "_(stopped)_" : "\n\n_(stopped)_");
        }
        catch (Exception ex)
        {
            assistant.Text = $"⚠️ {ex.Message}";
            if (user.Images.Count > 0)
                assistant.Append(
                    "\n\nℹ️ Image input requires a vision-capable model (your current model is text-only). " +
                    "Install one — e.g. `ollama pull llava` or `ollama pull llama3.2-vision` — then select it in the sidebar.");
        }
        finally
        {
            assistant.IsStreaming = false;
            IsBusy = false;
            StatusText = "";
            _cts?.Dispose();
            _cts = null;
            RequestScroll();
            PersistCurrentSession(); // keep the chat log up to date after every turn
        }
    }

    /// <summary>Reads each attachment: images become base64 (vision), PDFs become extracted text context.</summary>
    private async Task ProcessAttachmentsAsync(
        List<Attachment> attachments, MessageViewModel user, CancellationToken ct)
    {
        var images = new List<string>();
        var docs = new StringBuilder();

        foreach (var a in attachments)
        {
            ct.ThrowIfCancellationRequested();
            if (a.Kind == AttachmentKind.Photo)
            {
                var b64 = await _attachments.ReadImageBase64Async(a.Path, ct);
                if (!string.IsNullOrEmpty(b64))
                    images.Add(b64);
            }
            else
            {
                var text = await _attachments.ExtractTextAsync(a.Path, _settings.Current.MaxCharsPerPage, ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    docs.AppendLine($"--- {a.FileName} ---");
                    docs.AppendLine(text);
                    docs.AppendLine();
                }
            }
        }

        user.Images = images;
        user.AttachedContext = docs.ToString().Trim();
    }

    private async Task RunChatAsync(IChatClient client, string model, MessageViewModel assistant, CancellationToken ct)
    {
        var history = BuildChatHistory(assistant);
        var raw = new StringBuilder();
        // No ConfigureAwait(false): stream deltas must apply on the UI thread.
        await foreach (var delta in client.ChatStreamAsync(model, history, ThinkingEnabled, ct))
        {
            ApplyStreamDelta(assistant, raw, delta);
            RequestScroll();
        }
    }

    /// <summary>
    /// Accumulates a streamed reply and re-splits it into the visible answer and the model's hidden
    /// reasoning (<c>&lt;think&gt;…&lt;/think&gt;</c>), so reasoning shows in the collapsible block, not the answer.
    /// </summary>
    private static void ApplyStreamDelta(MessageViewModel assistant, StringBuilder raw, string delta)
    {
        raw.Append(delta);
        var (reasoning, answer) = ReasoningSplit.Split(raw.ToString());
        assistant.SetWork(reasoning);
        assistant.Text = answer;
    }

    private async Task RunWebSearchAsync(
        IChatClient client, string model, string prompt, MessageViewModel user,
        MessageViewModel assistant, CancellationToken ct)
    {
        StatusText = "Searching the web…";
        var results = await _search.SearchAsync(prompt, _settings.Current.SearchResultsPerQuery, ct);

        if (results.Count == 0)
        {
            assistant.Text = "I couldn't find any web results. Check your connection or rephrase.";
            return;
        }

        StatusText = $"Found {results.Count} results — answering…";
        var messages = BuildWebSearchMessages(
            WithAttachedContext(prompt, user), results, user.Images, PersonaPrefix(), ThinkingDirective());
        var raw = new StringBuilder();
        await foreach (var delta in client.ChatStreamAsync(model, messages, ThinkingEnabled, ct))
        {
            ApplyStreamDelta(assistant, raw, delta);
            RequestScroll();
        }
        assistant.SetSources(results);
    }

    private async Task RunDeepResearchAsync(
        IChatClient client, string model, string prompt, MessageViewModel user,
        MessageViewModel assistant, CancellationToken ct)
    {
        // Progress is constructed on the UI thread, so its callbacks marshal back automatically.
        var progress = new Progress<string>(s => StatusText = s);
        var raw = new StringBuilder();

        // "Use Multiple LLMs": optionally route planning/synthesis to their own models. An override
        // resolves to null (and so falls back to the chat model) when the toggle is off or the saved
        // pick isn't in the live list — i.e. it isn't currently reachable.
        ModelEndpoint? planner = null, synthesizer = null;
        if (_settings.Current.DeepResearchUseMultipleModels)
        {
            var plan = ResolveModelOverride(_settings.Current.DeepResearchPlanningModel, Models);
            if (plan is not null)
                planner = new ModelEndpoint(_router.For(plan.Provider), plan.Id);

            var synth = ResolveModelOverride(_settings.Current.DeepResearchSynthesisModel, Models);
            if (synth is not null)
                synthesizer = new ModelEndpoint(_router.For(synth.Provider), synth.Id);
        }

        var sources = await _research.RunAsync(
            client,
            WithAttachedContext(prompt, user),
            model,
            PersonaPrefix(),
            progress,
            // The research service streams on a background thread; marshal each token to the UI.
            delta => Dispatcher.UIThread.Post(() =>
            {
                ApplyStreamDelta(assistant, raw, delta);
                RequestScroll();
            }),
            planner,
            synthesizer,
            ct);

        assistant.SetSources(sources);
    }

    private async Task RunProjectAgentAsync(
        IChatClient client, string model, MessageViewModel assistant, CancellationToken ct)
    {
        if (ActiveProject is null)
        {
            assistant.Text = "No active project. Click “📁 Project” in the sidebar to create one.";
            return;
        }

        // Progress is constructed on the UI thread, so its callbacks marshal back automatically.
        var progress = new Progress<string>(s => StatusText = s);
        var conversation = BuildAgentConversation(assistant);

        // The agent/lead runs on background threads; marshal its callbacks back to the UI.
        // OnActivity is the legacy monospace "work" block channel — used only by the single-agent path now
        // (and even there the block is suppressed once the structured feed has rows; see ShowWorkBlock).
        void OnActivity(string activity) => Dispatcher.UIThread.Post(() =>
        {
            assistant.AppendWork(activity);
            RequestScroll();
        });
        void OnAnswer(string answer) => Dispatcher.UIThread.Post(() =>
        {
            var (reasoning, text) = ReasoningSplit.Split(answer);
            if (!string.IsNullOrEmpty(reasoning))
                assistant.AppendWork(reasoning);
            assistant.Append(text);
            RequestScroll();
        });

        // Structured per-step updates → the message's structured activity feed (one row per tool call +
        // interim narration notes). The single-agent path passes this for the agent's own steps; the
        // orchestrator path passes it for the LEAD's own read/scan steps (each delegation's steps go to its
        // card instead, via OnDelegation below).
        void OnActivityStep(ActivityUpdate u) => Dispatcher.UIThread.Post(() =>
        {
            assistant.ApplyActivity(u);
            RequestScroll();
        });

        // The agent's plan/phases (update_plan tool) → the message's plan card. Used by the single agent
        // and the Lead (which owns its phase plan); delegated specialists are suppressed via onPlan: null.
        void OnPlan(PlanUpdate u) => Dispatcher.UIThread.Post(() =>
        {
            assistant.SetPlan(u);
            RequestScroll();
        });

        // Orchestrator-only: structured per-delegation updates → per-delegation cards in the transcript.
        // A delegation's Activity carries the specialist's structured step (u.Step), routed into the card's
        // own feed so it renders identically to a single-agent run.
        void OnDelegation(DelegationUpdate u) => Dispatcher.UIThread.Post(() =>
        {
            switch (u.Phase)
            {
                case DelegationPhase.Started:
                    assistant.StartDelegation(u.Index, u.AgentName, u.Glyph, u.Task);
                    break;
                case DelegationPhase.Activity:
                    if (u.Step is not null)
                        assistant.ApplyDelegationActivity(u.Index, u.Step);
                    break;
                case DelegationPhase.Finished:
                    assistant.FinishDelegation(u.Index, u.Text);
                    break;
            }
            RequestScroll();
        });

        if (SelectedAgent?.IsOrchestrator == true)
        {
            // Lead/orchestrator: it reads the roster and delegates subtasks to specialist agents, each run
            // via the existing project-agent loop with its own persona/tools/model. The single global approval
            // setting governs the whole coordination loop and every delegated run (its read tools are gated by
            // Tools). The lead's own steps (narration + read/scan/update_docs) go to the structured activity
            // feed (OnActivityStep); each delegation renders as its own card (OnDelegation).
            // ProjectContext() = the AI_DOCS.md handbook + project skills, injected only in Project mode
            // (the lead passes it on to every delegated specialist run).
            await _orchestrator.RunAsync(
                SelectedAgent, client, model, ActiveProject, conversation,
                MemoryBlock(), MemoryActive(), ProjectContext(), ThinkingDirective(),
                _settings.Current.SoftwareInstall, _settings.Current.AgentApproval,
                progress, OnActivityStep, OnAnswer, OnDelegation, OnPlan,
                RequestToolApprovalAsync, _settings.Current.AutoFlowPhases, RequestPhaseContinueAsync,
                RequestClarificationAsync, ct);
        }
        else
        {
            // Single-agent path: the global approval setting is authoritative for the run — it sets the
            // approval mode and step budget (ConfirmEverything → confirm-everything/8, ConfirmDestructive →
            // confirm-destructive/24, AutoRun → auto-run/40). SoftwareInstall stays independent.
            var approvalMode = _settings.Current.AgentApproval;
            var (approval, maxSteps) = AutonomyMap.ForApprovalMode(approvalMode);
            // Under AutoRun the agent gets a plan-then-execute directive appended to the system prompt; it
            // slots in alongside the Thinking directive (both lead with their own blank line, both may be empty).
            // ClarifyDirective (ask when vague) + PhasesDirective (structure complex work into named phases)
            // are approval-independent.
            var directives = ThinkingDirective() + AgentPromptBuilder.PlanningDirective(approvalMode)
                + AgentPromptBuilder.ClarifyDirective() + AgentPromptBuilder.PhasesDirective();

            // ProjectContext() = the AI_DOCS.md handbook + project skills, injected only in Project mode.
            // allowDocsUpdate: true — the active top-level agent is the main agent and may maintain the
            // handbook via update_docs (delegated specialists, run from the orchestrator, get false).
            await _agent.RunAsync(
                client, ActiveProject, model, conversation, approval, maxSteps,
                SelectedAgent?.Tools ?? new AgentTools(),
                PersonaPrefix(), directives, ProjectContext(), _settings.Current.SoftwareInstall, MemoryActive(),
                allowDocsUpdate: true, progress,
                OnActivity, OnActivityStep, OnPlan, OnAnswer, RequestToolApprovalAsync,
                _settings.Current.AutoFlowPhases, RequestPhaseContinueAsync, RequestClarificationAsync, ct);
        }

        // The turn may have created/edited project skills (create_skill, or write_file under .AI/skills) —
        // re-scan so they load on the next turn and the sidebar's "N skills" count stays current.
        if (ActiveProject is not null)
        {
            await LoadProjectSkillsAsync(ActiveProject);
            // The turn may also have authored a project agent (create_agent) — refresh the picker so it
            // appears now (LoadAgents preserves the current selection). The Lead reads the roster from disk.
            LoadAgents();
        }
    }

    /// <summary>Asks the view to confirm a tool call (raised on a background thread, marshalled to the UI).</summary>
    private Task<bool> RequestToolApprovalAsync(ToolApprovalRequest request)
    {
        if (ToolApprovalRequested is null)
            return Task.FromResult(true); // no UI wired (e.g. design time) → allow

        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.UIThread.Post(() =>
            ToolApprovalRequested?.Invoke(this, new ToolApprovalEventArgs(request, tcs)));
        return tcs.Task;
    }

    /// <summary>
    /// Asks the view to confirm advancing to the next phase (when AutoFlowPhases is off). Raised from the
    /// agent's background loop and marshalled to the UI; returns true to continue, false to stop the run.
    /// </summary>
    private Task<bool> RequestPhaseContinueAsync(PhaseGate gate)
    {
        if (PhaseGateRequested is null)
            return Task.FromResult(true); // no UI wired (e.g. design time) → continue

        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.UIThread.Post(() =>
            PhaseGateRequested?.Invoke(this, new PhaseGateEventArgs(gate, tcs)));
        return tcs.Task;
    }

    /// <summary>
    /// Asks the view to pop a clarifying question (the agent's <c>ask_user</c> tool). Raised from the agent's
    /// background loop and marshalled to the UI; returns the user's answer string, or null if dismissed / no UI.
    /// </summary>
    private Task<string?> RequestClarificationAsync(UserClarificationRequest request)
    {
        if (ClarificationRequested is null)
            return Task.FromResult<string?>(null); // no UI wired (e.g. design time)

        var tcs = new TaskCompletionSource<string?>();
        Dispatcher.UIThread.Post(() =>
            ClarificationRequested?.Invoke(this, new ClarifyEventArgs(request, tcs)));
        return tcs.Task;
    }

    /// <summary>Builds the user/assistant turns for the agent; it prepends its own system prompt.</summary>
    private List<ChatMessage> BuildAgentConversation(MessageViewModel pendingAssistant)
    {
        var conversation = new List<ChatMessage>();
        foreach (var m in Messages)
        {
            if (ReferenceEquals(m, pendingAssistant))
                continue;
            if (m.Role != ChatRole.User && m.Role != ChatRole.Assistant)
                continue;
            if (string.IsNullOrEmpty(m.Text) && string.IsNullOrEmpty(m.AttachedContext))
                continue;

            var content = m.Text;
            if (!string.IsNullOrEmpty(m.AttachedContext))
                content += $"\n\n[Attached documents]\n{m.AttachedContext}";

            conversation.Add(new ChatMessage(m.Role, content));
        }
        return conversation;
    }

    /// <summary>
    /// Planning instruction appended to a system prompt when Thinking is on, scaled by the Effort
    /// setting. Empty when the toggle is off so behaviour is unchanged.
    /// </summary>
    private string ThinkingDirective()
    {
        if (!ThinkingEnabled)
            return "";

        var depth = _settings.Current.ThinkingEffort switch
        {
            <= 33 => "Before answering, briefly think about what the user needs and sketch a quick plan " +
                     "(a sentence or two), then respond.",
            <= 66 => "Before answering, think step by step about what the task requires and outline your " +
                     "approach, then carry it out.",
            _ => "Before answering, think carefully and thoroughly: break the task into steps, consider " +
                 "edge cases and alternatives, lay out a clear plan, and only then execute it. " +
                 "Prioritise correctness over speed."
        };

        return "\n\n" + depth;
    }

    private List<ChatMessage> BuildChatHistory(MessageViewModel pendingAssistant)
    {
        // System prompt = active agent's persona → base chat instructions → skills → memory → Thinking directive.
        var systemPrompt = AgentPromptBuilder.Compose(SelectedAgent, ChatBaseInstructions, ThinkingDirective(), MemoryBlock());
        var history = new List<ChatMessage> { ChatMessage.System(systemPrompt) };
        foreach (var m in Messages)
        {
            if (ReferenceEquals(m, pendingAssistant))
                continue; // skip the empty placeholder we're about to fill
            if (string.IsNullOrEmpty(m.Text) && m.Images.Count == 0 && string.IsNullOrEmpty(m.AttachedContext))
                continue;

            var content = m.Text;
            if (!string.IsNullOrEmpty(m.AttachedContext))
                content += $"\n\n[Attached documents]\n{m.AttachedContext}";

            history.Add(new ChatMessage(m.Role, content, m.Images.Count > 0 ? m.Images : null));
        }
        return history;
    }

    private static string WithAttachedContext(string prompt, MessageViewModel user) =>
        string.IsNullOrEmpty(user.AttachedContext)
            ? prompt
            : $"{prompt}\n\n[Attached documents]\n{user.AttachedContext}";

    private static List<ChatMessage> BuildWebSearchMessages(
        string prompt, IReadOnlyList<SearchResult> results, IReadOnlyList<string> images,
        string personaPrefix, string thinkingDirective)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Web search results:");
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] {r.Title}");
            sb.AppendLine($"URL: {r.Url}");
            if (!string.IsNullOrWhiteSpace(r.Snippet))
                sb.AppendLine(r.Snippet);
            sb.AppendLine();
        }
        sb.AppendLine($"Question: {prompt}");

        return new List<ChatMessage>
        {
            ChatMessage.System(
                personaPrefix +
                "Answer the question using the web search results below. Cite sources inline with " +
                "bracketed numbers like [1]. If the results are insufficient, say so." + thinkingDirective),
            new ChatMessage(ChatRole.User, sb.ToString(), images.Count > 0 ? images : null)
        };
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _cts?.Cancel();

    /// <summary>Sidebar "New Chat": save the current conversation, then start a fresh one (staying in
    /// the active project, if any, so the new chat is still saved under it).</summary>
    [RelayCommand]
    private void NewChat()
    {
        PersistCurrentSession();
        _currentSession = new ChatSession();
        Messages.Clear();
        StatusText = "";
        SetMode(ActiveProject is not null ? AppMode.Project : AppMode.Chat);
    }

    /// <summary>
    /// "Compact": summarise the current conversation into a short briefing and replace the transcript with
    /// it, freeing context tokens (like Claude Code's /compact). Best-effort; uses the selected chat model
    /// and shares the Send cancellation (the Stop button cancels it).
    /// </summary>
    [RelayCommand]
    private async Task CompactAsync()
    {
        if (IsBusy || SelectedModel is null)
            return;
        if (Messages.Count(m => !string.IsNullOrEmpty(m.Text)) < 2)
        {
            StatusText = "Nothing to compact yet.";
            return;
        }

        var client = _router.For(SelectedModel.Provider);
        var model = SelectedModel.Id;
        var prompt = BuildCompactMessages();

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "Compacting conversation…";
        try
        {
            var summary = (await client.CompleteAsync(model, prompt, _cts.Token)).Trim();
            if (string.IsNullOrEmpty(summary))
            {
                StatusText = "Compact produced no summary.";
                return;
            }

            // Replace the transcript with the summary so the next turn continues from it. Update the SAME
            // session in place (not a fork) and preserve its title — after compaction there's no user turn
            // for PersistCurrentSession's title-builder to derive one from, so it'd otherwise become "New chat".
            var priorTitle = _currentSession.Title;
            Messages.Clear();
            Messages.Add(new MessageViewModel(ChatRole.Assistant,
                "**Summary of the earlier conversation (compacted to save context):**\n\n" + summary));
            PersistCurrentSession();
            if (!string.IsNullOrWhiteSpace(priorTitle) && priorTitle != "New chat")
            {
                _currentSession.Title = priorTitle;
                SaveLog();
            }
            StatusText = "";
            RequestScroll();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Compact cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Compact failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Builds the summarisation request from the current transcript (one labelled line per message).</summary>
    private List<ChatMessage> BuildCompactMessages()
    {
        var sb = new StringBuilder();
        foreach (var m in Messages)
        {
            if (string.IsNullOrEmpty(m.Text))
                continue;
            var who = m.Role == ChatRole.User ? "User" : m.Role == ChatRole.Assistant ? "Assistant" : "System";
            sb.Append(who).Append(": ").AppendLine(m.Text.Trim()).AppendLine();
        }
        return new List<ChatMessage>
        {
            ChatMessage.System(
                "You compress a conversation to save context. Summarise the conversation below into a compact " +
                "briefing a new session can continue from: the user's goal, key facts and decisions, the current " +
                "state, and any open threads or next steps. Be concise but keep specifics (names, files, numbers, " +
                "code identifiers). Output only the summary."),
            ChatMessage.User("Conversation to summarise:\n\n" + sb.ToString().Trim())
        };
    }

    /// <summary>"Clear": discard the current conversation (removing it from the log if it was saved).</summary>
    [RelayCommand]
    private void ClearCurrentChat()
    {
        if (IsBusy)
            return;
        // Passing the current session makes DeleteSession remove it from the log (if present) AND reset the
        // transcript to a fresh session — i.e. discard the current chat without saving it.
        DeleteSession(_currentSession);
    }

    // ---- slash (/) command palette --------------------------------------------------------

    /// <summary>The slash palette's command set. Actions reference existing VM commands; built once.</summary>
    private IReadOnlyList<SlashCommand> AllSlashCommands =>
        (_allSlashCommands ??= BuildSlashCommands()).Concat(_mcpPromptCommands).ToList();

    private IReadOnlyList<SlashCommand> BuildSlashCommands() => new List<SlashCommand>
    {
        // The conversation actions self-guard on IsBusy; hide them while a turn is streaming so the palette
        // doesn't offer a silent no-op.
        new() { Name = "new",       Description = "Start a new chat",                      Run = () => NewChatCommand.Execute(null),          IsAvailable = () => !IsBusy },
        new() { Name = "compact",   Description = "Summarise & compact this conversation", Run = () => CompactCommand.Execute(null),          IsAvailable = () => !IsBusy },
        new() { Name = "clear",     Description = "Discard the current conversation",      Run = () => ClearCurrentChatCommand.Execute(null), IsAvailable = () => !IsBusy },
        // Project-mode agent kick-offs: send a preset prompt; the agent does the work with its tools.
        new() { Name = "init",        Description = "Set up AI_DOCS.md + memory for this project", Run = () => SendAgentPreset(InitPrompt),      IsAvailable = () => ActiveProject is not null && !IsBusy },
        new() { Name = "update-docs", Description = "Update the project handbook (AI_DOCS.md)",     Run = () => SendAgentPreset(UpdateDocsPrompt), IsAvailable = () => ActiveProject is not null && !IsBusy },
        new() { Name = "run-app",     Description = "Build and run the app",                        Run = () => SendAgentPreset(RunAppPrompt),     IsAvailable = () => ActiveProject is not null && !IsBusy },
        new() { Name = "project",   Description = "Create or open a project",              Run = () => OpenProjectCommand.Execute(null) },
        new() { Name = "settings",  Description = "Open settings",                         Run = () => OpenSettingsCommand.Execute(null) },
        // Mode switches don't apply inside a project (the agent runs there).
        new() { Name = "chat",      Description = "Switch to plain chat",     Run = () => SetMode(AppMode.Chat),         IsAvailable = () => ActiveProject is null },
        new() { Name = "web",       Description = "Switch to web search",     Run = () => SetMode(AppMode.WebSearch),    IsAvailable = () => ActiveProject is null },
        new() { Name = "research",  Description = "Switch to deep research",  Run = () => SetMode(AppMode.DeepResearch), IsAvailable = () => ActiveProject is null },
        new() { Name = "thinking",  Description = "Toggle Thinking (plan before answering)", Run = () => ThinkingEnabled = !ThinkingEnabled },
        new() { Name = "auto-read", Description = "Toggle reading replies aloud",          Run = () => AutoSpeakEnabled = !AutoSpeakEnabled, IsAvailable = () => IsVoiceConfigured },
    };

    // Preset prompts for the project-mode slash actions — sent to the agent, which carries them out with its
    // own tools (update_docs / remember / run_command), gated by the active agent's permissions and approval.
    private const string InitPrompt =
        "Initialise this project's knowledge base:\n" +
        "1. Inspect the project — list its files, read the key ones, and search where useful.\n" +
        "2. Create or update the project handbook with the update_docs tool (.AI/AI_DOCS.md): a concise, " +
        "durable guide to how this project works — its purpose, structure and architecture, the most " +
        "important files/folders, how to build/run/test it, and its conventions. Write rules and " +
        "orientation, not a log.\n" +
        "3. Record the most important durable facts about the project (and any clear preferences) with the " +
        "remember tool.\n" +
        "Then give me a short summary of what you set up.";

    private const string UpdateDocsPrompt =
        "Review the current state of this project and update its handbook (.AI/AI_DOCS.md) with the " +
        "update_docs tool so it accurately reflects the project as it is now — its purpose, structure, key " +
        "build/run/test commands, and conventions. Inspect the project first and revise surgically, " +
        "preserving useful existing content. Then summarise what you changed.";

    private const string RunAppPrompt =
        "Build and run this application. Work out the right commands for this project from its " +
        "manifest/config (e.g. package.json, a .csproj/.sln, pyproject.toml, Makefile, …), run them with " +
        "run_command (build first if needed), and report what happened. If it fails, show the error and " +
        "suggest a fix.";

    /// <summary>Fills the composer with a preset prompt and sends it to the agent (used by project slash actions).</summary>
    private void SendAgentPreset(string prompt)
    {
        if (IsBusy || SelectedModel is null)
            return;
        InputText = prompt;
        if (SendCommand.CanExecute(null))
            SendCommand.Execute(null);
    }

    /// <summary>
    /// Discovers the connected MCP servers' prompts and surfaces them as slash-commands (e.g. <c>/code_review</c>)
    /// for the active project. Best-effort and project-scoped: cleared outside a project. Fire-and-forget from
    /// project activation and after Settings closes (the servers may have changed).
    /// </summary>
    public async Task RefreshMcpPromptCommandsAsync()
    {
        if (ActiveProject is null)
        {
            _mcpPromptCommands = Array.Empty<SlashCommand>();
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var prompts = await _mcp.ListPromptsAsync(ActiveProject.Directory, cts.Token);
            _mcpPromptCommands = prompts.Select(p => new SlashCommand
            {
                Name = p.Name,
                Description = $"{p.ServerName}: {(string.IsNullOrWhiteSpace(p.Description) ? "MCP prompt" : p.Description)}",
                Run = () => ApplyMcpPrompt(p.ServerId, p.Name),
                IsAvailable = () => ActiveProject is not null && !IsBusy
            }).ToList();
        }
        catch
        {
            _mcpPromptCommands = Array.Empty<SlashCommand>(); // best-effort: no prompts on failure
        }
    }

    /// <summary>Expands an MCP prompt and drops its text into the composer to edit/send (fire-and-forget).</summary>
    private void ApplyMcpPrompt(string serverId, string promptName) => _ = ApplyMcpPromptAsync(serverId, promptName);

    private async Task ApplyMcpPromptAsync(string serverId, string promptName)
    {
        try
        {
            StatusText = $"Fetching prompt “{promptName}”…";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var text = await _mcp.GetPromptTextAsync(serverId, promptName, cts.Token);
            StatusText = "";
            if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("Error:", StringComparison.Ordinal))
                InputText = text; // drop into the composer for the user to edit/send
            else
                StatusText = string.IsNullOrWhiteSpace(text) ? "" : text;
        }
        catch
        {
            StatusText = "";
        }
    }

    /// <summary>Opens/filters/closes the palette for the current composer text. Call on the UI thread.</summary>
    private void UpdateSlashMenu(string? input)
    {
        if (!SlashMenu.ShouldOpen(input))
        {
            CloseSlashMenu();
            return;
        }

        var available = AllSlashCommands.Where(c => c.IsAvailable()).ToList();
        var matches = SlashMenu.Filter(available, SlashMenu.ExtractQuery(input));

        SlashCommands.Clear();
        foreach (var c in matches)
            SlashCommands.Add(c);

        if (SlashCommands.Count == 0)
        {
            IsSlashMenuOpen = false;
            return;
        }
        SelectedSlashIndex = 0;
        IsSlashMenuOpen = true;
    }

    /// <summary>Moves the palette highlight (↑/↓), wrapping around. Called from the composer key handler.</summary>
    public void MoveSlashSelection(int delta)
    {
        if (!IsSlashMenuOpen || SlashCommands.Count == 0)
            return;
        var n = SlashCommands.Count;
        SelectedSlashIndex = ((SelectedSlashIndex + delta) % n + n) % n;
    }

    /// <summary>Runs the highlighted command, clears the composer, and closes the palette.</summary>
    public void AcceptSlashCommand()
    {
        if (!IsSlashMenuOpen)
            return;
        var idx = SelectedSlashIndex;
        if (idx < 0 || idx >= SlashCommands.Count)
        {
            CloseSlashMenu();
            return;
        }

        var command = SlashCommands[idx];
        CloseSlashMenu();
        InputText = "";   // also re-runs UpdateSlashMenu("") → stays closed
        command.Run();
    }

    /// <summary>Closes the palette (no-op if already closed). Call on the UI thread.</summary>
    public void CloseSlashMenu() => IsSlashMenuOpen = false;

    /// <summary>Sidebar "Project" button: ask the view to open the New Project window.</summary>
    [RelayCommand]
    private void OpenProject() => ProjectRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Sidebar tab: show the chat log.</summary>
    [RelayCommand]
    private void ShowChatLog() => ShowProjectFiles = false;

    /// <summary>Sidebar tab: show the project file tree (and refresh it to reflect any agent changes).</summary>
    [RelayCommand]
    private void ShowFiles()
    {
        ShowProjectFiles = true;
        LoadFileTree();
    }

    /// <summary>Re-read the project file tree from disk.</summary>
    [RelayCommand]
    private void RefreshFiles() => LoadFileTree();

    /// <summary>Rebuilds the file tree from the active project's directory (root node, expanded).</summary>
    private void LoadFileTree()
    {
        FileTree.Clear();
        if (ActiveProject is null)
            return;

        var root = new FileNode(ActiveProject.Directory, isDirectory: true) { IsExpanded = true };
        FileTree.Add(root);
    }

    /// <summary>Records a just-opened project at the front of the recent list (deduped, capped) and persists it.</summary>
    private void AddRecentProject(Project project)
    {
        _settings.Current.RecentProjects =
            WithRecentProjectAtFront(_settings.Current.RecentProjects, project, MaxRecentProjects, PathCmp);
        _settings.Save();
    }

    /// <summary>How many recent projects are kept on disk (the launcher shows up to <c>StartupViewModel.MaxShown</c>).</summary>
    private const int MaxRecentProjects = 12;

    /// <summary>
    /// Returns a new recent-projects list with <paramref name="project"/> moved to the front (deduped by
    /// directory using <paramref name="cmp"/>, blank entries dropped, capped at <paramref name="cap"/>). Pure —
    /// unit-tested.
    /// </summary>
    internal static List<RecentProject> WithRecentProjectAtFront(
        IReadOnlyList<RecentProject> existing, Project project, int cap, StringComparison cmp)
    {
        var list = new List<RecentProject> { new() { Name = project.Name, Directory = project.Directory } };
        if (existing is not null)
            foreach (var r in existing)
            {
                if (list.Count >= cap)
                    break; // checked before adding so the result never exceeds the cap
                if (r is null || string.IsNullOrWhiteSpace(r.Directory))
                    continue;
                if (string.Equals(r.Directory, project.Directory, cmp))
                    continue; // the moved-to-front entry already represents this directory
                list.Add(r);
            }
        return list;
    }

    /// <summary>
    /// Keeps the recents whose directory still exists, in order, up to <paramref name="max"/> — so a deleted
    /// project folder silently drops off the launcher. Blank/missing entries are skipped. <c>internal</c> for tests.
    /// </summary>
    internal static IReadOnlyList<RecentProject> PrunedRecent(IReadOnlyList<RecentProject>? recents, int max)
    {
        var result = new List<RecentProject>();
        if (recents is null)
            return result;
        foreach (var r in recents)
        {
            if (result.Count >= max)
                break;
            if (r is null || string.IsNullOrWhiteSpace(r.Directory) || !Directory.Exists(r.Directory))
                continue;
            result.Add(r);
        }
        return result;
    }

    /// <summary>Start watching when the Files tab opens; stop when it closes (or the project exits).</summary>
    partial void OnShowProjectFilesChanged(bool value)
    {
        if (value)
            StartFileWatcher();
        else
            StopFileWatcher();
    }

    /// <summary>Begins watching the active project directory for add/remove/rename so the tree stays live.</summary>
    private void StartFileWatcher()
    {
        StopFileWatcher();
        if (ActiveProject is null)
            return;

        try
        {
            var watcher = new FileSystemWatcher(ActiveProject.Directory)
            {
                IncludeSubdirectories = true,
                // Only structural changes affect the tree (create/delete/rename of files & folders).
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024, // headroom against overflow during bursts
            };
            watcher.Created += OnFileSystemChanged;
            watcher.Deleted += OnFileSystemChanged;
            watcher.Renamed += OnFileSystemRenamed;
            watcher.Error += OnFileSystemError;

            // Threadpool timer (re-armed per event) debounces bursts; Change() is thread-safe.
            _fileDebounce = new System.Threading.Timer(_ => FlushFileRefresh(), null, Timeout.Infinite, Timeout.Infinite);
            watcher.EnableRaisingEvents = true;
            _fileWatcher = watcher;
        }
        catch
        {
            // Some environments forbid file watchers; fall back silently to the ⟳ button.
            StopFileWatcher();
        }
    }

    /// <summary>Stops and disposes the watcher + debounce timer and clears any pending refreshes.</summary>
    private void StopFileWatcher()
    {
        if (_fileWatcher is not null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Created -= OnFileSystemChanged;
            _fileWatcher.Deleted -= OnFileSystemChanged;
            _fileWatcher.Renamed -= OnFileSystemRenamed;
            _fileWatcher.Error -= OnFileSystemError;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
        _fileDebounce?.Dispose();
        _fileDebounce = null;
        lock (_pendingLock) _pendingDirRefresh.Clear();
    }

    /// <summary>Releases the file watcher (the window disposes the VM on close — see MainWindow.OnClosed).</summary>
    public void Dispose() => StopFileWatcher();

    // FS events arrive on a threadpool thread: record the affected parent directory and (re)arm the debounce.
    private void OnFileSystemChanged(object? sender, FileSystemEventArgs e) =>
        QueueDirRefresh(Path.GetDirectoryName(e.FullPath));

    private void OnFileSystemRenamed(object? sender, RenamedEventArgs e)
    {
        QueueDirRefresh(Path.GetDirectoryName(e.FullPath));
        QueueDirRefresh(Path.GetDirectoryName(e.OldFullPath));
    }

    // Buffer overflow / lost events: fall back to a full rebuild of the tree (unless we've since stopped).
    private void OnFileSystemError(object? sender, ErrorEventArgs e) =>
        Dispatcher.UIThread.Post(() => { if (_fileWatcher is not null) LoadFileTree(); });

    private void QueueDirRefresh(string? dir)
    {
        if (string.IsNullOrEmpty(dir))
            return;
        lock (_pendingLock) _pendingDirRefresh.Add(dir);
        _fileDebounce?.Change(300, Timeout.Infinite);
    }

    /// <summary>Debounce fired (threadpool): reconcile every touched directory node on the UI thread.</summary>
    private void FlushFileRefresh()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_fileWatcher is null) // torn down between the timer firing and this post running
                return;
            string[] dirs;
            lock (_pendingLock)
            {
                dirs = _pendingDirRefresh.ToArray();
                _pendingDirRefresh.Clear();
            }
            foreach (var dir in dirs)
                FindLoadedDirNode(dir)?.Reconcile();
        });
    }

    /// <summary>Finds the (already-loaded) tree node for a directory path, or null if it isn't shown yet.</summary>
    private FileNode? FindLoadedDirNode(string path)
    {
        foreach (var root in FileTree)
            if (FindDirNode(root, path) is { } hit)
                return hit;
        return null;
    }

    private static FileNode? FindDirNode(FileNode node, string path)
    {
        if (!node.IsDirectory)
            return null;
        if (string.Equals(
                node.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                PathCmp))
            return node;

        foreach (var child in node.Children)
            if (child.IsDirectory && FindDirNode(child, path) is { } hit)
                return hit;
        return null;
    }

    /// <summary>
    /// Called by the view once a project is created/opened: enter project (agent) mode, load that
    /// project's saved chats (from <c>.AI/chats</c>) into the log, and scan it for skill files.
    /// </summary>
    public async Task ActivateProjectAsync(Project project)
    {
        PersistCurrentSession();          // save whatever conversation we're leaving (to the old store)
        _currentSession = new ChatSession();
        Messages.Clear();
        StatusText = "";
        ShowStartupLauncher = false;      // any project activation leaves the launcher
        ActiveProject = project;          // project store is now active
        AddRecentProject(project);        // remember it for the startup launcher
        SetMode(AppMode.Project);
        ShowProjectFiles = false;         // start on the Chat Log tab
        LoadLog();                        // load this project's chats into the sidebar log
        LoadFileTree();                   // build the Files tab tree
        LoadAgents();                     // surface this project's custom agents in the picker

        // Project mode leads with the "team" experience: if the current pick isn't an orchestrator, switch
        // to the Lead and remember the previous pick to restore on exit (so we don't hijack the global one).
        if (SelectedAgent?.IsOrchestrator != true)
        {
            var lead = ProjectAgentPicker.PreferredOrchestrator(Agents);
            if (lead is not null)
            {
                _preProjectAgentId = SelectedAgent?.Id;
                SelectedAgent = lead;
            }
        }

        await LoadProjectSkillsAsync(project);

        // Discover this project's MCP prompts in the background → they appear as composer slash-commands.
        _ = RefreshMcpPromptCommandsAsync();
    }

    /// <summary>Leave project mode, restore the global chat log, and drop the project's skills.</summary>
    [RelayCommand]
    private void ExitProject()
    {
        PersistCurrentSession();          // save to the project store while it's still active
        _currentSession = new ChatSession();
        Messages.Clear();
        StatusText = "";
        ActiveProject = null;             // global store is now active
        _projectSkills = Array.Empty<ProjectSkill>();
        _mcpPromptCommands = Array.Empty<SlashCommand>(); // drop the project's MCP prompt slash-commands
        ProjectSkillCount = 0;
        _projectDocsText = "";
        HasProjectDocs = false;
        ShowProjectFiles = false;
        FileTree.Clear();
        LoadLog();                        // reload the global chat log
        LoadAgents();                     // drop the project's custom agents from the picker

        // Restore the agent we auto-switched away from on project entry (see ActivateProjectAsync), so the
        // global agent preference survives a project session unchanged.
        if (_preProjectAgentId is not null)
        {
            var prior = Agents.FirstOrDefault(a => string.Equals(a.Id, _preProjectAgentId, StringComparison.OrdinalIgnoreCase));
            if (prior is not null)
                SelectedAgent = prior;
            _preProjectAgentId = null;
        }

        SetMode(AppMode.Chat);
    }

    /// <summary>
    /// Loads the active project's skill files and AI_DOCS.md handbook off the UI thread, updating the
    /// sidebar count + flag. Both are project-mode context only (see <see cref="ProjectContext"/>).
    /// </summary>
    private async Task LoadProjectSkillsAsync(Project project)
    {
        var skills = await Task.Run(() => _skills.Load(project.Directory)).ConfigureAwait(true);
        _projectSkills = skills;
        ProjectSkillCount = skills.Count;

        _projectDocsText = await Task.Run(() => _projectDocs.Load(project.Directory)).ConfigureAwait(true);
        HasProjectDocs = _projectDocsText.Length > 0;
    }

    /// <summary>Replaces the sidebar chat log with the currently active store's sessions.</summary>
    private void LoadLog()
    {
        ChatLog.Clear();
        var sessions = ActiveProject is not null
            ? _history.LoadFrom(ActiveProject.Directory)
            : _history.Load();
        foreach (var session in sessions)
            ChatLog.Add(session);
    }

    /// <summary>Persists the chat log to whichever store is active: the project's, or the global one.</summary>
    private void SaveLog()
    {
        if (ActiveProject is not null)
            _history.SaveTo(ActiveProject.Directory, ChatLog.ToList());
        else
            _history.Save(ChatLog.ToList());
    }

    /// <summary>
    /// Combined project SKILL.md text appended to the agent's system prompt (empty when none). If the
    /// active agent selected specific project skills (by name, in its <see cref="Agent.Skills"/> list),
    /// only those are included; if it selected none, all discovered project skills are included for
    /// back-compat (built-in skill packs are handled independently by <see cref="AgentPromptBuilder"/>).
    /// </summary>
    private string ProjectSkillsContext()
    {
        if (_projectSkills.Count == 0)
            return "";

        // The agent's project-skill selection = the entries that aren't built-in pack ids.
        var selectedNames = (SelectedAgent?.Skills ?? new List<string>())
            .Where(id => !SkillCatalog.IsBuiltInId(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skills = selectedNames.Count == 0
            ? _projectSkills
            : _projectSkills.Where(s => selectedNames.Contains(s.Name)).ToList();

        if (skills.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("The user has added project skills. Treat them as authoritative guidance for this project:");
        foreach (var skill in skills)
        {
            sb.AppendLine();
            sb.AppendLine($"--- skill: {skill.Name} ---");
            sb.AppendLine(skill.Content);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The project handbook (.AI/AI_DOCS.md) text appended to the agent's system prompt (empty when none) —
    /// the app's equivalent of how Claude Code reads CLAUDE.md. Treated as authoritative project context.
    /// </summary>
    private string ProjectDocsContext()
    {
        if (_projectDocsText.Length == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("The user has provided a project handbook (.AI/AI_DOCS.md). Treat it as authoritative instructions and");
        sb.AppendLine("context for this project and follow it:");
        sb.AppendLine();
        sb.AppendLine("--- AI_DOCS.md ---");
        sb.AppendLine(_projectDocsText);
        return sb.ToString();
    }

    /// <summary>
    /// The full Project-mode context appended to the agent's system prompt: the AI_DOCS.md handbook first
    /// (more prominent/authoritative), then any project SKILL.md guidance. Injected only in Project mode —
    /// the single agent, the Lead orchestrator, and its delegated specialists all receive it via this one
    /// string. It is never threaded into the Chat / Web Search / Deep Research prompts.
    /// </summary>
    private string ProjectContext() => ProjectDocsContext() + ProjectSkillsContext();

    /// <summary>Open a saved conversation from the chat log.</summary>
    [RelayCommand]
    private void OpenSession(ChatSession? session)
    {
        if (session is null || ReferenceEquals(session, _currentSession))
            return;

        PersistCurrentSession(); // save the conversation we're leaving
        Messages.Clear();
        foreach (var turn in session.Messages)
        {
            var vm = new MessageViewModel(turn.Role, turn.Text) { ModelName = turn.ModelName };
            if (turn.Sources is { Count: > 0 })
                vm.SetSources(turn.Sources);          // restore the clickable "Sources" list
            if (!string.IsNullOrEmpty(turn.Work))
                vm.SetWork(turn.Work);                // restore the agent activity / reasoning log
            Messages.Add(vm);
        }

        _currentSession = session;
        // Project sessions need a live active project (not persisted); fall back to chat without one.
        SetMode(session.Mode == AppMode.Project && ActiveProject is null ? AppMode.Chat : session.Mode);
        StatusText = "";
        RequestScroll();
    }

    /// <summary>Delete a saved conversation from the chat log.</summary>
    [RelayCommand]
    private void DeleteSession(ChatSession? session)
    {
        if (session is null)
            return;

        ChatLog.Remove(session);
        SaveLog();

        if (ReferenceEquals(session, _currentSession))
        {
            _currentSession = new ChatSession();
            Messages.Clear();
            StatusText = "";
            SetMode(ActiveProject is not null ? AppMode.Project : AppMode.Chat);
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Captures the transcript into the current session and persists the whole log.</summary>
    private void PersistCurrentSession()
    {
        var turns = new List<ChatTurn>();
        foreach (var m in Messages)
        {
            if (string.IsNullOrEmpty(m.Text))
                continue;
            turns.Add(new ChatTurn
            {
                Role = m.Role,
                Text = m.Text,
                ModelName = m.ModelName,
                // Save web/deep-search sources (without the bulky fetched page Content) so the clickable
                // Sources list survives a reopen.
                Sources = m.Sources.Count == 0
                    ? null
                    : m.Sources.Select(s => new SearchResult { Title = s.Title, Url = s.Url, Snippet = s.Snippet }).ToList(),
                // Save the agent's activity log (Project mode) / reasoning (Thinking) as text.
                Work = NullIfEmpty(m.BuildActivityLog())
            });
        }

        if (turns.Count == 0)
            return; // nothing worth logging

        _currentSession.Messages = turns;
        _currentSession.Mode = SelectedMode.Mode;
        _currentSession.Title = BuildSessionTitle(turns);

        if (!ChatLog.Contains(_currentSession))
            ChatLog.Insert(0, _currentSession);

        SaveLog();
    }

    private static string BuildSessionTitle(List<ChatTurn> turns)
    {
        var firstUser = turns.FirstOrDefault(t => t.Role == ChatRole.User);
        var text = firstUser?.Text.Replace('\n', ' ').Trim();
        if (string.IsNullOrEmpty(text))
            return "New chat";
        return text.Length <= 40 ? text : text[..40] + "…";
    }

    /// <summary>Re-submit a user message's prompt as a new turn (the ↻ button on user bubbles).</summary>
    [RelayCommand]
    private void RerunPrompt(MessageViewModel? message)
    {
        if (message is null || IsBusy || !message.IsUser || string.IsNullOrWhiteSpace(message.Text))
            return;

        InputText = message.Text;
        if (SendCommand.CanExecute(null))
            SendCommand.Execute(null);
    }

    /// <summary>Read an assistant message aloud (the 🔈 button); clicking again stops it.</summary>
    [RelayCommand]
    private async Task SpeakMessage(MessageViewModel? message)
    {
        if (message is null)
            return;

        // Stop whatever is currently playing (covers both "stop this one" and "switch to another").
        var wasSpeaking = message.IsSpeaking;
        await _speech.StopAsync();
        if (wasSpeaking)
            return; // toggled off — the in-flight call clears its own IsSpeaking in its finally

        if (!_speech.IsConfigured)
        {
            StatusText = "No voice configured — choose one in Settings → Voice.";
            return;
        }

        message.IsSpeaking = true;
        try
        {
            await _speech.SpeakAsync(message.Text);
        }
        catch (Exception ex)
        {
            StatusText = $"Voice error: {ex.Message}";
        }
        finally
        {
            message.IsSpeaking = false;
        }
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AttachPhotos() => AttachFilesRequested?.Invoke(this, AttachmentKind.Photo);

    [RelayCommand]
    private void AttachDocuments() => AttachFilesRequested?.Invoke(this, AttachmentKind.Document);

    [RelayCommand]
    private void RemoveAttachment(Attachment? attachment)
    {
        if (attachment is not null && Attachments.Remove(attachment))
            HasAttachments = Attachments.Count > 0;
    }

    /// <summary>Adds files chosen in the file dialog (called by the view), de-duplicating by path.</summary>
    public void AddAttachments(IEnumerable<Attachment> items)
    {
        foreach (var a in items)
        {
            if (Attachments.Any(x => string.Equals(x.Path, a.Path, StringComparison.OrdinalIgnoreCase)))
                continue;
            Attachments.Add(a);
        }
        HasAttachments = Attachments.Count > 0;
    }

    // --- MCP resources (composer 📎 → "From MCP server…") ---

    /// <summary>Composer 📎 menu: ask the view to open the MCP resource browser.</summary>
    [RelayCommand]
    private void AttachMcpResources() => McpResourcesRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Stages MCP resources fetched in the browser as context chips for the next prompt (called by the view).</summary>
    public void AddMcpResources(IEnumerable<McpAttachedResource> items)
    {
        foreach (var r in items)
            McpResources.Add(r);
        HasMcpResources = McpResources.Count > 0;
    }

    [RelayCommand]
    private void RemoveMcpResource(McpAttachedResource? resource)
    {
        if (resource is not null && McpResources.Remove(resource))
            HasMcpResources = McpResources.Count > 0;
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            // UseShellExecute lets the OS pick the default browser on both Windows and Linux.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Opening a link should never crash the app.
        }
    }

    // --- Project file tree context actions (right-click / double-click) ----------------------------

    /// <summary>Context menu (folder): open the folder in the OS file manager.</summary>
    [RelayCommand]
    private void OpenInFileExplorer(FileNode? node)
    {
        if (node is { IsDirectory: true, FullPath.Length: > 0 })
            ShellOpenFolder(node.FullPath);
    }

    /// <summary>Context menu (file): open the file's containing folder, selecting the file where supported.</summary>
    [RelayCommand]
    private void RevealInFolder(FileNode? node)
    {
        if (node is { IsDirectory: false, FullPath.Length: > 0 })
            ShellRevealFile(node.FullPath);
    }

    /// <summary>Double-click (file): ask the OS to open the file with its default application.</summary>
    [RelayCommand]
    private void OpenFileWithOs(FileNode? node)
    {
        if (node is { IsDirectory: false, FullPath.Length: > 0 })
            ShellOpenFile(node.FullPath);
    }

    // OS shell launchers — best-effort; opening something in the file manager must never crash the app.

    private static void ShellOpenFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"\"{path}\"", UseShellExecute = true });
            else
                StartUnixOpener(path);
        }
        catch { /* ignored */ }
    }

    private static void ShellRevealFile(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // explorer's /select opens the containing folder with the file highlighted.
                Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{filePath}\"", UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add("-R");
                psi.ArgumentList.Add(filePath);
                Process.Start(psi);
            }
            else
            {
                // No portable "reveal & select" on Linux — open the containing folder instead.
                var folder = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(folder))
                    StartUnixOpener(folder);
            }
        }
        catch { /* ignored */ }
    }

    private static void ShellOpenFile(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            else
                StartUnixOpener(filePath);
        }
        catch { /* ignored */ }
    }

    // Linux: xdg-open / macOS: open — picks the default handler for a file or folder. ArgumentList avoids
    // manual quoting (paths with spaces).
    private static void StartUnixOpener(string target)
    {
        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var psi = new ProcessStartInfo(opener) { UseShellExecute = false };
        psi.ArgumentList.Add(target);
        Process.Start(psi);
    }

    private void RequestScroll() => ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
}
