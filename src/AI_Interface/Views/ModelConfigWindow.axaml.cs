using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AI_Interface.ViewModels;

namespace AI_Interface.Views;

public partial class ModelConfigWindow : Window
{
    public ModelConfigWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Auto-scan on open so the list is ranked for the real machine immediately.
        if (DataContext is ModelConfigViewModel vm && vm.ScanCommand.CanExecute(null))
            vm.ScanCommand.Execute(null);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
