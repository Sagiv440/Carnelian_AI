using System.Net.Http;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// <see cref="IChatClient"/> over the OpenAI Chat Completions API (api.openai.com/v1). All the wire logic
/// lives in <see cref="OpenAiCompatibleClient"/>; this subclass only supplies OpenAI's identity, key, and a
/// model-list filter that keeps chat-capable ids (GPT / o-series).
/// </summary>
public sealed class OpenAiClient : OpenAiCompatibleClient, IOpenAiClient
{
    public OpenAiClient(HttpClient http, ISettingsService settings) : base(http, settings) { }

    public override AiProvider Provider => AiProvider.OpenAI;

    protected override string ApiKey => Settings.Current.OpenAiApiKey.Trim();

    protected override string ProviderLabel => "OpenAI";

    protected override bool KeepModelId(string id) => IsChatModel(id);

    /// <summary>Keep ids that look like chat-capable models (skip embeddings/audio/whisper/etc.).</summary>
    private static bool IsChatModel(string id)
    {
        var s = id.ToLowerInvariant();
        return s.Contains("gpt") || s.StartsWith("o1") || s.StartsWith("o3") || s.StartsWith("o4")
               || s.Contains("-o1") || s.Contains("-o3") || s.Contains("-o4");
    }
}
