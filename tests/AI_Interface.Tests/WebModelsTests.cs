using System.Linq;
using AI_Interface.Models;
using AI_Interface.Services;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the Web Models "Add Provider" feature's pure logic: <see cref="ModelPricing"/> (token +
/// cost estimation), <see cref="UsageTracker"/> (per-provider spend accumulation), the budget parser, and
/// the active-provider row's billing display.
/// </summary>
public sealed class WebModelsTests
{
    // ---- a minimal in-memory settings store ----------------------------------------------------

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public int SaveCount { get; private set; }
        public void Save() => SaveCount++;
    }

    // ---- ModelPricing ---------------------------------------------------------------------------

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("abcd", 1)]      // 4 chars  → (4+3)/4 = 1
    [InlineData("abcdefgh", 2)]  // 8 chars  → (8+3)/4 = 2
    public void EstimateTokens_IsRoughlyCharsOverFour(string? text, long expected) =>
        Assert.Equal(expected, ModelPricing.EstimateTokens(text));

    [Theory]
    [InlineData(AiProvider.OpenAI, "gpt-4o-mini", 0.15, 0.60)]
    [InlineData(AiProvider.OpenAI, "gpt-4o", 2.50, 10.00)]
    [InlineData(AiProvider.OpenAI, "gpt-4o-2024-08-06", 2.50, 10.00)] // dated id still hits gpt-4o
    [InlineData(AiProvider.OpenAI, "o3-mini", 1.10, 4.40)]            // mini caught before the o1/o3 arm
    [InlineData(AiProvider.OpenAI, "gpt-4.1-nano", 0.40, 1.60)]
    [InlineData(AiProvider.OpenAI, "some-unknown-model", 2.50, 10.00)] // provider fallback
    [InlineData(AiProvider.Anthropic, "claude-3-5-sonnet-latest", 3.00, 15.00)]
    [InlineData(AiProvider.Anthropic, "claude-3-5-haiku", 0.80, 4.00)]
    [InlineData(AiProvider.Anthropic, "mystery", 3.00, 15.00)]        // provider fallback
    [InlineData(AiProvider.Gemini, "gemini-1.5-flash", 0.15, 0.60)]
    [InlineData(AiProvider.Gemini, "gemini-1.5-pro", 1.25, 5.00)]
    [InlineData(AiProvider.Gemini, "whatever", 0.50, 1.50)]           // provider fallback
    [InlineData(AiProvider.DeepSeek, "deepseek-chat", 0.27, 1.10)]
    [InlineData(AiProvider.DeepSeek, "deepseek-reasoner", 0.55, 2.19)]
    [InlineData(AiProvider.Nvidia, "meta/llama-3.1-8b-instruct", 0.20, 0.60)]
    [InlineData(AiProvider.Mistral, "mistral-large-latest", 2.00, 6.00)]
    [InlineData(AiProvider.Mistral, "mistral-small-latest", 0.20, 0.60)]
    [InlineData(AiProvider.Mistral, "codestral-latest", 0.30, 0.90)]
    [InlineData(AiProvider.Mistral, "ministral-3b-latest", 0.04, 0.04)]
    [InlineData(AiProvider.Mistral, "open-mistral-nemo", 0.10, 0.10)]
    [InlineData(AiProvider.Mistral, "mystery-model", 0.20, 0.60)]    // provider fallback
    [InlineData(AiProvider.Ollama, "llama3", 0, 0)]
    public void For_MatchesBySubstring_WithProviderFallback(
        AiProvider provider, string model, double input, double output)
    {
        var (inPerM, outPerM) = ModelPricing.For(provider, model);
        Assert.Equal((decimal)input, inPerM);
        Assert.Equal((decimal)output, outPerM);
    }

    [Fact]
    public void EstimateCostUsd_IsZeroForOllama_AndPositiveForACloudReply()
    {
        Assert.Equal(0m, ModelPricing.EstimateCostUsd(AiProvider.Ollama, "llama3", "hello", "world"));

        var cost = ModelPricing.EstimateCostUsd(
            AiProvider.OpenAI, "gpt-4o", new string('a', 4000), new string('b', 4000));
        Assert.True(cost > 0m);
    }

    // ---- UsageTracker ---------------------------------------------------------------------------

    [Fact]
    public void RecordEstimatedUsage_AddsCostToTheMatchingAccount_AndSaves()
    {
        var settings = new FakeSettings();
        settings.Current.ActiveProviders.Add(
            new ProviderAccount { Provider = AiProvider.OpenAI, Billing = ProviderBilling.Budget, BudgetUsd = 10m });
        var tracker = new UsageTracker(settings);

        tracker.RecordEstimatedUsage(AiProvider.OpenAI, "gpt-4o", new string('a', 8000), new string('b', 8000));

        var account = settings.Current.ActiveProviders[0];
        Assert.True(account.SpentUsd > 0m);
        Assert.Equal(1, settings.SaveCount);
    }

    [Fact]
    public void RecordEstimatedUsage_AccumulatesAcrossCalls_AndSavesEachTime()
    {
        var settings = new FakeSettings();
        settings.Current.ActiveProviders.Add(
            new ProviderAccount { Provider = AiProvider.OpenAI, Billing = ProviderBilling.Budget });
        var tracker = new UsageTracker(settings);

        tracker.RecordEstimatedUsage(AiProvider.OpenAI, "gpt-4o", new string('a', 8000), new string('b', 8000));
        var afterFirst = settings.Current.ActiveProviders[0].SpentUsd;
        tracker.RecordEstimatedUsage(AiProvider.OpenAI, "gpt-4o", new string('a', 8000), new string('b', 8000));

        Assert.True(settings.Current.ActiveProviders[0].SpentUsd > afterFirst);
        Assert.Equal(2, settings.SaveCount);
    }

    [Fact]
    public void RecordEstimatedUsage_IgnoresOllama_AndUnaddedProviders()
    {
        var settings = new FakeSettings();
        settings.Current.ActiveProviders.Add(
            new ProviderAccount { Provider = AiProvider.OpenAI, Billing = ProviderBilling.Budget });
        var tracker = new UsageTracker(settings);

        // Local model: free, never tracked.
        tracker.RecordEstimatedUsage(AiProvider.Ollama, "llama3", "in", "out");
        // A provider the user hasn't added: nothing to track against.
        tracker.RecordEstimatedUsage(AiProvider.Gemini, "gemini-1.5-pro", new string('a', 8000), new string('b', 8000));

        Assert.Equal(0m, settings.Current.ActiveProviders[0].SpentUsd);
        Assert.Equal(0, settings.SaveCount);
    }

    // ---- Provider dropdown list -----------------------------------------------------------------

    [Fact]
    public void CloudProviders_AreTheFiveAddableProviders_AndExcludeOllama()
    {
        Assert.Equal(
            new[] { AiProvider.OpenAI, AiProvider.Gemini, AiProvider.Anthropic, AiProvider.DeepSeek, AiProvider.Nvidia, AiProvider.Mistral },
            AiProviderExtensions.CloudProviders);
        Assert.DoesNotContain(AiProvider.Ollama, AiProviderExtensions.CloudProviders);
    }

    [Fact]
    public void NewProvider_OptionsMatchCloudProviders()
    {
        var vm = new WebModelsViewModel(new FakeSettings(), new DesignModelRouter());
        Assert.Equal(AiProviderExtensions.CloudProviders, vm.AvailableProviders.Select(o => o.Provider).ToArray());
    }

    // ---- WebModelsViewModel.ParseBudget ---------------------------------------------------------

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("10", 10)]
    [InlineData("10.50", 10.50)]
    [InlineData("$25", 25)]
    [InlineData("$ 25", 25)]
    [InlineData("abc", 0)]
    [InlineData("-5", 0)]
    [InlineData("0", 0)]
    public void ParseBudget_IsTolerant(string? text, double expected) =>
        Assert.Equal((decimal)expected, WebModelsViewModel.ParseBudget(text));

    // ---- ActiveProviderViewModel billing display ------------------------------------------------

    [Fact]
    public void BudgetRow_ShowsSpentOfBudget_AndFlagsOverBudget()
    {
        var under = new ActiveProviderViewModel(new ProviderAccount
        {
            Provider = AiProvider.OpenAI, Billing = ProviderBilling.Budget, BudgetUsd = 10m, SpentUsd = 3m
        });
        Assert.False(under.IsOverBudget);
        Assert.Contains("$3.00 of $10.00", under.BillingDisplay);

        var over = new ActiveProviderViewModel(new ProviderAccount
        {
            Provider = AiProvider.OpenAI, Billing = ProviderBilling.Budget, BudgetUsd = 10m, SpentUsd = 12m
        });
        Assert.True(over.IsOverBudget);
        Assert.Contains("over budget", over.BillingDisplay);
    }

    [Fact]
    public void SubscriptionRow_ShowsSubscription_AndIsNeverOverBudget()
    {
        var row = new ActiveProviderViewModel(new ProviderAccount
        {
            Provider = AiProvider.Anthropic, Billing = ProviderBilling.Subscription, SpentUsd = 5m
        });
        Assert.False(row.IsOverBudget);
        Assert.StartsWith("Subscription", row.BillingDisplay);
    }

    [Fact]
    public void BudgetRow_ExactlyAtBudget_IsNotOverBudget()
    {
        var row = new ActiveProviderViewModel(new ProviderAccount
        {
            Provider = AiProvider.OpenAI, Billing = ProviderBilling.Budget, BudgetUsd = 10m, SpentUsd = 10m
        });
        Assert.False(row.IsOverBudget); // strictly greater-than is over budget
    }

    // ---- WebModelsViewModel: add (upsert) / remove / legacy migration ---------------------------

    private static WebModelsViewModel Panel(FakeSettings settings) =>
        new(settings, new DesignModelRouter());

    private static ProviderOption Option(WebModelsViewModel vm, AiProvider provider) =>
        vm.AvailableProviders.Single(o => o.Provider == provider);

    [Fact]
    public async System.Threading.Tasks.Task InitializeAsync_MigratesAStrayKeyToASubscriptionAccount_Idempotently()
    {
        var settings = new FakeSettings();
        settings.Current.OpenAiApiKey = "sk-existing";
        var vm = Panel(settings);

        await vm.InitializeAsync();
        Assert.Single(settings.Current.ActiveProviders);
        Assert.Equal(AiProvider.OpenAI, settings.Current.ActiveProviders[0].Provider);
        Assert.Equal(ProviderBilling.Subscription, settings.Current.ActiveProviders[0].Billing);

        await vm.InitializeAsync(); // re-run must not duplicate
        Assert.Single(settings.Current.ActiveProviders);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddProvider_Upserts_PreservingSpentUsd_AndUpdatingBudget()
    {
        var settings = new FakeSettings();
        settings.Current.ActiveProviders.Add(new ProviderAccount
        {
            Provider = AiProvider.OpenAI, Billing = ProviderBilling.Subscription, SpentUsd = 4.20m
        });
        var vm = Panel(settings);
        await vm.InitializeAsync();

        vm.SelectedProviderOption = Option(vm, AiProvider.OpenAI);
        vm.NewApiKey = "sk-x";
        vm.ConnectSucceeded = true;          // simulate a successful Connect
        vm.IsNewBudget = true;
        vm.NewBudgetText = "30";
        await vm.AddProviderCommand.ExecuteAsync(null);

        var account = settings.Current.ActiveProviders.Single(p => p.Provider == AiProvider.OpenAI);
        Assert.Equal(ProviderBilling.Budget, account.Billing);
        Assert.Equal(30m, account.BudgetUsd);
        Assert.Equal(4.20m, account.SpentUsd);  // running spend preserved across the re-add
    }

    [Fact]
    public async System.Threading.Tasks.Task RemoveProvider_DropsAccount_AndClearsTheApiKey()
    {
        var settings = new FakeSettings();
        settings.Current.AnthropicApiKey = "sk-ant-x";
        settings.Current.ActiveProviders.Add(new ProviderAccount { Provider = AiProvider.Anthropic });
        var vm = Panel(settings);
        await vm.InitializeAsync();

        var row = vm.ActiveProviders.Single(r => r.Provider == AiProvider.Anthropic);
        vm.RemoveProviderCommand.Execute(row);

        Assert.Empty(settings.Current.ActiveProviders);
        Assert.Equal("", settings.Current.AnthropicApiKey); // cleared so it drops from the model picker
        Assert.False(vm.HasActiveProviders);
    }

    [Fact]
    public void EditingTheKeyAfterConnect_RequiresAFreshConnect()
    {
        var vm = Panel(new FakeSettings());
        vm.SelectedProviderOption = Option(vm, AiProvider.OpenAI);
        vm.ConnectSucceeded = true;

        vm.NewApiKey = "sk-edited-after-connect";

        Assert.False(vm.ConnectSucceeded); // must reconnect before Add
    }

    [Fact]
    public void SpentUsd_Change_RaisesBillingDisplay()
    {
        var row = new ActiveProviderViewModel(new ProviderAccount
        {
            Provider = AiProvider.OpenAI, Billing = ProviderBilling.Budget, BudgetUsd = 10m
        });
        var raised = false;
        row.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(ActiveProviderViewModel.BillingDisplay);

        row.SpentUsd = 2m;

        Assert.True(raised);
    }
}
