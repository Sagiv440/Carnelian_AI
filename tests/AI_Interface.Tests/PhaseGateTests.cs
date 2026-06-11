using System;
using System.Collections.Generic;
using AI_Interface.Models;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the phase-gate transition detection: <see cref="ProjectAgentService.ActivePhaseName"/> and
/// <see cref="ProjectAgentService.DetectPhaseAdvance"/>. These decide when the agent has moved into a NEW
/// phase (so the run pauses when AutoFlowPhases is off). Pure and I/O-free; the gate must NOT fire before the
/// first active phase, when nothing is active, or when the active phase is unchanged — so a non-phased or
/// non-compliant plan never pauses.
/// </summary>
public sealed class PhaseGateTests
{
    private static PlanPhase Phase(string name, PlanStepStatus status) =>
        new(name, status, Array.Empty<PlanStep>());

    private static IReadOnlyList<PlanPhase> Phases(params PlanPhase[] p) => p;

    // ---- ActivePhaseName -----------------------------------------------------------------------

    [Fact]
    public void ActivePhaseName_ReturnsTheActivePhase()
    {
        var phases = Phases(
            Phase("Explore", PlanStepStatus.Done),
            Phase("Implement", PlanStepStatus.Active),
            Phase("Verify", PlanStepStatus.Pending));

        Assert.Equal("Implement", ProjectAgentService.ActivePhaseName(phases));
    }

    [Fact]
    public void ActivePhaseName_NoneActive_ReturnsNull()
    {
        var phases = Phases(Phase("A", PlanStepStatus.Done), Phase("B", PlanStepStatus.Pending));
        Assert.Null(ProjectAgentService.ActivePhaseName(phases));
    }

    [Fact]
    public void ActivePhaseName_Empty_ReturnsNull() =>
        Assert.Null(ProjectAgentService.ActivePhaseName(Array.Empty<PlanPhase>()));

    // ---- DetectPhaseAdvance --------------------------------------------------------------------

    [Fact]
    public void DetectPhaseAdvance_NoPreviousPhase_ReturnsNull()
    {
        // First time a phase becomes active — there's no boundary to gate yet.
        var advance = ProjectAgentService.DetectPhaseAdvance(null, Phases(Phase("Explore", PlanStepStatus.Active)));
        Assert.Null(advance);
    }

    [Fact]
    public void DetectPhaseAdvance_SameActivePhase_ReturnsNull()
    {
        var advance = ProjectAgentService.DetectPhaseAdvance("Implement",
            Phases(Phase("Explore", PlanStepStatus.Done), Phase("Implement", PlanStepStatus.Active)));
        Assert.Null(advance);
    }

    [Fact]
    public void DetectPhaseAdvance_ActivePhaseChanged_ReturnsGateWithBothNames()
    {
        var advance = ProjectAgentService.DetectPhaseAdvance("Explore",
            Phases(Phase("Explore", PlanStepStatus.Done), Phase("Implement", PlanStepStatus.Active)));

        Assert.NotNull(advance);
        Assert.Equal("Explore", advance!.CompletedPhase);
        Assert.Equal("Implement", advance.NextPhase);
    }

    [Fact]
    public void DetectPhaseAdvance_NoActivePhaseNow_ReturnsNull()
    {
        // The whole plan finished (all done) — no next phase to gate into.
        var advance = ProjectAgentService.DetectPhaseAdvance("Implement",
            Phases(Phase("Explore", PlanStepStatus.Done), Phase("Implement", PlanStepStatus.Done)));
        Assert.Null(advance);
    }

    [Fact]
    public void DetectPhaseAdvance_IsCaseInsensitiveOnTheName()
    {
        // Same phase, different casing across calls → not a transition.
        var advance = ProjectAgentService.DetectPhaseAdvance("implement",
            Phases(Phase("Implement", PlanStepStatus.Active)));
        Assert.Null(advance);
    }

    // ---- ApplyPhaseGateAsync (the shared loop step) -------------------------------------------

    private static System.Text.Json.JsonElement PhasesArg(params (string Name, string Status)[] phases) =>
        System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            phases = Array.ConvertAll(phases, p => (object)new { name = p.Name, status = p.Status })
        });

    [Fact]
    public async System.Threading.Tasks.Task ApplyPhaseGate_AutoFlow_NeverStops_ButAdvancesTracker()
    {
        var invoked = false;
        var (prev, stop) = await ProjectAgentService.ApplyPhaseGateAsync(
            "Explore", PhasesArg(("Explore", "done"), ("Implement", "active")),
            autoFlowPhases: true, phaseGate: _ => { invoked = true; return System.Threading.Tasks.Task.FromResult(true); });

        Assert.Null(stop);                 // auto-flow never pauses
        Assert.False(invoked);             // the gate callback isn't even consulted
        Assert.Equal("Implement", prev);   // tracker still advances to the new active phase
    }

    [Fact]
    public async System.Threading.Tasks.Task ApplyPhaseGate_GateDeclines_ReturnsStopAtNextPhase()
    {
        var (prev, stop) = await ProjectAgentService.ApplyPhaseGateAsync(
            "Explore", PhasesArg(("Explore", "done"), ("Implement", "active")),
            autoFlowPhases: false, phaseGate: _ => System.Threading.Tasks.Task.FromResult(false));

        Assert.NotNull(stop);
        Assert.Equal("Implement", stop!.NextPhase);
        Assert.Equal("Implement", prev);
    }

    [Fact]
    public async System.Threading.Tasks.Task ApplyPhaseGate_GateApproves_ContinuesAndAdvances()
    {
        var (prev, stop) = await ProjectAgentService.ApplyPhaseGateAsync(
            "Explore", PhasesArg(("Explore", "done"), ("Implement", "active")),
            autoFlowPhases: false, phaseGate: _ => System.Threading.Tasks.Task.FromResult(true));

        Assert.Null(stop);
        Assert.Equal("Implement", prev);
    }

    [Fact]
    public async System.Threading.Tasks.Task ApplyPhaseGate_NoActivePhase_KeepsPreviousTracker()
    {
        // All phases done → no active phase now; the tracker keeps its prior value (?? fallback) and no stop.
        var (prev, stop) = await ProjectAgentService.ApplyPhaseGateAsync(
            "Implement", PhasesArg(("Explore", "done"), ("Implement", "done")),
            autoFlowPhases: false, phaseGate: _ => System.Threading.Tasks.Task.FromResult(false));

        Assert.Null(stop);
        Assert.Equal("Implement", prev);
    }
}
