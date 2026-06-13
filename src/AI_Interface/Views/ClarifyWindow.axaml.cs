using Avalonia.Controls;
using Avalonia.Interactivity;
using AI_Interface.ViewModels;

namespace AI_Interface.Views;

/// <summary>
/// The clarify popup (the agent's <c>ask_user</c> tool). Closes with the user's combined answer (selected
/// options + any "Other" text), or null if cancelled/dismissed.
/// </summary>
public partial class ClarifyWindow : Window
{
    public ClarifyWindow()
    {
        InitializeComponent();
    }

    private void OnSubmit(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClarifyViewModel vm)
            Close(vm.BuildAnswer());
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
