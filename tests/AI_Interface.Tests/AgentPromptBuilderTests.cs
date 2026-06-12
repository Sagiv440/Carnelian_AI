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

    // --- PhasesDirective: always present, mentions phases + update_plan ------------------------

    [Fact]
    public void PhasesDirective_IsNonEmpty_AndMentionsPhasesAndTheTool()
    {
        var directive = AgentPromptBuilder.PhasesDirective();

        Assert.False(string.IsNullOrEmpty(directive));
        Assert.Contains("phases", directive);
        Assert.Contains("update_plan", directive);
        Assert.StartsWith("\n\n", directive); // leads with a blank line so it slots after preceding content
    }

    // --- ClarifyDirective: always present, tells the agent to ask when vague --------------------

    [Fact]
    public void ClarifyDirective_IsNonEmpty_AndMentionsClarifyingQuestionsWhenVague()
    {
        var directive = AgentPromptBuilder.ClarifyDirective();

        Assert.False(string.IsNullOrEmpty(directive));
        Assert.Contains("clarifying questions", directive);
        Assert.Contains("vague", directive);
        Assert.StartsWith("\n\n", directive); // leads with a blank line so it slots after preceding content
    }
}
