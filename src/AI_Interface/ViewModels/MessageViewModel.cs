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

    /// <summary>For assistant messages, the model that generated the reply (shown as the header).</summary>
    public string? ModelName { get; init; }

    public string Header => Role switch
    {
        ChatRole.User => "You",
        ChatRole.Assistant => string.IsNullOrWhiteSpace(ModelName) ? "Assistant" : ModelName,
        ChatRole.System => "System",
        _ => "?"
    };

    /// <summary>Message body. Grows in place while a reply is streaming.</summary>
    [ObservableProperty]
    private string _text;

    /// <summary>True while this (assistant) message is still being generated.</summary>
    [ObservableProperty]
    private bool _isStreaming;

    /// <summary>Web sources backing this answer (web-search / deep-research modes).</summary>
    public ObservableCollection<SearchResult> Sources { get; } = new();

    [ObservableProperty]
    private bool _hasSources;

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
    }

    /// <summary>Appends a streamed token to the message body. Call on the UI thread.</summary>
    public void Append(string delta) => Text += delta;

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
}
