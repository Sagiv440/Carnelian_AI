using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AI_Interface.Models;
using AI_Interface.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AI_Interface.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        InputBox.KeyDown += OnInputKeyDown;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.InitializeAsync();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ScrollToEndRequested -= OnScrollToEndRequested;
            _vm.SettingsRequested -= OnSettingsRequested;
            _vm.AttachFilesRequested -= OnAttachFilesRequested;
            _vm.ProjectRequested -= OnProjectRequested;
            _vm.ToolApprovalRequested -= OnToolApprovalRequested;
        }

        _vm = DataContext as MainWindowViewModel;

        if (_vm is not null)
        {
            _vm.ScrollToEndRequested += OnScrollToEndRequested;
            _vm.SettingsRequested += OnSettingsRequested;
            _vm.AttachFilesRequested += OnAttachFilesRequested;
            _vm.ProjectRequested += OnProjectRequested;
            _vm.ToolApprovalRequested += OnToolApprovalRequested;
        }
    }

    private async void OnProjectRequested(object? sender, EventArgs e)
    {
        var dialog = new ProjectWindow
        {
            DataContext = App.Services.GetRequiredService<ProjectViewModel>()
        };
        var project = await dialog.ShowDialog<Project?>(this);
        if (project is not null && _vm is not null)
            await _vm.ActivateProjectAsync(project);
    }

    private async void OnToolApprovalRequested(object? sender, ToolApprovalEventArgs e)
    {
        var dialog = new ToolApprovalWindow(e.Request);
        var approved = await dialog.ShowDialog<bool>(this);
        e.Completion.TrySetResult(approved);
    }

    private async void OnAttachFilesRequested(object? sender, AttachmentKind kind)
    {
        // MainWindow is itself a TopLevel, so its StorageProvider drives the native file dialog.
        var filter = kind == AttachmentKind.Photo
            ? new FilePickerFileType("Images")
            {
                Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp" }
            }
            : new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } };

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = kind == AttachmentKind.Photo ? "Attach photos" : "Attach PDFs",
            AllowMultiple = true,
            FileTypeFilter = new[] { filter }
        });

        var picked = new List<Attachment>();
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
                continue;
            picked.Add(new Attachment { Path = path, FileName = Path.GetFileName(path), Kind = kind });
        }

        if (picked.Count > 0)
            _vm?.AddAttachments(picked);
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        var settings = new SettingsWindow
        {
            DataContext = App.Services.GetRequiredService<SettingsViewModel>()
        };
        await settings.ShowDialog(this);
    }

    private async void OnCopyMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageViewModel message } && Clipboard is not null)
            await Clipboard.SetTextAsync(message.Text);
    }

    private void OnRerunMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageViewModel message } && _vm is not null)
            _vm.RerunPromptCommand.Execute(message);
    }

    private void OnScrollToEndRequested(object? sender, EventArgs e)
    {
        // Defer so layout reflects the just-added/extended message before we scroll.
        Dispatcher.UIThread.Post(
            () => TranscriptScroll.Offset = TranscriptScroll.Offset.WithY(TranscriptScroll.Extent.Height),
            DispatcherPriority.Background);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter sends; Shift+Enter inserts a newline (default TextBox behaviour).
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        if (DataContext is MainWindowViewModel vm && vm.SendCommand.CanExecute(null))
            vm.SendCommand.Execute(null);

        e.Handled = true;
    }
}
