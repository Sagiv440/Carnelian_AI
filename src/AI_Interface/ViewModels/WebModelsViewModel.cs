using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Interface.ViewModels;

/// <summary>One cloud provider offered in the "Add Provider" dropdown (display label + glyph).</summary>
public sealed class ProviderOption
{
    public ProviderOption(AiProvider provider)
    {
        Provider = provider;
        Name = provider.DisplayName();
        Glyph = provider.Glyph();
    }

    public AiProvider Provider { get; }
    public string Name { get; }
    public string Glyph { get; }
    public string Display => $"{Glyph}  {Name}";
}

/// <summary>
/// One row in the "Active Providers" list: the provider's logo + name, its billing line (an estimated
/// "$ spent of $ budget" or just "Subscription"), a browse dropdown of the models it offers, and Remove.
/// </summary>
public sealed partial class ActiveProviderViewModel : ObservableObject
{
    public ActiveProviderViewModel(ProviderAccount account)
    {
        Provider = account.Provider;
        Billing = account.Billing;
        BudgetUsd = account.BudgetUsd;
        _spentUsd = account.SpentUsd;
    }

    public AiProvider Provider { get; }
    public string Glyph => Provider.Glyph();
    public string Name => Provider.DisplayName();
    public ProviderBilling Billing { get; }
    public decimal BudgetUsd { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BillingDisplay))]
    [NotifyPropertyChangedFor(nameof(IsOverBudget))]
    [NotifyPropertyChangedFor(nameof(BillingColor))]
    private decimal _spentUsd;

    /// <summary>The billing line shown on the row.</summary>
    public string BillingDisplay => Billing == ProviderBilling.Budget
        ? $"${SpentUsd.ToString("0.00", CultureInfo.InvariantCulture)} of ${BudgetUsd.ToString("0.00", CultureInfo.InvariantCulture)} (est.)"
          + (IsOverBudget ? "  —  over budget!" : "")
        : $"Subscription · ~${SpentUsd.ToString("0.00", CultureInfo.InvariantCulture)} est. used";

    /// <summary>True when a budget provider's estimated spend has passed its budget (shown in red).</summary>
    public bool IsOverBudget => Billing == ProviderBilling.Budget && SpentUsd > BudgetUsd;

    /// <summary>Billing-line colour: red over budget, otherwise the muted neutral grey.</summary>
    public string BillingColor => IsOverBudget ? "#E5534B" : "#858585";

    /// <summary>The models this provider offers (fetched on load) — a read-only browse list.</summary>
    public ObservableCollection<string> Models { get; } = new();

    [ObservableProperty]
    private string? _selectedModel;

    public bool HasModels => Models.Count > 0;
    public void NotifyModelsChanged() => OnPropertyChanged(nameof(HasModels));
}

/// <summary>
/// Backs Settings → AI Model → <b>Web Models</b>: an "Add Provider" form (pick a provider → enter its API
/// key → Connect → Add) over an "Active Providers" list. Adding upserts a <see cref="ProviderAccount"/> and
/// persists the key; removing deletes the account and clears the key (so the provider drops from the model
/// picker). Mirrors the master/detail panels (<see cref="AgentsViewModel"/>/<see cref="McpViewModel"/>):
/// the host creates it, calls <see cref="InitializeAsync"/> before the dialog opens, and listens to
/// <see cref="ProvidersChanged"/> to reload the main window's model picker.
/// </summary>
public sealed partial class WebModelsViewModel : ViewModelBase
{
    private const string OkColor = "#3FB950";
    private const string ErrColor = "#E5534B";
    private const string BusyColor = "#858585";

    private readonly ISettingsService _settings;
    private readonly IModelRouter _router;

    /// <summary>Raised after Add/Remove so the host can reload the top-bar model picker.</summary>
    public event EventHandler? ProvidersChanged;

    public WebModelsViewModel(ISettingsService settings, IModelRouter router)
    {
        _settings = settings;
        _router = router;
        foreach (var p in AiProviderExtensions.CloudProviders)
            AvailableProviders.Add(new ProviderOption(p));
    }

    /// <summary>Design-time constructor for the XAML previewer.</summary>
    public WebModelsViewModel() : this(new DesignSettingsService(), new DesignModelRouter()) { }

    // ---- Add Provider form ---------------------------------------------------------------------

    /// <summary>The cloud providers offered in the dropdown.</summary>
    public ObservableCollection<ProviderOption> AvailableProviders { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAddDetails))]
    [NotifyCanExecuteChangedFor(nameof(ConnectProviderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddProviderCommand))]
    private ProviderOption? _selectedProviderOption;

    /// <summary>The connection "dependency" for the selected provider — its API key.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectProviderCommand))]
    private string _newApiKey = "";

    // Editing the key after a successful Connect invalidates it — require a fresh Connect before Add.
    partial void OnNewApiKeyChanged(string value) => ConnectSucceeded = false;

    /// <summary>The key field's placeholder, hinting the selected provider's key format.</summary>
    public string ApiKeyPlaceholder => SelectedProviderOption?.Provider switch
    {
        AiProvider.OpenAI => "sk-…",
        AiProvider.Gemini => "AIza…",
        AiProvider.Anthropic => "sk-ant-…",
        AiProvider.DeepSeek => "sk-…",
        AiProvider.Nvidia => "nvapi-…",
        AiProvider.Mistral => "API key…",
        _ => "API key"
    };

    [ObservableProperty]
    private ProviderBilling _newBilling = ProviderBilling.Budget;

    public bool IsNewBudget
    {
        get => NewBilling == ProviderBilling.Budget;
        set { if (value) NewBilling = ProviderBilling.Budget; }
    }

    public bool IsNewSubscription
    {
        get => NewBilling == ProviderBilling.Subscription;
        set { if (value) NewBilling = ProviderBilling.Subscription; }
    }

    partial void OnNewBillingChanged(ProviderBilling value)
    {
        OnPropertyChanged(nameof(IsNewBudget));
        OnPropertyChanged(nameof(IsNewSubscription));
    }

    /// <summary>Dollar budget typed by the user (only used when <see cref="IsNewBudget"/>).</summary>
    [ObservableProperty]
    private string _newBudgetText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectProviderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddProviderCommand))]
    private bool _isConnecting;

    /// <summary>True once a successful Connect has validated the key — gates the Add button.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddProviderCommand))]
    private bool _connectSucceeded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddStatus))]
    private string _addStatus = "";

    [ObservableProperty]
    private string _addStatusColor = OkColor;

    public bool HasAddStatus => !string.IsNullOrEmpty(AddStatus);

    /// <summary>Show the key/billing/buttons only once a provider is selected in the dropdown.</summary>
    public bool ShowAddDetails => SelectedProviderOption is not null;

    partial void OnSelectedProviderOptionChanged(ProviderOption? value)
    {
        // Switching the target provider invalidates any prior Connect; prefill from an existing account.
        ConnectSucceeded = false;
        AddStatus = "";
        OnPropertyChanged(nameof(ApiKeyPlaceholder));

        if (value is null)
            return;
        NewApiKey = GetKey(value.Provider);
        var existing = _settings.Current.ActiveProviders.FirstOrDefault(p => p.Provider == value.Provider);
        NewBilling = existing?.Billing ?? ProviderBilling.Budget;
        NewBudgetText = existing is { Billing: ProviderBilling.Budget, BudgetUsd: > 0 }
            ? existing.BudgetUsd.ToString("0.##", CultureInfo.InvariantCulture)
            : "";
    }

    // ---- Active Providers ----------------------------------------------------------------------

    public ObservableCollection<ActiveProviderViewModel> ActiveProviders { get; } = new();

    /// <summary>True when at least one provider has been added (toggles the empty-state hint).</summary>
    public bool HasActiveProviders => ActiveProviders.Count > 0;

    /// <summary>Load active providers (migrating legacy keys) and fetch each one's browse model list.</summary>
    public async Task InitializeAsync()
    {
        MigrateLegacyKeys();
        LoadActiveProviders();
        await RefreshModelsAsync().ConfigureAwait(true);
    }

    private bool CanConnect =>
        !IsConnecting && SelectedProviderOption is not null && !string.IsNullOrWhiteSpace(NewApiKey);

    /// <summary>Persist the typed key for the selected provider, then probe it; success enables Add.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectProvider()
    {
        var opt = SelectedProviderOption;
        if (opt is null)
            return;

        SetKey(opt.Provider, NewApiKey.Trim());
        _settings.Save();

        IsConnecting = true;
        ConnectSucceeded = false;
        AddStatusColor = BusyColor;
        AddStatus = $"Connecting to {opt.Name}…";
        try
        {
            var ok = await _router.For(opt.Provider).IsConfiguredAndReachableAsync().ConfigureAwait(true);
            if (ok)
            {
                ConnectSucceeded = true;
                AddStatusColor = OkColor;
                AddStatus = $"Connected. Click Add to activate {opt.Name}.";
            }
            else
            {
                AddStatusColor = ErrColor;
                AddStatus = "Could not connect — check the API key.";
            }
        }
        catch (Exception ex)
        {
            AddStatusColor = ErrColor;
            AddStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanAdd => ConnectSucceeded && SelectedProviderOption is not null && !IsConnecting;

    /// <summary>Add (or update) the selected provider as an active provider with the chosen billing.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddProvider()
    {
        var opt = SelectedProviderOption;
        if (opt is null)
            return;

        var list = _settings.Current.ActiveProviders;
        var account = list.FirstOrDefault(p => p.Provider == opt.Provider);
        if (account is null)
        {
            account = new ProviderAccount { Provider = opt.Provider };
            list.Add(account);
        }
        account.Billing = NewBilling;
        account.BudgetUsd = NewBilling == ProviderBilling.Budget ? ParseBudget(NewBudgetText) : 0m;
        _settings.Save();

        LoadActiveProviders();
        await RefreshModelsAsync().ConfigureAwait(true);
        ResetAddForm();
        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remove an active provider: drop its account and clear its API key.</summary>
    [RelayCommand]
    private void RemoveProvider(ActiveProviderViewModel? row)
    {
        if (row is null)
            return;
        _settings.Current.ActiveProviders.RemoveAll(p => p.Provider == row.Provider);
        SetKey(row.Provider, ""); // so the provider drops out of the model picker
        _settings.Save();
        ActiveProviders.Remove(row);
        OnPropertyChanged(nameof(HasActiveProviders));

        // If the removed provider was the one selected in the Add form, reflect that the key is gone.
        if (SelectedProviderOption?.Provider == row.Provider)
        {
            NewApiKey = "";
            ConnectSucceeded = false;
        }
        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- helpers (internal for unit tests) -----------------------------------------------------

    /// <summary>Parse a user-typed dollar budget tolerantly (strips a leading "$"); 0 when blank/invalid.</summary>
    internal static decimal ParseBudget(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0m;
        var cleaned = text.Trim().TrimStart('$').Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0m
            ? v
            : 0m;
    }

    /// <summary>A cloud key set but with no account yet (e.g. configured before this feature) becomes a Subscription account.</summary>
    private void MigrateLegacyKeys()
    {
        var list = _settings.Current.ActiveProviders;
        var changed = false;
        foreach (var p in AiProviderExtensions.CloudProviders)
        {
            if (!string.IsNullOrWhiteSpace(GetKey(p)) && list.All(a => a.Provider != p))
            {
                list.Add(new ProviderAccount { Provider = p, Billing = ProviderBilling.Subscription });
                changed = true;
            }
        }
        if (changed)
            _settings.Save();
    }

    private void LoadActiveProviders()
    {
        ActiveProviders.Clear();
        foreach (var account in _settings.Current.ActiveProviders)
            ActiveProviders.Add(new ActiveProviderViewModel(account));
        OnPropertyChanged(nameof(HasActiveProviders));
    }

    private async Task RefreshModelsAsync()
    {
        foreach (var row in ActiveProviders)
        {
            try
            {
                var models = await _router.For(row.Provider).ListModelsAsync().ConfigureAwait(true);
                row.Models.Clear();
                foreach (var m in models)
                    row.Models.Add(m);
                row.SelectedModel = row.Models.FirstOrDefault();
            }
            catch
            {
                // best-effort: a provider that fails to list just shows an empty browse dropdown
            }
            row.NotifyModelsChanged();
        }
    }

    private void ResetAddForm()
    {
        SelectedProviderOption = null;
        NewApiKey = "";
        NewBudgetText = "";
        NewBilling = ProviderBilling.Budget;
        ConnectSucceeded = false;
        AddStatus = "";
    }

    private string GetKey(AiProvider provider)
    {
        var s = _settings.Current;
        return provider switch
        {
            AiProvider.OpenAI => s.OpenAiApiKey,
            AiProvider.Gemini => s.GeminiApiKey,
            AiProvider.Anthropic => s.AnthropicApiKey,
            AiProvider.DeepSeek => s.DeepSeekApiKey,
            AiProvider.Nvidia => s.NvidiaApiKey,
            AiProvider.Mistral => s.MistralApiKey,
            _ => ""
        };
    }

    private void SetKey(AiProvider provider, string key)
    {
        var s = _settings.Current;
        switch (provider)
        {
            case AiProvider.OpenAI: s.OpenAiApiKey = key; break;
            case AiProvider.Gemini: s.GeminiApiKey = key; break;
            case AiProvider.Anthropic: s.AnthropicApiKey = key; break;
            case AiProvider.DeepSeek: s.DeepSeekApiKey = key; break;
            case AiProvider.Nvidia: s.NvidiaApiKey = key; break;
            case AiProvider.Mistral: s.MistralApiKey = key; break;
        }
    }
}
