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
    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> PingAsync(string baseUrl, CancellationToken ct = default) => Task.FromResult(true);

    public Task PullModelAsync(string name, IProgress<string>? progress, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task DeleteModelAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(new[] { "llama3:latest", "mistral:latest" });

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model, IEnumerable<ChatMessage> messages,
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
        string question, string model, IProgress<string> status,
        Action<string> onAnswerDelta, CancellationToken ct = default)
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
        Project project, string model, IReadOnlyList<ChatMessage> conversation,
        AgentApprovalMode approvalMode, string thinkingDirective, string projectSkills,
        SoftwareInstallPermission installPermission, IProgress<string> status, Action<string> onDelta,
        Func<ToolApprovalRequest, Task<bool>> approve, CancellationToken ct)
    {
        onDelta("Design-time project agent response.");
        return Task.CompletedTask;
    }
}
