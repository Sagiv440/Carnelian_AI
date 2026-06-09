using Avalonia;
using Avalonia.Controls;

namespace AI_Interface.Behaviors;

/// <summary>
/// Attached behavior that keeps a <see cref="ScrollViewer"/> pinned to the bottom as content grows —
/// e.g. a streaming activity log — but releases the moment the user scrolls up to read, so they aren't
/// yanked back down. Safe inside a virtualized/recycled <c>ItemsControl</c> DataTemplate: each
/// container manages its own subscription, removed on disable or detach so nothing leaks.
/// </summary>
public static class AutoScrollToEnd
{
    /// <summary>Slack (px) within which the view counts as "at the bottom", so it keeps following.</summary>
    private const double BottomSlack = 24;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Enabled", typeof(AutoScrollToEnd));

    public static void SetEnabled(ScrollViewer element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(ScrollViewer element) => element.GetValue(EnabledProperty);

    static AutoScrollToEnd()
    {
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(ScrollViewer viewer, AvaloniaPropertyChangedEventArgs e)
    {
        // Always detach first so toggling or re-applying never double-subscribes.
        viewer.ScrollChanged -= OnScrollChanged;
        viewer.DetachedFromVisualTree -= OnDetached;

        if (e.NewValue is true)
        {
            viewer.ScrollChanged += OnScrollChanged;
            viewer.DetachedFromVisualTree += OnDetached;
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // A recycled DataTemplate container leaves the tree; drop the subscriptions so it doesn't leak.
        if (sender is ScrollViewer viewer)
        {
            viewer.ScrollChanged -= OnScrollChanged;
            viewer.DetachedFromVisualTree -= OnDetached;
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
            return;

        // Only react when the content actually grew (new line appended), not on plain scrolling.
        if (e.ExtentDelta.Y <= 0)
            return;

        // Was the user at/near the bottom *before* this growth? Use the pre-growth extent so a user who
        // scrolled up to read isn't dragged back down once a new line arrives.
        var preGrowthExtent = viewer.Extent.Height - e.ExtentDelta.Y;
        var wasAtBottom = viewer.Offset.Y >= preGrowthExtent - viewer.Viewport.Height - BottomSlack;

        if (wasAtBottom)
            viewer.ScrollToEnd();
    }
}
