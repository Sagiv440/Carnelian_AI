using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using AI_Interface.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Interface.ViewModels;

/// <summary>Observable wrapper around one chat message, used directly by the message list UI.</summary>
public sealed partial class MessageViewModel : ObservableObject
{
    public ChatRole Role { get; }

    public bool IsUser => Role == ChatRole.User;

    /// <summary>For assistant messages, the model that generated the reply (shown as a tooltip).</summary>
    public string? ModelName { get; init; }

    /// <summary>For assistant messages, the agent that produced the reply (its glyph + name show in the header).</summary>
    public string? AgentGlyph { get; init; }
    public string? AgentName { get; init; }

    public string Header => Role switch
    {
        ChatRole.User => "You",
        ChatRole.Assistant => AssistantHeader,
        ChatRole.System => "System",
        _ => "?"
    };

    /// <summary>Assistant header: the agent's glyph + name when known, else the model id, else "Assistant".</summary>
    private string AssistantHeader
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AgentName))
                return string.IsNullOrWhiteSpace(AgentGlyph) ? AgentName! : $"{AgentGlyph}  {AgentName}";
            return string.IsNullOrWhiteSpace(ModelName) ? "Assistant" : ModelName;
        }
    }

    /// <summary>Message body (the final answer). Grows in place while a reply is streaming.</summary>
    [ObservableProperty]
    private string _text;

    /// <summary>
    /// <see cref="Text"/> parsed into prose / code-block parts for rendering: prose as normal text,
    /// fenced code/commands as a monospace bubble with a copy button. Rebuilt as the text streams in.
    /// </summary>
    public ObservableCollection<MessageSegment> Segments { get; } = new();

    partial void OnTextChanged(string value) => RebuildSegments();

    /// <summary>Re-parse <see cref="Text"/> into <see cref="Segments"/>, updating parts in place so a
    /// streaming code block grows without recreating its container.</summary>
    private void RebuildSegments()
    {
        var parts = MarkdownSegmenter.Parse(Text);

        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (i < Segments.Count && Segments[i].Kind == p.Kind
                                   && Segments[i].Language == p.Language && Segments[i].Marker == p.Marker)
                Segments[i].Text = p.Text;                 // same kind of block — just grow it
            else if (i < Segments.Count)
                Segments[i] = new MessageSegment(p.Kind, p.Text, p.Language, p.Marker);
            else
                Segments.Add(new MessageSegment(p.Kind, p.Text, p.Language, p.Marker));
        }

        while (Segments.Count > parts.Count)
            Segments.RemoveAt(Segments.Count - 1);
    }

    /// <summary>
    /// The model's reasoning / the agent's action log — "how it got to the answer". Shown in a
    /// collapsible block above the answer. Empty for user messages and non-reasoning replies.
    /// </summary>
    [ObservableProperty]
    private string _work = "";

    /// <summary>True when <see cref="Work"/> has content (drives the collapsible block's visibility).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWorkBlock))]
    private bool _hasWork;

    /// <summary>Whether the reasoning block is expanded. Collapsed by default.</summary>
    [ObservableProperty]
    private bool _isWorkExpanded;

    /// <summary>True while this (assistant) message is still being generated.</summary>
    [ObservableProperty]
    private bool _isStreaming;

    /// <summary>
    /// Auto-reveal the activity log while the turn runs (so the user sees what the agent is doing live),
    /// and collapse it back to the tidy default when the turn finishes. The two-way <c>IsChecked</c>
    /// binding still lets the user toggle it manually mid-run; nothing re-opens it until the next run.
    /// When <see cref="HasWork"/> is false the block is hidden anyway, so this is a no-op there.
    /// </summary>
    partial void OnIsStreamingChanged(bool value)
    {
        IsWorkExpanded = value;
        OnPropertyChanged(nameof(WorkLabel));
    }

    /// <summary>Disclosure label for the activity block: "Working…" while live, "Activity" once done.</summary>
    public string WorkLabel => IsStreaming ? "Working…" : "Activity";

    /// <summary>True while this message is being read aloud (drives the speak button's glyph).</summary>
    [ObservableProperty]
    private bool _isSpeaking;

    /// <summary>Speak-button glyph: ⏹ to stop while reading, 🔈 to start otherwise.</summary>
    public string SpeakGlyph => IsSpeaking ? "⏹" : "🔈";

    partial void OnIsSpeakingChanged(bool value) => OnPropertyChanged(nameof(SpeakGlyph));

    /// <summary>
    /// Per-delegation cards for an orchestrator (lead) run — one per subtask handed to a specialist.
    /// The lead's own reasoning stays in <see cref="Work"/>; each specialist's activity + result lives here.
    /// </summary>
    public ObservableCollection<DelegationStepViewModel> Delegations { get; } = new();

    /// <summary>True once any delegation card exists (drives the delegations section's visibility).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWorkBlock))]
    private bool _hasDelegations;

    /// <summary>
    /// Structured activity feed for a single-agent project run — one row per tool call (icon + title +
    /// target + running/done status with an expandable result), plus "note" rows for the model's interim
    /// narration. The orchestrator (lead) path uses <see cref="Delegations"/> instead.
    /// </summary>
    public ObservableCollection<ActivityStepViewModel> Activities { get; } = new();

    /// <summary>True once any structured activity step exists (drives the structured feed's visibility).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWorkBlock))]
    private bool _hasActivities;

    /// <summary>
    /// The agent's live plan/checklist (the <c>update_plan</c> tool). Replaced wholesale on each update —
    /// the agent resends the full ordered list — so it's a simple rebuild, not a per-row reconcile.
    /// </summary>
    public ObservableCollection<PlanStepViewModel> Plan { get; } = new();

    /// <summary>True once the agent has posted a flat plan (drives the checklist card's visibility).</summary>
    [ObservableProperty]
    private bool _hasPlan;

    /// <summary>
    /// The agent's live plan organised into named phases (the <c>update_plan</c> tool's <c>phases</c>). Like
    /// <see cref="Plan"/> it's rebuilt wholesale on each update. When present it replaces the flat checklist.
    /// </summary>
    public ObservableCollection<PlanPhaseViewModel> Phases { get; } = new();

    /// <summary>True when the agent has posted a phased plan (drives the phase card's visibility).</summary>
    [ObservableProperty]
    private bool _hasPhases;

    /// <summary>
    /// Whether to show the legacy monospace <see cref="Work"/> block: only when there's work text but no
    /// structured feed (e.g. chat-with-thinking). Project runs populate <see cref="Activities"/> /
    /// <see cref="Delegations"/>, so the structured feed/cards show and the raw block is hidden (no duplicate).
    /// </summary>
    public bool ShowWorkBlock => HasWork && !HasActivities && !HasDelegations;

    /// <summary>Web sources backing this answer (web-search / deep-research modes).</summary>
    public ObservableCollection<SearchResult> Sources { get; } = new();

    [ObservableProperty]
    private bool _hasSources;

    /// <summary>Proactive next-step suggestions (Phase 5), shown as clickable chips below the answer.</summary>
    public ObservableCollection<string> Suggestions { get; } = new();

    [ObservableProperty]
    private bool _hasSuggestions;

    /// <summary>Files attached to this (user) message, shown as chips in the bubble.</summary>
    public ObservableCollection<Attachment> Attachments { get; } = new();

    [ObservableProperty]
    private bool _hasAttachments;

    /// <summary>Base64 images sent to the model (vision). Not displayed.</summary>
    public List<string> Images { get; set; } = new();

    /// <summary>Extracted text from attached documents (PDFs), folded into the model prompt. Not displayed.</summary>
    public string AttachedContext { get; set; } = "";

    public MessageViewModel(ChatRole role, string text = "")
    {
        Role = role;
        _text = text;
        RebuildSegments(); // the field initializer above bypasses OnTextChanged
    }

    /// <summary>Appends a streamed token to the message body. Call on the UI thread.</summary>
    public void Append(string delta) => Text += delta;

    /// <summary>Appends to the reasoning/action log and flags it visible. Call on the UI thread.</summary>
    public void AppendWork(string delta)
    {
        if (string.IsNullOrEmpty(delta))
            return;
        Work += delta;
        HasWork = true;
    }

    /// <summary>Replaces the reasoning/action log wholesale (used while re-splitting a stream).</summary>
    public void SetWork(string work)
    {
        Work = work ?? "";
        HasWork = !string.IsNullOrWhiteSpace(Work);
    }

    /// <summary>Adds a delegation card for a freshly started subtask. Call on the UI thread.</summary>
    public void StartDelegation(int index, string name, string glyph, string task)
    {
        Delegations.Add(new DelegationStepViewModel
        {
            Index = index,
            AgentName = name,
            Glyph = glyph,
            Task = task
        });
        HasDelegations = true;
    }

    /// <summary>Routes a structured specialist step into the delegation card with the given index. Call on the UI thread.</summary>
    public void ApplyDelegationActivity(int index, ActivityUpdate step)
    {
        var card = FindDelegation(index);
        card?.ApplyActivity(step);
    }

    /// <summary>Marks the delegation card with the given index finished and records its result. Call on the UI thread.</summary>
    public void FinishDelegation(int index, string result)
    {
        var step = FindDelegation(index);
        if (step is null)
            return;
        step.Result = result ?? "";
        step.IsRunning = false;
    }

    /// <summary>Finds a delegation card by its index (robust if a stray index has no matching card).</summary>
    private DelegationStepViewModel? FindDelegation(int index)
    {
        foreach (var d in Delegations)
            if (d.Index == index)
                return d;
        return null;
    }

    /// <summary>
    /// Applies one structured activity update from a single-agent project run. Call on the UI thread.
    /// A Note adds a narration row; Started adds a running tool row; Finished resolves the matching tool
    /// row's result/status (robust if no matching row is found — just ignored, like the delegation methods).
    /// </summary>
    public void ApplyActivity(ActivityUpdate u)
    {
        ActivityFeed.Apply(Activities, u);
        HasActivities = Activities.Count > 0;
    }

    /// <summary>
    /// Replaces the plan with the agent's latest full list. A phased plan (<see cref="PlanUpdate.Phases"/>)
    /// renders the phase card and wins over the flat checklist; otherwise the flat <see cref="Plan"/> shows.
    /// Call on the UI thread.
    /// </summary>
    public void SetPlan(PlanUpdate update)
    {
        Phases.Clear();
        foreach (var p in update.Phases)
        {
            var phase = new PlanPhaseViewModel { Name = p.Name, Status = p.Status };
            foreach (var s in p.Steps)
                phase.Steps.Add(new PlanStepViewModel { Text = s.Text, Status = s.Status });
            Phases.Add(phase);
        }
        HasPhases = Phases.Count > 0;

        Plan.Clear();
        foreach (var s in update.Steps)
            Plan.Add(new PlanStepViewModel { Text = s.Text, Status = s.Status });
        // The flat checklist only shows when there are no phases (phases win the single plan slot).
        HasPlan = Plan.Count > 0 && !HasPhases;
    }

    public void SetSources(IEnumerable<SearchResult> sources)
    {
        Sources.Clear();
        foreach (var s in sources)
            Sources.Add(s);
        HasSources = Sources.Count > 0;
    }

    /// <summary>Per-activity result cap when flattening to a persistable log (keeps the chat file small).</summary>
    private const int MaxLoggedResultChars = 1000;

    /// <summary>
    /// A plain-text record of this turn's agent activity for persistence. Project runs render their work as
    /// the structured <see cref="Activities"/> / <see cref="Delegations"/> feed live; this flattens that to
    /// text so a reopened conversation keeps a readable log (shown in the "Activity" disclosure). Falls back
    /// to the raw <see cref="Work"/> (a Thinking turn's reasoning), or "" when there's nothing to record.
    /// </summary>
    public string BuildActivityLog()
    {
        if (Activities.Count == 0 && Delegations.Count == 0)
            return Work;

        var sb = new StringBuilder();
        foreach (var a in Activities)
            AppendActivityLine(sb, a, "");
        foreach (var d in Delegations)
        {
            sb.Append(d.Header);
            if (!string.IsNullOrWhiteSpace(d.Task))
                sb.Append(" — ").Append(d.Task.Trim());
            sb.Append('\n');
            foreach (var a in d.Activities)
                AppendActivityLine(sb, a, "    ");
            if (!string.IsNullOrWhiteSpace(d.Result))
                sb.Append("    → ").Append(Cap(d.Result)).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    private static void AppendActivityLine(StringBuilder sb, ActivityStepViewModel a, string indent)
    {
        sb.Append(indent);
        if (a.IsNote)
        {
            sb.Append(a.Text.Trim()).Append('\n');
            return;
        }

        if (!string.IsNullOrWhiteSpace(a.StatusGlyph))
            sb.Append(a.StatusGlyph).Append(' ');
        if (!string.IsNullOrWhiteSpace(a.Icon))
            sb.Append(a.Icon).Append(' ');
        sb.Append(a.Title);
        if (!string.IsNullOrWhiteSpace(a.Detail))
            sb.Append(": ").Append(a.Detail.Trim());
        sb.Append('\n');

        if (!string.IsNullOrWhiteSpace(a.Result))
            sb.Append(indent).Append("    ").Append(Cap(a.Result)).Append('\n');
    }

    private static string Cap(string text)
    {
        var t = text.Trim();
        return t.Length <= MaxLoggedResultChars ? t : t[..MaxLoggedResultChars] + " …";
    }

    public void SetAttachments(IEnumerable<Attachment> attachments)
    {
        Attachments.Clear();
        foreach (var a in attachments)
            Attachments.Add(a);
        HasAttachments = Attachments.Count > 0;
    }

    public void SetSuggestions(IEnumerable<string> suggestions)
    {
        Suggestions.Clear();
        foreach (var s in suggestions)
            Suggestions.Add(s);
        HasSuggestions = Suggestions.Count > 0;
    }

    // --- Structured persistence (Project mode): export the live plan / activity / delegation state to
    //     serializable turn DTOs, and restore them back into the same cards when a saved chat is reopened. ---

    /// <summary>Exports the plan (flat steps and/or named phases) for persistence; null when there is none.</summary>
    public PlanTurn? ExportPlan()
    {
        if (!HasPlan && !HasPhases)
            return null;

        var turn = new PlanTurn();
        foreach (var s in Plan)
            turn.Steps.Add(new PlanStepTurn { Text = s.Text, Status = s.Status });
        foreach (var p in Phases)
        {
            var phase = new PlanPhaseTurn { Name = p.Name, Status = p.Status };
            foreach (var s in p.Steps)
                phase.Steps.Add(new PlanStepTurn { Text = s.Text, Status = s.Status });
            turn.Phases.Add(phase);
        }
        return turn;
    }

    /// <summary>Exports the single-agent activity feed for persistence; null when empty.</summary>
    public List<ActivityTurn>? ExportActivities()
    {
        if (Activities.Count == 0)
            return null;
        var list = new List<ActivityTurn>(Activities.Count);
        foreach (var a in Activities)
            list.Add(ToActivityTurn(a));
        return list;
    }

    /// <summary>Exports the orchestrator's delegation cards (subagent briefs, feeds, and results); null when none.</summary>
    public List<DelegationTurn>? ExportDelegations()
    {
        if (Delegations.Count == 0)
            return null;
        var list = new List<DelegationTurn>(Delegations.Count);
        foreach (var d in Delegations)
        {
            var card = new DelegationTurn
            {
                AgentName = d.AgentName, Glyph = d.Glyph, Task = d.Task, Result = d.Result
            };
            foreach (var a in d.Activities)
                card.Activities.Add(ToActivityTurn(a));
            list.Add(card);
        }
        return list;
    }

    private static ActivityTurn ToActivityTurn(ActivityStepViewModel a) => new()
    {
        IsNote = a.IsNote, Icon = a.Icon, Title = a.Title, Detail = a.Detail,
        Text = a.Text, Result = a.Result, Failed = a.Failed
    };

    /// <summary>Rebuilds the plan card from a saved turn (reuses <see cref="SetPlan"/>). Call on the UI thread.</summary>
    public void RestorePlan(PlanTurn plan)
    {
        var steps = new List<PlanStep>(plan.Steps.Count);
        foreach (var s in plan.Steps)
            steps.Add(new PlanStep(s.Text, s.Status));

        var phases = new List<PlanPhase>(plan.Phases.Count);
        foreach (var p in plan.Phases)
        {
            var inner = new List<PlanStep>(p.Steps.Count);
            foreach (var s in p.Steps)
                inner.Add(new PlanStep(s.Text, s.Status));
            phases.Add(new PlanPhase(p.Name, p.Status, inner));
        }
        SetPlan(new PlanUpdate(steps, phases));
    }

    /// <summary>Rebuilds the single-agent activity feed from a saved turn (all rows finished). Call on the UI thread.</summary>
    public void RestoreActivities(IEnumerable<ActivityTurn> rows)
    {
        Activities.Clear();
        var index = 0;
        foreach (var r in rows)
            Activities.Add(FromActivityTurn(r, index++));
        HasActivities = Activities.Count > 0;
    }

    /// <summary>Rebuilds the orchestrator's delegation cards from a saved turn (all finished). Call on the UI thread.</summary>
    public void RestoreDelegations(IEnumerable<DelegationTurn> cards)
    {
        Delegations.Clear();
        var index = 0;
        foreach (var c in cards)
        {
            var card = new DelegationStepViewModel
            {
                Index = index++, AgentName = c.AgentName, Glyph = c.Glyph, Task = c.Task
            };
            var i = 0;
            foreach (var a in c.Activities)
                card.Activities.Add(FromActivityTurn(a, i++));
            card.HasActivities = card.Activities.Count > 0;
            card.Result = c.Result;
            card.IsRunning = false;
            Delegations.Add(card);
        }
        HasDelegations = Delegations.Count > 0;
    }

    private static ActivityStepViewModel FromActivityTurn(ActivityTurn r, int index) => new()
    {
        Index = index, IsNote = r.IsNote, Icon = r.Icon, Title = r.Title, Detail = r.Detail,
        Text = r.Text, Result = r.Result, Failed = r.Failed, IsRunning = false
    };
}
