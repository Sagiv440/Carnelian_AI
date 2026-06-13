using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using AI_Interface.ViewModels;

namespace AI_Interface.Behaviors;

/// <summary>
/// Attached property that renders a Markdown <b>prose</b> string into a <see cref="TextBlock"/>'s
/// <see cref="TextBlock.Inlines"/> with inline formatting — <c>**bold**</c>, <c>*italic*</c>, and
/// <c>`code`</c> — instead of showing the raw symbols. Set <c>beh:MarkdownText.Text="{Binding ...}"</c>
/// in place of the usual <c>Text</c> binding; it re-parses on every change (so a streaming reply updates
/// in place). The parse itself is the pure, unit-tested <see cref="InlineMarkdown"/>; this class is only
/// the thin Avalonia glue (hence it lives in the view layer, not a view model).
/// </summary>
public static class MarkdownText
{
    private static readonly FontFamily CodeFont = new("Cascadia Code,Consolas,monospace");

    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(MarkdownText));

    public static void SetText(TextBlock element, string? value) => element.SetValue(TextProperty, value);
    public static string? GetText(TextBlock element) => element.GetValue(TextProperty);

    static MarkdownText()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) => Apply(tb, e.NewValue as string));
    }

    private static void Apply(TextBlock tb, string? text)
    {
        var inlines = tb.Inlines;
        if (inlines is null)
        {
            inlines = new InlineCollection();
            tb.Inlines = inlines;
        }
        inlines.Clear();

        foreach (var span in InlineMarkdown.Parse(text))
        {
            var run = new Run(span.Text);
            switch (span.Style)
            {
                case InlineStyle.Bold:
                    run.FontWeight = FontWeight.Bold;
                    break;
                case InlineStyle.Italic:
                    run.FontStyle = FontStyle.Italic;
                    break;
                case InlineStyle.BoldItalic:
                    run.FontWeight = FontWeight.Bold;
                    run.FontStyle = FontStyle.Italic;
                    break;
                case InlineStyle.Code:
                    run.FontFamily = CodeFont;
                    break;
            }
            inlines.Add(run);
        }
    }
}
