using System.Net.Http;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// <see cref="IChatClient"/> over Nvidia's hosted NIM endpoint (integrate.api.nvidia.com), which is
/// OpenAI-compatible — logic inherited from <see cref="OpenAiCompatibleClient"/>. Nvidia's catalog also
/// exposes embedding / reranking models, so <see cref="KeepModelId"/> drops those from the chat picker.
/// </summary>
public sealed class NvidiaClient : OpenAiCompatibleClient, INvidiaClient
{
    public NvidiaClient(HttpClient http, ISettingsService settings) : base(http, settings) { }

    public override AiProvider Provider => AiProvider.Nvidia;

    protected override string ApiKey => Settings.Current.NvidiaApiKey.Trim();

    protected override string ProviderLabel => "Nvidia";

    protected override bool KeepModelId(string id)
    {
        var s = id.ToLowerInvariant();
        return !s.Contains("embed") && !s.Contains("rerank");
    }
}
