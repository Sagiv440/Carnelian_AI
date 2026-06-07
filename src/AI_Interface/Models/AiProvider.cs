namespace AI_Interface.Models;

/// <summary>
/// Which AI backend serves a model. <see cref="Ollama"/> is the local server; the others are
/// cloud APIs configured with an API key in Settings → AI Model → Web Models.
/// </summary>
public enum AiProvider
{
    /// <summary>Local Ollama server (no key).</summary>
    Ollama,

    /// <summary>OpenAI (ChatGPT) cloud API.</summary>
    OpenAI,

    /// <summary>Google Gemini cloud API.</summary>
    Gemini,

    /// <summary>Anthropic (Claude) cloud API.</summary>
    Anthropic
}

/// <summary>Friendly labels for <see cref="AiProvider"/> values.</summary>
public static class AiProviderExtensions
{
    /// <summary>Human-readable provider name shown in settings/menus.</summary>
    public static string DisplayName(this AiProvider provider) => provider switch
    {
        AiProvider.Ollama => "Ollama (local)",
        AiProvider.OpenAI => "ChatGPT",
        AiProvider.Gemini => "Gemini",
        AiProvider.Anthropic => "Claude",
        _ => provider.ToString()
    };

    /// <summary>Short tag shown next to a model id in the picker (e.g. "Local", "OpenAI").</summary>
    public static string Tag(this AiProvider provider) => provider switch
    {
        AiProvider.Ollama => "Local",
        AiProvider.OpenAI => "OpenAI",
        AiProvider.Gemini => "Gemini",
        AiProvider.Anthropic => "Claude",
        _ => provider.ToString()
    };
}
