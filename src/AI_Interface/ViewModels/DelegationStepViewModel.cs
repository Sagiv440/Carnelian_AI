using System.Collections.ObjectModel;
using AI_Interface.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Interface.ViewModels;

/// <summary>
/// Observable wrapper around one delegated subtask in an orchestrator (lead) run — rendered as a
/// collapsible card in the transcript. The lead's own reasoning stays in the message's structured feed;
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

    /// <summary>
    /// The specialist's structured activity feed — one row per tool call (icon · title · target · running/
    /// done status with an expandable result) plus the model's interim narration as note rows. Identical to
    /// the single-agent feed (both go through <see cref="ActivityFeed"/>); grown in place as it works.
    /// </summary>
    public ObservableCollection<ActivityStepViewModel> Activities { get; } = new();

    /// <summary>True once any activity row exists (drives the card body's feed visibility).</summary>
    [ObservableProperty]
    private bool _hasActivities;

    /// <summary>True while the specialist is still working (no result yet).</summary>
    [ObservableProperty]
    private bool _isRunning = true;

    /// <summary>The specialist's final answer (or an error string on failure).</summary>
    [ObservableProperty]
    private string _result = "";

    /// <summary>Whether this card is expanded. Collapsed by default.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Applies one structured step from the specialist to this card's feed. Call on the UI thread.</summary>
    public void ApplyActivity(ActivityUpdate u)
    {
        ActivityFeed.Apply(Activities, u);
        HasActivities = Activities.Count > 0;
    }
}
