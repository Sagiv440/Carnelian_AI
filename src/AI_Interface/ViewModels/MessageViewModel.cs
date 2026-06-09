using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            if (i < Segments.Count && Segments[i].IsCode == p.IsCode && Segments[i].Language == p.Language)
                Segments[i].Text = p.Text;                 // same kind of part — just grow it
            else if (i < Segments.Count)
                Segments[i] = new MessageSegment(p.IsCode, p.Language, p.Text);
            else
                Segments.Add(new MessageSegment(p.IsCode, p.Language, p.Text));
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
    private bool _hasWork;

    /// <summary>Whether the reasoning block is expanded. Collapsed by default.</summary>
    [ObservableProperty]
    private bool _isWorkExpanded;

    /// <summary>True while this (assistant) message is still being generated.</summary>
    [ObservableProperty]
    private bool _isStreaming;

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
    private bool _hasDelegations;

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

    /// <summary>Appends an activity line to the delegation card with the given index. Call on the UI thread.</summary>
    public void AppendDelegationActivity(int index, string text)
    {
        var step = FindDelegation(index);
        step?.AppendActivity(text);
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

    public void SetSources(IEnumerable<SearchResult> sources)
    {
        Sources.Clear();
        foreach (var s in sources)
            Sources.Add(s);
        HasSources = Sources.Count > 0;
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
}
