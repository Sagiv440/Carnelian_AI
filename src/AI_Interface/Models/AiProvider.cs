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
    Anthropic,

    /// <summary>DeepSeek cloud API (OpenAI-compatible).</summary>
    DeepSeek,

    /// <summary>Nvidia NIM cloud API (OpenAI-compatible).</summary>
    Nvidia,

    /// <summary>Mistral AI cloud API (OpenAI-compatible).</summary>
    Mistral
}

/// <summary>Friendly labels for <see cref="AiProvider"/> values.</summary>
public static class AiProviderExtensions
{
    /// <summary>Human-readable provider name shown in settings/menus.</summary>
    public static string DisplayName(this AiProvider provider) => provider switch
    {
        AiProvider.Ollama => "Ollama (local)",
        AiProvider.OpenAI => "OpenAI (ChatGPT)",
        AiProvider.Gemini => "Google (Gemini)",
        AiProvider.Anthropic => "Anthropic (Claude)",
        AiProvider.DeepSeek => "DeepSeek",
        AiProvider.Nvidia => "Nvidia (NIM)",
        AiProvider.Mistral => "Mistral AI",
        _ => provider.ToString()
    };

    /// <summary>Short tag shown next to a model id in the picker (e.g. "Local", "OpenAI").</summary>
    public static string Tag(this AiProvider provider) => provider switch
    {
        AiProvider.Ollama => "Local",
        AiProvider.OpenAI => "OpenAI",
        AiProvider.Gemini => "Gemini",
        AiProvider.Anthropic => "Claude",
        AiProvider.DeepSeek => "DeepSeek",
        AiProvider.Nvidia => "Nvidia",
        AiProvider.Mistral => "Mistral",
        _ => provider.ToString()
    };

    /// <summary>A small "logo" glyph for the provider (emoji, matching the app's glyph aesthetic).</summary>
    public static string Glyph(this AiProvider provider) => provider switch
    {
        AiProvider.Ollama => "🦙",
        AiProvider.OpenAI => "🟢",
        AiProvider.Gemini => "🔷",
        AiProvider.Anthropic => "🟠",
        AiProvider.DeepSeek => "🟣",
        AiProvider.Nvidia => "🟩",
        AiProvider.Mistral => "🟡",
        _ => "🤖"
    };

    /// <summary>The cloud providers (everything except local Ollama) — the ones added in Web Models.</summary>
    public static readonly AiProvider[] CloudProviders =
    {
        AiProvider.OpenAI, AiProvider.Gemini, AiProvider.Anthropic, AiProvider.DeepSeek, AiProvider.Nvidia,
        AiProvider.Mistral
    };
}
