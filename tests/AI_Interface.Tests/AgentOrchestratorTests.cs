using System.Collections.Generic;
using System.Linq;
using AI_Interface.Models;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the pure, I/O-free helpers in <see cref="AgentOrchestrator"/> - the delegation roster
/// (the no-nested-orchestration invariant), the roster catalog the lead sees, the per-agent
/// tools/description summaries it is built from, and the repeat-guard key that detects an identical
/// re-delegation. These avoid <c>RunAsync</c> (which needs a live tool-calling model and the project-agent
/// loop); they exercise only the deterministic helpers, reachable from the test assembly via
/// <c>[assembly: InternalsVisibleTo("AI_Interface.Tests")]</c>.
/// </summary>
public class AgentOrchestratorTests
{
    private static Agent Make(string id, string name, string persona = "", string? description = null,
        AgentTools? tools = null, bool isOrchestrator = false) => new()
    {
        Id = id, Name = name, Persona = persona, Description = description ?? "",
        Tools = tools ?? new AgentTools(), IsOrchestrator = isOrchestrator
    };

    // --- BuildRoster (no-nested-orchestration invariant) ---------------------------------------

    [Fact]
    public void BuildRoster_ExcludesOrchestrators_EvenWhenIdDiffersFromLead()
    {
        var lead = Make("lead", "Lead", isOrchestrator: true);
        var otherLead = Make("lead-2", "Other Lead", isOrchestrator: true);
        var specialist = Make("code-buddy", "Code Buddy");

        var roster = AgentOrchestrator.BuildRoster(new List<Agent> { otherLead, specialist }, lead);

        // The other orchestrator is excluded even though its id differs from the lead's.
        Assert.DoesNotContain(roster, a => a.Id == "lead-2");
        Assert.Contains(roster, a => a.Id == "code-buddy");
    }

    [Fact]
    public void BuildRoster_ExcludesLeadById_CaseInsensitively()
    {
        var lead = Make("lead", "Lead", isOrchestrator: true);
        // A non-orchestrator whose id matches the lead's only by case must still be excluded.
        var leadByCase = Make("LEAD", "Lead (caps)", isOrchestrator: false);
        var specialist = Make("researcher", "Researcher");

        var roster = AgentOrchestrator.BuildRoster(new List<Agent> { leadByCase, specialist }, lead);

        Assert.DoesNotContain(roster, a => string.Equals(a.Id, "lead", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(roster, a => a.Id == "researcher");
    }

    [Fact]
    public void BuildRoster_NormalSpecialist_Survives()
    {
        var lead = Make("lead", "Lead", isOrchestrator: true);
        var specialist = Make("code-buddy", "Code Buddy", isOrchestrator: false);

        var roster = AgentOrchestrator.BuildRoster(new List<Agent> { specialist }, lead);

        Assert.Single(roster);
        Assert.Equal("code-buddy", roster[0].Id);
    }

    [Fact]
    public void BuildRoster_NullInput_ReturnsEmpty_NoThrow()
    {
        var lead = Make("lead", "Lead", isOrchestrator: true);

        var roster = AgentOrchestrator.BuildRoster(null!, lead);

        Assert.Empty(roster);
    }

    [Fact]
    public void BuildRoster_EmptyInput_ReturnsEmpty_NoThrow()
    {
        var lead = Make("lead", "Lead", isOrchestrator: true);

        var roster = AgentOrchestrator.BuildRoster(new List<Agent>(), lead);

        Assert.Empty(roster);
    }

    [Fact]
    public void BuildRoster_MultipleOrchestratorsAndLead_ReturnsOnlyNonOrchestratorSpecialists()
    {
        var lead = Make("lead", "Lead", isOrchestrator: true);
        var all = new List<Agent>
        {
            lead,                                                    // the lead itself (by id)
            Make("LEAD", "Lead alias", isOrchestrator: false),       // lead id, different case
            Make("orchestrator-2", "Second Lead", isOrchestrator: true),
            Make("orchestrator-3", "Third Lead", isOrchestrator: true),
            Make("code-buddy", "Code Buddy", isOrchestrator: false),
            Make("researcher", "Researcher", isOrchestrator: false)
        };

        var roster = AgentOrchestrator.BuildRoster(all, lead);

        Assert.Equal(2, roster.Count);
        Assert.Equal(new[] { "code-buddy", "researcher" }, roster.Select(a => a.Id).ToArray());
    }

    // --- BuildRosterCatalog --------------------------------------------------------------------

    [Fact]
    public void BuildRosterCatalog_Empty_ReturnsNoneMessage()
    {
        var catalog = AgentOrchestrator.BuildRosterCatalog(new List<Agent>());
        Assert.Contains("no specialist agents", catalog);
    }

    [Fact]
    public void BuildRosterCatalog_OneLinePerAgent_IncludesIdNameDescriptionTools()
    {
        var roster = new List<Agent>
        {
            Make("code-buddy", "Code Buddy", description: "Careful engineer.",
                tools: new AgentTools { AllowAll = false, ReadFiles = true, WriteFiles = true, DeleteFiles = true, RunCommands = true, InstallSoftware = false }),
            Make("researcher", "Researcher", description: "Cites sources.",
                tools: new AgentTools { AllowAll = false, ReadFiles = true, WriteFiles = false, DeleteFiles = false, RunCommands = false, InstallSoftware = false })
        };

        var catalog = AgentOrchestrator.BuildRosterCatalog(roster);
        var lines = catalog.Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("- code-buddy (Code Buddy): Careful engineer.", lines[0]);
        Assert.Contains("Can: read, write, delete, run commands.", lines[0]);
        Assert.StartsWith("- researcher (Researcher): Cites sources.", lines[1]);
        Assert.Contains("Can: read.", lines[1]);
    }

    // --- ShortDescription ----------------------------------------------------------------------

    [Fact]
    public void ShortDescription_PrefersExplicitDescription()
    {
        var a = Make("x", "X", persona: "A long persona. With many sentences.", description: "The summary.");
        Assert.Equal("The summary.", AgentOrchestrator.ShortDescription(a));
    }

    [Fact]
    public void ShortDescription_FallsBackToFirstPersonaSentence()
    {
        var a = Make("x", "X", persona: "You are a careful engineer. You read before you change.");
        Assert.Equal("You are a careful engineer", AgentOrchestrator.ShortDescription(a));
    }

    [Fact]
    public void ShortDescription_EmptyPersona_UsesNeutralFallback()
    {
        var a = Make("x", "X");
        Assert.Equal("general-purpose agent", AgentOrchestrator.ShortDescription(a));
    }

    [Fact]
    public void ShortDescription_PersonaWithNoPeriod_ReturnsWholeTrimmedString()
    {
        // No '.' so IndexOf returns -1 and the whole (trimmed) persona is used.
        var a = Make("x", "X", persona: "  A persona with no sentence terminator  ");
        Assert.Equal("A persona with no sentence terminator", AgentOrchestrator.ShortDescription(a));
    }

    [Fact]
    public void ShortDescription_WhitespaceOnlyPersona_UsesNeutralFallback()
    {
        // IsNullOrWhiteSpace skips the (empty) description; trimming the persona empties it -> fallback.
        var a = Make("x", "X", persona: "   \t  ");
        Assert.Equal("general-purpose agent", AgentOrchestrator.ShortDescription(a));
    }

    // --- ToolsSummary --------------------------------------------------------------------------

    [Fact]
    public void ToolsSummary_AllowAll_ListsAllTools()
    {
        var summary = AgentOrchestrator.ToolsSummary(new AgentTools { AllowAll = true });
        Assert.Contains("all tools", summary);
    }

    [Fact]
    public void ToolsSummary_ReadOnly_ReportsReadOnly()
    {
        var summary = AgentOrchestrator.ToolsSummary(new AgentTools
        {
            AllowAll = false, ReadFiles = true, WriteFiles = false, DeleteFiles = false, RunCommands = false, InstallSoftware = false
        });
        Assert.Equal("read", summary);
    }

    [Fact]
    public void ToolsSummary_NoTools_ReportsAnswerOnly()
    {
        var summary = AgentOrchestrator.ToolsSummary(new AgentTools
        {
            AllowAll = false, ReadFiles = false, WriteFiles = false, DeleteFiles = false, RunCommands = false, InstallSoftware = false
        });
        Assert.Contains("answer only", summary);
    }

    [Fact]
    public void ToolsSummary_Null_TreatedAsUnrestricted()
    {
        // A null allow-list defaults to AgentTools() which is AllowAll = true.
        Assert.Contains("all tools", AgentOrchestrator.ToolsSummary(null));
    }

    [Fact]
    public void ToolsSummary_MultipleFlags_EmittedInFixedOrder()
    {
        // read + write + run set (no delete, no install). The helper appends groups in the fixed order
        // read, write, delete, run commands, install software and joins them with ", ".
        var summary = AgentOrchestrator.ToolsSummary(new AgentTools
        {
            AllowAll = false, ReadFiles = true, WriteFiles = true, DeleteFiles = false, RunCommands = true, InstallSoftware = false
        });
        Assert.Equal("read, write, run commands", summary);
    }

    // --- DelegationKey (repeat guard) ----------------------------------------------------------

    [Fact]
    public void DelegationKey_SameAgentAndTask_IgnoringCaseAndWhitespace_MatchEqual()
    {
        var a = AgentOrchestrator.DelegationKey("Code-Buddy", "  Write the README ");
        var b = AgentOrchestrator.DelegationKey("code-buddy", "write the readme");
        Assert.Equal(a, b);
    }

    [Fact]
    public void DelegationKey_DifferentTask_DiffersFromOriginal()
    {
        var a = AgentOrchestrator.DelegationKey("code-buddy", "write the README");
        var b = AgentOrchestrator.DelegationKey("code-buddy", "write the tests");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DelegationKey_NullArgs_DoesNotThrow_ReturnsBareSeparator()
    {
        // Each "?? \"\"" half is trimmed + lowercased, so both collapse to "" and the result is JUST the
        // separator (a single space) between them. Asserts null args don't throw and the key normalizes cleanly.
        var key = AgentOrchestrator.DelegationKey(null!, null!);
        Assert.Equal(" ", key);
    }

    // --- CapTools (the delegation permission ceiling) ------------------------------------------
    // Every CapTools result is an EXPLICIT allow-list (AllowAll = false) so the per-group bool props and
    // Allows(group) agree; the tests assert via Allows(group) for the "post-cap effective" reading.

    private static AgentTools ReadOnly() => new()
    {
        AllowAll = false, ReadFiles = true, WriteFiles = false, DeleteFiles = false,
        RunCommands = false, InstallSoftware = false
    };

    private static AgentTools WriteOnly() => new()
    {
        AllowAll = false, ReadFiles = false, WriteFiles = true, DeleteFiles = false,
        RunCommands = false, InstallSoftware = false
    };

    private static AgentTools RunOnly() => new()
    {
        AllowAll = false, ReadFiles = false, WriteFiles = false, DeleteFiles = false,
        RunCommands = true, InstallSoftware = false
    };

    /// <summary>Built-in-Lead-style ceiling: read/write/delete/run permitted, install withheld.</summary>
    private static AgentTools LeadStyle() => new()
    {
        AllowAll = false, ReadFiles = true, WriteFiles = true, DeleteFiles = true,
        RunCommands = true, InstallSoftware = false
    };

    /// <summary>Autopilot-style specialist: every group permitted, including install.</summary>
    private static AgentTools AutopilotStyle() => new()
    {
        AllowAll = false, ReadFiles = true, WriteFiles = true, DeleteFiles = true,
        RunCommands = true, InstallSoftware = true
    };

    [Fact]
    public void CapTools_ReadOnlyCeiling_UnrestrictedSpecialist_ClampedToCeiling()
    {
        // The key bypass guard: an unrestricted specialist (AllowAll = true) is clamped to a read-only ceiling.
        var result = AgentOrchestrator.CapTools(ReadOnly(), new AgentTools());

        Assert.False(result.AllowAll);
        Assert.True(result.Allows(AgentToolGroup.ReadFiles));
        Assert.False(result.Allows(AgentToolGroup.WriteFiles));
        Assert.False(result.Allows(AgentToolGroup.DeleteFiles));
        Assert.False(result.Allows(AgentToolGroup.RunCommands));
        Assert.False(result.Allows(AgentToolGroup.InstallSoftware));
    }

    [Fact]
    public void CapTools_UnrestrictedCeiling_RestrictedSpecialist_KeepsSpecialistSet()
    {
        // An unrestricted ceiling (AllowAll = true) lets the (more restrictive) specialist set pass through.
        var result = AgentOrchestrator.CapTools(new AgentTools(), WriteOnly());

        Assert.False(result.AllowAll);
        Assert.False(result.Allows(AgentToolGroup.ReadFiles));
        Assert.True(result.Allows(AgentToolGroup.WriteFiles));
        Assert.False(result.Allows(AgentToolGroup.DeleteFiles));
        Assert.False(result.Allows(AgentToolGroup.RunCommands));
        Assert.False(result.Allows(AgentToolGroup.InstallSoftware));
    }

    [Fact]
    public void CapTools_LeadStyleCeiling_AutopilotSpecialist_WithholdsInstall()
    {
        // Lead permits read/write/delete/run but not install; the autopilot specialist wants all five.
        // The intersection grants the four the lead allows and withholds install.
        var result = AgentOrchestrator.CapTools(LeadStyle(), AutopilotStyle());

        Assert.False(result.AllowAll);
        Assert.True(result.Allows(AgentToolGroup.ReadFiles));
        Assert.True(result.Allows(AgentToolGroup.WriteFiles));
        Assert.True(result.Allows(AgentToolGroup.DeleteFiles));
        Assert.True(result.Allows(AgentToolGroup.RunCommands));
        Assert.False(result.Allows(AgentToolGroup.InstallSoftware));
    }

    [Fact]
    public void CapTools_DisjointRestrictedSets_GrantNothing()
    {
        // Read-only ceiling vs run-only specialist: no group is allowed by BOTH, so all are denied.
        var result = AgentOrchestrator.CapTools(ReadOnly(), RunOnly());

        Assert.False(result.AllowAll);
        Assert.False(result.Allows(AgentToolGroup.ReadFiles));
        Assert.False(result.Allows(AgentToolGroup.WriteFiles));
        Assert.False(result.Allows(AgentToolGroup.DeleteFiles));
        Assert.False(result.Allows(AgentToolGroup.RunCommands));
        Assert.False(result.Allows(AgentToolGroup.InstallSoftware));
    }

    [Fact]
    public void CapTools_NullArgs_DoNotThrow_AndStayExplicit()
    {
        // Both null: each is treated as new AgentTools() (AllowAll = true), so the intersection is all-on,
        // but the result is still an EXPLICIT allow-list (AllowAll = false).
        var both = AgentOrchestrator.CapTools(null!, null!);
        Assert.False(both.AllowAll);
        Assert.True(both.Allows(AgentToolGroup.ReadFiles));
        Assert.True(both.Allows(AgentToolGroup.WriteFiles));
        Assert.True(both.Allows(AgentToolGroup.DeleteFiles));
        Assert.True(both.Allows(AgentToolGroup.RunCommands));
        Assert.True(both.Allows(AgentToolGroup.InstallSoftware));

        // Null ceiling (= unrestricted) capped by a read-only request → read-only survives.
        var nullCeiling = AgentOrchestrator.CapTools(null!, ReadOnly());
        Assert.False(nullCeiling.AllowAll);
        Assert.True(nullCeiling.Allows(AgentToolGroup.ReadFiles));
        Assert.False(nullCeiling.Allows(AgentToolGroup.WriteFiles));
        Assert.False(nullCeiling.Allows(AgentToolGroup.DeleteFiles));
        Assert.False(nullCeiling.Allows(AgentToolGroup.RunCommands));
        Assert.False(nullCeiling.Allows(AgentToolGroup.InstallSoftware));

        // Read-only ceiling with a null (= unrestricted) request → still capped to the ceiling.
        var nullRequest = AgentOrchestrator.CapTools(ReadOnly(), null!);
        Assert.False(nullRequest.AllowAll);
        Assert.True(nullRequest.Allows(AgentToolGroup.ReadFiles));
        Assert.False(nullRequest.Allows(AgentToolGroup.WriteFiles));
        Assert.False(nullRequest.Allows(AgentToolGroup.DeleteFiles));
        Assert.False(nullRequest.Allows(AgentToolGroup.RunCommands));
        Assert.False(nullRequest.Allows(AgentToolGroup.InstallSoftware));
    }

    [Fact]
    public void CapTools_IsSymmetricPerGroup()
    {
        // The cap is a per-group AND of two allow-lists, so the result's group flags are order-independent.
        var a = LeadStyle();      // read/write/delete/run, no install
        var b = AutopilotStyle(); // all five

        var ab = AgentOrchestrator.CapTools(a, b);
        var ba = AgentOrchestrator.CapTools(b, a);

        foreach (var group in System.Enum.GetValues<AgentToolGroup>())
            Assert.Equal(ab.Allows(group), ba.Allows(group));
    }

}
