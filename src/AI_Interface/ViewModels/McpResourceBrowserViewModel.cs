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
/// Backs the MCP resource browser dialog: lists the resources exposed by the connected MCP servers, lets the
/// user tick the ones to attach, fetches their text, and hands them back (via <see cref="AttachCompleted"/>)
/// so the composer can stage them as prompt context. Opened from the composer's 📎 menu.
/// </summary>
public sealed partial class McpResourceBrowserViewModel : ViewModelBase
{
    private readonly IMcpService _mcp;
    private string? _projectDir;

    /// <summary>The discovered resources (one tickable row each).</summary>
    public ObservableCollection<McpResourceRow> Resources { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AttachCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>Raised when the user confirms a selection (carries the fetched resources). The view closes the dialog with it.</summary>
    public event EventHandler<IReadOnlyList<McpAttachedResource>>? AttachCompleted;

    public McpResourceBrowserViewModel(IMcpService mcp) => _mcp = mcp;

    // Design-time constructor for the previewer.
    public McpResourceBrowserViewModel() : this(new DesignMcpService())
    {
    }

    /// <summary>Scopes resource discovery to the active project (so its <c>.AI/mcp.json</c> servers are included).</summary>
    public void Initialize(string? projectDir) => _projectDir = projectDir;

    private bool CanRun => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Load()
    {
        IsBusy = true;
        StatusMessage = "Connecting to servers and loading resources…";
        Resources.Clear();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var list = await _mcp.ListResourcesAsync(_projectDir, cts.Token);
            foreach (var r in list)
                Resources.Add(new McpResourceRow(r));
            StatusMessage = list.Count == 0
                ? "No resources found. Configure an MCP server that exposes resources (Settings → MCP Servers) and make sure it's reachable."
                : $"{list.Count} resource(s) across your servers. Tick the ones to attach.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't load resources: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsEmpty = Resources.Count == 0;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Attach()
    {
        var picked = Resources.Where(r => r.IsSelected).ToList();
        var result = new List<McpAttachedResource>();
        if (picked.Count == 0)
        {
            AttachCompleted?.Invoke(this, result); // nothing selected → close with empty
            return;
        }

        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            foreach (var row in picked)
            {
                StatusMessage = $"Fetching {row.Label}…";
                var text = await _mcp.ReadResourceAsync(row.Info.ServerId, row.Info.Uri, cts.Token);
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(new McpAttachedResource($"{row.Info.ServerName}: {row.Label}", text));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't fetch a resource: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        AttachCompleted?.Invoke(this, result);
    }
}

/// <summary>One tickable row in the resource browser (an <see cref="McpResourceInfo"/> plus its checkbox state).</summary>
public sealed partial class McpResourceRow : ViewModelBase
{
    public McpResourceRow(McpResourceInfo info) => Info = info;

    public McpResourceInfo Info { get; }
    public string Label => Info.Label;
    public string ServerName => Info.ServerName;
    public string Description => Info.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Info.Description);

    [ObservableProperty] private bool _isSelected;
}
