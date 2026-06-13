using System.Net.Http;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// <see cref="IChatClient"/> over the Mistral AI API (api.mistral.ai), which is OpenAI-compatible — so all
/// the logic is inherited from <see cref="OpenAiCompatibleClient"/>. Only the identity + key differ.
/// Mistral's <c>v1/models</c> also lists embedding / moderation models, so <see cref="KeepModelId"/> drops
/// those from the chat picker.
/// </summary>
public sealed class MistralClient : OpenAiCompatibleClient, IMistralClient
{
    public MistralClient(HttpClient http, ISettingsService settings) : base(http, settings) { }

    public override AiProvider Provider => AiProvider.Mistral;

    protected override string ApiKey => Settings.Current.MistralApiKey.Trim();

    protected override string ProviderLabel => "Mistral AI";

    protected override bool KeepModelId(string id)
    {
        var s = id.ToLowerInvariant();
        return !s.Contains("embed") && !s.Contains("moderation") && !s.Contains("ocr");
    }
}
