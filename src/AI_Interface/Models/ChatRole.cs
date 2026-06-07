namespace AI_Interface.Models;

/// <summary>Conversation role as understood by the Ollama chat API.</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,

    /// <summary>Result of a tool call fed back to the model (Ollama role "tool").</summary>
    Tool
}

public static class ChatRoleExtensions
{
    /// <summary>The wire value Ollama expects ("system" / "user" / "assistant").</summary>
    public static string ToWire(this ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        ChatRole.Tool => "tool",
        _ => "user"
    };
}
