using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
            _vm.ModelConfigRequested -= OnModelConfigRequested;

        _vm = DataContext as SettingsViewModel;

        if (_vm is not null)
            _vm.ModelConfigRequested += OnModelConfigRequested;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Probe the configured server so the Model Config button reflects connectivity on open.
        if (DataContext is SettingsViewModel vm)
            await vm.RefreshConnectionAsync();
    }

    private async void OnModelConfigRequested(object? sender, EventArgs e)
    {
        var window = new ModelConfigWindow
        {
            DataContext = App.Services.GetRequiredService<ModelConfigViewModel>()
        };
        await window.ShowDialog(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
