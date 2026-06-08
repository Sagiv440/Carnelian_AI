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
    /// <summary>
    /// Base (mode-neutral) chat instructions. The active agent's persona is layered on top via
    /// <see cref="AgentPromptBuilder"/>, so this is no longer the whole system prompt.
    /// </summary>
    private const string ChatBaseInstructions =
        "You are running locally via Ollama. Be accurate and concise.";

    private readonly IModelRouter _router;
    private readonly IWebSearchService _search;
    private readonly IDeepResearchService _research;
    private readonly ISettingsService _settings;
    private readonly IAttachmentService _attachments;
    private readonly IChatHistoryService _history;
    private readonly IProjectAgentService _agent;
    private readonly IProjectSkillService _skills;
    private readonly ISpeechService _speech;
    private readonly IAgentService _agents;

    /// <summary>Skill files loaded from the active project (empty when not in a project).</summary>
    private IReadOnlyList<ProjectSkill> _projectSkills = Array.Empty<ProjectSkill>();

    private CancellationTokenSource? _cts;

    /// <summary>The conversation currently shown in the transcript.</summary>
    private ChatSession _currentSession = new();

    /// <summary>Raised when the view should scroll the transcript to the bottom.</summary>
    public event EventHandler? ScrollToEndRequested;

    /// <summary>Raised when the view should open the Settings window (a view-only concern).</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the view should open a file picker for the given attachment kind.</summary>
    public event EventHandler<AttachmentKind>? AttachFilesRequested;

    /// <summary>Raised when the view should open the New Project window (a view-only concern).</summary>
    public event EventHandler? ProjectRequested;

    /// <summary>Raised when the project agent needs the user to approve a tool call.</summary>
    public event EventHandler<ToolApprovalEventArgs>? ToolApprovalRequested;

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    /// <summary>Models offered in the top-bar picker, across every configured provider.</summary>
    public ObservableCollection<ChatModel> Models { get; } = new();

    /// <summary>Agents offered in the top-bar picker (built-in + global + the active project's customs).</summary>
    public ObservableCollection<Agent> Agents { get; } = new();
    public IReadOnlyList<ModeOption> Modes { get; }

    /// <summary>The composer's search-scope dropdown options (Local / Web / Deep), a subset of <see cref="Modes"/>.</summary>
    public IReadOnlyList<ModeOption> SearchModes { get; }

    /// <summary>Saved conversations, newest first (the sidebar "Chat Log").</summary>
    public ObservableCollection<ChatSession> ChatLog { get; } = new();

    /// <summary>Files staged for the next prompt (shown as chips in the composer).</summary>
    public ObservableCollection<Attachment> Attachments { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _hasAttachments;

    /// <summary>Per-prompt toggle: when on, the model is asked to plan before answering (depth = Effort setting).</summary>
    [ObservableProperty]
    private bool _thinkingEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private ChatModel? _selectedModel;

    /// <summary>The active agent whose persona is applied to every mode's system prompt.</summary>
    [ObservableProperty]
    private Agent? _selectedAgent;

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

    /// <summary>The active project, or null when not in project mode. Single active project per session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveProject))]
    [NotifyPropertyChangedFor(nameof(ActiveProjectName))]
    [NotifyPropertyChangedFor(nameof(ActiveProjectDirectory))]
    private Project? _activeProject;

    public bool HasActiveProject => ActiveProject is not null;
    public string ActiveProjectName => ActiveProject?.Name ?? "";
    public string ActiveProjectDirectory => ActiveProject?.Directory ?? "";

    /// <summary>Number of skill files loaded from the active project (shown in the project card).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjectSkills))]
    [NotifyPropertyChangedFor(nameof(ProjectSkillSummary))]
    private int _projectSkillCount;

    public bool HasProjectSkills => ProjectSkillCount > 0;
    public string ProjectSkillSummary => ProjectSkillCount == 1 ? "1 skill loaded" : $"{ProjectSkillCount} skills loaded";

    /// <summary>Sidebar tab state (only meaningful while a project is loaded): false = Chat Log, true = Files.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatLogTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsFilesTabSelected))]
    private bool _showProjectFiles;

    public bool IsChatLogTabSelected => !ShowProjectFiles;
    public bool IsFilesTabSelected => ShowProjectFiles;

    /// <summary>Root of the project file tree (one node = the project directory) for the Files tab.</summary>
    public ObservableCollection<FileNode> FileTree { get; } = new();

    public MainWindowViewModel(
        IModelRouter router,
        IWebSearchService search,
        IDeepResearchService research,
        ISettingsService settings,
        IAttachmentService attachments,
        IChatHistoryService history,
        IProjectAgentService agent,
        IProjectSkillService skills,
        ISpeechService speech,
        IAgentService agents)
    {
        _router = router;
        _search = search;
        _research = research;
        _settings = settings;
        _attachments = attachments;
        _history = history;
        _agent = agent;
        _skills = skills;
        _speech = speech;
        _agents = agents;

        Modes = new[]
        {
            new ModeOption(AppMode.Chat, "Chat", "Talk directly to the local model."),
            new ModeOption(AppMode.WebSearch, "Web Search", "Search the web once, then answer with citations."),
            new ModeOption(AppMode.DeepResearch, "Deep Research", "Plan queries, read pages, synthesize a cited report."),
            new ModeOption(AppMode.Project, "Project", "Use tools to edit files and run commands in a project directory.")
        };
        SearchModes = new[]
        {
            new ModeOption(AppMode.Chat, "💬  Local search", "Answer with the selected model only — no web."),
            new ModeOption(AppMode.WebSearch, "🌐  Web search", "Search the web once, then answer with citations."),
            new ModeOption(AppMode.DeepResearch, "🔎  Deep search", "Plan queries, read pages, then synthesize a cited report.")
        };
        _selectedMode = Modes[0];
        _ollamaBaseUrl = settings.Current.OllamaBaseUrl;
        // The saved selection is restored after the model list loads (see RefreshAsync).

        LoadAgents(); // built-in roster + global customs (project customs join on project activation)

        foreach (var session in _history.Load())
            ChatLog.Add(session);
    }

    // Design-time / fallback constructor so the XAML previewer can instantiate the window.
    public MainWindowViewModel()
        : this(new DesignModelRouter(), new DesignWebSearchService(),
               new DesignDeepResearchService(), new DesignSettingsService(),
               new DesignAttachmentService(), new DesignChatHistoryService(),
               new DesignProjectAgentService(), new DesignProjectSkillService(),
               new DesignSpeechService(), new DesignAgentService())
    {
    }

    partial void OnOllamaBaseUrlChanged(string value)
    {
        // Keep the live setting in sync so the Ollama client picks up the new URL immediately.
        _settings.Current.OllamaBaseUrl = value.Trim();
    }

    partial void OnSelectedModelChanged(ChatModel? value)
    {
        // Persist as "{provider}:{id}" so a cloud model round-trips by provider, not just by id.
        _settings.Current.DefaultModel = value is null ? null : $"{value.Provider}:{value.Id}";
        _settings.Save();
    }

    partial void OnSelectedAgentChanged(Agent? value)
    {
        // Persist the chosen agent's id so it's restored next launch (like DefaultModel).
        if (value is not null)
        {
            _settings.Current.ActiveAgentId = value.Id;
            _settings.Save();
        }
    }

    /// <summary>The active agent's persona block, prepended to every mode's system prompt (empty = none).</summary>
    private string PersonaPrefix() => AgentPromptBuilder.PersonaPrefix(SelectedAgent);

    /// <summary>
    /// Rebuilds the agent picker from the registry (built-in + global + the active project's customs)
    /// and restores the saved selection. Called on construction, on project enter/exit, and after the
    /// Settings → Agents panel may have changed the roster.
    /// </summary>
    public void LoadAgents()
    {
        var previousId = SelectedAgent?.Id ?? _settings.Current.ActiveAgentId;

        Agents.Clear();
        foreach (var agent in _agents.ListAgents(ActiveProject?.Directory))
            Agents.Add(agent);

        SelectedAgent =
            Agents.FirstOrDefault(a => string.Equals(a.Id, previousId, StringComparison.OrdinalIgnoreCase))
            ?? Agents.FirstOrDefault(a => string.Equals(a.Id, _agents.Default.Id, StringComparison.OrdinalIgnoreCase))
            ?? Agents.FirstOrDefault();
    }

    /// <summary>Parses a persisted "{provider}:{id}" selection back into a <see cref="ChatModel"/>.</summary>
    private static ChatModel? ParseSavedModel(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
            return null;

        var sep = saved.IndexOf(':');
        if (sep > 0 && Enum.TryParse<AiProvider>(saved[..sep], out var provider))
        {
            var id = saved[(sep + 1)..];
            if (!string.IsNullOrEmpty(id))
                return new ChatModel(provider, id);
        }

        // Legacy value (a bare Ollama model name with no provider prefix) — treat as Ollama.
        return new ChatModel(AiProvider.Ollama, saved);
    }

    /// <summary>
    /// The composer's Local / Web / Deep search dropdown selection, mapped to the active
    /// <see cref="AppMode"/>. (In Project mode the dropdown is hidden; the getter falls back to the first option.)
    /// </summary>
    public ModeOption SelectedSearchMode
    {
        get => SearchModes.FirstOrDefault(o => o.Mode == SelectedMode.Mode) ?? SearchModes[0];
        set { if (value is not null) SetMode(value.Mode); }
    }

    partial void OnSelectedModeChanged(ModeOption value) =>
        OnPropertyChanged(nameof(SelectedSearchMode));

    private void SetMode(AppMode mode)
    {
        var option = Modes.FirstOrDefault(o => o.Mode == mode);
        if (option is not null)
            SelectedMode = option;
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

        IReadOnlyList<ChatModel> models;
        try
        {
            // The router queries every configured provider best-effort and aggregates the results.
            models = await _router.ListAllModelsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = $"Error listing models: {ex.Message}";
            Models.Clear();
            return;
        }

        Models.Clear();
        foreach (var m in models)
            Models.Add(m);

        // Connected if ANY provider returned at least one model.
        IsConnected = Models.Count > 0;
        if (!IsConnected)
        {
            ConnectionStatus =
                $"Offline — is Ollama running at {OllamaBaseUrl}, or add a cloud API key in Settings → AI Model?";
            SelectedModel = null;
            return;
        }

        var providerCount = Models.Select(m => m.Provider).Distinct().Count();
        ConnectionStatus = providerCount == 1
            ? $"Connected — {Models.Count} model(s)"
            : $"Connected — {Models.Count} model(s) across {providerCount} provider(s)";

        // Restore the saved selection if still present, otherwise pick the first model.
        var saved = ParseSavedModel(_settings.Current.DefaultModel);
        SelectedModel = saved is not null && Models.Contains(saved)
            ? saved
            : Models.FirstOrDefault();
    }

    private bool CanSend =>
        !IsBusy && (!string.IsNullOrWhiteSpace(InputText) || HasAttachments) && SelectedModel is not null;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var prompt = InputText.Trim();
        var selected = SelectedModel!;
        var client = _router.For(selected.Provider);
        var model = selected.Id;
        InputText = "";

        // Snapshot and clear the staged attachments.
        var attachments = Attachments.ToList();
        Attachments.Clear();
        HasAttachments = false;

        // The per-prompt toggle upgrades a plain chat into a web-searched answer.
        var mode = SelectedMode.Mode;

        var user = new MessageViewModel(ChatRole.User, prompt);
        user.SetAttachments(attachments);
        Messages.Add(user);
        // Stamp the reply with the active agent's identity (glyph + name show in the header; the model id
        // moves to a tooltip). The agent's persona is layered into the system prompt for every mode below.
        var agent = SelectedAgent;
        var assistant = new MessageViewModel(ChatRole.Assistant)
        {
            IsStreaming = true,
            ModelName = model,
            AgentGlyph = agent?.Glyph,
            AgentName = agent?.Name
        };
        Messages.Add(assistant);
        RequestScroll();

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            if (attachments.Count > 0)
            {
                StatusText = "Reading attachments…";
                await ProcessAttachmentsAsync(attachments, user, ct);
            }

            switch (mode)
            {
                case AppMode.Chat:
                    await RunChatAsync(client, model, assistant, ct);
                    break;
                case AppMode.WebSearch:
                    await RunWebSearchAsync(client, model, prompt, user, assistant, ct);
                    break;
                case AppMode.DeepResearch:
                    await RunDeepResearchAsync(client, model, prompt, user, assistant, ct);
                    break;
                case AppMode.Project:
                    await RunProjectAgentAsync(client, model, assistant, ct);
                    break;
            }

            if (assistant.Text.Length == 0 && assistant.Work.Length == 0)
                assistant.Text = "_(no response)_";
        }
        catch (OperationCanceledException)
        {
            assistant.Append(assistant.Text.Length == 0 ? "_(stopped)_" : "\n\n_(stopped)_");
        }
        catch (Exception ex)
        {
            assistant.Text = $"⚠️ {ex.Message}";
            if (user.Images.Count > 0)
                assistant.Append(
                    "\n\nℹ️ Image input requires a vision-capable model (your current model is text-only). " +
                    "Install one — e.g. `ollama pull llava` or `ollama pull llama3.2-vision` — then select it in the sidebar.");
        }
        finally
        {
            assistant.IsStreaming = false;
            IsBusy = false;
            StatusText = "";
            _cts?.Dispose();
            _cts = null;
            RequestScroll();
            PersistCurrentSession(); // keep the chat log up to date after every turn
        }
    }

    /// <summary>Reads each attachment: images become base64 (vision), PDFs become extracted text context.</summary>
    private async Task ProcessAttachmentsAsync(
        List<Attachment> attachments, MessageViewModel user, CancellationToken ct)
    {
        var images = new List<string>();
        var docs = new StringBuilder();

        foreach (var a in attachments)
        {
            ct.ThrowIfCancellationRequested();
            if (a.Kind == AttachmentKind.Photo)
            {
                var b64 = await _attachments.ReadImageBase64Async(a.Path, ct);
                if (!string.IsNullOrEmpty(b64))
                    images.Add(b64);
            }
            else
            {
                var text = await _attachments.ExtractTextAsync(a.Path, _settings.Current.MaxCharsPerPage, ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    docs.AppendLine($"--- {a.FileName} ---");
                    docs.AppendLine(text);
                    docs.AppendLine();
                }
            }
        }

        user.Images = images;
        user.AttachedContext = docs.ToString().Trim();
    }

    private async Task RunChatAsync(IChatClient client, string model, MessageViewModel assistant, CancellationToken ct)
    {
        var history = BuildChatHistory(assistant);
        var raw = new StringBuilder();
        // No ConfigureAwait(false): stream deltas must apply on the UI thread.
        await foreach (var delta in client.ChatStreamAsync(model, history, ThinkingEnabled, ct))
        {
            ApplyStreamDelta(assistant, raw, delta);
            RequestScroll();
        }
    }

    /// <summary>
    /// Accumulates a streamed reply and re-splits it into the visible answer and the model's hidden
    /// reasoning (<c>&lt;think&gt;…&lt;/think&gt;</c>), so reasoning shows in the collapsible block, not the answer.
    /// </summary>
    private static void ApplyStreamDelta(MessageViewModel assistant, StringBuilder raw, string delta)
    {
        raw.Append(delta);
        var (reasoning, answer) = ReasoningSplit.Split(raw.ToString());
        assistant.SetWork(reasoning);
        assistant.Text = answer;
    }

    private async Task RunWebSearchAsync(
        IChatClient client, string model, string prompt, MessageViewModel user,
        MessageViewModel assistant, CancellationToken ct)
    {
        StatusText = "Searching the web…";
        var results = await _search.SearchAsync(prompt, _settings.Current.SearchResultsPerQuery, ct);

        if (results.Count == 0)
        {
            assistant.Text = "I couldn't find any web results. Check your connection or rephrase.";
            return;
        }

        StatusText = $"Found {results.Count} results — answering…";
        var messages = BuildWebSearchMessages(
            WithAttachedContext(prompt, user), results, user.Images, PersonaPrefix(), ThinkingDirective());
        var raw = new StringBuilder();
        await foreach (var delta in client.ChatStreamAsync(model, messages, ThinkingEnabled, ct))
        {
            ApplyStreamDelta(assistant, raw, delta);
            RequestScroll();
        }
        assistant.SetSources(results);
    }

    private async Task RunDeepResearchAsync(
        IChatClient client, string model, string prompt, MessageViewModel user,
        MessageViewModel assistant, CancellationToken ct)
    {
        // Progress is constructed on the UI thread, so its callbacks marshal back automatically.
        var progress = new Progress<string>(s => StatusText = s);
        var raw = new StringBuilder();

        var sources = await _research.RunAsync(
            client,
            WithAttachedContext(prompt, user),
            model,
            PersonaPrefix(),
            progress,
            // The research service streams on a background thread; marshal each token to the UI.
            delta => Dispatcher.UIThread.Post(() =>
            {
                ApplyStreamDelta(assistant, raw, delta);
                RequestScroll();
            }),
            ct);

        assistant.SetSources(sources);
    }

    private async Task RunProjectAgentAsync(
        IChatClient client, string model, MessageViewModel assistant, CancellationToken ct)
    {
        if (ActiveProject is null)
        {
            assistant.Text = "No active project. Click “📁 Project” in the sidebar to create one.";
            return;
        }

        // Progress is constructed on the UI thread, so its callbacks marshal back automatically.
        var progress = new Progress<string>(s => StatusText = s);
        var conversation = BuildAgentConversation(assistant);

        // The agent runs on background threads; marshal its callbacks back to the UI.
        // Activity (the 🔧 action log) goes to the collapsible "work" block; the final reply to the answer.
        await _agent.RunAsync(
            client, ActiveProject, model, conversation, _settings.Current.AgentApproval,
            PersonaPrefix(), ThinkingDirective(), ProjectSkillsContext(), _settings.Current.SoftwareInstall, progress,
            activity => Dispatcher.UIThread.Post(() =>
            {
                assistant.AppendWork(activity);
                RequestScroll();
            }),
            answer => Dispatcher.UIThread.Post(() =>
            {
                var (reasoning, text) = ReasoningSplit.Split(answer);
                if (!string.IsNullOrEmpty(reasoning))
                    assistant.AppendWork(reasoning);
                assistant.Append(text);
                RequestScroll();
            }),
            RequestToolApprovalAsync,
            ct);
    }

    /// <summary>Asks the view to confirm a tool call (raised on a background thread, marshalled to the UI).</summary>
    private Task<bool> RequestToolApprovalAsync(ToolApprovalRequest request)
    {
        if (ToolApprovalRequested is null)
            return Task.FromResult(true); // no UI wired (e.g. design time) → allow

        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.UIThread.Post(() =>
            ToolApprovalRequested?.Invoke(this, new ToolApprovalEventArgs(request, tcs)));
        return tcs.Task;
    }

    /// <summary>Builds the user/assistant turns for the agent; it prepends its own system prompt.</summary>
    private List<ChatMessage> BuildAgentConversation(MessageViewModel pendingAssistant)
    {
        var conversation = new List<ChatMessage>();
        foreach (var m in Messages)
        {
            if (ReferenceEquals(m, pendingAssistant))
                continue;
            if (m.Role != ChatRole.User && m.Role != ChatRole.Assistant)
                continue;
            if (string.IsNullOrEmpty(m.Text) && string.IsNullOrEmpty(m.AttachedContext))
                continue;

            var content = m.Text;
            if (!string.IsNullOrEmpty(m.AttachedContext))
                content += $"\n\n[Attached documents]\n{m.AttachedContext}";

            conversation.Add(new ChatMessage(m.Role, content));
        }
        return conversation;
    }

    /// <summary>
    /// Planning instruction appended to a system prompt when Thinking is on, scaled by the Effort
    /// setting. Empty when the toggle is off so behaviour is unchanged.
    /// </summary>
    private string ThinkingDirective()
    {
        if (!ThinkingEnabled)
            return "";

        var depth = _settings.Current.ThinkingEffort switch
        {
            <= 33 => "Before answering, briefly think about what the user needs and sketch a quick plan " +
                     "(a sentence or two), then respond.",
            <= 66 => "Before answering, think step by step about what the task requires and outline your " +
                     "approach, then carry it out.",
            _ => "Before answering, think carefully and thoroughly: break the task into steps, consider " +
                 "edge cases and alternatives, lay out a clear plan, and only then execute it. " +
                 "Prioritise correctness over speed."
        };

        return "\n\n" + depth;
    }

    private List<ChatMessage> BuildChatHistory(MessageViewModel pendingAssistant)
    {
        // System prompt = active agent's persona → base chat instructions → Thinking directive.
        var systemPrompt = AgentPromptBuilder.Compose(SelectedAgent, ChatBaseInstructions, ThinkingDirective());
        var history = new List<ChatMessage> { ChatMessage.System(systemPrompt) };
        foreach (var m in Messages)
        {
            if (ReferenceEquals(m, pendingAssistant))
                continue; // skip the empty placeholder we're about to fill
            if (string.IsNullOrEmpty(m.Text) && m.Images.Count == 0 && string.IsNullOrEmpty(m.AttachedContext))
                continue;

            var content = m.Text;
            if (!string.IsNullOrEmpty(m.AttachedContext))
                content += $"\n\n[Attached documents]\n{m.AttachedContext}";

            history.Add(new ChatMessage(m.Role, content, m.Images.Count > 0 ? m.Images : null));
        }
        return history;
    }

    private static string WithAttachedContext(string prompt, MessageViewModel user) =>
        string.IsNullOrEmpty(user.AttachedContext)
            ? prompt
            : $"{prompt}\n\n[Attached documents]\n{user.AttachedContext}";

    private static List<ChatMessage> BuildWebSearchMessages(
        string prompt, IReadOnlyList<SearchResult> results, IReadOnlyList<string> images,
        string personaPrefix, string thinkingDirective)
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
                personaPrefix +
                "Answer the question using the web search results below. Cite sources inline with " +
                "bracketed numbers like [1]. If the results are insufficient, say so." + thinkingDirective),
            new ChatMessage(ChatRole.User, sb.ToString(), images.Count > 0 ? images : null)
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

    /// <summary>Sidebar "New Chat": save the current conversation, then start a fresh one (staying in
    /// the active project, if any, so the new chat is still saved under it).</summary>
    [RelayCommand]
    private void NewChat()
    {
        PersistCurrentSession();
        _currentSession = new ChatSession();
        Messages.Clear();
        StatusText = "";
        SetMode(ActiveProject is not null ? AppMode.Project : AppMode.Chat);
    }

    /// <summary>Sidebar "Project" button: ask the view to open the New Project window.</summary>
    [RelayCommand]
    private void OpenProject() => ProjectRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Sidebar tab: show the chat log.</summary>
    [RelayCommand]
    private void ShowChatLog() => ShowProjectFiles = false;

    /// <summary>Sidebar tab: show the project file tree (and refresh it to reflect any agent changes).</summary>
    [RelayCommand]
    private void ShowFiles()
    {
        ShowProjectFiles = true;
        LoadFileTree();
    }

    /// <summary>Re-read the project file tree from disk.</summary>
    [RelayCommand]
    private void RefreshFiles() => LoadFileTree();

    /// <summary>Rebuilds the file tree from the active project's directory (root node, expanded).</summary>
    private void LoadFileTree()
    {
        FileTree.Clear();
        if (ActiveProject is null)
            return;

        var root = new FileNode(ActiveProject.Directory, isDirectory: true) { IsExpanded = true };
        FileTree.Add(root);
    }

    /// <summary>
    /// Called by the view once a project is created/opened: enter project (agent) mode, load that
    /// project's saved chats (from <c>.AI/chats</c>) into the log, and scan it for skill files.
    /// </summary>
    public async Task ActivateProjectAsync(Project project)
    {
        PersistCurrentSession();          // save whatever conversation we're leaving (to the old store)
        _currentSession = new ChatSession();
        Messages.Clear();
        StatusText = "";
        ActiveProject = project;          // project store is now active
        SetMode(AppMode.Project);
        ShowProjectFiles = false;         // start on the Chat Log tab
        LoadLog();                        // load this project's chats into the sidebar log
        LoadFileTree();                   // build the Files tab tree
        LoadAgents();                     // surface this project's custom agents in the picker
        await LoadProjectSkillsAsync(project);
    }

    /// <summary>Leave project mode, restore the global chat log, and drop the project's skills.</summary>
    [RelayCommand]
    private void ExitProject()
    {
        PersistCurrentSession();          // save to the project store while it's still active
        _currentSession = new ChatSession();
        Messages.Clear();
        StatusText = "";
        ActiveProject = null;             // global store is now active
        _projectSkills = Array.Empty<ProjectSkill>();
        ProjectSkillCount = 0;
        ShowProjectFiles = false;
        FileTree.Clear();
        LoadLog();                        // reload the global chat log
        LoadAgents();                     // drop the project's custom agents from the picker
        SetMode(AppMode.Chat);
    }

    /// <summary>Loads the active project's skill files off the UI thread and updates the count.</summary>
    private async Task LoadProjectSkillsAsync(Project project)
    {
        var skills = await Task.Run(() => _skills.Load(project.Directory)).ConfigureAwait(true);
        _projectSkills = skills;
        ProjectSkillCount = skills.Count;
    }

    /// <summary>Replaces the sidebar chat log with the currently active store's sessions.</summary>
    private void LoadLog()
    {
        ChatLog.Clear();
        var sessions = ActiveProject is not null
            ? _history.LoadFrom(ActiveProject.Directory)
            : _history.Load();
        foreach (var session in sessions)
            ChatLog.Add(session);
    }

    /// <summary>Persists the chat log to whichever store is active: the project's, or the global one.</summary>
    private void SaveLog()
    {
        if (ActiveProject is not null)
            _history.SaveTo(ActiveProject.Directory, ChatLog.ToList());
        else
            _history.Save(ChatLog.ToList());
    }

    /// <summary>Combined skill text appended to the agent's system prompt (empty when no skills).</summary>
    private string ProjectSkillsContext()
    {
        if (_projectSkills.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("The user has added project skills. Treat them as authoritative guidance for this project:");
        foreach (var skill in _projectSkills)
        {
            sb.AppendLine();
            sb.AppendLine($"--- skill: {skill.Name} ---");
            sb.AppendLine(skill.Content);
        }
        return sb.ToString();
    }

    /// <summary>Open a saved conversation from the chat log.</summary>
    [RelayCommand]
    private void OpenSession(ChatSession? session)
    {
        if (session is null || ReferenceEquals(session, _currentSession))
            return;

        PersistCurrentSession(); // save the conversation we're leaving
        Messages.Clear();
        foreach (var turn in session.Messages)
            Messages.Add(new MessageViewModel(turn.Role, turn.Text) { ModelName = turn.ModelName });

        _currentSession = session;
        // Project sessions need a live active project (not persisted); fall back to chat without one.
        SetMode(session.Mode == AppMode.Project && ActiveProject is null ? AppMode.Chat : session.Mode);
        StatusText = "";
        RequestScroll();
    }

    /// <summary>Delete a saved conversation from the chat log.</summary>
    [RelayCommand]
    private void DeleteSession(ChatSession? session)
    {
        if (session is null)
            return;

        ChatLog.Remove(session);
        SaveLog();

        if (ReferenceEquals(session, _currentSession))
        {
            _currentSession = new ChatSession();
            Messages.Clear();
            StatusText = "";
            SetMode(ActiveProject is not null ? AppMode.Project : AppMode.Chat);
        }
    }

    /// <summary>Captures the transcript into the current session and persists the whole log.</summary>
    private void PersistCurrentSession()
    {
        var turns = new List<ChatTurn>();
        foreach (var m in Messages)
        {
            if (string.IsNullOrEmpty(m.Text))
                continue;
            turns.Add(new ChatTurn { Role = m.Role, Text = m.Text, ModelName = m.ModelName });
        }

        if (turns.Count == 0)
            return; // nothing worth logging

        _currentSession.Messages = turns;
        _currentSession.Mode = SelectedMode.Mode;
        _currentSession.Title = BuildSessionTitle(turns);

        if (!ChatLog.Contains(_currentSession))
            ChatLog.Insert(0, _currentSession);

        SaveLog();
    }

    private static string BuildSessionTitle(List<ChatTurn> turns)
    {
        var firstUser = turns.FirstOrDefault(t => t.Role == ChatRole.User);
        var text = firstUser?.Text.Replace('\n', ' ').Trim();
        if (string.IsNullOrEmpty(text))
            return "New chat";
        return text.Length <= 40 ? text : text[..40] + "…";
    }

    /// <summary>Re-submit a user message's prompt as a new turn (the ↻ button on user bubbles).</summary>
    [RelayCommand]
    private void RerunPrompt(MessageViewModel? message)
    {
        if (message is null || IsBusy || !message.IsUser || string.IsNullOrWhiteSpace(message.Text))
            return;

        InputText = message.Text;
        if (SendCommand.CanExecute(null))
            SendCommand.Execute(null);
    }

    /// <summary>Read an assistant message aloud (the 🔈 button); clicking again stops it.</summary>
    [RelayCommand]
    private async Task SpeakMessage(MessageViewModel? message)
    {
        if (message is null)
            return;

        // Stop whatever is currently playing (covers both "stop this one" and "switch to another").
        var wasSpeaking = message.IsSpeaking;
        await _speech.StopAsync();
        if (wasSpeaking)
            return; // toggled off — the in-flight call clears its own IsSpeaking in its finally

        if (!_speech.IsConfigured)
        {
            StatusText = "No voice configured — choose one in Settings → Voice.";
            return;
        }

        message.IsSpeaking = true;
        try
        {
            await _speech.SpeakAsync(message.Text);
        }
        catch (Exception ex)
        {
            StatusText = $"Voice error: {ex.Message}";
        }
        finally
        {
            message.IsSpeaking = false;
        }
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AttachPhotos() => AttachFilesRequested?.Invoke(this, AttachmentKind.Photo);

    [RelayCommand]
    private void AttachDocuments() => AttachFilesRequested?.Invoke(this, AttachmentKind.Document);

    [RelayCommand]
    private void RemoveAttachment(Attachment? attachment)
    {
        if (attachment is not null && Attachments.Remove(attachment))
            HasAttachments = Attachments.Count > 0;
    }

    /// <summary>Adds files chosen in the file dialog (called by the view), de-duplicating by path.</summary>
    public void AddAttachments(IEnumerable<Attachment> items)
    {
        foreach (var a in items)
        {
            if (Attachments.Any(x => string.Equals(x.Path, a.Path, StringComparison.OrdinalIgnoreCase)))
                continue;
            Attachments.Add(a);
        }
        HasAttachments = Attachments.Count > 0;
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
