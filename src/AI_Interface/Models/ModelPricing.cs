namespace AI_Interface.Models;

/// <summary>
/// Coarse, curated per-model pricing used to <b>estimate</b> the dollar cost of a cloud reply so the
/// "Active Providers" budget can count down. Numbers are USD per 1,000,000 tokens (input, output) and are
/// approximate — both the prices (matched loosely by model-id substring, with a per-provider fallback) and
/// the token counts (a ~4-chars-per-token heuristic, since the chat surface doesn't return real usage).
/// This is intentionally a rough estimate, not a billing-grade figure.
/// </summary>
public static class ModelPricing
{
    /// <summary>Approximate tokens in <paramref name="text"/> (~4 characters per token).</summary>
    public static long EstimateTokens(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4;

    /// <summary>Estimated USD cost of one reply, from the input/output text and the model's price.</summary>
    public static decimal EstimateCostUsd(AiProvider provider, string modelId, string? inputText, string? outputText)
    {
        var (inPerM, outPerM) = For(provider, modelId);
        var inTokens = EstimateTokens(inputText);
        var outTokens = EstimateTokens(outputText);
        return inTokens / 1_000_000m * inPerM + outTokens / 1_000_000m * outPerM;
    }

    /// <summary>(inputPerMTok, outputPerMTok) in USD for a model — substring match, else a provider default.</summary>
    public static (decimal Input, decimal Output) For(AiProvider provider, string modelId)
    {
        var id = (modelId ?? "").ToLowerInvariant();
        return provider switch
        {
            AiProvider.OpenAI => id switch
            {
                _ when id.Contains("gpt-4o-mini") => (0.15m, 0.60m),
                _ when id.Contains("gpt-4o") => (2.50m, 10.00m),
                _ when id.Contains("gpt-4.1-mini") || id.Contains("gpt-4.1-nano") => (0.40m, 1.60m),
                _ when id.Contains("gpt-4.1") => (2.00m, 8.00m),
                _ when id.Contains("o3-mini") || id.Contains("o4-mini") => (1.10m, 4.40m),
                _ when id.Contains("o1") || id.Contains("o3") => (15.00m, 60.00m),
                _ when id.Contains("3.5-turbo") => (0.50m, 1.50m),
                _ => (2.50m, 10.00m)
            },
            AiProvider.Anthropic => id switch
            {
                _ when id.Contains("haiku") => (0.80m, 4.00m),
                _ when id.Contains("opus") => (15.00m, 75.00m),
                _ when id.Contains("sonnet") => (3.00m, 15.00m),
                _ => (3.00m, 15.00m)
            },
            AiProvider.Gemini => id switch
            {
                _ when id.Contains("flash-lite") => (0.075m, 0.30m),
                _ when id.Contains("flash") => (0.15m, 0.60m),
                _ when id.Contains("pro") => (1.25m, 5.00m),
                _ => (0.50m, 1.50m)
            },
            AiProvider.DeepSeek => id switch
            {
                _ when id.Contains("reasoner") => (0.55m, 2.19m),
                _ => (0.27m, 1.10m) // deepseek-chat
            },
            // Nvidia NIM hosts many models at varied prices; use one coarse blended estimate.
            AiProvider.Nvidia => (0.20m, 0.60m),
            // Local Ollama models are free to run.
            _ => (0m, 0m)
        };
    }
}
