using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AI_Interface.ViewModels;

namespace AI_Interface.Views;

public partial class VoiceBrowserWindow : Window
{
    public VoiceBrowserWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Fetch the catalog as soon as the window opens.
        if (DataContext is VoiceBrowserViewModel vm && vm.LoadCommand.CanExecute(null))
            vm.LoadCommand.Execute(null);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
