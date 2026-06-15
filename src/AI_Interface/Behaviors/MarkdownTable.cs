using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AI_Interface.ViewModels;

namespace AI_Interface.Behaviors;

/// <summary>
/// Attached property that renders a parsed <see cref="TableData"/> into a host <see cref="Border"/> as a
/// bordered <see cref="Grid"/> — header row bold on a surface fill, hairline cell separators matching the
/// app's flat IDE style. Cell text reuses <see cref="MarkdownText"/> so inline formatting (bold, links, …)
/// still applies. Set <c>beh:MarkdownTable.Source="{Binding Table}"</c> on the host Border; it rebuilds on
/// each change (so a streamed table grows row-by-row). Thin Avalonia glue over the pure parse in
/// <see cref="MarkdownSegmenter.ParseTable"/> — hence it lives in the view layer.
/// Brushes are bound to <b>resource observables</b> (not resolved once) so they track the current theme
/// variant — otherwise dark-mode cells render with the stale/fallback colour and become unreadable.
/// </summary>
public static class MarkdownTable
{
    public static readonly AttachedProperty<TableData?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Border, TableData?>("Source", typeof(MarkdownTable));

    public static void SetSource(Border element, TableData? value) => element.SetValue(SourceProperty, value);
    public static TableData? GetSource(Border element) => element.GetValue(SourceProperty);

    static MarkdownTable()
    {
        SourceProperty.Changed.AddClassHandler<Border>((host, e) => Build(host, e.NewValue as TableData));
    }

    private static void Build(Border host, TableData? table)
    {
        if (table is null || table.ColumnCount == 0)
        {
            host.Child = null;
            return;
        }

        var cols = table.ColumnCount;
        var grid = new Grid();
        for (var c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var rowIndex = 0;
        AddRow(host, grid, table.Header, rowIndex++, cols, isHeader: true);
        foreach (var row in table.Rows)
            AddRow(host, grid, row, rowIndex++, cols, isHeader: false);

        // Outer left+top edges; each cell supplies its own bottom+right edge (no doubled lines).
        var outer = new Border
        {
            BorderThickness = new Thickness(1, 1, 0, 0),
            Child = grid
        };
        outer.Bind(Border.BorderBrushProperty, host.GetResourceObservable("AppSurfaceBorderBrush"));
        host.Child = outer;
    }

    private static void AddRow(Border host, Grid grid, IReadOnlyList<string> cells, int rowIndex, int cols, bool isHeader)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < cols; c++)
        {
            var text = c < cells.Count ? cells[c] : "";
            var tb = new SelectableTextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontWeight = isHeader ? FontWeight.Bold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Top
            };
            tb.Bind(TextBlock.ForegroundProperty, host.GetResourceObservable("AppTextPrimary"));
            MarkdownText.SetText(tb, text);

            var cell = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(8, 5),
                Child = tb
            };
            cell.Bind(Border.BorderBrushProperty, host.GetResourceObservable("AppSurfaceBorderBrush"));
            if (isHeader)
                cell.Bind(Border.BackgroundProperty, host.GetResourceObservable("AppSurfaceBrush"));

            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, c);
            grid.Children.Add(cell);
        }
    }
}
