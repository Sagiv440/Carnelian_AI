using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AI_Interface.Views;

/// <summary>A small format picker for the <c>/saveToDoc</c> action. <c>ShowDialog&lt;string?&gt;</c>
/// returns <c>"pdf"</c> or <c>"docx"</c> when a format is chosen, or <c>null</c> when cancelled.</summary>
public partial class DocFormatWindow : Window
{
    public DocFormatWindow()
    {
        InitializeComponent();
    }

    private void OnPdf(object? sender, RoutedEventArgs e) => Close("pdf");

    private void OnDocx(object? sender, RoutedEventArgs e) => Close("docx");

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
