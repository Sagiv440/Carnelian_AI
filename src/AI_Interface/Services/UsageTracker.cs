using System.Linq;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IUsageTracker"/>: looks up the provider's <see cref="ProviderAccount"/> in settings,
/// adds the estimated cost of the reply to its running spend, and saves. Pure aside from the settings store,
/// so the accumulation logic is unit-testable with a fake <see cref="ISettingsService"/>.
/// </summary>
public sealed class UsageTracker : IUsageTracker
{
    private readonly ISettingsService _settings;

    public UsageTracker(ISettingsService settings) => _settings = settings;

    public void RecordEstimatedUsage(AiProvider provider, string modelId, string? inputText, string? outputText)
    {
        if (provider == AiProvider.Ollama)
            return; // local models are free

        var account = _settings.Current.ActiveProviders.FirstOrDefault(p => p.Provider == provider);
        if (account is null)
            return; // not an added provider — nothing to track against

        var cost = ModelPricing.EstimateCostUsd(provider, modelId, inputText, outputText);
        if (cost <= 0m)
            return;

        account.SpentUsd += cost;
        _settings.Save();
    }
}
