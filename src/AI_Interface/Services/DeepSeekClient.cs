using System.Net.Http;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// <see cref="IChatClient"/> over the DeepSeek API (api.deepseek.com), which is OpenAI-compatible — so all
/// the logic is inherited from <see cref="OpenAiCompatibleClient"/>. Only the identity + key differ.
/// DeepSeek's <c>v1/models</c> lists just chat models (deepseek-chat / deepseek-reasoner), so no filter.
/// </summary>
public sealed class DeepSeekClient : OpenAiCompatibleClient, IDeepSeekClient
{
    public DeepSeekClient(HttpClient http, ISettingsService settings) : base(http, settings) { }

    public override AiProvider Provider => AiProvider.DeepSeek;

    protected override string ApiKey => Settings.Current.DeepSeekApiKey.Trim();

    protected override string ProviderLabel => "DeepSeek";
}
