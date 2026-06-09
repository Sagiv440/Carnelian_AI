using System.Collections.Generic;
using System.Linq;
using AI_Interface.Models;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the pure, I/O-free helpers in <see cref="ProjectAgentPicker"/> - the "prefer team
/// agents in Project mode" feature. <see cref="ProjectAgentPicker.Arrange"/> shapes the top-bar picker
/// (optionally team-only, orchestrators stable-sorted first, input never mutated) and
/// <see cref="ProjectAgentPicker.PreferredOrchestrator"/> chooses the orchestrator to auto-select (the
/// built-in Lead by id, else the first orchestrator). Both are reachable from the test assembly via
/// <c>[assembly: InternalsVisibleTo("AI_Interface.Tests")]</c>; neither touches a model, the filesystem,
/// or the network.
/// </summary>
public class ProjectAgentPickerTests
{
    private static Agent A(string id, bool orchestrator = false) =>
        new() { Id = id, Name = id, IsOrchestrator = orchestrator };

    // --- Arrange ------------------------------------------------------------------------------

    [Fact]
    public void Arrange_NullInput_ReturnsEmpty()
    {
        Assert.Empty(ProjectAgentPicker.Arrange(null, teamOnly: false));
    }

    [Fact]
    public void Arrange_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ProjectAgentPicker.Arrange(new List<Agent>(), teamOnly: true));
    }

    [Fact]
    public void Arrange_TeamOnlyFalse_MixedInput_OrchestratorsFirst_IntraGroupOrderPreserved()
    {
        // Mixed input: a single, an orchestrator, a single, an orchestrator. With teamOnly=false all are
        // kept; the stable sort moves orchestrators to the front WITHOUT reordering within either group.
        var input = new List<Agent>
        {
            A("singleA"), A("lead", orchestrator: true), A("singleB"), A("orchX", orchestrator: true)
        };

        var result = ProjectAgentPicker.Arrange(input, teamOnly: false);

        Assert.Equal(new[] { "lead", "orchX", "singleA", "singleB" }, result.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void Arrange_TeamOnlyTrue_MixedInput_DropsSingles_KeepsOrchestratorOrder()
    {
        var input = new List<Agent>
        {
            A("singleA"), A("lead", orchestrator: true), A("singleB"), A("orchX", orchestrator: true)
        };

        var result = ProjectAgentPicker.Arrange(input, teamOnly: true);

        Assert.Equal(new[] { "lead", "orchX" }, result.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void Arrange_TeamOnlyTrue_NoOrchestrators_ReturnsEmpty()
    {
        var input = new List<Agent> { A("singleA"), A("singleB") };

        var result = ProjectAgentPicker.Arrange(input, teamOnly: true);

        Assert.Empty(result);
    }

    [Fact]
    public void Arrange_TeamOnlyFalse_AllOrchestrators_AllRetained_OrderPreserved()
    {
        var input = new List<Agent>
        {
            A("orchX", orchestrator: true), A("lead", orchestrator: true), A("orchY", orchestrator: true)
        };

        var result = ProjectAgentPicker.Arrange(input, teamOnly: false);

        Assert.Equal(new[] { "orchX", "lead", "orchY" }, result.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void Arrange_DoesNotMutateInput_ReturnsNewList()
    {
        var input = new List<Agent>
        {
            A("singleA"), A("lead", orchestrator: true), A("singleB"), A("orchX", orchestrator: true)
        };
        var before = input.Select(a => a.Id).ToArray();

        var result = ProjectAgentPicker.Arrange(input, teamOnly: false);

        // The input list's element order is unchanged: Arrange returns a new (re-ordered) list.
        Assert.Equal(before, input.Select(a => a.Id).ToArray());
        Assert.NotSame(input, result);
    }

    // --- PreferredOrchestrator ----------------------------------------------------------------

    [Fact]
    public void PreferredOrchestrator_NullInput_ReturnsNull()
    {
        Assert.Null(ProjectAgentPicker.PreferredOrchestrator(null));
    }

    [Fact]
    public void PreferredOrchestrator_EmptyInput_ReturnsNull()
    {
        Assert.Null(ProjectAgentPicker.PreferredOrchestrator(new List<Agent>()));
    }

    [Fact]
    public void PreferredOrchestrator_LeadPresent_PreferredOverEarlierOrchestrator()
    {
        // orchX comes first in list order, but the built-in Lead (by id) is always preferred.
        var input = new List<Agent>
        {
            A("orchX", orchestrator: true), A("lead", orchestrator: true)
        };

        var picked = ProjectAgentPicker.PreferredOrchestrator(input);

        Assert.NotNull(picked);
        Assert.Equal("lead", picked!.Id);
    }

    [Fact]
    public void PreferredOrchestrator_LeadIdMatchedCaseInsensitively()
    {
        // "Lead" (mixed case) still matches the built-in lead id via OrdinalIgnoreCase.
        var input = new List<Agent>
        {
            A("orchX", orchestrator: true), A("Lead", orchestrator: true)
        };

        var picked = ProjectAgentPicker.PreferredOrchestrator(input);

        Assert.NotNull(picked);
        Assert.Equal("Lead", picked!.Id);
    }

    [Fact]
    public void PreferredOrchestrator_NoLead_ReturnsFirstOrchestratorInOrder()
    {
        var input = new List<Agent>
        {
            A("singleA"), A("orchX", orchestrator: true), A("orchY", orchestrator: true)
        };

        var picked = ProjectAgentPicker.PreferredOrchestrator(input);

        Assert.NotNull(picked);
        Assert.Equal("orchX", picked!.Id);
    }

    [Fact]
    public void PreferredOrchestrator_OnlySingleAgents_ReturnsNull()
    {
        var input = new List<Agent> { A("singleA"), A("singleB") };

        Assert.Null(ProjectAgentPicker.PreferredOrchestrator(input));
    }
}
