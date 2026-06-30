using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Interface.ViewModels;

/// <summary>
/// Backs the Settings window. Theme changes apply live (and persist) as the user edits them;
/// General changes persist on edit. All values are mirrored into <see cref="ISettingsService.Current"/>.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IModelRouter _router;
    private readonly IOllamaClient _ollama;
    private readonly ISpeechService _speech;
    private readonly IPiperInstaller _piperInstaller;
    private readonly IOllamaInstaller _ollamaInstaller;
    private readonly ISearxngInstaller _searxngInstaller;
    private readonly IWebSearchService _webSearch;
    private readonly IMemoryService _memory;
    private readonly bool _loading;

    /// <summary>The default local Ollama endpoint, used when the URL field is blank.</summary>
    private const string DefaultOllamaUrl = "http://localhost:11434";

    /// <summary>The active project's directory (set by <see cref="InitializeMemory"/>), or null when none.</summary>
    private string? _memoryProjectDir;

    /// <summary>The Agents (AI Features) master/detail panel.</summary>
    public AgentsViewModel AgentsPanel { get; }

    /// <summary>The MCP Servers (AI Features) master/detail panel.</summary>
    public McpViewModel McpPanel { get; }

    /// <summary>The Web Models (AI Model) add-provider / active-providers panel.</summary>
    public WebModelsViewModel WebModelsPanel { get; }

    // --- left-rail category navigation (Editor Features / AI Features) ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppearance))]
    [NotifyPropertyChangedFor(nameof(IsTypography))]
    [NotifyPropertyChangedFor(nameof(IsLayout))]
    [NotifyPropertyChangedFor(nameof(IsModels))]
    [NotifyPropertyChangedFor(nameof(IsAgents))]
    [NotifyPropertyChangedFor(nameof(IsMcp))]
    [NotifyPropertyChangedFor(nameof(IsAutonomyAndMemory))]
    [NotifyPropertyChangedFor(nameof(IsWebSearch))]
    [NotifyPropertyChangedFor(nameof(IsVoice))]
    [NotifyPropertyChangedFor(nameof(IsResearchAndThinking))]
    private SettingsCategory _selectedCategory = SettingsCategory.Appearance;

    public bool IsAppearance => SelectedCategory == SettingsCategory.Appearance;
    public bool IsTypography => SelectedCategory == SettingsCategory.Typography;
    public bool IsLayout => SelectedCategory == SettingsCategory.Layout;
    public bool IsModels => SelectedCategory == SettingsCategory.Models;
    public bool IsAgents => SelectedCategory == SettingsCategory.Agents;
    public bool IsMcp => SelectedCategory == SettingsCategory.Mcp;
    public bool IsAutonomyAndMemory => SelectedCategory == SettingsCategory.AutonomyAndMemory;
    public bool IsWebSearch => SelectedCategory == SettingsCategory.WebSearch;
    public bool IsVoice => SelectedCategory == SettingsCategory.Voice;
    public bool IsResearchAndThinking => SelectedCategory == SettingsCategory.ResearchAndThinking;

    [RelayCommand]
    private void SelectCategory(SettingsCategory category) => SelectedCategory = category;

    private const string OkColor = "#3FB950";   // status OK (green)
    private const string ErrColor = "#E5534B";  // status error (red)
    private const string BusyColor = "#858585"; // neutral grey while a probe runs

    // --- AI model: connection probe (Local AI) ---

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(QuickSetupCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isTestingConnection;

    /// <summary>Result of the last Quick setup / Test connection probe (empty = none yet).</summary>
    [ObservableProperty] private string _connectionTestMessage = "";

    /// <summary>Hex color for <see cref="ConnectionTestMessage"/> (green ok / red error / grey busy).</summary>
    [ObservableProperty] private string _connectionTestColor = OkColor;

    /// <summary>True when the configured Ollama server has answered a probe — gates Model Config.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ModelConfigCommand))]
    private bool _isOllamaConnected;

    /// <summary>True while the one-click Ollama install is running (disables the button).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallOllamaCommand))]
    private bool _isInstallingOllama;

    /// <summary>Raised before installing Ollama so the view can confirm with the user.
    /// The view completes the supplied source with <c>true</c> to proceed, <c>false</c> to cancel.</summary>
    public event System.EventHandler<TaskCompletionSource<bool>>? InstallOllamaConfirmationRequested;

    /// <summary>True while a SearXNG install/remove is running (disables both buttons).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSearxngCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSearxngCommand))]
    private bool _isBusySearxng;

    /// <summary>Live status of the last SearXNG install/remove (shown under the buttons).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearxngStatus))]
    private string _searxngStatus = "";

    public bool HasSearxngStatus => !string.IsNullOrEmpty(SearxngStatus);

    /// <summary>Raised before installing/removing SearXNG so the view can confirm (Docker software).</summary>
    public event System.EventHandler<TaskCompletionSource<bool>>? InstallSearxngConfirmationRequested;
    public event System.EventHandler<TaskCompletionSource<bool>>? RemoveSearxngConfirmationRequested;

    /// <summary>Raised when the view should open the Model Config window.</summary>
    public event System.EventHandler? ModelConfigRequested;

    /// <summary>Raised by the Connect button — the main window reconnects and reloads its model list.</summary>
    public event System.EventHandler? ConnectRequested;

    /// <summary>
    /// Ask the host to reload its model list. Used after the Model Config window closes (a local model may
    /// have been downloaded or removed there) — reuses the <see cref="ConnectRequested"/> reload path.
    /// </summary>
    public void NotifyModelsChanged() => ConnectRequested?.Invoke(this, System.EventArgs.Empty);

    /// <summary>Preset color swatches (the flat IDE palette).</summary>
    public IReadOnlyList<string> Palette { get; } = ThemeDefaults.Palette;

    /// <summary>Selectable web search backends for the Web Search tab.</summary>
    public IReadOnlyList<SearchProviderOption> SearchProviders { get; } = new[]
    {
        new SearchProviderOption(SearchProvider.DuckDuckGo, SearchProvider.DuckDuckGo.DisplayName()),
        new SearchProviderOption(SearchProvider.SearXNG, SearchProvider.SearXNG.DisplayName()),
        new SearchProviderOption(SearchProvider.Brave, SearchProvider.Brave.DisplayName()),
        new SearchProviderOption(SearchProvider.Tavily, SearchProvider.Tavily.DisplayName()),
        new SearchProviderOption(SearchProvider.Google, SearchProvider.Google.DisplayName())
    };

    // --- Theme ---

    [ObservableProperty]
    private ThemeMode _mode;

    public bool IsSystemMode
    {
        get => Mode == ThemeMode.System;
        set { if (value) Mode = ThemeMode.System; }
    }

    public bool IsLightMode
    {
        get => Mode == ThemeMode.Light;
        set { if (value) Mode = ThemeMode.Light; }
    }

    public bool IsDarkMode
    {
        get => Mode == ThemeMode.Dark;
        set { if (value) Mode = ThemeMode.Dark; }
    }

    [ObservableProperty] private string _accentColor;
    [ObservableProperty] private string _userBubbleColor;
    [ObservableProperty] private string _assistantBubbleColor;
    [ObservableProperty] private string _fontFamily;
    [ObservableProperty] private double _fontSize;

    /// <summary>Selectable font families for the Theme tab.</summary>
    public IReadOnlyList<string> Fonts { get; } = ThemeDefaults.Fonts;

    // --- General ---

    [ObservableProperty] private string _ollamaBaseUrl;
    [ObservableProperty] private decimal _resultsPerQuery;
    [ObservableProperty] private decimal _maxPagesToRead;
    [ObservableProperty] private decimal _researchQueryCount;
    [ObservableProperty] private double _thinkingEffort;

    // --- Deep Research: "Use Multiple LLMs" (give planning + synthesis their own models) ---

    /// <summary>When on, the two model pickers below assign separate models to planning vs. synthesis.</summary>
    [ObservableProperty] private bool _deepResearchUseMultipleModels;

    /// <summary>Models offered in the planning/synthesis pickers (across every configured provider).</summary>
    public ObservableCollection<ChatModel> ResearchModels { get; } = new();

    /// <summary>Model for the query-planning step (null = use the chat model).</summary>
    [ObservableProperty] private ChatModel? _planningModel;

    /// <summary>Model for the report-synthesis step (null = use the chat model). Receives page contents.</summary>
    [ObservableProperty] private ChatModel? _synthesisModel;

    /// <summary>Set while restoring saved picks so the change handlers don't persist back (avoids a loop).</summary>
    private bool _syncingResearchModels;

    // --- Project agent ---

    [ObservableProperty] private AgentApprovalMode _agentApproval;
    [ObservableProperty] private SoftwareInstallPermission _softwareInstall;

    public bool IsAutoRun
    {
        get => AgentApproval == AgentApprovalMode.AutoRun;
        set { if (value) AgentApproval = AgentApprovalMode.AutoRun; }
    }

    public bool IsConfirmDestructive
    {
        get => AgentApproval == AgentApprovalMode.ConfirmDestructive;
        set { if (value) AgentApproval = AgentApprovalMode.ConfirmDestructive; }
    }

    public bool IsConfirmEverything
    {
        get => AgentApproval == AgentApprovalMode.ConfirmEverything;
        set { if (value) AgentApproval = AgentApprovalMode.ConfirmEverything; }
    }

    // Software-install permission, as three mutually exclusive radio options.
    public bool IsInstallNever
    {
        get => SoftwareInstall == SoftwareInstallPermission.Never;
        set { if (value) SoftwareInstall = SoftwareInstallPermission.Never; }
    }

    public bool IsInstallAsk
    {
        get => SoftwareInstall == SoftwareInstallPermission.Ask;
        set { if (value) SoftwareInstall = SoftwareInstallPermission.Ask; }
    }

    public bool IsInstallAllow
    {
        get => SoftwareInstall == SoftwareInstallPermission.Allow;
        set { if (value) SoftwareInstall = SoftwareInstallPermission.Allow; }
    }

    // --- Memory (Autonomy & Memory) ---

    /// <summary>Master switch for persistent memory; mirrors <see cref="AppSettings.GlobalMemoryEnabled"/>.</summary>
    [ObservableProperty] private bool _globalMemoryEnabled;

    /// <summary>
    /// When on, Project mode offers only orchestrator ("team") agents in the picker; mirrors
    /// <see cref="AppSettings.ProjectTeamAgentsOnly"/>. The main window reloads the picker after Settings
    /// closes, so toggling this re-applies to an active project.
    /// </summary>
    [ObservableProperty] private bool _projectTeamAgentsOnly;

    /// <summary>
    /// When on, the project agent advances through phases automatically; when off it pauses at each phase
    /// boundary for the user's OK. Mirrors <see cref="AppSettings.AutoFlowPhases"/>; independent of approval.
    /// </summary>
    [ObservableProperty] private bool _autoFlowPhases;

    /// <summary>Facts remembered about the user (global scope).</summary>
    public ObservableCollection<MemoryEntry> GlobalMemories { get; } = new();

    /// <summary>Facts remembered about the active project (populated only when a project is open).</summary>
    public ObservableCollection<MemoryEntry> ProjectMemories { get; } = new();

    /// <summary>True when a project is active, so the project-memory section is shown.</summary>
    [ObservableProperty] private bool _hasProjectMemoryScope;

    public bool HasGlobalMemories => GlobalMemories.Count > 0;
    public bool HasProjectMemories => ProjectMemories.Count > 0;

    /// <summary>Loads the memory lists for the Autonomy &amp; Memory panel. Called before the window opens.</summary>
    public void InitializeMemory(string? projectDir)
    {
        _memoryProjectDir = string.IsNullOrWhiteSpace(projectDir) ? null : projectDir;
        HasProjectMemoryScope = _memoryProjectDir is not null;
        ReloadMemories();
    }

    private void ReloadMemories()
    {
        GlobalMemories.Clear();
        foreach (var e in _memory.Load(MemoryScope.Global, null))
            GlobalMemories.Add(e);

        ProjectMemories.Clear();
        if (_memoryProjectDir is not null)
            foreach (var e in _memory.Load(MemoryScope.Project, _memoryProjectDir))
                ProjectMemories.Add(e);

        OnPropertyChanged(nameof(HasGlobalMemories));
        OnPropertyChanged(nameof(HasProjectMemories));
    }

    partial void OnGlobalMemoryEnabledChanged(bool value)
    {
        if (_loading)
            return;
        _settings.Current.GlobalMemoryEnabled = value;
        _settings.Save();
    }

    partial void OnProjectTeamAgentsOnlyChanged(bool value)
    {
        if (_loading)
            return;
        _settings.Current.ProjectTeamAgentsOnly = value;
        _settings.Save();
    }

    partial void OnAutoFlowPhasesChanged(bool value)
    {
        if (_loading)
            return;
        _settings.Current.AutoFlowPhases = value;
        _settings.Save();
    }

    [RelayCommand]
    private void DeleteGlobalMemory(MemoryEntry? entry)
    {
        if (entry is null)
            return;
        _memory.Remove(MemoryScope.Global, entry.Text, null);
        ReloadMemories();
    }

    [RelayCommand]
    private void DeleteProjectMemory(MemoryEntry? entry)
    {
        if (entry is null || _memoryProjectDir is null)
            return;
        _memory.Remove(MemoryScope.Project, entry.Text, _memoryProjectDir);
        ReloadMemories();
    }

    [RelayCommand]
    private void ClearGlobalMemory()
    {
        _memory.Clear(MemoryScope.Global, null);
        ReloadMemories();
    }

    [RelayCommand]
    private void ClearProjectMemory()
    {
        if (_memoryProjectDir is null)
            return;
        _memory.Clear(MemoryScope.Project, _memoryProjectDir);
        ReloadMemories();
    }

    // --- Web search ---

    [ObservableProperty] private SearchProvider _searchProvider;

    // Dependency-field visibility: each is true only when its provider is selected,
    // mirroring the appearance-mode bools above. Raised together in OnSearchProviderChanged.
    public bool IsDuckDuckGoSelected => SearchProvider == SearchProvider.DuckDuckGo;
    public bool IsSearxngSelected => SearchProvider == SearchProvider.SearXNG;
    public bool IsBraveSelected => SearchProvider == SearchProvider.Brave;
    public bool IsTavilySelected => SearchProvider == SearchProvider.Tavily;
    public bool IsGoogleSelected => SearchProvider == SearchProvider.Google;

    /// <summary>The option object bound to the provider ComboBox (kept in sync with <see cref="SearchProvider"/>).</summary>
    public SearchProviderOption? SelectedSearchProvider
    {
        get
        {
            foreach (var option in SearchProviders)
                if (option.Provider == SearchProvider)
                    return option;
            return null;
        }
        set { if (value is not null) SearchProvider = value.Provider; }
    }

    [ObservableProperty] private string _searxngUrl;
    [ObservableProperty] private string _braveApiKey;
    [ObservableProperty] private string _tavilyApiKey;
    [ObservableProperty] private string _googleApiKey;
    [ObservableProperty] private string _googleSearchEngineId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestWebSearchCommand))]
    private bool _isTestingWebSearch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWebSearchTestMessage))]
    private string _webSearchTestMessage = "";

    [ObservableProperty] private string _webSearchTestColor = OkColor;
    [ObservableProperty] private bool _isWebSearchTestError;

    public bool HasWebSearchTestMessage => !string.IsNullOrEmpty(WebSearchTestMessage);

    // --- Voice (text-to-speech) ---

    [ObservableProperty] private SpeechProvider _speechProvider;
    [ObservableProperty] private string _piperExecutablePath;
    [ObservableProperty] private string _piperModelPath;

    [ObservableProperty] private string _voiceStatus = "";
    [ObservableProperty] private string _voiceStatusColor = OkColor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestVoiceCommand))]
    private bool _isTestingVoice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallPiperCommand))]
    private bool _isInstallingPiper;

    /// <summary>Raised when the view should open the Voice browser window.</summary>
    public event System.EventHandler? VoiceBrowserRequested;

    /// <summary>Raised before installing Piper so the view can confirm with the user.
    /// The view completes the supplied source with <c>true</c> to proceed, <c>false</c> to cancel.</summary>
    public event System.EventHandler<TaskCompletionSource<bool>>? InstallPiperConfirmationRequested;

    // Provider as mutually exclusive radio options (mirrors the appearance-mode bools).
    public bool IsVoiceOff
    {
        get => SpeechProvider == SpeechProvider.None;
        set { if (value) SpeechProvider = SpeechProvider.None; }
    }

    public bool IsVoicePiper
    {
        get => SpeechProvider == SpeechProvider.Piper;
        set { if (value) SpeechProvider = SpeechProvider.Piper; }
    }

    // Cloud providers are managed by WebModelsPanel (the "Add Provider" / "Active Providers" UI).

    public SettingsViewModel(
        ISettingsService settings, IThemeService theme, IModelRouter router, IOllamaClient ollama,
        ISpeechService speech, AgentsViewModel agentsPanel, McpViewModel mcpPanel, IMemoryService memory,
        IPiperInstaller piperInstaller, IOllamaInstaller ollamaInstaller, ISearxngInstaller searxngInstaller,
        IWebSearchService webSearch, WebModelsViewModel webModelsPanel)
    {
        _settings = settings;
        _theme = theme;
        _router = router;
        _ollama = ollama;
        _speech = speech;
        _memory = memory;
        _piperInstaller = piperInstaller;
        _ollamaInstaller = ollamaInstaller;
        _searxngInstaller = searxngInstaller;
        _webSearch = webSearch;
        AgentsPanel = agentsPanel;
        McpPanel = mcpPanel;
        WebModelsPanel = webModelsPanel;
        // Adding/removing a cloud provider should reload the main window's model picker, same as Connect.
        WebModelsPanel.ProvidersChanged += (_, _) => ConnectRequested?.Invoke(this, System.EventArgs.Empty);

        _loading = true;
        var s = settings.Current;
        _mode = s.ThemeMode;
        _accentColor = s.AccentColor;
        _userBubbleColor = s.UserBubbleColor;
        _assistantBubbleColor = s.AssistantBubbleColor;
        _fontFamily = s.FontFamily;
        _fontSize = s.FontSize;
        _ollamaBaseUrl = s.OllamaBaseUrl;
        _resultsPerQuery = s.SearchResultsPerQuery;
        _maxPagesToRead = s.MaxPagesToRead;
        _researchQueryCount = s.ResearchQueryCount;
        _deepResearchUseMultipleModels = s.DeepResearchUseMultipleModels;
        _thinkingEffort = s.ThinkingEffort;
        _agentApproval = s.AgentApproval;
        _softwareInstall = s.SoftwareInstall;
        _globalMemoryEnabled = s.GlobalMemoryEnabled;
        _projectTeamAgentsOnly = s.ProjectTeamAgentsOnly;
        _autoFlowPhases = s.AutoFlowPhases;
        _searchProvider = s.SearchProvider;
        _searxngUrl = s.SearxngUrl;
        _braveApiKey = s.BraveApiKey;
        _tavilyApiKey = s.TavilyApiKey;
        _googleApiKey = s.GoogleApiKey;
        _googleSearchEngineId = s.GoogleSearchEngineId;
        _speechProvider = s.SpeechProvider;
        _piperExecutablePath = s.PiperExecutablePath;
        _piperModelPath = s.PiperModelPath;
        _loading = false;
    }

    // Design-time constructor for the XAML previewer.
    public SettingsViewModel() : this(
        new DesignSettingsService(), new ThemeService(), new DesignModelRouter(), new DesignOllamaClient(),
        new DesignSpeechService(), new AgentsViewModel(),
        new McpViewModel(), new DesignMemoryService(), new DesignPiperInstaller(), new DesignOllamaInstaller(),
        new DesignSearxngInstaller(), new DesignWebSearchService(), new WebModelsViewModel())
    {
    }

    partial void OnModeChanged(ThemeMode value)
    {
        OnPropertyChanged(nameof(IsSystemMode));
        OnPropertyChanged(nameof(IsLightMode));
        OnPropertyChanged(nameof(IsDarkMode));
        ApplyTheme();
    }

    partial void OnAccentColorChanged(string value) => ApplyTheme();
    partial void OnUserBubbleColorChanged(string value) => ApplyTheme();
    partial void OnAssistantBubbleColorChanged(string value) => ApplyTheme();
    partial void OnFontFamilyChanged(string value) => ApplyTheme();
    partial void OnFontSizeChanged(double value) => ApplyTheme();

    partial void OnOllamaBaseUrlChanged(string value) => SaveGeneral();
    partial void OnResultsPerQueryChanged(decimal value) => SaveGeneral();
    partial void OnMaxPagesToReadChanged(decimal value) => SaveGeneral();
    partial void OnResearchQueryCountChanged(decimal value) => SaveGeneral();
    partial void OnThinkingEffortChanged(double value) => SaveGeneral();

    partial void OnDeepResearchUseMultipleModelsChanged(bool value)
    {
        if (_loading)
            return;
        _settings.Current.DeepResearchUseMultipleModels = value;
        _settings.Save();
    }

    partial void OnPlanningModelChanged(ChatModel? value)
    {
        if (_loading || _syncingResearchModels)
            return;
        _settings.Current.DeepResearchPlanningModel = value is null ? null : $"{value.Provider}:{value.Id}";
        _settings.Save();
    }

    partial void OnSynthesisModelChanged(ChatModel? value)
    {
        if (_loading || _syncingResearchModels)
            return;
        _settings.Current.DeepResearchSynthesisModel = value is null ? null : $"{value.Provider}:{value.Id}";
        _settings.Save();
    }

    /// <summary>
    /// Populates the planning/synthesis model pickers (best-effort; offline/missing providers contribute
    /// nothing) and restores the saved picks without re-persisting them. Mirrors
    /// <see cref="AgentsViewModel.LoadModelsAsync"/>. Called by the Settings window after it loads.
    /// </summary>
    public async Task LoadResearchModelsAsync()
    {
        try
        {
            var models = await _router.ListAllModelsAsync();
            ResearchModels.Clear();
            foreach (var m in models)
                ResearchModels.Add(m);
        }
        catch
        {
            // The pickers simply stay empty if no provider answers.
        }

        // Restore the saved picks against the freshly loaded list, guarded so the change handlers
        // don't write the settings back (which could clobber an as-yet-unloaded selection).
        _syncingResearchModels = true;
        PlanningModel = ResolveResearchModel(_settings.Current.DeepResearchPlanningModel);
        SynthesisModel = ResolveResearchModel(_settings.Current.DeepResearchSynthesisModel);
        _syncingResearchModels = false;
    }

    /// <summary>Resolves a saved "{provider}:{id}" pick to a current <see cref="ResearchModels"/> entry, or null.</summary>
    private ChatModel? ResolveResearchModel(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
            return null;
        var sep = saved.IndexOf(':');
        if (sep > 0 && Enum.TryParse<AiProvider>(saved[..sep], out var provider))
        {
            var id = saved[(sep + 1)..];
            return ResearchModels.FirstOrDefault(m => m.Provider == provider && m.Id == id);
        }
        return null;
    }

    partial void OnSearchProviderChanged(SearchProvider value)
    {
        OnPropertyChanged(nameof(IsDuckDuckGoSelected));
        OnPropertyChanged(nameof(IsSearxngSelected));
        OnPropertyChanged(nameof(IsBraveSelected));
        OnPropertyChanged(nameof(IsTavilySelected));
        OnPropertyChanged(nameof(IsGoogleSelected));
        OnPropertyChanged(nameof(SelectedSearchProvider));
        SaveWebSearch();
    }

    partial void OnAgentApprovalChanged(AgentApprovalMode value)
    {
        OnPropertyChanged(nameof(IsAutoRun));
        OnPropertyChanged(nameof(IsConfirmDestructive));
        OnPropertyChanged(nameof(IsConfirmEverything));
        SaveAgent();
    }

    partial void OnSoftwareInstallChanged(SoftwareInstallPermission value)
    {
        OnPropertyChanged(nameof(IsInstallNever));
        OnPropertyChanged(nameof(IsInstallAsk));
        OnPropertyChanged(nameof(IsInstallAllow));
        SaveAgent();
    }

    private void SaveAgent()
    {
        if (_loading)
            return;
        _settings.Current.AgentApproval = AgentApproval;
        _settings.Current.SoftwareInstall = SoftwareInstall;
        _settings.Save();
    }

    partial void OnSearxngUrlChanged(string value) => SaveWebSearch();
    partial void OnBraveApiKeyChanged(string value) => SaveWebSearch();
    partial void OnTavilyApiKeyChanged(string value) => SaveWebSearch();
    partial void OnGoogleApiKeyChanged(string value) => SaveWebSearch();
    partial void OnGoogleSearchEngineIdChanged(string value) => SaveWebSearch();

    private bool CanTestWebSearch => !IsTestingWebSearch;

    /// <summary>Probe the currently configured web-search provider and report success or a specific error.</summary>
    [RelayCommand(CanExecute = nameof(CanTestWebSearch))]
    private async Task TestWebSearch()
    {
        IsTestingWebSearch = true;
        IsWebSearchTestError = false;
        WebSearchTestColor = BusyColor;
        WebSearchTestMessage = "Testing…";
        try
        {
            var error = await _webSearch.TestAsync();
            if (error is null)
            {
                WebSearchTestColor = OkColor;
                WebSearchTestMessage = "Connected";
                IsWebSearchTestError = false;
            }
            else
            {
                WebSearchTestColor = ErrColor;
                WebSearchTestMessage = error;
                IsWebSearchTestError = true;
            }
        }
        catch (Exception ex)
        {
            WebSearchTestColor = ErrColor;
            WebSearchTestMessage = $"Error: {ex.Message}";
            IsWebSearchTestError = true;
        }
        finally
        {
            IsTestingWebSearch = false;
        }
    }

    partial void OnSpeechProviderChanged(SpeechProvider value)
    {
        OnPropertyChanged(nameof(IsVoiceOff));
        OnPropertyChanged(nameof(IsVoicePiper));
        SaveVoice();
    }

    partial void OnPiperExecutablePathChanged(string value) => SaveVoice();
    partial void OnPiperModelPathChanged(string value) => SaveVoice();

    /// <summary>Persist the voice settings (called as each field changes, like the web-search keys).</summary>
    private void SaveVoice()
    {
        if (_loading)
            return;
        var s = _settings.Current;
        s.SpeechProvider = SpeechProvider;
        s.PiperExecutablePath = PiperExecutablePath.Trim();
        s.PiperModelPath = PiperModelPath.Trim();
        _settings.Save();
    }

    private bool CanTestVoice => !IsTestingVoice;

    /// <summary>Speak a short sample so the user can hear the configured voice.</summary>
    [RelayCommand(CanExecute = nameof(CanTestVoice))]
    private async Task TestVoice()
    {
        SaveVoice(); // make sure the latest paths are persisted before the engine reads them
        if (!_speech.IsConfigured)
        {
            VoiceStatusColor = ErrColor;
            VoiceStatus = "Not configured — set the Piper executable and a voice model below.";
            return;
        }

        IsTestingVoice = true;
        VoiceStatusColor = BusyColor;
        VoiceStatus = "Speaking…";
        try
        {
            await _speech.SpeakAsync("This is a test of the selected voice.");
            VoiceStatusColor = OkColor;
            VoiceStatus = "Voice is working.";
        }
        catch (Exception ex)
        {
            VoiceStatusColor = ErrColor;
            VoiceStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsTestingVoice = false;
        }
    }

    private bool CanInstallPiper => !IsInstallingPiper;

    /// <summary>Download and install the Piper engine, then switch the voice provider on.</summary>
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
        VoiceStatusColor = BusyColor;
        VoiceStatus = "Downloading Piper…";
        var progress = new Progress<string>(s => VoiceStatus = s);
        try
        {
            var exe = await _piperInstaller.InstallEngineAsync(progress, System.Threading.CancellationToken.None);
            PiperExecutablePath = exe; // updates the field + persists via OnPiperExecutablePathChanged
            IsVoicePiper = true;       // turn voice on now that the engine is present
            VoiceStatusColor = OkColor;
            VoiceStatus = "Piper installed. Click “Browse voices” to add a voice.";
        }
        catch (Exception ex)
        {
            VoiceStatusColor = ErrColor;
            VoiceStatus = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsInstallingPiper = false;
        }
    }

    /// <summary>Open the Voice browser to download voices for different languages.</summary>
    [RelayCommand]
    private void BrowseVoices() => VoiceBrowserRequested?.Invoke(this, System.EventArgs.Empty);

    [RelayCommand] private void SetAccent(string? hex) { if (hex is not null) AccentColor = hex; }
    [RelayCommand] private void SetUserColor(string? hex) { if (hex is not null) UserBubbleColor = hex; }
    [RelayCommand] private void SetAssistantColor(string? hex) { if (hex is not null) AssistantBubbleColor = hex; }

    [RelayCommand]
    private void ResetTheme()
    {
        Mode = ThemeMode.System;
        AccentColor = ThemeDefaults.Accent;
        UserBubbleColor = ThemeDefaults.UserBubble;
        AssistantBubbleColor = ThemeDefaults.AssistantBubble;
        FontFamily = ThemeDefaults.FontFamily;
        FontSize = ThemeDefaults.FontSize;
    }

    /// <summary>Scan localhost:11434 and, if Ollama answers, fill in the server URL.</summary>
    [RelayCommand(CanExecute = nameof(CanProbe))]
    private async Task QuickSetup()
    {
        const string local = DefaultOllamaUrl;
        IsTestingConnection = true;
        ConnectionTestColor = BusyColor;
        ConnectionTestMessage = "Scanning localhost:11434…";
        try
        {
            if (await _ollama.PingAsync(local))
            {
                OllamaBaseUrl = local; // populate the field (persists via OnOllamaBaseUrlChanged)
                IsOllamaConnected = true;
                ConnectionTestColor = OkColor;
                ConnectionTestMessage = "Connected — found Ollama at localhost:11434";
            }
            else
            {
                ConnectionTestColor = ErrColor;
                ConnectionTestMessage = "Error: no Ollama server found at localhost:11434";
            }
        }
        catch (Exception ex)
        {
            ConnectionTestColor = ErrColor;
            ConnectionTestMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    /// <summary>Probe the configured server URL and report success (green) or failure (red).</summary>
    [RelayCommand(CanExecute = nameof(CanProbe))]
    private async Task TestConnection()
    {
        var url = string.IsNullOrWhiteSpace(OllamaBaseUrl) ? DefaultOllamaUrl : OllamaBaseUrl.Trim();
        IsTestingConnection = true;
        ConnectionTestColor = BusyColor;
        ConnectionTestMessage = "Testing…";
        try
        {
            var ok = await _ollama.PingAsync(url);
            IsOllamaConnected = ok;
            if (ok)
            {
                ConnectionTestColor = OkColor;
                ConnectionTestMessage = "Connected";
            }
            else
            {
                ConnectionTestColor = ErrColor;
                ConnectionTestMessage = $"Error: could not reach Ollama at {url}";
            }
        }
        catch (Exception ex)
        {
            IsOllamaConnected = false;
            ConnectionTestColor = ErrColor;
            ConnectionTestMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    /// <summary>Reconnect: reload the main window's model list (via the host) and re-probe to show the result here.</summary>
    [RelayCommand(CanExecute = nameof(CanProbe))]
    private async Task Connect()
    {
        ConnectRequested?.Invoke(this, System.EventArgs.Empty);
        await TestConnection();
    }

    private bool CanProbe => !IsTestingConnection;

    private bool CanInstallOllama => !IsInstallingOllama;

    /// <summary>Download and install the local Ollama runtime, then re-probe so the model list refreshes.</summary>
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
        ConnectionTestColor = BusyColor;
        var progress = new Progress<string>(s => ConnectionTestMessage = s);
        try
        {
            // Idempotent: when Ollama is already present, InstallAsync skips the download/installer and
            // just starts the server (so we don't re-download on every click). Reflect that in the status.
            ConnectionTestMessage = _ollamaInstaller.IsOllamaInstalled
                ? "Ollama is already installed — connecting…"
                : "Downloading Ollama…";
            await _ollamaInstaller.InstallAsync(progress, System.Threading.CancellationToken.None);

            // The fresh server normally comes up on localhost; point the URL there if it's blank, then
            // reuse the connect flow so the main window reloads its model list and the probe shows green.
            if (string.IsNullOrWhiteSpace(OllamaBaseUrl))
                OllamaBaseUrl = DefaultOllamaUrl;

            ConnectionTestColor = OkColor;
            ConnectionTestMessage = "Connecting…";
            await Connect();
        }
        catch (Exception ex)
        {
            ConnectionTestColor = ErrColor;
            ConnectionTestMessage = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsInstallingOllama = false;
        }
    }

    private bool CanRunSearxng => !IsBusySearxng;

    /// <summary>Install + start a local SearXNG instance (Docker), then point the URL field at it.</summary>
    [RelayCommand(CanExecute = nameof(CanRunSearxng))]
    private async Task InstallSearxng()
    {
        if (InstallSearxngConfirmationRequested is not null)
        {
            var tcs = new TaskCompletionSource<bool>();
            InstallSearxngConfirmationRequested.Invoke(this, tcs);
            if (!await tcs.Task)
                return;
        }

        IsBusySearxng = true;
        var progress = new Progress<string>(s => SearxngStatus = s);
        try
        {
            await _searxngInstaller.InstallAsync(progress, System.Threading.CancellationToken.None);
            SearxngUrl = _searxngInstaller.LocalUrl; // persists via OnSearxngUrlChanged
        }
        catch (Exception ex)
        {
            SearxngStatus = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsBusySearxng = false;
        }
    }

    /// <summary>Stop and remove the local SearXNG container/image, and clear the URL field.</summary>
    [RelayCommand(CanExecute = nameof(CanRunSearxng))]
    private async Task RemoveSearxng()
    {
        if (RemoveSearxngConfirmationRequested is not null)
        {
            var tcs = new TaskCompletionSource<bool>();
            RemoveSearxngConfirmationRequested.Invoke(this, tcs);
            if (!await tcs.Task)
                return;
        }

        IsBusySearxng = true;
        var progress = new Progress<string>(s => SearxngStatus = s);
        try
        {
            await _searxngInstaller.RemoveAsync(progress, System.Threading.CancellationToken.None);
            if (string.Equals(SearxngUrl.Trim(), _searxngInstaller.LocalUrl, StringComparison.OrdinalIgnoreCase))
                SearxngUrl = ""; // only clear it if it still points at our managed instance
        }
        catch (Exception ex)
        {
            SearxngStatus = $"Remove failed: {ex.Message}";
        }
        finally
        {
            IsBusySearxng = false;
        }
    }

    /// <summary>Silent connectivity check (no status message) so Model Config reflects reality on open.</summary>
    public async Task RefreshConnectionAsync()
    {
        var url = string.IsNullOrWhiteSpace(OllamaBaseUrl) ? DefaultOllamaUrl : OllamaBaseUrl.Trim();
        try { IsOllamaConnected = await _ollama.PingAsync(url); }
        catch { IsOllamaConnected = false; }
    }

    /// <summary>Open the hardware-aware Model Config tool (enabled only when Ollama is connected).</summary>
    [RelayCommand(CanExecute = nameof(IsOllamaConnected))]
    private void ModelConfig() => ModelConfigRequested?.Invoke(this, System.EventArgs.Empty);

    private void ApplyTheme()
    {
        if (_loading)
            return;

        var s = _settings.Current;
        s.ThemeMode = Mode;
        s.AccentColor = AccentColor;
        s.UserBubbleColor = UserBubbleColor;
        s.AssistantBubbleColor = AssistantBubbleColor;
        s.FontFamily = FontFamily;
        s.FontSize = FontSize;

        _theme.Apply(s);
        _settings.Save();
    }

    private void SaveGeneral()
    {
        if (_loading)
            return;

        var s = _settings.Current;
        s.OllamaBaseUrl = OllamaBaseUrl.Trim();
        s.SearchResultsPerQuery = (int)ResultsPerQuery;
        s.MaxPagesToRead = (int)MaxPagesToRead;
        s.ResearchQueryCount = (int)ResearchQueryCount;
        s.ThinkingEffort = (int)ThinkingEffort;
        _settings.Save();
    }

    private void SaveWebSearch()
    {
        if (_loading)
            return;

        var s = _settings.Current;
        s.SearchProvider = SearchProvider;
        s.SearxngUrl = SearxngUrl.Trim();
        s.BraveApiKey = BraveApiKey.Trim();
        s.TavilyApiKey = TavilyApiKey.Trim();
        s.GoogleApiKey = GoogleApiKey.Trim();
        s.GoogleSearchEngineId = GoogleSearchEngineId.Trim();
        _settings.Save();
    }
}
