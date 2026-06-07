using System.Collections.Generic;

namespace AI_Interface.Models;

/// <summary>
/// A single message in a conversation. This is the plain transport model handed to
/// <see cref="Services.IOllamaClient"/>; the UI uses a separate observable view model.
/// </summary>
/// <param name="Images">Optional base64-encoded images for multimodal (vision) models.</param>
public sealed record ChatMessage(ChatRole Role, string Content, IReadOnlyList<string>? Images = null)
{
    /// <summary>Tool calls requested by an assistant turn (set only during the project agent loop).</summary>
    public IReadOnlyList<AgentToolCall>? ToolCalls { get; init; }

    /// <summary>For a <see cref="ChatRole.Tool"/> message: the name of the tool whose result this carries.</summary>
    public string? ToolName { get; init; }

    public static ChatMessage System(string content) => new(ChatRole.System, content);
    public static ChatMessage User(string content) => new(ChatRole.User, content);
    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);
}
