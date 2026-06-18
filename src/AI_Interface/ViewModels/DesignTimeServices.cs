using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;

namespace AI_Interface.ViewModels;

// Lightweight stand-ins used only by the XAML previewer's design-time DataContext
// (MainWindowViewModel's parameterless constructor). They never run at runtime, where
// real services are injected via DI.

internal sealed class DesignOllamaClient : IOllamaClient
{
    public AiProvider Provider => AiProvider.Ollama;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> IsConfiguredAndReachableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> PingAsync(string baseUrl, CancellationToken ct = default) => Task.FromResult(true);

    public Task PullModelAsync(string name, IProgress<string>? progress, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task DeleteModelAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(new[] { "llama3:latest", "mistral:latest" });

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model, IEnumerable<ChatMessage> messages, bool think,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield return "This is a design-time preview response.";
    }

    public Task<string> CompleteAsync(
        string model, IEnumerable<ChatMessage> messages, CancellationToken ct = default) =>
        Task.FromResult("design-time");

    public Task<AgentTurn> ChatWithToolsAsync(
        string model, IEnumerable<ChatMessage> messages,
        IReadOnlyList<AgentTool> tools, CancellationToken ct = default) =>
        Task.FromResult(new AgentTurn("design-time", Array.Empty<AgentToolCall>()));
}

/// <summary>Design-time cloud chat client: reports "not configured" so it contributes no models.</summary>
internal sealed class DesignCloudClient : IOpenAiClient, IGeminiClient, IAnthropicClient
{
    private readonly AiProvider _provider;
    public DesignCloudClient(AiProvider provider) => _provider = provider;

    public AiProvider Provider => _provider;

    public Task<bool> IsConfiguredAndReachableAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model, IEnumerable<ChatMessage> messages, bool think,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield return "design-time";
    }

    public Task<string> CompleteAsync(
        string model, IEnumerable<ChatMessage> messages, CancellationToken ct = default) =>
        Task.FromResult("design-time");

    public Task<AgentTurn> ChatWithToolsAsync(
        string model, IEnumerable<ChatMessage> messages,
        IReadOnlyList<AgentTool> tools, CancellationToken ct = default) =>
        Task.FromResult(new AgentTurn("design-time", Array.Empty<AgentToolCall>()));
}

internal sealed class DesignModelRouter : IModelRouter
{
    private readonly DesignOllamaClient _ollama = new();

    public Task<IReadOnlyList<ChatModel>> ListAllModelsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChatModel>>(new[]
        {
            new ChatModel(AiProvider.Ollama, "llama3:latest"),
            new ChatModel(AiProvider.Ollama, "mistral:latest")
        });

    public IChatClient For(AiProvider provider) => provider == AiProvider.Ollama
        ? _ollama
        : new DesignCloudClient(provider);
}

internal sealed class DesignWebSearchService : IWebSearchService
{
    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SearchResult>>(new[]
        {
            new SearchResult { Title = "Example result", Url = "https://example.com", Snippet = "Sample snippet." }
        });

    public Task<string> FetchReadableTextAsync(string url, int maxChars, CancellationToken ct = default) =>
        Task.FromResult("Sample page text.");
}

internal sealed class DesignDeepResearchService : IDeepResearchService
{
    public Task<IReadOnlyList<SearchResult>> RunAsync(
        IChatClient client, string question, string model, string personaPrefix, IProgress<string> status,
        Action<string> onAnswerDelta, ModelEndpoint? planner = null, ModelEndpoint? synthesizer = null,
        CancellationToken ct = default)
    {
        onAnswerDelta("Design-time research answer.");
        return Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());
    }
}

internal sealed class DesignSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new();
    public void Save() { }
}

internal sealed class DesignSpeechService : ISpeechService
{
    public bool IsConfigured => false;
    public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
}

internal sealed class DesignPiperInstaller : IPiperInstaller
{
    public string EngineDirectory => "";
    public string VoicesDirectory => "";
    public string? ResolvedExecutablePath => null;
    public bool IsEngineInstalled => false;
    public Task<string> InstallEngineAsync(IProgress<string>? progress, CancellationToken ct) =>
        Task.FromResult("");
}

internal sealed class DesignOllamaInstaller : IOllamaInstaller
{
    public bool IsOllamaInstalled => false;
    public Task InstallAsync(IProgress<string>? progress, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class DesignSearxngInstaller : ISearxngInstaller
{
    public string LocalUrl => "http://localhost:8888";
    public Task<bool> IsRunningAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task InstallAsync(IProgress<string>? progress, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveAsync(IProgress<string>? progress, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class DesignUsageTracker : IUsageTracker
{
    public void RecordEstimatedUsage(AiProvider provider, string modelId, string? inputText, string? outputText) { }
}

internal sealed class DesignPiperVoiceCatalog : IPiperVoiceCatalog
{
    public Task<IReadOnlyList<PiperVoiceInfo>> ListAvailableAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PiperVoiceInfo>>(new[]
        {
            new PiperVoiceInfo
            {
                Key = "en_US-amy-medium", LanguageCode = "en_US", LanguageFamily = "en",
                LanguageName = "English (United States)", Name = "amy", Quality = "medium",
                OnnxRepoPath = "", OnnxJsonRepoPath = "", SizeBytes = 63_000_000
            }
        });

    public Task DownloadAsync(PiperVoiceInfo voice, IProgress<string>? progress, CancellationToken ct) =>
        Task.CompletedTask;

    public void Delete(PiperVoiceInfo voice) { }
    public bool IsDownloaded(PiperVoiceInfo voice) => false;
    public string? ResolveModelPathForLanguage(string languageFamily) => null;
    public string? AnyInstalledModelPath() => null;
}

internal sealed class DesignAttachmentService : IAttachmentService
{
    public Task<string> ExtractTextAsync(string path, int maxChars, CancellationToken ct = default) =>
        Task.FromResult("");

    public Task<string> ReadImageBase64Async(string path, CancellationToken ct = default) =>
        Task.FromResult("");
}

internal sealed class DesignChatHistoryService : IChatHistoryService
{
    public IReadOnlyList<ChatSession> Load() => Array.Empty<ChatSession>();
    public void Save(IReadOnlyList<ChatSession> sessions) { }
    public IReadOnlyList<ChatSession> LoadFrom(string projectDirectory) => Array.Empty<ChatSession>();
    public void SaveTo(string projectDirectory, IReadOnlyList<ChatSession> sessions) { }
}

internal sealed class DesignProjectSkillService : IProjectSkillService
{
    public IReadOnlyList<ProjectSkill> Load(string projectDirectory) => Array.Empty<ProjectSkill>();
}

internal sealed class DesignProjectDocsService : IProjectDocsService
{
    public string Load(string projectDirectory) => "";
    public string Save(string projectDirectory, string content) => "";
}

internal sealed class DesignHardwareService : IHardwareService
{
    public Task<HardwareInfo> ScanAsync(CancellationToken ct = default) =>
        Task.FromResult(new HardwareInfo
        {
            CpuName = "Design CPU", CpuCores = 8, TotalRamGb = 16, GpuName = "Design GPU", VramGb = 8
        });
}

internal sealed class DesignProjectAgentService : IProjectAgentService
{
    public Task RunAsync(
        IChatClient client, Project project, string model, IReadOnlyList<ChatMessage> conversation,
        AgentApprovalMode approvalMode, int maxSteps, AgentTools allowedTools, string personaPrefix,
        string thinkingDirective, string projectSkills, SoftwareInstallPermission installPermission,
        bool memoryEnabled, bool allowDocsUpdate, IProgress<string> status, Action<string> onActivity,
        Action<ActivityUpdate>? onActivityStep, Action<PlanUpdate>? onPlan, Action<string> onAnswer,
        Func<ToolApprovalRequest, Task<bool>> approve, bool autoFlowPhases,
        Func<PhaseGate, Task<bool>>? phaseGate, Func<UserClarificationRequest, Task<string?>>? askUser,
        CancellationToken ct)
    {
        onAnswer("Design-time project agent response.");
        return Task.CompletedTask;
    }
}

internal sealed class DesignAgentOrchestrator : IAgentOrchestrator
{
    public Task RunAsync(
        Agent lead, IChatClient leadClient, string leadModel, Project project,
        IReadOnlyList<ChatMessage> conversation, string memoryBlock, bool memoryEnabled, string projectSkills,
        string thinkingDirective, SoftwareInstallPermission installPermission, AgentApprovalMode approval,
        IProgress<string> status,
        Action<ActivityUpdate> onActivityStep, Action<string> onAnswer, Action<DelegationUpdate> onDelegation,
        Action<PlanUpdate>? onPlan, Func<ToolApprovalRequest, Task<bool>> approve,
        bool autoFlowPhases, Func<PhaseGate, Task<bool>>? phaseGate,
        Func<UserClarificationRequest, Task<string?>>? askUser, CancellationToken ct)
    {
        onAnswer("Design-time orchestrator response.");
        return Task.CompletedTask;
    }
}

internal sealed class DesignMcpService : IMcpService
{
    public Task<IReadOnlyList<AgentTool>> ListToolsAsync(string? projectDir, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgentTool>>(Array.Empty<AgentTool>());
    public Task<string> CallToolAsync(string toolName, System.Text.Json.JsonElement args, CancellationToken ct) =>
        Task.FromResult("design-time");
    public bool IsAutoApproved(string toolName) => false;
    public Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(string? projectDir, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<McpResourceInfo>>(Array.Empty<McpResourceInfo>());
    public Task<string> ReadResourceAsync(string serverId, string uri, CancellationToken ct) =>
        Task.FromResult("design-time");
    public Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(string? projectDir, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<McpPromptInfo>>(Array.Empty<McpPromptInfo>());
    public Task<string> GetPromptTextAsync(string serverId, string promptName, CancellationToken ct) =>
        Task.FromResult("design-time");
    public Task<McpProbe> TestAsync(McpServerConfig server, CancellationToken ct) =>
        Task.FromResult(new McpProbe(false, 0, "design-time", Array.Empty<McpToolSummary>()));
    public Task DisconnectAllAsync() => Task.CompletedTask;
}

internal sealed class DesignMemoryService : IMemoryService
{
    public IReadOnlyList<MemoryEntry> Load(MemoryScope scope, string? projectDir) => Array.Empty<MemoryEntry>();
    public void Add(MemoryScope scope, string text, string source, string? projectDir) { }
    public void Remove(MemoryScope scope, string text, string? projectDir) { }
    public void Clear(MemoryScope scope, string? projectDir) { }
    public string BuildContextBlock(string? projectDir) => "";
}

internal sealed class DesignAgentService : IAgentService
{
    private readonly AgentService _real = new();

    public Agent Default => _real.Default;
    public IReadOnlyList<Agent> ListAgents(string? projectDir) => _real.ListAgents(null);
    public Agent? Get(string id, string? projectDir) => _real.Get(id, null);
    public void SaveCustom(Agent agent, string? projectDir) { }
    public void DeleteCustom(string id, string? projectDir) { }
}

internal sealed class DesignDocumentService : IDocumentService
{
    public int CreateWord(string fullPath, string content) => 0;
    public int AppendWord(string fullPath, string content) => 0;
    public int ReplaceInWord(string fullPath, string find, string replace) => 0;
    public int CreatePdf(string fullPath, string content) => 0;
}
