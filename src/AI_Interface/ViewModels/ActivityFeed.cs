using System.Collections.ObjectModel;
using AI_Interface.Models;

namespace AI_Interface.ViewModels;

/// <summary>
/// Shared logic for applying a streamed <see cref="ActivityUpdate"/> to a structured activity feed
/// (an <see cref="ObservableCollection{T}"/> of <see cref="ActivityStepViewModel"/>). Used by BOTH the
/// single-agent feed on <see cref="MessageViewModel"/> and the per-specialist feed inside a
/// <see cref="DelegationStepViewModel"/>, so the two render identically. Call on the UI thread.
/// </summary>
internal static class ActivityFeed
{
    /// <summary>
    /// Applies one update: a <see cref="ActivityPhase.Note"/> appends a narration row; a
    /// <see cref="ActivityPhase.Started"/> appends a running tool row; a <see cref="ActivityPhase.Finished"/>
    /// resolves the matching Started row's result/status (robust if no matching row is found — just ignored).
    /// </summary>
    public static void Apply(ObservableCollection<ActivityStepViewModel> feed, ActivityUpdate u)
    {
        switch (u.Phase)
        {
            case ActivityPhase.Note:
                feed.Add(new ActivityStepViewModel { Index = u.Index, IsNote = true, Text = u.Text });
                break;

            case ActivityPhase.Started:
                feed.Add(new ActivityStepViewModel
                {
                    Index = u.Index, Icon = u.Icon, Title = u.Title, Detail = u.Detail, IsRunning = true
                });
                break;

            case ActivityPhase.Finished:
                var step = FindStarted(feed, u.Index);
                if (step is null)
                    return;
                step.Result = u.Text ?? "";
                step.Failed = u.Failed;
                step.IsRunning = false;
                break;
        }
    }

    /// <summary>Finds the last non-note step with the given index — its Started row, to resolve on Finished.</summary>
    private static ActivityStepViewModel? FindStarted(ObservableCollection<ActivityStepViewModel> feed, int index)
    {
        for (var i = feed.Count - 1; i >= 0; i--)
            if (!feed[i].IsNote && feed[i].Index == index)
                return feed[i];
        return null;
    }
}
