using AI_Interface.Models;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for <see cref="AutonomyMap.ForApprovalMode"/> — the pure, I/O-free mapping from the single
/// global <see cref="AgentApprovalMode"/> onto a project-agent run's knobs (approval mode + step budget).
/// Per-agent autonomy was removed, so this map is the authoritative source of approval policy + step
/// budget for every project-agent run. The tests assert the full mapping and that the
/// <see cref="AutonomyMap.GuidedSteps"/> const stays the source of truth for the ConfirmDestructive budget.
/// </summary>
public class AutonomyMapTests
{
    // --- ForApprovalMode (the full mapping) ----------------------------------------------------

    [Theory]
    [InlineData(AgentApprovalMode.ConfirmEverything, AgentApprovalMode.ConfirmEverything, 8)]
    [InlineData(AgentApprovalMode.ConfirmDestructive, AgentApprovalMode.ConfirmDestructive, 24)]
    [InlineData(AgentApprovalMode.AutoRun, AgentApprovalMode.AutoRun, 40)]
    public void ForApprovalMode_MapsModeToApprovalAndStepBudget(
        AgentApprovalMode mode, AgentApprovalMode expectedApproval, int expectedMaxSteps)
    {
        // Act
        var (approval, maxSteps) = AutonomyMap.ForApprovalMode(mode);

        // Assert: the mode round-trips and carries the expected step budget.
        Assert.Equal(expectedApproval, approval);
        Assert.Equal(expectedMaxSteps, maxSteps);
    }

    // --- GuidedSteps coupling ------------------------------------------------------------------

    [Fact]
    public void ForApprovalMode_ConfirmDestructive_UsesGuidedStepsConst()
    {
        // The ConfirmDestructive budget must come from the GuidedSteps const, not a magic literal,
        // so the const stays the single source of truth.
        Assert.Equal(24, AutonomyMap.GuidedSteps);

        var (_, maxSteps) = AutonomyMap.ForApprovalMode(AgentApprovalMode.ConfirmDestructive);
        Assert.Equal(AutonomyMap.GuidedSteps, maxSteps);
    }
}
