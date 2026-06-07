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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = ConfigureServices();
            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>()
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

        // Typed HttpClient for web search/page fetching with a desktop User-Agent.
        services.AddHttpClient<IWebSearchService, WebSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        });

        services.AddTransient<IDeepResearchService, DeepResearchService>();
        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
