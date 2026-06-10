using AI_Interface.Models;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the plan/checklist projection on <see cref="MessageViewModel.SetPlan"/> (the
/// <c>update_plan</c> tool). The view model is a plain <c>ObservableObject</c> with no I/O, so the
/// wholesale rebuild contract — the agent resends the FULL ordered list each call, so the VM replaces
/// (never appends to) <see cref="MessageViewModel.Plan"/> — and the <see cref="MessageViewModel.HasPlan"/>
/// flag are directly unit-testable.
/// </summary>
public sealed class MessageViewModelPlanTests
{
    private static MessageViewModel NewAssistant() => new(ChatRole.Assistant);

    private static PlanUpdate Plan(params PlanStep[] steps) => new(steps);

    // ---- Empty -------------------------------------------------------------------------------

    [Fact]
    public void SetPlan_EmptySteps_PlanEmpty_HasPlanFalse()
    {
        var msg = NewAssistant();

        msg.SetPlan(Plan());

        Assert.Empty(msg.Plan);
        Assert.False(msg.HasPlan);
    }

    // ---- Non-empty: mapped 1:1 in order ------------------------------------------------------

    [Fact]
    public void SetPlan_NonEmpty_MapsRowsOneToOneInOrder_HasPlanTrue()
    {
        var msg = NewAssistant();

        msg.SetPlan(Plan(
            new PlanStep("design the schema", PlanStepStatus.Done),
            new PlanStep("write the migration", PlanStepStatus.Active),
            new PlanStep("add tests", PlanStepStatus.Pending)));

        Assert.True(msg.HasPlan);
        Assert.Collection(msg.Plan,
            r => { Assert.Equal("design the schema", r.Text); Assert.Equal(PlanStepStatus.Done, r.Status); },
            r => { Assert.Equal("write the migration", r.Text); Assert.Equal(PlanStepStatus.Active, r.Status); },
            r => { Assert.Equal("add tests", r.Text); Assert.Equal(PlanStepStatus.Pending, r.Status); });
    }

    // ---- Rebuild / replace semantics ---------------------------------------------------------

    [Fact]
    public void SetPlan_CalledAgainWithShorterList_ReplacesNotAppends()
    {
        var msg = NewAssistant();

        msg.SetPlan(Plan(
            new PlanStep("a", PlanStepStatus.Done),
            new PlanStep("b", PlanStepStatus.Active),
            new PlanStep("c", PlanStepStatus.Pending)));
        Assert.Equal(3, msg.Plan.Count);

        // The agent resends the full (now shorter) list -> the VM rebuilds, leaving exactly one row.
        msg.SetPlan(Plan(new PlanStep("only", PlanStepStatus.Active)));

        Assert.True(msg.HasPlan);
        var only = Assert.Single(msg.Plan);
        Assert.Equal("only", only.Text);
        Assert.Equal(PlanStepStatus.Active, only.Status);
    }

    // ---- Reset back to empty -----------------------------------------------------------------

    [Fact]
    public void SetPlan_EmptyAfterNonEmpty_ClearsPlan_HasPlanFalse()
    {
        var msg = NewAssistant();

        msg.SetPlan(Plan(
            new PlanStep("a", PlanStepStatus.Done),
            new PlanStep("b", PlanStepStatus.Pending)));
        Assert.True(msg.HasPlan);

        msg.SetPlan(Plan());

        Assert.Empty(msg.Plan);
        Assert.False(msg.HasPlan);
    }
}
