using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AI_Interface.ViewModels;

namespace AI_Interface.Views;

public partial class ProjectWindow : Window
{
    public ProjectWindow()
    {
        InitializeComponent();
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Choose the project directory");
        if (path is not null && DataContext is ProjectViewModel vm)
            vm.Directory = path;
    }

    private async void OnBrowseOpen(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Open a project folder");
        if (path is not null && DataContext is ProjectViewModel vm)
            vm.OpenDirectory = path;
    }

    private async System.Threading.Tasks.Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return null;
        var path = folders[0].TryGetLocalPath();
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectViewModel vm || !vm.CanCreate)
            return;

        var project = vm.Build();
        try
        {
            // A typed-in directory that doesn't exist yet is created so the agent has somewhere to work.
            Directory.CreateDirectory(project.Directory);
        }
        catch (Exception)
        {
            // Leave validation to the agent's first tool call rather than blocking creation here.
        }

        Close(project);
    }

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectViewModel vm && vm.CanOpen)
            Close(vm.BuildOpen()); // the folder already exists — no need to create it
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
