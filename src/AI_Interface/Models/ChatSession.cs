using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AI_Interface.Models;

/// <summary>One persisted turn of a conversation (role + text). Attachments/sources are not stored.</summary>
public sealed class ChatTurn
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChatRole Role { get; set; }

    public string Text { get; set; } = "";

    /// <summary>Model that produced this turn (assistant turns only); restores the header on reopen.</summary>
    public string? ModelName { get; set; }
}

/// <summary>A saved conversation shown in the sidebar chat log and persisted across runs.</summary>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New chat";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppMode Mode { get; set; } = AppMode.Chat;

    public List<ChatTurn> Messages { get; set; } = new();
}
