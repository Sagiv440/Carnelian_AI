using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AI_Interface.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AI_Interface.Views;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ModelConfigRequested -= OnModelConfigRequested;
            _vm.VoiceBrowserRequested -= OnVoiceBrowserRequested;
        }

        _vm = DataContext as SettingsViewModel;

        if (_vm is not null)
        {
            _vm.ModelConfigRequested += OnModelConfigRequested;
            _vm.VoiceBrowserRequested += OnVoiceBrowserRequested;
        }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Probe the configured server so the Model Config button reflects connectivity on open.
        if (DataContext is SettingsViewModel vm)
        {
            await vm.RefreshConnectionAsync();
            // Populate the Agents → Default model picker (best-effort).
            await vm.AgentsPanel.LoadModelsAsync();
        }
    }

    private async void OnModelConfigRequested(object? sender, EventArgs e)
    {
        var window = new ModelConfigWindow
        {
            DataContext = App.Services.GetRequiredService<ModelConfigViewModel>()
        };
        await window.ShowDialog(this);
    }

    private async void OnVoiceBrowserRequested(object? sender, EventArgs e)
    {
        var window = new VoiceBrowserWindow
        {
            DataContext = App.Services.GetRequiredService<VoiceBrowserViewModel>()
        };
        await window.ShowDialog(this);
    }

    private async void OnBrowsePiperExe(object? sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync("Select the Piper executable", null);
        if (path is not null && _vm is not null)
            _vm.PiperExecutablePath = path;
    }

    private async void OnBrowsePiperModel(object? sender, RoutedEventArgs e)
    {
        var onnx = new FilePickerFileType("Piper voice (*.onnx)") { Patterns = new[] { "*.onnx" } };
        var path = await PickFileAsync("Select a Piper voice model", onnx);
        if (path is not null && _vm is not null)
            _vm.PiperModelPath = path;
    }

    /// <summary>Open a single-file picker and return the chosen local path, or null if cancelled.</summary>
    private async Task<string?> PickFileAsync(string title, FilePickerFileType? fileType)
    {
        var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false };
        if (fileType is not null)
            options.FileTypeFilter = new List<FilePickerFileType> { fileType };

        var files = await StorageProvider.OpenFilePickerAsync(options);
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
