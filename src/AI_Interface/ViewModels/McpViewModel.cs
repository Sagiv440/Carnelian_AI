using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Interface.ViewModels;

/// <summary>
/// Backs the Settings → AI Features → MCP Servers master/detail panel: lists configured MCP servers, edits the
/// selected one (name / command / args / env / enabled / trusted), tests its connection (reports the tool
/// count), and persists to <see cref="AppSettings.McpServers"/>. Adding a server makes its tools available to
/// the Project-mode agent on the next turn. Mirrors <see cref="AgentsViewModel"/>'s master/detail shape.
/// </summary>
public sealed partial class McpViewModel : ViewModelBase
{
    // Status colors (match the connection-probe palette used elsewhere in Settings).
    private const string OkColor = "#3FB950";
    private const string ErrColor = "#E5534B";
    private const string BusyColor = "#858585";

    private readonly ISettingsService _settings;
    private readonly IMcpService _mcp;
    private bool _loadingDetail;

    /// <summary>The configured servers shown in the master list.</summary>
    public ObservableCollection<McpServerConfig> Servers { get; } = new();

    /// <summary>Tools the selected server exposes, populated by the last successful <see cref="TestCommand"/>.</summary>
    public ObservableCollection<McpToolSummary> DiscoveredTools { get; } = new();

    [ObservableProperty] private bool _hasDiscoveredTools;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private McpServerConfig? _selectedServer;

    /// <summary>True when a server is selected (drives the detail editor's visibility).</summary>
    public bool HasSelection => SelectedServer is not null;

    /// <summary>The two transports offered in the editor's dropdown.</summary>
    public IReadOnlyList<McpTransport> Transports { get; } = new[] { McpTransport.Stdio, McpTransport.Http };

    // --- detail fields (bound to the editor) ---
    [ObservableProperty] private string _editName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStdio))]
    [NotifyPropertyChangedFor(nameof(IsHttp))]
    private McpTransport _editTransport = McpTransport.Stdio;

    [ObservableProperty] private string _editCommand = "";
    [ObservableProperty] private string _editArgs = "";   // one argument per line
    [ObservableProperty] private string _editEnv = "";    // one KEY=VALUE per line
    [ObservableProperty] private string _editUrl = "";
    [ObservableProperty] private string _editHeaders = ""; // one KEY=VALUE per line (HTTP)
    [ObservableProperty] private bool _editEnabled = true;
    [ObservableProperty] private bool _editAutoApprove;

    /// <summary>Field visibility: stdio shows Command/Args/Env, HTTP shows URL/Headers.</summary>
    public bool IsStdio => EditTransport == McpTransport.Stdio;
    public bool IsHttp => EditTransport == McpTransport.Http;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _isTesting;

    /// <summary>Result of the last "Test" probe (empty = none yet).</summary>
    [ObservableProperty] private string _testMessage = "";

    /// <summary>Hex color for <see cref="TestMessage"/> (green ok / red error / grey busy).</summary>
    [ObservableProperty] private string _testColor = BusyColor;

    public McpViewModel(ISettingsService settings, IMcpService mcp)
    {
        _settings = settings;
        _mcp = mcp;
    }

    // Design-time constructor for the XAML previewer.
    public McpViewModel() : this(new DesignSettingsService(), new DesignMcpService())
    {
    }

    /// <summary>Loads the configured servers. (<paramref name="projectDir"/> is reserved for per-project servers — Phase 2.)</summary>
    public void Initialize(string? projectDir)
    {
        Servers.Clear();
        foreach (var s in _settings.Current.McpServers ?? new List<McpServerConfig>())
            Servers.Add(s);
        SelectedServer = Servers.FirstOrDefault();
    }

    partial void OnSelectedServerChanged(McpServerConfig? value)
    {
        _loadingDetail = true;
        EditName = value?.Name ?? "";
        EditTransport = value?.Transport ?? McpTransport.Stdio;
        EditCommand = value?.Command ?? "";
        EditArgs = value is null ? "" : string.Join("\n", value.Args ?? new List<string>());
        EditEnv = value is null ? "" : JoinMap(value.Env);
        EditUrl = value?.Url ?? "";
        EditHeaders = value is null ? "" : JoinMap(value.Headers);
        EditEnabled = value?.Enabled ?? true;
        EditAutoApprove = value?.AutoApprove ?? false;
        TestMessage = "";
        DiscoveredTools.Clear();        // last test's tools are for the previously selected server
        HasDiscoveredTools = false;
        _loadingDetail = false;
    }

    // Editor changes write through to the selected server + settings file.
    partial void OnEditNameChanged(string value) => Persist(s =>
    {
        s.Name = value.Trim();
        s.Id = UniqueId(McpToolName.SanitizeId(value), s); // keep the tool-namespace id in sync with the name
    });
    partial void OnEditTransportChanged(McpTransport value) => Persist(s => s.Transport = value);
    partial void OnEditCommandChanged(string value) => Persist(s => s.Command = value.Trim());
    partial void OnEditArgsChanged(string value) => Persist(s => s.Args = SplitLines(value));
    partial void OnEditEnvChanged(string value) => Persist(s => s.Env = ParseEnv(value));
    partial void OnEditUrlChanged(string value) => Persist(s => s.Url = value.Trim());
    partial void OnEditHeadersChanged(string value) => Persist(s => s.Headers = ParseEnv(value));
    partial void OnEditEnabledChanged(bool value) => Persist(s => s.Enabled = value);
    partial void OnEditAutoApproveChanged(bool value) => Persist(s => s.AutoApprove = value);

    private void Persist(Action<McpServerConfig> apply)
    {
        if (_loadingDetail || SelectedServer is null)
            return;
        apply(SelectedServer);
        _settings.Save();
    }

    [RelayCommand]
    private void New()
    {
        var server = new McpServerConfig { Name = "New MCP Server", Enabled = true, Transport = McpTransport.Stdio };
        server.Id = UniqueId("new-mcp-server", server);

        _settings.Current.McpServers ??= new List<McpServerConfig>();
        _settings.Current.McpServers.Add(server);
        _settings.Save();

        Servers.Add(server);
        SelectedServer = server;
    }

    private bool CanDelete => SelectedServer is not null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedServer is null)
            return;

        var victim = SelectedServer;
        _settings.Current.McpServers?.Remove(victim);
        _settings.Save();

        Servers.Remove(victim);
        SelectedServer = Servers.FirstOrDefault();
    }

    private bool CanTest => SelectedServer is not null && !IsTesting;

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task Test()
    {
        if (SelectedServer is null)
            return;

        IsTesting = true;
        TestColor = BusyColor;
        TestMessage = "Connecting…";
        DiscoveredTools.Clear();
        HasDiscoveredTools = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var probe = await _mcp.TestAsync(SelectedServer, cts.Token);
            TestColor = probe.Ok ? OkColor : ErrColor;
            TestMessage = probe.Message;
            foreach (var t in probe.Tools)
                DiscoveredTools.Add(t);
            HasDiscoveredTools = DiscoveredTools.Count > 0;
        }
        catch (Exception ex)
        {
            TestColor = ErrColor;
            TestMessage = ex.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string JoinMap(Dictionary<string, string>? map) =>
        map is null ? "" : string.Join("\n", map.Select(kv => $"{kv.Key}={kv.Value}"));

    private static List<string> SplitLines(string? text) =>
        (text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private static Dictionary<string, string> ParseEnv(string? text)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in SplitLines(text))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            if (key.Length > 0)
                dict[key] = line[(eq + 1)..].Trim();
        }
        return dict;
    }

    /// <summary>A server id unique among the others (the namespace key), disambiguated with a numeric suffix.</summary>
    private string UniqueId(string baseId, McpServerConfig self)
    {
        var id = string.IsNullOrWhiteSpace(baseId) ? "server" : baseId;
        var existing = Servers.Where(s => !ReferenceEquals(s, self))
            .Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(id))
            return id;
        for (var n = 2; ; n++)
            if (!existing.Contains($"{id}-{n}"))
                return $"{id}-{n}";
    }
}
