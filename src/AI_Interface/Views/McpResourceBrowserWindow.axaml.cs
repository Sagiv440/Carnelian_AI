using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AI_Interface.Models;
using AI_Interface.ViewModels;

namespace AI_Interface.Views;

public partial class McpResourceBrowserWindow : Window
{
    private McpResourceBrowserViewModel? _vm;

    public McpResourceBrowserWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Discover resources as soon as the dialog opens.
        if (DataContext is McpResourceBrowserViewModel vm && vm.LoadCommand.CanExecute(null))
            vm.LoadCommand.Execute(null);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
            _vm.AttachCompleted -= OnAttachCompleted;
        _vm = DataContext as McpResourceBrowserViewModel;
        if (_vm is not null)
            _vm.AttachCompleted += OnAttachCompleted;
    }

    // The VM finished fetching the ticked resources → close the dialog, returning them to the caller.
    private void OnAttachCompleted(object? sender, IReadOnlyList<McpAttachedResource> result) => Close(result);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
