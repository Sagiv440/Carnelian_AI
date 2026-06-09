using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Interface.ViewModels;

/// <summary>
/// Observable wrapper around one delegated subtask in an orchestrator (lead) run — rendered as a
/// collapsible card in the transcript. The lead's own reasoning stays in the message's "work" block;
/// each specialist's activity + result lives in its own card here. Keyed by <see cref="Index"/> so
/// streamed updates land on the right step.
/// </summary>
public sealed partial class DelegationStepViewModel : ObservableObject
{
    /// <summary>0-based per-run delegation index; updates map to the matching card by this value.</summary>
    public int Index { get; init; }

    /// <summary>The specialist agent's display name (header) and glyph.</summary>
    public string AgentName { get; init; } = "";
    public string Glyph { get; init; } = "";

    /// <summary>The brief the lead handed this specialist.</summary>
    public string Task { get; init; } = "";

    /// <summary>Card header: the specialist's glyph + name.</summary>
    public string Header => string.IsNullOrWhiteSpace(Glyph) ? AgentName : $"{Glyph}  {AgentName}";

    /// <summary>The specialist's action log, grown in place as it works.</summary>
    [ObservableProperty]
    private string _activity = "";

    /// <summary>True while the specialist is still working (no result yet).</summary>
    [ObservableProperty]
    private bool _isRunning = true;

    /// <summary>The specialist's final answer (or an error string on failure).</summary>
    [ObservableProperty]
    private string _result = "";

    /// <summary>Whether this card is expanded. Collapsed by default.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Appends a line to the activity log. Call on the UI thread.</summary>
    public void AppendActivity(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        Activity += text;
    }
}
