using System.Text.Json;
using AI_Interface.Models;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the "Phases" extension of the <c>update_plan</c> tool:
/// <see cref="ProjectAgentService.ParsePhases"/> (value-kind-guarded parse of the <c>phases</c> array — must
/// never throw, skips items without a usable <c>name</c>) and <see cref="ProjectAgentService.UpdatePlan"/>'s
/// flat-vs-phased choice (phases win when present; otherwise the existing flat <c>steps</c> path). All
/// deterministic and I/O-free; the side effect is captured via the <c>onPlan</c> callback.
/// </summary>
public sealed class PhasesToolTests
{
    // ---- ParsePhases: well-formed --------------------------------------------------------------

    [Fact]
    public void ParsePhases_WellFormed_ReturnsPhasesWithStatusesAndNestedStepsInOrder()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            phases = new object[]
            {
                new { name = "Explore", status = "done", steps = new object[] { new { text = "read", status = "done" } } },
                new { name = "Implement", status = "active", steps = new object[]
                    {
                        new { text = "model", status = "done" },
                        new { text = "wire UI", status = "active" },
                        new { text = "verify" }
                    } },
                new { name = "Verify" }
            }
        });

        var phases = ProjectAgentService.ParsePhases(args);

        Assert.Collection(phases,
            p => { Assert.Equal("Explore", p.Name); Assert.Equal(PlanStepStatus.Done, p.Status); Assert.Single(p.Steps); },
            p =>
            {
                Assert.Equal("Implement", p.Name);
                Assert.Equal(PlanStepStatus.Active, p.Status);
                Assert.Collection(p.Steps,
                    s => { Assert.Equal("model", s.Text); Assert.Equal(PlanStepStatus.Done, s.Status); },
                    s => { Assert.Equal("wire UI", s.Text); Assert.Equal(PlanStepStatus.Active, s.Status); },
                    s => { Assert.Equal("verify", s.Text); Assert.Equal(PlanStepStatus.Pending, s.Status); });
            },
            p => { Assert.Equal("Verify", p.Name); Assert.Equal(PlanStepStatus.Pending, p.Status); Assert.Empty(p.Steps); });
    }

    [Fact]
    public void ParsePhases_PhaseWithoutSteps_IsKeptWithEmptySteps()
    {
        var args = JsonSerializer.SerializeToElement(new { phases = new object[] { new { name = "Plan", status = "active" } } });

        var phases = ProjectAgentService.ParsePhases(args);

        Assert.Single(phases);
        Assert.Empty(phases[0].Steps);
        Assert.Equal(PlanStepStatus.Active, phases[0].Status);
    }

    // ---- ParsePhases: degenerate / malformed -> never throws -----------------------------------

    [Fact]
    public void ParsePhases_MissingPhasesProperty_ReturnsEmpty()
    {
        var args = JsonSerializer.SerializeToElement(new { steps = new[] { "x" } });
        Assert.Empty(ProjectAgentService.ParsePhases(args));
    }

    [Fact]
    public void ParsePhases_PhasesNotAnArray_ReturnsEmpty()
    {
        var args = JsonSerializer.SerializeToElement(new { phases = "nope" });
        Assert.Empty(ProjectAgentService.ParsePhases(args));
    }

    [Fact]
    public void ParsePhases_UndefinedValueKind_ReturnsEmpty() =>
        Assert.Empty(ProjectAgentService.ParsePhases(default));

    [Fact]
    public void ParsePhases_NonObjectArg_ReturnsEmpty()
    {
        var args = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 });
        Assert.Empty(ProjectAgentService.ParsePhases(args));
    }

    [Fact]
    public void ParsePhases_ItemMissingOrBlankName_IsSkipped()
    {
        using var doc = JsonDocument.Parse(
            """{ "phases": [ { "status": "active" }, { "name": "   " }, { "name": "Real", "steps": [] }, "junk", 7 ] }""");

        var phases = ProjectAgentService.ParsePhases(doc.RootElement);

        Assert.Single(phases);
        Assert.Equal("Real", phases[0].Name);
    }

    [Fact]
    public void ParsePhases_NonStringStatus_DegradesToPending()
    {
        using var doc = JsonDocument.Parse("""{ "phases": [ { "name": "P", "status": 3 } ] }""");

        var phases = ProjectAgentService.ParsePhases(doc.RootElement);

        Assert.Equal(PlanStepStatus.Pending, phases[0].Status);
    }

    // ---- UpdatePlan: flat-vs-phased choice -----------------------------------------------------

    [Fact]
    public void UpdatePlan_PhasesPresent_EmitsPhasedPlanAndReportsPhaseCount()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            phases = new object[]
            {
                new { name = "A", status = "done" },
                new { name = "B", status = "active" }
            }
        });

        PlanUpdate? captured = null;
        var result = ProjectAgentService.UpdatePlan(args, u => captured = u);

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Phases.Count);
        Assert.Empty(captured.Steps);
        Assert.Contains("phase(s)", result);
    }

    [Fact]
    public void UpdatePlan_OnlySteps_EmitsFlatPlanAndReportsStepCount()
    {
        var args = JsonSerializer.SerializeToElement(new { steps = new[] { "one", "two" } });

        PlanUpdate? captured = null;
        var result = ProjectAgentService.UpdatePlan(args, u => captured = u);

        Assert.NotNull(captured);
        Assert.Empty(captured!.Phases);
        Assert.Equal(2, captured.Steps.Count);
        Assert.Contains("step(s)", result);
    }

    [Fact]
    public void UpdatePlan_PhasesWinWhenBothPresent()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            steps = new[] { "ignored" },
            phases = new object[] { new { name = "Only phase" } }
        });

        PlanUpdate? captured = null;
        ProjectAgentService.UpdatePlan(args, u => captured = u);

        Assert.NotNull(captured);
        Assert.Single(captured!.Phases);
        Assert.Empty(captured.Steps);
    }

    [Fact]
    public void UpdatePlan_NeitherStepsNorPhases_DoesNotInvokeCallback_AndReturnsGuidance()
    {
        var args = JsonSerializer.SerializeToElement(new { });

        var invoked = false;
        var result = ProjectAgentService.UpdatePlan(args, _ => invoked = true);

        Assert.False(invoked);
        Assert.Contains("phases", result);
    }
}
