using System.Text.Json;
using AI_Interface.Models;
using AI_Interface.Services;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the "Batch 2" <c>update_plan</c> plan/checklist tool's pure logic:
/// <see cref="ProjectAgentService.ParseStatus"/> (status-string synonyms → <see cref="PlanStepStatus"/>),
/// <see cref="ProjectAgentService.ParsePlanSteps"/> (a value-kind-guarded parse of the tool's <c>steps</c>
/// argument that must never throw on malformed input), and <see cref="PlanStepViewModel"/>'s computed
/// glyph/flags. All deterministic and I/O-free; the parser is exercised with hand-built
/// <see cref="JsonElement"/>s (anonymous objects + raw JSON for the malformed-matrix cases).
/// </summary>
public sealed class PlanToolTests
{
    // ---- ParseStatus: synonyms -> PlanStepStatus ----------------------------------------------

    [Theory]
    [InlineData("done")]
    [InlineData("complete")]
    [InlineData("completed")]
    [InlineData("finished")]
    [InlineData("x")]
    [InlineData("✓")]
    public void ParseStatus_DoneSynonyms_ReturnsDone(string s)
    {
        Assert.Equal(PlanStepStatus.Done, ProjectAgentService.ParseStatus(s));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("in_progress")]
    [InlineData("in-progress")]
    [InlineData("in progress")]
    [InlineData("doing")]
    [InlineData("current")]
    [InlineData("wip")]
    public void ParseStatus_ActiveSynonyms_ReturnsActive(string s)
    {
        Assert.Equal(PlanStepStatus.Active, ProjectAgentService.ParseStatus(s));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("pending")]
    [InlineData("todo")]
    public void ParseStatus_NullEmptyOrUnknown_ReturnsPendingDefault(string? s)
    {
        Assert.Equal(PlanStepStatus.Pending, ProjectAgentService.ParseStatus(s));
    }

    [Theory]
    [InlineData(" DONE ", PlanStepStatus.Done)]      // surrounding whitespace + upper-case
    [InlineData("In_Progress", PlanStepStatus.Active)] // mixed case
    public void ParseStatus_IsCaseAndWhitespaceInsensitive(string s, PlanStepStatus expected)
    {
        Assert.Equal(expected, ProjectAgentService.ParseStatus(s));
    }

    // ---- ParsePlanSteps: well-formed -----------------------------------------------------------

    [Fact]
    public void ParsePlanSteps_WellFormedObjects_ReturnsStepsInOrderWithStatuses()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            steps = new object[]
            {
                new { text = "a", status = "done" },
                new { text = "b", status = "active" },
                new { text = "c" }
            }
        });

        var steps = ProjectAgentService.ParsePlanSteps(args);

        Assert.Collection(steps,
            s => { Assert.Equal("a", s.Text); Assert.Equal(PlanStepStatus.Done, s.Status); },
            s => { Assert.Equal("b", s.Text); Assert.Equal(PlanStepStatus.Active, s.Status); },
            s => { Assert.Equal("c", s.Text); Assert.Equal(PlanStepStatus.Pending, s.Status); });
    }

    [Fact]
    public void ParsePlanSteps_PlainStringElements_AreAllPending()
    {
        var args = JsonSerializer.SerializeToElement(new { steps = new[] { "first", "second" } });

        var steps = ProjectAgentService.ParsePlanSteps(args);

        Assert.Collection(steps,
            s => { Assert.Equal("first", s.Text); Assert.Equal(PlanStepStatus.Pending, s.Status); },
            s => { Assert.Equal("second", s.Text); Assert.Equal(PlanStepStatus.Pending, s.Status); });
    }

    // ---- ParsePlanSteps: degenerate / non-array -> empty (never throws) ------------------------

    [Fact]
    public void ParsePlanSteps_MissingStepsProperty_ReturnsEmpty()
    {
        var args = JsonSerializer.SerializeToElement(new { other = 1 });
        Assert.Empty(ProjectAgentService.ParsePlanSteps(args));
    }

    [Fact]
    public void ParsePlanSteps_StepsNotAnArray_ReturnsEmpty()
    {
        var args = JsonSerializer.SerializeToElement(new { steps = "nope" });
        Assert.Empty(ProjectAgentService.ParsePlanSteps(args));
    }

    [Fact]
    public void ParsePlanSteps_UndefinedValueKind_ReturnsEmpty()
    {
        // default(JsonElement) has ValueKind == Undefined (not an object) -> the guard returns early.
        Assert.Empty(ProjectAgentService.ParsePlanSteps(default));
    }

    [Fact]
    public void ParsePlanSteps_NonObjectArg_ReturnsEmpty()
    {
        var args = JsonSerializer.SerializeToElement(42);
        Assert.Empty(ProjectAgentService.ParsePlanSteps(args));
    }

    // ---- ParsePlanSteps: malformed matrix — junk items skipped ---------------------------------

    [Fact]
    public void ParsePlanSteps_ArrayWithJunkElements_SkipsAllButValidObject()
    {
        // Numbers, nulls, and nested arrays are not String/Object items -> skipped; only {text:"keep"} survives.
        using var doc = JsonDocument.Parse("""{"steps":[1,null,["x"],{"text":"keep"}]}""");

        var steps = ProjectAgentService.ParsePlanSteps(doc.RootElement);

        var only = Assert.Single(steps);
        Assert.Equal("keep", only.Text);
        Assert.Equal(PlanStepStatus.Pending, only.Status);
    }

    [Fact]
    public void ParsePlanSteps_ObjectMissingText_IsSkipped()
    {
        using var doc = JsonDocument.Parse("""{"steps":[{"status":"done"}]}""");
        Assert.Empty(ProjectAgentService.ParsePlanSteps(doc.RootElement));
    }

    [Fact]
    public void ParsePlanSteps_WhitespaceOnlyText_IsSkipped()
    {
        // text is trimmed; an all-whitespace text has zero length after trimming -> not added.
        using var doc = JsonDocument.Parse("""{"steps":[{"text":"   "}]}""");
        Assert.Empty(ProjectAgentService.ParsePlanSteps(doc.RootElement));
    }

    [Fact]
    public void ParsePlanSteps_NumericStatus_DegradesToPending()
    {
        // status is honored only when a JSON string; a number is ignored -> default Pending.
        using var doc = JsonDocument.Parse("""{"steps":[{"text":"a","status":2}]}""");

        var steps = ProjectAgentService.ParsePlanSteps(doc.RootElement);

        var only = Assert.Single(steps);
        Assert.Equal("a", only.Text);
        Assert.Equal(PlanStepStatus.Pending, only.Status);
    }

    [Fact]
    public void ParsePlanSteps_BoolStatus_DegradesToPending()
    {
        using var doc = JsonDocument.Parse("""{"steps":[{"text":"a","status":true}]}""");

        var steps = ProjectAgentService.ParsePlanSteps(doc.RootElement);

        var only = Assert.Single(steps);
        Assert.Equal(PlanStepStatus.Pending, only.Status);
    }

    // ---- PlanStepViewModel: computed glyph + flags --------------------------------------------

    [Fact]
    public void PlanStepViewModel_Done_GlyphIsCheckedBox_IsDoneTrue_IsActiveFalse()
    {
        var vm = new PlanStepViewModel { Text = "ship it", Status = PlanStepStatus.Done };

        Assert.Equal("☑", vm.Glyph);
        Assert.True(vm.IsDone);
        Assert.False(vm.IsActive);
    }

    [Fact]
    public void PlanStepViewModel_Active_GlyphIsTriangle_IsActiveTrue_IsDoneFalse()
    {
        var vm = new PlanStepViewModel { Text = "build it", Status = PlanStepStatus.Active };

        Assert.Equal("▶", vm.Glyph);
        Assert.False(vm.IsDone);
        Assert.True(vm.IsActive);
    }

    [Fact]
    public void PlanStepViewModel_Pending_GlyphIsEmptyBox_BothFlagsFalse()
    {
        var vm = new PlanStepViewModel { Text = "plan it", Status = PlanStepStatus.Pending };

        Assert.Equal("☐", vm.Glyph);
        Assert.False(vm.IsDone);
        Assert.False(vm.IsActive);
    }

    [Fact]
    public void PlanStepViewModel_Text_RoundTripsTheInitValue()
    {
        var vm = new PlanStepViewModel { Text = "write the docs", Status = PlanStepStatus.Pending };
        Assert.Equal("write the docs", vm.Text);
    }
}
