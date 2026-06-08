using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AI_Interface.Models;
using AI_Interface.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Interface.ViewModels;

/// <summary>
/// Backs the Settings → AI Features → Agents master/detail panel. Lists the roster (built-in + global +
/// the active project's customs), edits a selected agent's Name / Glyph / Persona / Default model /
/// <b>tool permissions</b> / <b>skills</b>, and persists via <see cref="IAgentService"/>. Built-ins are
/// read-only but can be duplicated into an editable global custom. (Autonomy / Memory editors are later
/// phases — omitted here.)
/// </summary>
public sealed partial class AgentsViewModel : ViewModelBase
{
    private readonly IAgentService _agents;
    private readonly IModelRouter _router;
    private readonly IProjectSkillService _projectSkills;
    private string? _projectDir;
    private bool _loadingDetail;

    /// <summary>Raised after the roster changes so the host can refresh the main-window agent picker.</summary>
    public event EventHandler? AgentsChanged;

    /// <summary>The agents shown in the master list (built-in + global + project).</summary>
    public ObservableCollection<Agent> Agents { get; } = new();

    /// <summary>Models offered in the "Default model" picker (across every configured provider).</summary>
    public ObservableCollection<ChatModel> Models { get; } = new();

    /// <summary>The skills checklist for the selected agent: built-in packs always, project SKILL.md names when a project is open.</summary>
    public ObservableCollection<SkillChoice> Skills { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(IsBuiltInSelected))]
    private Agent? _selectedAgent;

    // --- detail fields (bound to the editor) ---
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editGlyph = "";
    [ObservableProperty] private string _editPersona = "";
    [ObservableProperty] private ChatModel? _editDefaultModel;

    // --- tool permissions (bound to the checkbox row) ---
    [ObservableProperty] private bool _toolReadFiles = true;
    [ObservableProperty] private bool _toolWriteFiles = true;
    [ObservableProperty] private bool _toolDeleteFiles = true;
    [ObservableProperty] private bool _toolRunCommands = true;
    [ObservableProperty] private bool _toolInstallSoftware;

    /// <summary>True when a custom agent is selected (built-ins are read-only).</summary>
    public bool IsEditable => SelectedAgent is { IsBuiltIn: false };

    /// <summary>True when the selected agent is a read-only built-in (drives the "Built-in" badge + disabled fields).</summary>
    public bool IsBuiltInSelected => SelectedAgent is { IsBuiltIn: true };

    public AgentsViewModel(IAgentService agents, IModelRouter router, IProjectSkillService projectSkills)
    {
        _agents = agents;
        _router = router;
        _projectSkills = projectSkills;
    }

    // Design-time constructor for the XAML previewer.
    public AgentsViewModel() : this(new DesignAgentService(), new DesignModelRouter(), new DesignProjectSkillService())
    {
        Initialize(null);
    }

    /// <summary>Loads the roster for the given project scope and the model list for the picker.</summary>
    public void Initialize(string? projectDir)
    {
        _projectDir = projectDir;
        ReloadAgents(_agents.Default.Id);
    }

    /// <summary>Populates the Default-model picker (best-effort; missing/offline providers just contribute nothing).</summary>
    public async Task LoadModelsAsync()
    {
        try
        {
            var models = await _router.ListAllModelsAsync();
            Models.Clear();
            foreach (var m in models)
                Models.Add(m);
            // Re-resolve the selected agent's saved model now that the list exists.
            SyncEditDefaultModel();
        }
        catch
        {
            // The picker simply stays empty if no provider answers.
        }
    }

    private void ReloadAgents(string? selectId)
    {
        Agents.Clear();
        foreach (var agent in _agents.ListAgents(_projectDir))
            Agents.Add(agent);

        SelectedAgent =
            Agents.FirstOrDefault(a => string.Equals(a.Id, selectId, StringComparison.OrdinalIgnoreCase))
            ?? Agents.FirstOrDefault();
    }

    partial void OnSelectedAgentChanged(Agent? value)
    {
        _loadingDetail = true;
        EditName = value?.Name ?? "";
        EditGlyph = value?.Glyph ?? "";
        EditPersona = value?.Persona ?? "";
        SyncEditDefaultModel();

        // Tool permission checkboxes reflect the agent's effective allow-list (AllowAll → all on).
        var tools = value?.Tools ?? new AgentTools();
        ToolReadFiles = tools.Allows(AgentToolGroup.ReadFiles);
        ToolWriteFiles = tools.Allows(AgentToolGroup.WriteFiles);
        ToolDeleteFiles = tools.Allows(AgentToolGroup.DeleteFiles);
        ToolRunCommands = tools.Allows(AgentToolGroup.RunCommands);
        ToolInstallSoftware = tools.Allows(AgentToolGroup.InstallSoftware);

        RebuildSkillChoices(value);
        _loadingDetail = false;
    }

    /// <summary>
    /// Rebuilds the skills checklist for the selected agent: every built-in pack, plus the current
    /// project's discovered SKILL.md names (only when a project is open). Each entry's checkbox reflects
    /// the agent's <see cref="Agent.Skills"/> membership and writes back through to disk on toggle.
    /// </summary>
    private void RebuildSkillChoices(Agent? agent)
    {
        Skills.Clear();
        if (agent is null)
            return;

        var selected = new HashSet<string>(agent.Skills, StringComparer.OrdinalIgnoreCase);

        foreach (var pack in SkillCatalog.BuiltIn)
            Skills.Add(NewSkillChoice(pack.Id, pack.Name, isBuiltIn: true, selected.Contains(pack.Id)));

        // Project SKILL.md names (best-effort scan; none when no project is open).
        if (!string.IsNullOrWhiteSpace(_projectDir))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var skill in _projectSkills.Load(_projectDir!))
            {
                if (!seen.Add(skill.Name))
                    continue;
                Skills.Add(NewSkillChoice(skill.Name, skill.Name, isBuiltIn: false, selected.Contains(skill.Name)));
            }
        }
    }

    private SkillChoice NewSkillChoice(string id, string name, bool isBuiltIn, bool isSelected)
    {
        var choice = new SkillChoice(id, name, isBuiltIn) { IsSelected = isSelected };
        choice.PropertyChanged += OnSkillChoiceChanged;
        return choice;
    }

    private void OnSkillChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingDetail || e.PropertyName != nameof(SkillChoice.IsSelected) || sender is not SkillChoice choice)
            return;

        Persist(a =>
        {
            a.Skills.RemoveAll(s => string.Equals(s, choice.Id, StringComparison.OrdinalIgnoreCase));
            if (choice.IsSelected)
                a.Skills.Add(choice.Id);
        });
    }

    /// <summary>Resolves the selected agent's saved "{provider}:{id}" to a list entry for the ComboBox.</summary>
    private void SyncEditDefaultModel()
    {
        EditDefaultModel = ParseModel(SelectedAgent?.DefaultModel);
    }

    private ChatModel? ParseModel(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
            return null;
        var sep = saved.IndexOf(':');
        if (sep > 0 && Enum.TryParse<AiProvider>(saved[..sep], out var provider))
        {
            var id = saved[(sep + 1)..];
            var match = Models.FirstOrDefault(m => m.Provider == provider && m.Id == id);
            return match;
        }
        return null;
    }

    // --- editor field changes write through to the agent + disk (custom agents only) ---

    partial void OnEditNameChanged(string value) => Persist(a => a.Name = value.Trim());
    partial void OnEditGlyphChanged(string value) => Persist(a => a.Glyph = string.IsNullOrWhiteSpace(value) ? "🤖" : value.Trim());
    partial void OnEditPersonaChanged(string value) => Persist(a => a.Persona = value);
    partial void OnEditDefaultModelChanged(ChatModel? value) =>
        Persist(a => a.DefaultModel = value is null ? null : $"{value.Provider}:{value.Id}");

    // Toggling any permission leaves "unrestricted" mode (Restrict snapshots today's all-on state) so the
    // per-tool flags become authoritative, then applies this checkbox's value.
    partial void OnToolReadFilesChanged(bool value) => PersistTool(t => t.ReadFiles = value);
    partial void OnToolWriteFilesChanged(bool value) => PersistTool(t => t.WriteFiles = value);
    partial void OnToolDeleteFilesChanged(bool value) => PersistTool(t => t.DeleteFiles = value);
    partial void OnToolRunCommandsChanged(bool value) => PersistTool(t => t.RunCommands = value);
    partial void OnToolInstallSoftwareChanged(bool value) => PersistTool(t => t.InstallSoftware = value);

    private void PersistTool(Action<AgentTools> apply) => Persist(a =>
    {
        a.Tools ??= new AgentTools();
        a.Tools.Restrict();
        apply(a.Tools);
    });

    private void Persist(Action<Agent> apply)
    {
        if (_loadingDetail || SelectedAgent is null || SelectedAgent.IsBuiltIn)
            return;

        apply(SelectedAgent);
        _agents.SaveCustom(SelectedAgent, _projectDir);
        // No AgentsChanged here: field edits would fire on every keystroke. The main-window picker is
        // refreshed once when the Settings dialog closes (see MainWindow.OnSettingsRequested).
    }

    [RelayCommand]
    private void New()
    {
        var agent = new Agent
        {
            Id = "agent-" + Guid.NewGuid().ToString("N")[..8],
            Name = "New Agent",
            Glyph = "🤖",
            Persona = "",
            Scope = AgentScope.Global,
            IsBuiltIn = false
        };
        _agents.SaveCustom(agent, _projectDir);
        ReloadAgents(agent.Id);
        AgentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanDuplicate => SelectedAgent is not null;

    [RelayCommand(CanExecute = nameof(CanDuplicate))]
    private void Duplicate()
    {
        if (SelectedAgent is null)
            return;

        var src = SelectedAgent;
        var copy = new Agent
        {
            Id = "agent-" + Guid.NewGuid().ToString("N")[..8],
            Name = src.Name + " (copy)",
            Glyph = src.Glyph,
            Persona = src.Persona,
            DefaultModel = src.DefaultModel,
            Skills = new List<string>(src.Skills),
            Tools = new AgentTools
            {
                AllowAll = src.Tools.AllowAll,
                ReadFiles = src.Tools.ReadFiles,
                WriteFiles = src.Tools.WriteFiles,
                DeleteFiles = src.Tools.DeleteFiles,
                RunCommands = src.Tools.RunCommands,
                InstallSoftware = src.Tools.InstallSoftware
            },
            Autonomy = src.Autonomy,
            MemoryEnabled = src.MemoryEnabled,
            Proactive = src.Proactive,
            Scope = AgentScope.Global, // duplicates are always editable global customs
            IsBuiltIn = false
        };
        _agents.SaveCustom(copy, _projectDir);
        ReloadAgents(copy.Id);
        AgentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanDelete => SelectedAgent is { IsBuiltIn: false };

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedAgent is null || SelectedAgent.IsBuiltIn)
            return;

        _agents.DeleteCustom(SelectedAgent.Id, _projectDir);
        ReloadAgents(_agents.Default.Id);
        AgentsChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// One row in the Agents-editor skills checklist: a built-in pack or a project SKILL.md name, with a
/// checkbox-bound <see cref="IsSelected"/>. <see cref="Id"/> is what gets stored in <see cref="Agent.Skills"/>
/// (a pack id for built-ins, the SKILL name for project skills).
/// </summary>
public sealed partial class SkillChoice : ViewModelBase
{
    public SkillChoice(string id, string name, bool isBuiltIn)
    {
        Id = id;
        Name = name;
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>Stored in the agent's Skills list: a built-in pack id, or a project SKILL.md name.</summary>
    public string Id { get; }

    /// <summary>Display label.</summary>
    public string Name { get; }

    /// <summary>True for a built-in pack (vs a project SKILL.md), used to show a small "built-in"/"project" tag.</summary>
    public bool IsBuiltIn { get; }

    /// <summary>The "project" tag shows for project skills (the inverse of <see cref="IsBuiltIn"/>).</summary>
    public bool IsProject => !IsBuiltIn;

    [ObservableProperty] private bool _isSelected;
}
