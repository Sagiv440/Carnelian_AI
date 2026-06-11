using System;
using AI_Interface.Models;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the phased-plan projection on <see cref="MessageViewModel.SetPlan"/> and the computed
/// glyph/flags of <see cref="PlanPhaseViewModel"/>. Phases win the single plan slot over the flat checklist
/// (<see cref="MessageViewModel.HasPlan"/> goes false when phases are present), and like the flat list the
/// phases are rebuilt wholesale on each update.
/// </summary>
public sealed class MessageViewModelPhasesTests
{
    private static MessageViewModel NewAssistant() => new(ChatRole.Assistant);

    private static PlanUpdate Phased(params PlanPhase[] phases) => new(Array.Empty<PlanStep>(), phases);

    // ---- PlanPhaseViewModel glyph / flags -----------------------------------------------------

    [Theory]
    [InlineData(PlanStepStatus.Done, "☑", true, false)]
    [InlineData(PlanStepStatus.Active, "▶", false, true)]
    [InlineData(PlanStepStatus.Pending, "☐", false, false)]
    public void PlanPhaseViewModel_GlyphAndFlags_MatchStatus(PlanStepStatus status, string glyph, bool done, bool active)
    {
        var vm = new PlanPhaseViewModel { Name = "P", Status = status };

        Assert.Equal(glyph, vm.Glyph);
        Assert.Equal(done, vm.IsDone);
        Assert.Equal(active, vm.IsActive);
    }

    // ---- SetPlan with phases ------------------------------------------------------------------

    [Fact]
    public void SetPlan_Phased_PopulatesPhasesWithNestedSteps_HasPhasesTrue_HasPlanFalse()
    {
        var msg = NewAssistant();

        msg.SetPlan(Phased(
            new PlanPhase("Explore", PlanStepStatus.Done, new[] { new PlanStep("read", PlanStepStatus.Done) }),
            new PlanPhase("Implement", PlanStepStatus.Active, new[]
            {
                new PlanStep("model", PlanStepStatus.Done),
                new PlanStep("wire UI", PlanStepStatus.Active)
            })));

        Assert.True(msg.HasPhases);
        Assert.False(msg.HasPlan);     // phases win the single plan slot
        Assert.Empty(msg.Plan);
        Assert.Collection(msg.Phases,
            p => { Assert.Equal("Explore", p.Name); Assert.Equal(PlanStepStatus.Done, p.Status); Assert.Single(p.Steps); },
            p =>
            {
                Assert.Equal("Implement", p.Name);
                Assert.Equal(PlanStepStatus.Active, p.Status);
                Assert.Collection(p.Steps,
                    s => { Assert.Equal("model", s.Text); Assert.Equal(PlanStepStatus.Done, s.Status); },
                    s => { Assert.Equal("wire UI", s.Text); Assert.Equal(PlanStepStatus.Active, s.Status); });
            });
    }

    [Fact]
    public void SetPlan_FlatAfterPhased_SwitchesBackToFlat()
    {
        var msg = NewAssistant();

        msg.SetPlan(Phased(new PlanPhase("A", PlanStepStatus.Active, Array.Empty<PlanStep>())));
        Assert.True(msg.HasPhases);

        // The agent resends a flat plan -> phases clear, flat checklist shows.
        msg.SetPlan(new PlanUpdate(new[] { new PlanStep("just a step", PlanStepStatus.Pending) }));

        Assert.False(msg.HasPhases);
        Assert.Empty(msg.Phases);
        Assert.True(msg.HasPlan);
        Assert.Single(msg.Plan);
    }

    [Fact]
    public void SetPlan_PhasedAfterFlat_SwitchesToPhases()
    {
        var msg = NewAssistant();

        msg.SetPlan(new PlanUpdate(new[] { new PlanStep("step", PlanStepStatus.Active) }));
        Assert.True(msg.HasPlan);

        msg.SetPlan(Phased(new PlanPhase("Phase 1", PlanStepStatus.Active, Array.Empty<PlanStep>())));

        Assert.True(msg.HasPhases);
        Assert.False(msg.HasPlan);
        Assert.Empty(msg.Plan);
        Assert.Single(msg.Phases);
    }
}
