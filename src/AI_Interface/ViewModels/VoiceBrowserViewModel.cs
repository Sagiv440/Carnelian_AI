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
/// Backs the Voice browser: lists the Piper voice catalog, filtered by a language dropdown and an
/// optional "downloaded only" toggle, with inline download/remove. Mirrors the Model Config window.
/// The app then picks a downloaded voice automatically based on each reply's language.
/// </summary>
public sealed partial class VoiceBrowserViewModel : ViewModelBase
{
    private readonly IPiperVoiceCatalog _catalog;

    private List<PiperVoiceInfo> _all = new();
    private bool _populating;

    /// <summary>Voices currently shown (after language + downloaded filtering).</summary>
    public ObservableCollection<PiperVoiceInfo> Voices { get; } = new();

    /// <summary>Languages for the dropdown ("All languages" first).</summary>
    public ObservableCollection<LanguageOption> Languages { get; } = new();

    [ObservableProperty] private LanguageOption? _selectedLanguage;
    [ObservableProperty] private bool _downloadedOnly;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadVoiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveVoiceCommand))]
    private bool _isBusy;

    public VoiceBrowserViewModel(IPiperVoiceCatalog catalog) => _catalog = catalog;

    // Design-time constructor for the previewer.
    public VoiceBrowserViewModel() : this(new DesignPiperVoiceCatalog())
    {
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value) => ApplyFilter();
    partial void OnDownloadedOnlyChanged(bool value) => ApplyFilter();

    /// <summary>Fetch the catalog and populate the language list. Called when the window opens.</summary>
    [RelayCommand]
    private async Task Load()
    {
        IsLoading = true;
        StatusMessage = "Loading voice catalog…";
        try
        {
            var list = await _catalog.ListAvailableAsync(CancellationToken.None);
            _all = list.ToList();
            BuildLanguages();
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            _all = new List<PiperVoiceInfo>();
            StatusMessage = $"Couldn't load the voice catalog: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            ApplyFilter();
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DownloadVoice(PiperVoiceInfo? voice)
    {
        if (voice is null)
            return;

        IsBusy = true;
        var progress = new Progress<string>(s => StatusMessage = s);
        try
        {
            await _catalog.DownloadAsync(voice, progress, CancellationToken.None);
            StatusMessage = $"Downloaded {voice.LanguageName} · {voice.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshDownloadedFlags();
            ApplyFilter();
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private void RemoveVoice(PiperVoiceInfo? voice)
    {
        if (voice is null)
            return;

        _catalog.Delete(voice);
        StatusMessage = $"Removed {voice.LanguageName} · {voice.DisplayName}.";
        RefreshDownloadedFlags();
        ApplyFilter();
    }

    private bool CanModify => !IsBusy;

    private void BuildLanguages()
    {
        var langs = _all
            .GroupBy(v => v.LanguageFamily)
            .Select(g => new LanguageOption(g.Key, LanguageLabel(g.First())))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _populating = true;
        Languages.Clear();
        Languages.Add(new LanguageOption("", "All languages"));
        foreach (var l in langs)
            Languages.Add(l);
        SelectedLanguage = Languages.Count > 0 ? Languages[0] : null;
        _populating = false;
    }

    /// <summary>Drop the "(Country)" suffix for the dropdown, e.g. "English (United States)" → "English".</summary>
    private static string LanguageLabel(PiperVoiceInfo v)
    {
        var name = v.LanguageName;
        var paren = name.IndexOf('(');
        return (paren > 0 ? name[..paren] : name).Trim();
    }

    private void ApplyFilter()
    {
        if (_populating)
            return;

        IEnumerable<PiperVoiceInfo> query = _all;

        var family = SelectedLanguage?.Family ?? "";
        if (!string.IsNullOrEmpty(family))
            query = query.Where(v => v.LanguageFamily == family);

        if (DownloadedOnly)
            query = query.Where(v => v.IsDownloaded);

        Voices.Clear();
        foreach (var v in query)
            Voices.Add(v);

        IsEmpty = Voices.Count == 0;
    }

    private void RefreshDownloadedFlags()
    {
        foreach (var v in _all)
            v.IsDownloaded = _catalog.IsDownloaded(v);
    }
}
