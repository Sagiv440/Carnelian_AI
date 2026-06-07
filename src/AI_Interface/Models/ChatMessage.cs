namespace AI_Interface.Models;

/// <summary>
/// A single message in a conversation. This is the plain transport model handed to
/// <see cref="Services.IOllamaClient"/>; the UI uses a separate observable view model.
/// </summary>
public sealed record ChatMessage(ChatRole Role, string Content)
{
    public static ChatMessage System(string content) => new(ChatRole.System, content);
    public static ChatMessage User(string content) => new(ChatRole.User, content);
    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);
}
