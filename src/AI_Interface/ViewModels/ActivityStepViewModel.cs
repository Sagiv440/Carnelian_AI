using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Interface.ViewModels;

/// <summary>
/// Observable wrapper around one entry in the single-agent project activity feed — rendered as a compact
/// row in the message's "work" disclosure. Mirrors <see cref="DelegationStepViewModel"/>: a tool call shows
/// icon + title + target + a running/done status with an expandable result; a "note" shows the model's
/// interim narration. Keyed by <see cref="Index"/> so a streamed Finished lands on the matching Started.
/// </summary>
public sealed partial class ActivityStepViewModel : ObservableObject
{
    /// <summary>0-based per-run index; a Finished update maps to the matching Started by this value.</summary>
    public int Index { get; init; }

    /// <summary>True for the model's interim narration (rendered as a muted note rather than a tool row).</summary>
    public bool IsNote { get; init; }

    /// <summary>Tool glyph, e.g. "✏️" (tool rows only).</summary>
    public string Icon { get; init; } = "";

    /// <summary>Tool title, e.g. "Write file" (tool rows only).</summary>
    public string Title { get; init; } = "";

    /// <summary>Tool target/command, e.g. "src/App.jsx" (tool rows only).</summary>
    public string Detail { get; init; } = "";

    /// <summary>The note narration text (note rows only).</summary>
    public string Text { get; init; } = "";

    /// <summary>True while the tool is still running (no result yet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    [NotifyPropertyChangedFor(nameof(Done))]
    private bool _isRunning = true;

    /// <summary>True when the tool finished but didn't succeed (drives the status glyph).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    [NotifyPropertyChangedFor(nameof(Done))]
    private bool _failed;

    /// <summary>The tool's result text, shown in the expandable body.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(ShowCollapsedCaret))]
    private string _result = "";

    /// <summary>Whether this row's result body is expanded. Collapsed by default.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCollapsedCaret))]
    private bool _isExpanded;

    /// <summary>True once a result is available (drives the row's expand affordance).</summary>
    public bool HasResult => !string.IsNullOrWhiteSpace(Result);

    /// <summary>
    /// True when the collapsed (▸) caret should show: there's a result to reveal and the row is closed.
    /// Resultless rows hide the caret entirely so they don't look clickable.
    /// </summary>
    public bool ShowCollapsedCaret => HasResult && !IsExpanded;

    /// <summary>True once the tool finished successfully (drives the faint-green status tint).</summary>
    public bool Done => !IsRunning && !Failed;

    /// <summary>Status glyph: ✗ on failure, ⏳ while running, ✓ once done.</summary>
    public string StatusGlyph => Failed ? "✗" : IsRunning ? "⏳" : "✓";
}
