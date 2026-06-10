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

    public IReadOnlyList<string> Quants { get; } = ModelCatalog.Quants;
    public IReadOnlyList<ContextOption> Contexts { get; } = ModelCatalog.Contexts;

    public IReadOnlyList<CategoryOption> Categories { get; } = new[]
    {
        new CategoryOption("Standard", ModelUseCase.Standard, false),
        new CategoryOption("Coding", ModelUseCase.Coding, false),
        new CategoryOption("Chat", ModelUseCase.Chat, false),
        new CategoryOption("Vision", ModelUseCase.Vision, false)
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

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasScanned;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusMessage = "";

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

    private async Task RefreshInstalledAsync()
    {
        try
        {
            var models = await _ollama.ListModelsAsync();
            _installed = new HashSet<string>(models, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        Recompute();
    }

    private void Recompute()
    {
        var useCase = SelectedCategory?.UseCase ?? ModelUseCase.Standard;

        var recs = ModelCatalog.Recommend(
            _hw, useCase, SelectedQuant, SelectedContext?.ValueK ?? 8, _installed, DownloadedOnly);

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
