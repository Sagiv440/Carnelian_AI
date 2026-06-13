using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IModelRouter"/>. Holds every chat client (Ollama + the cloud providers OpenAI,
/// Gemini, Anthropic, DeepSeek, Nvidia, Mistral) and fans out model listing across whichever are configured
/// and reachable.
/// </summary>
public sealed class ChatRouter : IModelRouter
{
    // Ordered: Ollama first, then the cloud providers (this is also the picker order).
    private readonly IReadOnlyList<IChatClient> _clients;

    public ChatRouter(
        IOllamaClient ollama,
        IOpenAiClient openAi,
        IGeminiClient gemini,
        IAnthropicClient anthropic,
        IDeepSeekClient deepSeek,
        INvidiaClient nvidia,
        IMistralClient mistral)
    {
        _clients = new IChatClient[] { ollama, openAi, gemini, anthropic, deepSeek, nvidia, mistral };
    }

    public async Task<IReadOnlyList<ChatModel>> ListAllModelsAsync(CancellationToken ct = default)
    {
        // Query each provider in parallel; a provider that's unconfigured/unreachable or errors out
        // simply contributes an empty list. Order of the aggregated result follows _clients.
        var tasks = _clients.Select(c => ListSafelyAsync(c, ct)).ToList();
        var perProvider = await Task.WhenAll(tasks).ConfigureAwait(false);

        var aggregated = new List<ChatModel>();
        for (var i = 0; i < _clients.Count; i++)
        {
            var provider = _clients[i].Provider;
            foreach (var id in perProvider[i])
                aggregated.Add(new ChatModel(provider, id));
        }
        return aggregated;
    }

    private static async Task<IReadOnlyList<string>> ListSafelyAsync(IChatClient client, CancellationToken ct)
    {
        try
        {
            if (!await client.IsConfiguredAndReachableAsync(ct).ConfigureAwait(false))
                return Array.Empty<string>();
            return await client.ListModelsAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: a failing provider must not break the others or the picker.
            return Array.Empty<string>();
        }
    }

    public IChatClient For(AiProvider provider) =>
        _clients.FirstOrDefault(c => c.Provider == provider)
        ?? throw new InvalidOperationException($"No chat client registered for provider '{provider}'.");
}
