using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AI_Interface.ViewModels;

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
            _vm.ScrollToEndRequested -= OnScrollToEndRequested;

        _vm = DataContext as MainWindowViewModel;

        if (_vm is not null)
            _vm.ScrollToEndRequested += OnScrollToEndRequested;
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
