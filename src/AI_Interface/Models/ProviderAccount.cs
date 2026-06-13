namespace AI_Interface.Models;

/// <summary>How a cloud provider is billed, chosen by the user when adding it in Settings → Web Models.</summary>
public enum ProviderBilling
{
    /// <summary>A flat subscription — no per-request dollar budget is tracked (spend is informational only).</summary>
    Subscription,

    /// <summary>A pay-as-you-go dollar budget that the estimated usage counts down against.</summary>
    Budget
}

/// <summary>
/// A cloud AI provider the user has <b>added</b> in Settings → Web Models (the "Active Providers" list).
/// The provider's API key still lives in the per-provider <c>AppSettings</c> field (read by the clients);
/// this record carries the user-chosen billing mode plus the running <see cref="SpentUsd"/> estimate the
/// <c>IUsageTracker</c> accumulates after each cloud reply. Persisted in <see cref="AppSettings.ActiveProviders"/>.
/// </summary>
public sealed class ProviderAccount
{
    /// <summary>Which cloud provider this account configures (never <see cref="AiProvider.Ollama"/>).</summary>
    public AiProvider Provider { get; set; }

    /// <summary>Subscription vs a pay-as-you-go dollar budget.</summary>
    public ProviderBilling Billing { get; set; } = ProviderBilling.Budget;

    /// <summary>The dollar budget when <see cref="Billing"/> is <see cref="ProviderBilling.Budget"/>.</summary>
    public decimal BudgetUsd { get; set; }

    /// <summary>Accumulated <i>estimated</i> spend in USD (token counts are approximate — see <c>ModelPricing</c>).</summary>
    public decimal SpentUsd { get; set; }
}
