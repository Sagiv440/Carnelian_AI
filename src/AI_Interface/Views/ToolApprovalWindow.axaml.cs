using Avalonia.Controls;
using Avalonia.Interactivity;
using AI_Interface.Models;

namespace AI_Interface.Views;

public partial class ToolApprovalWindow : Window
{
    public ToolApprovalWindow()
    {
        InitializeComponent();
    }

    public ToolApprovalWindow(ToolApprovalRequest request) : this()
    {
        DataContext = request;
    }

    private void OnApprove(object? sender, RoutedEventArgs e) => Close(true);

    private void OnDeny(object? sender, RoutedEventArgs e) => Close(false);
}
