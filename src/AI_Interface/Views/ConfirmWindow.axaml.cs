using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AI_Interface.Views;

/// <summary>A small reusable Yes/No confirmation dialog. <c>ShowDialog&lt;bool&gt;</c> returns
/// <c>true</c> when the user accepts, <c>false</c> when they exit/cancel.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void OnAccept(object? sender, RoutedEventArgs e) => Close(true);

    private void OnExit(object? sender, RoutedEventArgs e) => Close(false);
}
