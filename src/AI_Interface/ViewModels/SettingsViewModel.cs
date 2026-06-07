using System;
using System.Collections.Generic;
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
    private readonly IOllamaClient _ollama;
    private readonly bool _loading;

    private const string OkColor = "#00CEA8";   // status OK (teal)
    private const string ErrColor = "#E0573A";  // status error (red)
    private const string BusyColor = "#9AA0A6"; // neutral grey while a probe runs

    // --- AI model: connection probe (Local AI) ---

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(QuickSetupCommand))]
    private bool _isTestingConnection;

    /// <summary>Result of the last Quick setup / Test connection probe (empty = none yet).</summary>
    [ObservableProperty] private string _connectionTestMessage = "";

    /// <summary>Hex color for <see cref="ConnectionTestMessage"/> (green ok / red error / grey busy).</summary>
    [ObservableProperty] private string _connectionTestColor = OkColor;

    /// <summary>True when the configured Ollama server has answered a probe — gates Model Config.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ModelConfigCommand))]
    private bool _isOllamaConnected;

    /// <summary>Raised when the view should open the Model Config window.</summary>
    public event System.EventHandler? ModelConfigRequested;

    /// <summary>Preset color swatches (from the sagiv-reuben site palette).</summary>
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

    // --- General ---

    [ObservableProperty] private string _ollamaBaseUrl;
    [ObservableProperty] private decimal _resultsPerQuery;
    [ObservableProperty] private decimal _maxPagesToRead;
    [ObservableProperty] private decimal _researchQueryCount;
    [ObservableProperty] private double _thinkingEffort;

    // --- Project agent ---

    [ObservableProperty] private AgentApprovalMode _agentApproval;
    [ObservableProperty] private bool _allowSoftwareInstall;

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

    public SettingsViewModel(ISettingsService settings, IThemeService theme, IOllamaClient ollama)
    {
        _settings = settings;
        _theme = theme;
        _ollama = ollama;

        _loading = true;
        var s = settings.Current;
        _mode = s.ThemeMode;
        _accentColor = s.AccentColor;
        _userBubbleColor = s.UserBubbleColor;
        _assistantBubbleColor = s.AssistantBubbleColor;
        _ollamaBaseUrl = s.OllamaBaseUrl;
        _resultsPerQuery = s.SearchResultsPerQuery;
        _maxPagesToRead = s.MaxPagesToRead;
        _researchQueryCount = s.ResearchQueryCount;
        _thinkingEffort = s.ThinkingEffort;
        _agentApproval = s.AgentApproval;
        _allowSoftwareInstall = s.AllowSoftwareInstall;
        _searchProvider = s.SearchProvider;
        _searxngUrl = s.SearxngUrl;
        _braveApiKey = s.BraveApiKey;
        _tavilyApiKey = s.TavilyApiKey;
        _googleApiKey = s.GoogleApiKey;
        _googleSearchEngineId = s.GoogleSearchEngineId;
        _loading = false;
    }

    // Design-time constructor for the XAML previewer.
    public SettingsViewModel() : this(new DesignSettingsService(), new ThemeService(), new DesignOllamaClient())
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

    partial void OnOllamaBaseUrlChanged(string value) => SaveGeneral();
    partial void OnResultsPerQueryChanged(decimal value) => SaveGeneral();
    partial void OnMaxPagesToReadChanged(decimal value) => SaveGeneral();
    partial void OnResearchQueryCountChanged(decimal value) => SaveGeneral();
    partial void OnThinkingEffortChanged(double value) => SaveGeneral();

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

    partial void OnAllowSoftwareInstallChanged(bool value) => SaveAgent();

    private void SaveAgent()
    {
        if (_loading)
            return;
        _settings.Current.AgentApproval = AgentApproval;
        _settings.Current.AllowSoftwareInstall = AllowSoftwareInstall;
        _settings.Save();
    }

    partial void OnSearxngUrlChanged(string value) => SaveWebSearch();
    partial void OnBraveApiKeyChanged(string value) => SaveWebSearch();
    partial void OnTavilyApiKeyChanged(string value) => SaveWebSearch();
    partial void OnGoogleApiKeyChanged(string value) => SaveWebSearch();
    partial void OnGoogleSearchEngineIdChanged(string value) => SaveWebSearch();

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
    }

    /// <summary>Scan localhost:11434 and, if Ollama answers, fill in the server URL.</summary>
    [RelayCommand(CanExecute = nameof(CanProbe))]
    private async Task QuickSetup()
    {
        const string local = "http://localhost:11434";
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
        var url = string.IsNullOrWhiteSpace(OllamaBaseUrl) ? "http://localhost:11434" : OllamaBaseUrl.Trim();
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

    private bool CanProbe => !IsTestingConnection;

    /// <summary>Silent connectivity check (no status message) so Model Config reflects reality on open.</summary>
    public async Task RefreshConnectionAsync()
    {
        var url = string.IsNullOrWhiteSpace(OllamaBaseUrl) ? "http://localhost:11434" : OllamaBaseUrl.Trim();
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
