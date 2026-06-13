using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AI_Interface.Models;
using AI_Interface.Services;
using AI_Interface.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AI_Interface.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private bool _startedUp; // guards the one-time init + startup launcher (Loaded can re-fire on re-attach)

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        // Tunnel route: we must see Enter before TextBox's own bubble-phase handler inserts a
        // newline (AcceptsReturn="True") and marks the event handled, which would skip us.
        InputBox.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // The startup launcher is now an in-window overlay (MainWindowViewModel.ShowStartupLauncher), so
        // OnLoaded just runs the one-time model init. Loaded can re-fire on re-attach — guard against it.
        if (_startedUp || DataContext is not MainWindowViewModel vm)
            return;
        _startedUp = true;
        await vm.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm?.Dispose(); // release the project file watcher / debounce timer if still running
        // Best-effort: tear down any MCP server child processes we launched this session.
        try { _ = App.Services.GetService<IMcpService>()?.DisconnectAllAsync(); } catch { /* shutting down */ }
        base.OnClosed(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ScrollToEndRequested -= OnScrollToEndRequested;
            _vm.SettingsRequested -= OnSettingsRequested;
            _vm.AttachFilesRequested -= OnAttachFilesRequested;
            _vm.McpResourcesRequested -= OnMcpResourcesRequested;
            _vm.ProjectRequested -= OnProjectRequested;
            _vm.ToolApprovalRequested -= OnToolApprovalRequested;
            _vm.PhaseGateRequested -= OnPhaseGateRequested;
            _vm.ClarificationRequested -= OnClarificationRequested;
            _vm.DeleteFileRequested -= OnDeleteFileRequested;
            _vm.ModelConfigRequested -= OnModelConfigRequested;
            _vm.VoiceBrowserRequested -= OnVoiceBrowserRequested;
            _vm.InstallOllamaConfirmationRequested -= OnInstallOllamaConfirmationRequested;
            _vm.InstallPiperConfirmationRequested -= OnInstallPiperConfirmationRequested;
        }

        _vm = DataContext as MainWindowViewModel;

        if (_vm is not null)
        {
            _vm.ScrollToEndRequested += OnScrollToEndRequested;
            _vm.SettingsRequested += OnSettingsRequested;
            _vm.AttachFilesRequested += OnAttachFilesRequested;
            _vm.McpResourcesRequested += OnMcpResourcesRequested;
            _vm.ProjectRequested += OnProjectRequested;
            _vm.ToolApprovalRequested += OnToolApprovalRequested;
            _vm.PhaseGateRequested += OnPhaseGateRequested;
            _vm.ClarificationRequested += OnClarificationRequested;
            _vm.DeleteFileRequested += OnDeleteFileRequested;
            _vm.ModelConfigRequested += OnModelConfigRequested;
            _vm.VoiceBrowserRequested += OnVoiceBrowserRequested;
            _vm.InstallOllamaConfirmationRequested += OnInstallOllamaConfirmationRequested;
            _vm.InstallPiperConfirmationRequested += OnInstallPiperConfirmationRequested;
        }
    }

    /// <summary>Tools button clicked: refresh which entries are available before the flyout shows them.</summary>
    private void OnToolsOpening(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            _ = _vm.RefreshToolsAvailabilityAsync();
    }

    private async void OnModelConfigRequested(object? sender, EventArgs e)
    {
        var window = new ModelConfigWindow
        {
            DataContext = App.Services.GetRequiredService<ModelConfigViewModel>()
        };
        await window.ShowDialog(this);

        // A local model may have been downloaded/removed in there — reload the main window's picker.
        _vm?.RefreshCommand.Execute(null);
    }

    private async void OnVoiceBrowserRequested(object? sender, EventArgs e)
    {
        var window = new VoiceBrowserWindow
        {
            DataContext = App.Services.GetRequiredService<VoiceBrowserViewModel>()
        };
        await window.ShowDialog(this);

        // A voice may now be installed — refresh the composer's Auto-read toggle visibility.
        _vm?.RefreshVoiceAvailability();
    }

    private async void OnInstallOllamaConfirmationRequested(object? sender, TaskCompletionSource<bool> completion)
    {
        var dialog = new ConfirmWindow(
            "Install additional software?",
            "Additional software (the Ollama runtime) will be downloaded and " +
            "installed on your computer. Click Accept to continue, or Exit to cancel.");
        completion.TrySetResult(await dialog.ShowDialog<bool>(this));
    }

    private async void OnInstallPiperConfirmationRequested(object? sender, TaskCompletionSource<bool> completion)
    {
        var dialog = new ConfirmWindow(
            "Install additional software?",
            "Additional software (the Piper text-to-speech engine) will be downloaded and " +
            "installed on your computer. Click Accept to continue, or Exit to cancel.");
        completion.TrySetResult(await dialog.ShowDialog<bool>(this));
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

    private async void OnPhaseGateRequested(object? sender, PhaseGateEventArgs e)
    {
        var dialog = new ConfirmWindow(
            "Continue to the next phase?",
            $"Finished the “{e.Gate.CompletedPhase}” phase. Continue to “{e.Gate.NextPhase}”?");
        var cont = await dialog.ShowDialog<bool>(this);
        e.Completion.TrySetResult(cont);
    }

    private async void OnClarificationRequested(object? sender, ClarifyEventArgs e)
    {
        var dialog = new ClarifyWindow { DataContext = new ClarifyViewModel(e.Request) };
        var answer = await dialog.ShowDialog<string?>(this);
        e.Completion.TrySetResult(answer);
    }

    private async void OnDeleteFileRequested(object? sender, DeleteFileEventArgs e)
    {
        var dialog = new ConfirmWindow(
            "Delete file?",
            $"“{e.FileName}” will be permanently deleted from disk. This can't be undone.");
        e.Completion.TrySetResult(await dialog.ShowDialog<bool>(this));
    }

    private async void OnAttachFilesRequested(object? sender, AttachmentKind kind)
    {
        // MainWindow is itself a TopLevel, so its StorageProvider drives the native file dialog.
        FilePickerFileType[] filters = kind == AttachmentKind.Photo
            ? new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp" }
                }
            }
            : new[]
            {
                new FilePickerFileType("Documents & text")
                {
                    Patterns = new[]
                    {
                        "*.pdf", "*.docx", "*.doc", "*.rtf", "*.odt",
                        "*.txt", "*.md", "*.markdown", "*.csv", "*.tsv", "*.log",
                        "*.json", "*.xml", "*.yaml", "*.yml", "*.html", "*.htm",
                        "*.cs", "*.js", "*.ts", "*.py", "*.java", "*.c", "*.cpp", "*.h",
                        "*.go", "*.rs", "*.rb", "*.php", "*.sh", "*.sql", "*.css"
                    }
                },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            };

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = kind == AttachmentKind.Photo ? "Attach photos" : "Attach documents",
            AllowMultiple = true,
            FileTypeFilter = filters
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

    private async void OnMcpResourcesRequested(object? sender, EventArgs e)
    {
        var vm = App.Services.GetRequiredService<McpResourceBrowserViewModel>();
        vm.Initialize(_vm?.ActiveProjectDirectory);
        var dialog = new McpResourceBrowserWindow { DataContext = vm };
        var result = await dialog.ShowDialog<IReadOnlyList<McpAttachedResource>?>(this);
        if (result is { Count: > 0 })
            _vm?.AddMcpResources(result);
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        var settingsVm = App.Services.GetRequiredService<SettingsViewModel>();

        // The Connect button lives in Settings now, but reconnecting/reloading models is the main VM's job.
        void OnConnect(object? s, EventArgs args) => _vm?.RefreshCommand.Execute(null);
        settingsVm.ConnectRequested += OnConnect;

        // Scope the Agents panel + memory lists to the active project so its customs/facts appear.
        var projectDir = _vm?.ActiveProjectDirectory is { Length: > 0 } dir ? dir : null;
        settingsVm.AgentsPanel.Initialize(projectDir);
        settingsVm.McpPanel.Initialize(projectDir);
        settingsVm.InitializeMemory(projectDir);

        var settings = new SettingsWindow { DataContext = settingsVm };
        await settings.ShowDialog(this);

        // Reload the top-bar agent picker once, so persona edits / new / deleted agents are reflected.
        _vm?.LoadAgents();
        // Voice may have been installed/changed in Settings — refresh the composer's Auto-read toggle.
        _vm?.RefreshVoiceAvailability();
        // MCP servers may have changed — re-discover prompt slash-commands for the active project.
        if (_vm is not null)
            _ = _vm.RefreshMcpPromptCommandsAsync();
        settingsVm.ConnectRequested -= OnConnect;
    }

    private async void OnCopyMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageViewModel message } && Clipboard is not null)
            await Clipboard.SetTextAsync(message.Text);
    }

    // --- Project file tree: double-click opens a file; right-click context menu (see MainWindow.axaml) ---

    private void OnFileTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        // The first tap selects the row, so SelectedItem is the double-clicked node. Folders keep the
        // default expand/collapse behaviour; only files are opened with the OS default app.
        if ((sender as TreeView)?.SelectedItem is FileNode { IsDirectory: false } file)
            _vm?.OpenFileWithOsCommand.Execute(file);
    }

    private void OnOpenInFileExplorer(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileNode node)
            _vm?.OpenInFileExplorerCommand.Execute(node);
    }

    private void OnViewInFolder(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileNode node)
            _vm?.RevealInFolderCommand.Execute(node);
    }

    private void OnAttachFile(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileNode node)
            _vm?.AttachFromTreeCommand.Execute(node);
    }

    private void OnDeleteFile(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileNode node)
            _vm?.DeleteFileCommand.Execute(node);
    }

    private async void OnCopyCode(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageSegment segment } && Clipboard is not null)
            await Clipboard.SetTextAsync(segment.Text);
    }

    private void OnRerunMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageViewModel message } && _vm is not null)
            _vm.RerunPromptCommand.Execute(message);
    }

    private void OnSpeakMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageViewModel message } && _vm is not null)
            _vm.SpeakMessageCommand.Execute(message);
    }

    // A re-scroll is queued for the next settled layout pass (see OnScrollToEndRequested).
    private bool _scrollPending;

    private void OnScrollToEndRequested(object? sender, EventArgs e)
    {
        // The VM only raises this during an active turn (each streamed delta, activity/answer update,
        // and once in the turn's finally), i.e. every call means "follow the bottom" — so we always
        // scroll to the end here, which preserves the streaming auto-scroll behaviour.
        TranscriptScroll.ScrollToEnd();

        // …then re-scroll once the layout pass actually settles. Why both: the offset that pins the
        // bottom is computed against the ScrollViewer's *current* Extent/Viewport, and at the end of a
        // turn several layout changes land late and out of order — the final answer Segments rebuild, a
        // delegation card flipping to "done", and the busy status row collapsing (IsBusy→false) which
        // grows the viewport. The immediate ScrollToEnd reads a stale (too-short) Extent and undershoots,
        // so the last line ends up just below the fold, pinned against the composer. Re-scrolling on the
        // next LayoutUpdated guarantees the Extent is final when we land the offset, so the last line
        // clears the composer. One-shot + guarded so a deferred re-scroll never yanks the view down if
        // the user has meanwhile scrolled up to read earlier messages. (This handles only the *timing*
        // undershoot; the *measurement* clip — content measured shorter than it renders — is fixed
        // structurally by the bottom spacer child in MainWindow.axaml's TranscriptScroll.)
        if (_scrollPending)
            return;
        _scrollPending = true;
        TranscriptScroll.LayoutUpdated += OnTranscriptLayoutUpdated;
    }

    private void OnTranscriptLayoutUpdated(object? sender, EventArgs e)
    {
        TranscriptScroll.LayoutUpdated -= OnTranscriptLayoutUpdated;
        _scrollPending = false;

        // Only follow the bottom if the view is still near it. If the user dragged the scrollbar up in
        // the interval since the request, leave them where they are.
        if (IsNearBottom())
            TranscriptScroll.ScrollToEnd();
    }

    // True when the view is effectively pinned to the bottom (within a line's slack), or when the
    // content is shorter than the viewport (nothing to scroll). The slack absorbs sub-pixel rounding
    // and the immediate ScrollToEnd's possible undershoot — exactly the gap we're closing.
    private bool IsNearBottom()
    {
        var maxY = TranscriptScroll.Extent.Height - TranscriptScroll.Viewport.Height;
        if (maxY <= 0)
            return true;
        return TranscriptScroll.Offset.Y >= maxY - 64;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        // Slash-command palette navigation (when open): ↑/↓ move, Enter/Tab run, Esc closes. These are
        // intercepted before the TextBox so they don't move the caret / insert a newline / change focus.
        if (vm.IsSlashMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Down:
                    vm.MoveSlashSelection(1);
                    SlashList.ScrollIntoView(vm.SelectedSlashIndex); // unfocused list won't auto-scroll
                    e.Handled = true;
                    return;
                case Key.Up:
                    vm.MoveSlashSelection(-1);
                    SlashList.ScrollIntoView(vm.SelectedSlashIndex);
                    e.Handled = true;
                    return;
                case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                case Key.Tab when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    vm.AcceptSlashCommand();
                    e.Handled = true;
                    return;
                case Key.Escape:
                    vm.CloseSlashMenu();
                    e.Handled = true;
                    return;
            }
        }

        // Enter sends; Shift+Enter inserts a newline (default TextBox behaviour).
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        if (vm.SendCommand.CanExecute(null))
            vm.SendCommand.Execute(null);

        e.Handled = true;
    }

    /// <summary>Click on a slash-palette row: run that command, then return focus to the composer.</summary>
    private void OnSlashItemTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.AcceptSlashCommand();
            InputBox.Focus();
        }
    }
}
