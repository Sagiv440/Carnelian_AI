using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AI_Interface.Services;
using AI_Interface.ViewModels;
using AI_Interface.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AI_Interface;

public partial class App : Application
{
    /// <summary>App-wide service provider, used by views to resolve view models (e.g. settings dialog).</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Services = ConfigureServices();

            // Apply the saved theme before the first window shows.
            Services.GetRequiredService<IThemeService>()
                .Apply(Services.GetRequiredService<ISettingsService>().Current);

            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISettingsService, SettingsService>();

        // Typed HttpClient for Ollama. Long timeout: local generation can take minutes, and
        // streaming uses ResponseHeadersRead so the timeout only bounds time-to-first-byte.
        services.AddHttpClient<IOllamaClient, OllamaClient>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        // Cloud AI providers. Each gets its own typed HttpClient with the provider's base address and
        // a long timeout (matching Ollama) so streamed replies aren't cut off. Auth/version headers are
        // per-request (the API key is read from settings on each call), so they're not set here.
        services.AddHttpClient<IOpenAiClient, OpenAiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/");
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddHttpClient<IGeminiClient, GeminiClient>(client =>
        {
            client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddHttpClient<IAnthropicClient, AnthropicClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        // DeepSeek + Nvidia NIM + Mistral are OpenAI-compatible — same client logic, different base URL + key.
        services.AddHttpClient<IDeepSeekClient, DeepSeekClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.deepseek.com/");
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddHttpClient<INvidiaClient, NvidiaClient>(client =>
        {
            client.BaseAddress = new Uri("https://integrate.api.nvidia.com/");
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddHttpClient<IMistralClient, MistralClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.mistral.ai/");
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        // Routes a chosen ChatModel to the right provider client and aggregates the model list.
        services.AddSingleton<IModelRouter, ChatRouter>();

        // One-click local Ollama install (downloads the official installer/script for this OS).
        // Long timeout: the Windows installer download is sizeable.
        services.AddHttpClient<IOllamaInstaller, OllamaInstaller>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI_Interface");
        });

        // Typed HttpClient for web search/page fetching with a desktop User-Agent.
        services.AddHttpClient<IWebSearchService, WebSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        });

        services.AddTransient<IDeepResearchService, DeepResearchService>();

        // Voice (text-to-speech): the Piper engine + a cross-platform audio player, behind the
        // provider-agnostic ISpeechService (SpeechRouter picks the engine from settings). The engine
        // is auto-installed and voices are downloaded from the Piper catalog; the voice for each reply
        // is chosen from its detected language.
        services.AddSingleton<ILanguageDetector, LanguageDetector>();
        services.AddHttpClient<IPiperInstaller, PiperInstaller>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(20); // large engine/voice downloads
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI_Interface");
        });
        services.AddHttpClient<IPiperVoiceCatalog, PiperVoiceCatalog>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI_Interface");
        });
        services.AddSingleton<IAudioPlayer, AudioPlayer>();
        services.AddSingleton<IPiperSpeechService, PiperSpeechService>();
        services.AddSingleton<ISpeechService, SpeechRouter>();

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();
        services.AddSingleton<IChatHistoryService, ChatHistoryService>();
        // MCP (Model Context Protocol): connects to configured MCP servers and aggregates their tools for the
        // project agent. Singleton — it caches live connections (stdio servers are child processes).
        services.AddSingleton<IMcpService, McpService>();
        services.AddSingleton<IProjectAgentService, ProjectAgentService>();
        // Lead/orchestrator: runs a tool-calling loop that delegates subtasks to specialist agents (the
        // built-in "Lead") via IProjectAgentService — the "agents as tools" pattern.
        services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();
        services.AddSingleton<IProjectSkillService, ProjectSkillService>();
        services.AddSingleton<IProjectDocsService, ProjectDocsService>();
        services.AddSingleton<IHardwareService, HardwareService>();
        services.AddSingleton<IAgentService, AgentService>();
        services.AddSingleton<IMemoryService, MemoryService>();
        // Accumulates an estimated dollar spend per added cloud provider (Web Models budget tracking).
        services.AddSingleton<IUsageTracker, UsageTracker>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AgentsViewModel>();
        services.AddTransient<McpViewModel>();
        services.AddTransient<McpResourceBrowserViewModel>();
        services.AddTransient<WebModelsViewModel>();
        services.AddTransient<ProjectViewModel>();
        services.AddTransient<ModelConfigViewModel>();
        services.AddTransient<VoiceBrowserViewModel>();

        return services.BuildServiceProvider();
    }
}
