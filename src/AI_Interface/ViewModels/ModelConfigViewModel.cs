using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Interface.ViewModels;

/// <summary>A single result from the Ollama library search, with all metadata and resolved installed state.</summary>
public sealed partial class SearchResultEntry : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    // Metadata from ollama.com
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Pulls { get; init; } = "";
    public string Tags { get; init; } = "";
    public string Updated { get; init; } = "";
    public string SizesDisplay { get; init; } = "";         // e.g. "8B · 70B · 405B"
    public string CapabilitiesDisplay { get; init; } = "";  // e.g. "tools · vision"

    // Installed state (changes after download / remove)
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isInstalled;
    /// <summary>The exact installed tag, e.g. "llama3:latest". Empty when not installed.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _installedTag = "";
    /// <summary>Human-readable local disk size, e.g. "4.3 GB". Empty when not installed.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _localSizeDisplay = "";
}

/// <summary>
/// Backs the Model Config window: scans the machine, then ranks the catalog of Ollama models by how
/// well each fits the detected hardware and the chosen category / quant / context. Models can be
/// downloaded or removed inline; the "Downloaded" category shows only installed models. The list
/// re-ranks live whenever a preference changes.
/// </summary>
public sealed partial class ModelConfigViewModel : ViewModelBase
{
    private readonly IHardwareService _hardware;
    private readonly IOllamaClient _ollama;

    private HardwareInfo? _hw;
    private ISet<string> _installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, long> _installedSizes = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Quants { get; } = ModelCatalog.Quants;
    public IReadOnlyList<ContextOption> Contexts { get; } = ModelCatalog.Contexts;

    public IReadOnlyList<CategoryOption> Categories { get; } = new[]
    {
        new CategoryOption("Standard",  ModelUseCase.Standard,   false),
        new CategoryOption("Coding",    ModelUseCase.Coding,     false),
        new CategoryOption("Chat",      ModelUseCase.Chat,       false),
        new CategoryOption("Vision",    ModelUseCase.Vision,     false),
        new CategoryOption("Reasoning", ModelUseCase.Reasoning,  false),
    };

    /// <summary>Filter the list to models that support a given API/capability ("Any" = no filter).</summary>
    public IReadOnlyList<ApiOption> ApiFilters { get; } = new[]
    {
        new ApiOption("Any", ModelCapabilities.None),
        new ApiOption("🛠 Tools", ModelCapabilities.Tools),
        new ApiOption("👁 Vision", ModelCapabilities.Vision),
        new ApiOption("🧠 Reasoning", ModelCapabilities.Reasoning),
        new ApiOption("💬 Text", ModelCapabilities.Text)
    };

    /// <summary>Ranked recommendations, best match first.</summary>
    public ObservableCollection<ModelRecommendation> Models { get; } = new();

    /// <summary>Results from an Ollama library search. Visible when <see cref="IsSearchActive"/>.</summary>
    public ObservableCollection<SearchResultEntry> SearchResults { get; } = new();

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasScanned;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusMessage = "";

    // --- search ---
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private bool _isSearching;

    /// <summary>When true the search-results list is shown instead of the curated recommendations.</summary>
    [ObservableProperty] private bool _isSearchActive;

    /// <summary>When true, search result rows show full details (description, sizes, pulls, etc.).</summary>
    [ObservableProperty] private bool _showDetails = true;

    [ObservableProperty]
    private string _hardwareSummary = "Click “Scan hardware” to detect your CPU, RAM and GPU.";

    // --- preferences ---

    [ObservableProperty] private CategoryOption _selectedCategory;
    [ObservableProperty] private ApiOption _selectedApi;
    [ObservableProperty] private string _selectedQuant = "Q4_K_M";
    [ObservableProperty] private ContextOption _selectedContext;

    /// <summary>When on, the list is filtered to only models already downloaded locally.</summary>
    [ObservableProperty] private bool _downloadedOnly;

    public ModelConfigViewModel(IHardwareService hardware, IOllamaClient ollama)
    {
        _hardware = hardware;
        _ollama = ollama;
        _selectedCategory = Categories[0];
        _selectedApi = ApiFilters[0];   // "Any"
        _selectedContext = Contexts[1]; // 8K
        Recompute();                    // capability ordering before the first scan
    }

    // Design-time constructor for the previewer.
    public ModelConfigViewModel() : this(new DesignHardwareService(), new DesignOllamaClient())
    {
    }

    partial void OnSelectedCategoryChanged(CategoryOption value) => Recompute();
    partial void OnSelectedApiChanged(ApiOption value) => Recompute();
    partial void OnSelectedQuantChanged(string value) => Recompute();
    partial void OnSelectedContextChanged(ContextOption value) => Recompute();
    partial void OnDownloadedOnlyChanged(bool value) => Recompute();
    partial void OnIsScanningChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        DownloadModelCommand.NotifyCanExecuteChanged();
        RemoveModelCommand.NotifyCanExecuteChanged();
        DownloadSearchResultCommand.NotifyCanExecuteChanged();
        RemoveSearchResultCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task Scan()
    {
        IsScanning = true;
        HardwareSummary = "Scanning hardware…";
        try
        {
            _hw = await _hardware.ScanAsync();
            HardwareSummary = _hw.Summary;
            HasScanned = true;
        }
        catch (Exception ex)
        {
            HardwareSummary = $"Hardware scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }

        await RefreshInstalledAsync(); // also re-ranks
    }

    private bool CanScan => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DownloadModel(ModelRecommendation? model)
    {
        if (model is null)
            return;

        IsBusy = true;
        var progress = new Progress<string>(s => StatusMessage = $"Downloading {model.PullName} — {s}");
        try
        {
            StatusMessage = $"Downloading {model.PullName}…";
            await _ollama.PullModelAsync(model.PullName, progress, CancellationToken.None);
            StatusMessage = $"Downloaded {model.PullName}.";
            await RefreshInstalledAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task RemoveModel(ModelRecommendation? model)
    {
        if (model is null)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = $"Removing {model.PullName}…";
            await _ollama.DeleteModelAsync(model.PullName, CancellationToken.None);
            StatusMessage = $"Removed {model.PullName}.";
            await RefreshInstalledAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanModify => !IsBusy;

    [RelayCommand]
    private async Task Search()
    {
        var q = SearchQuery.Trim();
        if (string.IsNullOrEmpty(q))
            return;

        IsSearching = true;
        IsSearchActive = true;
        SearchResults.Clear();
        StatusMessage = $"Searching \"{q}\"…";
        IsEmpty = false;

        try
        {
            var raw = await _ollama.SearchAsync(q, CancellationToken.None);
            SearchResults.Clear();
            foreach (var r in raw)
            {
                var tag = FindInstalledTag(r.Name);
                var sz  = tag is not null && _installedSizes.TryGetValue(tag, out var s) ? s : 0L;
                SearchResults.Add(new SearchResultEntry
                {
                    Name               = r.Name,
                    Description        = r.Description,
                    Pulls              = r.Pulls,
                    Tags               = r.Tags,
                    Updated            = r.Updated,
                    SizesDisplay       = r.Sizes.Count > 0
                        ? string.Join(" · ", r.Sizes)
                        : "",
                    CapabilitiesDisplay = r.Capabilities.Count > 0
                        ? string.Join(" · ", r.Capabilities)
                        : "",
                    IsInstalled        = tag is not null,
                    InstalledTag       = tag ?? "",
                    LocalSizeDisplay   = sz > 0 ? FormatBytes(sz) : ""
                });
            }
            StatusMessage = SearchResults.Count == 0 ? $"No results for \"{q}\"." : "";
            IsEmpty = SearchResults.Count == 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = "";
        IsSearchActive = false;
        SearchResults.Clear();
        StatusMessage = "";
        Recompute();
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DownloadSearchResult(SearchResultEntry? entry)
    {
        if (entry is null)
            return;

        IsBusy = true;
        var progress = new Progress<string>(s => StatusMessage = $"Downloading {entry.Name} — {s}");
        try
        {
            StatusMessage = $"Downloading {entry.Name}…";
            await _ollama.PullModelAsync(entry.Name, progress, CancellationToken.None);
            StatusMessage = $"Downloaded {entry.Name}.";
            await RefreshInstalledAsync();
            RefreshSearchResultTags();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task RemoveSearchResult(SearchResultEntry? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.InstalledTag))
            return;

        IsBusy = true;
        try
        {
            StatusMessage = $"Removing {entry.InstalledTag}…";
            await _ollama.DeleteModelAsync(entry.InstalledTag, CancellationToken.None);
            StatusMessage = $"Removed {entry.InstalledTag}.";
            await RefreshInstalledAsync();
            RefreshSearchResultTags();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? FindInstalledTag(string name)
    {
        if (_installed.Contains(name))
            return name;
        return _installed.FirstOrDefault(i => i.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesInstalled(string name) => FindInstalledTag(name) is not null;

    private void RefreshSearchResultTags()
    {
        foreach (var r in SearchResults)
        {
            var tag = FindInstalledTag(r.Name);
            r.IsInstalled        = tag is not null;
            r.InstalledTag       = tag ?? "";
            r.LocalSizeDisplay   = tag is not null && _installedSizes.TryGetValue(tag, out var sz)
                ? FormatBytes(sz) : "";
        }
    }

    private async Task RefreshInstalledAsync()
    {
        try
        {
            var infos = await _ollama.ListModelsWithInfoAsync();
            _installed      = new HashSet<string>(infos.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            _installedSizes = infos.ToDictionary(m => m.Name, m => m.Size, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _installed      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _installedSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
        Recompute();
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1_073_741_824
            ? $"{bytes / 1_073_741_824.0:0.0} GB"
            : $"{bytes / 1_048_576.0:0} MB";

    private void Recompute()
    {
        var useCase = SelectedCategory?.UseCase ?? ModelUseCase.Standard;

        var recs = ModelCatalog.Recommend(
            _hw, useCase, SelectedQuant, SelectedContext?.ValueK ?? 8, _installed, DownloadedOnly)
            .ToList();

        // When showing only downloaded models, also include installed models that aren't
        // in the curated catalog (e.g. models pulled directly via `ollama pull`).
        if (DownloadedOnly)
        {
            var catalogNames = new HashSet<string>(
                recs.Select(r => r.PullName), StringComparer.OrdinalIgnoreCase);

            foreach (var tag in _installed.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                if (catalogNames.Any(cn =>
                        string.Equals(tag, cn, StringComparison.OrdinalIgnoreCase) ||
                        tag.StartsWith(cn, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var sizeGb = _installedSizes.TryGetValue(tag, out var bytes)
                    ? bytes / 1_073_741_824.0
                    : 0.0;

                string symbol, note;
                if (_hw is not null && sizeGb > 0)
                {
                    var budget = _hw.BudgetGb;
                    var ratio  = (sizeGb + 0.8) / budget;
                    if (ratio <= 0.75)
                    {
                        symbol = "✅";
                        note   = $"Runs comfortably — needs ~{sizeGb:0.0} GB of your ~{budget:0.0} GB.";
                    }
                    else if (ratio <= 1.0)
                    {
                        symbol = "🟡";
                        note   = $"Tight fit — needs ~{sizeGb:0.0} GB of your ~{budget:0.0} GB.";
                    }
                    else
                    {
                        symbol = "🔴";
                        note   = $"Likely too large — needs ~{sizeGb:0.0} GB but you have ~{budget:0.0} GB.";
                    }
                }
                else
                {
                    symbol = "•";
                    note   = sizeGb > 0
                        ? $"Not in catalog — actual size {sizeGb:0.0} GB. Scan hardware to check fit."
                        : "Not in catalog — scan hardware to check fit.";
                }

                recs.Add(new ModelRecommendation(
                    symbol, tag, tag, "", Math.Round(sizeGb, 1), ModelUseCase.Standard,
                    note, Score: -1, IsInstalled: true, MaxContextK: 0,
                    Capabilities: ModelCapabilities.Text));
            }
        }

        // Optional API/capability filter (keeps the ranked order from Recommend).
        var required = SelectedApi?.Required ?? ModelCapabilities.None;
        IEnumerable<ModelRecommendation> filtered = required == ModelCapabilities.None
            ? recs
            : recs.Where(r => r.Capabilities.HasFlag(required));

        Models.Clear();
        foreach (var r in filtered)
            Models.Add(r);

        IsEmpty = Models.Count == 0;
    }
}
