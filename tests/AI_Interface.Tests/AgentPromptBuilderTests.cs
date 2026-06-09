using AI_Interface.Models;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for <see cref="AgentPromptBuilder.PlanningDirective"/> — the pure, I/O-free helper that adds
/// a plan-then-execute directive to the Project-agent system prompt only when the global approval mode is
/// <see cref="AgentApprovalMode.AutoRun"/>. Under the confirm modes it contributes nothing (empty string),
/// so no planning pass is injected.
/// </summary>
public class AgentPromptBuilderTests
{
    // --- PlanningDirective: AutoRun emits the directive ----------------------------------------

    [Fact]
    public void PlanningDirective_AutoRun_ReturnsNonEmptyDirectiveWithPlanWording()
    {
        // Act
        var directive = AgentPromptBuilder.PlanningDirective(AgentApprovalMode.AutoRun);

        // Assert: non-empty and carries the directive's distinctive plan-then-execute wording.
        Assert.False(string.IsNullOrEmpty(directive));
        Assert.Contains("Work autonomously: first outline a short numbered plan", directive);
    }

    // --- PlanningDirective: confirm modes emit nothing -----------------------------------------

    [Theory]
    [InlineData(AgentApprovalMode.ConfirmDestructive)]
    [InlineData(AgentApprovalMode.ConfirmEverything)]
    public void PlanningDirective_ConfirmModes_ReturnEmptyString(AgentApprovalMode approval)
    {
        // Act
        var directive = AgentPromptBuilder.PlanningDirective(approval);

        // Assert: no planning pass for the confirm modes.
        Assert.Equal(string.Empty, directive);
    }
}
