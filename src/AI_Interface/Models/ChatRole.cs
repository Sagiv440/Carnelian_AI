namespace AI_Interface.Models;

/// <summary>Conversation role as understood by the Ollama chat API.</summary>
public enum ChatRole
{
    System,
    User,
    Assistant
}

public static class ChatRoleExtensions
{
    /// <summary>The wire value Ollama expects ("system" / "user" / "assistant").</summary>
    public static string ToWire(this ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        _ => "user"
    };
}
