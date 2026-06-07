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

        // Routes a chosen ChatModel to the right provider client and aggregates the model list.
        services.AddSingleton<IModelRouter, ChatRouter>();

        // Typed HttpClient for web search/page fetching with a desktop User-Agent.
        services.AddHttpClient<IWebSearchService, WebSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        });

        services.AddTransient<IDeepResearchService, DeepResearchService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();
        services.AddSingleton<IChatHistoryService, ChatHistoryService>();
        services.AddSingleton<IProjectAgentService, ProjectAgentService>();
        services.AddSingleton<IProjectSkillService, ProjectSkillService>();
        services.AddSingleton<IHardwareService, HardwareService>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProjectViewModel>();
        services.AddTransient<ModelConfigViewModel>();

        return services.BuildServiceProvider();
    }
}
