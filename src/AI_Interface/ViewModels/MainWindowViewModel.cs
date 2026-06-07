using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Interface.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private const string ChatSystemPrompt =
        "You are a helpful AI assistant running locally via Ollama. Be accurate and concise.";

    private readonly IOllamaClient _ollama;
    private readonly IWebSearchService _search;
    private readonly IDeepResearchService _research;
    private readonly ISettingsService _settings;

    private CancellationTokenSource? _cts;

    /// <summary>Raised when the view should scroll the transcript to the bottom.</summary>
    public event EventHandler? ScrollToEndRequested;

    public ObservableCollection<MessageViewModel> Messages { get; } = new();
    public ObservableCollection<string> Models { get; } = new();
    public IReadOnlyList<ModeOption> Modes { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _selectedModel;

    [ObservableProperty]
    private ModeOption _selectedMode;

    [ObservableProperty]
    private string _ollamaBaseUrl;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    private string _statusText = "";

    public MainWindowViewModel(
        IOllamaClient ollama,
        IWebSearchService search,
        IDeepResearchService research,
        ISettingsService settings)
    {
        _ollama = ollama;
        _search = search;
        _research = research;
        _settings = settings;

        Modes = new[]
        {
            new ModeOption(AppMode.Chat, "Chat", "Talk directly to the local model."),
            new ModeOption(AppMode.WebSearch, "Web Search", "Search the web once, then answer with citations."),
            new ModeOption(AppMode.DeepResearch, "Deep Research", "Plan queries, read pages, synthesize a cited report.")
        };
        _selectedMode = Modes[0];
        _ollamaBaseUrl = settings.Current.OllamaBaseUrl;
        _selectedModel = settings.Current.DefaultModel;
    }

    // Design-time / fallback constructor so the XAML previewer can instantiate the window.
    public MainWindowViewModel()
        : this(new DesignOllamaClient(), new DesignWebSearchService(),
               new DesignDeepResearchService(), new DesignSettingsService())
    {
    }

    partial void OnOllamaBaseUrlChanged(string value)
    {
        // Keep the live setting in sync so the Ollama client picks up the new URL immediately.
        _settings.Current.OllamaBaseUrl = value.Trim();
    }

    partial void OnSelectedModelChanged(string? value)
    {
        _settings.Current.DefaultModel = value;
        _settings.Save();
    }

    /// <summary>Called once after the view loads: connect and load the model list.</summary>
    public async Task InitializeAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _settings.Current.OllamaBaseUrl = OllamaBaseUrl.Trim();
        _settings.Save();

        ConnectionStatus = "Connecting…";
        IsConnected = await _ollama.IsAvailableAsync().ConfigureAwait(true);

        if (!IsConnected)
        {
            ConnectionStatus = $"Offline — is Ollama running at {OllamaBaseUrl}?";
            Models.Clear();
            return;
        }

        try
        {
            var models = await _ollama.ListModelsAsync().ConfigureAwait(true);
            Models.Clear();
            foreach (var m in models)
                Models.Add(m);

            ConnectionStatus = Models.Count > 0
                ? $"Connected — {Models.Count} model(s)"
                : "Connected — no models installed (try: ollama pull llama3)";

            // Restore the saved model if still present, otherwise pick the first.
            if (SelectedModel is null || !Models.Contains(SelectedModel))
                SelectedModel = Models.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error listing models: {ex.Message}";
        }
    }

    private bool CanSend =>
        !IsBusy && !string.IsNullOrWhiteSpace(InputText) && !string.IsNullOrEmpty(SelectedModel);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var prompt = InputText.Trim();
        var model = SelectedModel!;
        InputText = "";

        Messages.Add(new MessageViewModel(ChatRole.User, prompt));
        var assistant = new MessageViewModel(ChatRole.Assistant) { IsStreaming = true };
        Messages.Add(assistant);
        RequestScroll();

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            switch (SelectedMode.Mode)
            {
                case AppMode.Chat:
                    await RunChatAsync(model, assistant, ct);
                    break;
                case AppMode.WebSearch:
                    await RunWebSearchAsync(model, prompt, assistant, ct);
                    break;
                case AppMode.DeepResearch:
                    await RunDeepResearchAsync(model, prompt, assistant, ct);
                    break;
            }

            if (assistant.Text.Length == 0)
                assistant.Text = "_(no response)_";
        }
        catch (OperationCanceledException)
        {
            assistant.Append(assistant.Text.Length == 0 ? "_(stopped)_" : "\n\n_(stopped)_");
        }
        catch (Exception ex)
        {
            assistant.Text = $"⚠️ {ex.Message}";
        }
        finally
        {
            assistant.IsStreaming = false;
            IsBusy = false;
            StatusText = "";
            _cts?.Dispose();
            _cts = null;
            RequestScroll();
        }
    }

    private async Task RunChatAsync(string model, MessageViewModel assistant, CancellationToken ct)
    {
        var history = BuildChatHistory(assistant);
        // No ConfigureAwait(false): stream deltas must apply on the UI thread.
        await foreach (var delta in _ollama.ChatStreamAsync(model, history, ct))
        {
            assistant.Append(delta);
            RequestScroll();
        }
    }

    private async Task RunWebSearchAsync(
        string model, string prompt, MessageViewModel assistant, CancellationToken ct)
    {
        StatusText = "Searching the web…";
        var results = await _search.SearchAsync(prompt, _settings.Current.SearchResultsPerQuery, ct);

        if (results.Count == 0)
        {
            assistant.Text = "I couldn't find any web results. Check your connection or rephrase.";
            return;
        }

        StatusText = $"Found {results.Count} results — answering…";
        var messages = BuildWebSearchMessages(prompt, results);
        await foreach (var delta in _ollama.ChatStreamAsync(model, messages, ct))
        {
            assistant.Append(delta);
            RequestScroll();
        }
        assistant.SetSources(results);
    }

    private async Task RunDeepResearchAsync(
        string model, string prompt, MessageViewModel assistant, CancellationToken ct)
    {
        // Progress is constructed on the UI thread, so its callbacks marshal back automatically.
        var progress = new Progress<string>(s => StatusText = s);

        var sources = await _research.RunAsync(
            prompt,
            model,
            progress,
            // The research service streams on a background thread; marshal each token to the UI.
            delta => Dispatcher.UIThread.Post(() =>
            {
                assistant.Append(delta);
                RequestScroll();
            }),
            ct);

        assistant.SetSources(sources);
    }

    private List<ChatMessage> BuildChatHistory(MessageViewModel pendingAssistant)
    {
        var history = new List<ChatMessage> { ChatMessage.System(ChatSystemPrompt) };
        foreach (var m in Messages)
        {
            if (ReferenceEquals(m, pendingAssistant))
                continue; // skip the empty placeholder we're about to fill
            if (string.IsNullOrEmpty(m.Text))
                continue;
            history.Add(new ChatMessage(m.Role, m.Text));
        }
        return history;
    }

    private static List<ChatMessage> BuildWebSearchMessages(string prompt, IReadOnlyList<SearchResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Web search results:");
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] {r.Title}");
            sb.AppendLine($"URL: {r.Url}");
            if (!string.IsNullOrWhiteSpace(r.Snippet))
                sb.AppendLine(r.Snippet);
            sb.AppendLine();
        }
        sb.AppendLine($"Question: {prompt}");

        return new List<ChatMessage>
        {
            ChatMessage.System(
                "Answer the question using the web search results below. Cite sources inline with " +
                "bracketed numbers like [1]. If the results are insufficient, say so."),
            ChatMessage.User(sb.ToString())
        };
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        StatusText = "";
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            // UseShellExecute lets the OS pick the default browser on both Windows and Linux.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening a link should never crash the app.
        }
    }

    private void RequestScroll() => ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
}
