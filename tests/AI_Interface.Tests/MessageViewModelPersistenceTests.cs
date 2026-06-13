using System.Linq;
using System.Text.Json;
using AI_Interface.Models;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the structured Project-mode persistence on <see cref="MessageViewModel"/>: exporting the
/// live plan / activity feed / delegation cards to the serializable <see cref="ChatTurn"/> DTOs and restoring
/// them back into the same cards when a saved chat is reopened (the round-trip <c>PersistCurrentSession</c> /
/// <c>OpenSession</c> rely on). Also covers the JSON round-trip (enum-as-string) of those DTOs.
/// </summary>
public sealed class MessageViewModelPersistenceTests
{
    private static MessageViewModel NewAssistant() => new(ChatRole.Assistant);

    private static ActivityUpdate Started(int index, string icon, string title, string detail) =>
        new(ActivityPhase.Started, index, icon, title, detail, "", false);

    private static ActivityUpdate Finished(int index, string result, bool failed = false) =>
        new(ActivityPhase.Finished, index, "", "", "", result, failed);

    // ---- Plan round-trip ----------------------------------------------------------------------

    [Fact]
    public void Plan_ExportRestore_RoundTripsPhasesAndStatuses()
    {
        var src = NewAssistant();
        src.SetPlan(new PlanUpdate(
            System.Array.Empty<PlanStep>(),
            new[]
            {
                new PlanPhase("Design", PlanStepStatus.Done, new[]
                {
                    new PlanStep("Sketch UI", PlanStepStatus.Done)
                }),
                new PlanPhase("Build", PlanStepStatus.Active, new[]
                {
                    new PlanStep("Write code", PlanStepStatus.Active),
                    new PlanStep("Test", PlanStepStatus.Pending)
                })
            }));

        var exported = src.ExportPlan();
        Assert.NotNull(exported);

        var restored = NewAssistant();
        restored.RestorePlan(exported!);

        Assert.True(restored.HasPhases);
        Assert.False(restored.HasPlan);                 // phases win the single plan slot
        Assert.Equal(2, restored.Phases.Count);
        Assert.Equal("Build", restored.Phases[1].Name);
        Assert.Equal(PlanStepStatus.Active, restored.Phases[1].Status);
        Assert.Equal(new[] { "Write code", "Test" }, restored.Phases[1].Steps.Select(s => s.Text));
        Assert.Equal(PlanStepStatus.Pending, restored.Phases[1].Steps[1].Status);
    }

    [Fact]
    public void Plan_ExportFlatChecklist_RoundTrips()
    {
        var src = NewAssistant();
        src.SetPlan(new PlanUpdate(new[]
        {
            new PlanStep("Step one", PlanStepStatus.Done),
            new PlanStep("Step two", PlanStepStatus.Active)
        }));

        var restored = NewAssistant();
        restored.RestorePlan(src.ExportPlan()!);

        Assert.True(restored.HasPlan);
        Assert.False(restored.HasPhases);
        Assert.Equal(new[] { "Step one", "Step two" }, restored.Plan.Select(s => s.Text));
        Assert.Equal(PlanStepStatus.Active, restored.Plan[1].Status);
    }

    [Fact]
    public void ExportPlan_ReturnsNull_WhenNoPlan() => Assert.Null(NewAssistant().ExportPlan());

    // ---- Activity feed round-trip -------------------------------------------------------------

    [Fact]
    public void Activities_ExportRestore_PreservesRowsAndMarksFinished()
    {
        var src = NewAssistant();
        src.ApplyActivity(new ActivityUpdate(ActivityPhase.Note, 0, "", "", "", "Reading the project…", false));
        src.ApplyActivity(Started(1, "📄", "Read file", "README.md"));
        src.ApplyActivity(Finished(1, "read 120 lines"));
        src.ApplyActivity(Started(2, "⌘", "Run command", "npm test"));
        src.ApplyActivity(Finished(2, "Error: failed.", failed: true));

        var restored = NewAssistant();
        restored.RestoreActivities(src.ExportActivities()!);

        Assert.True(restored.HasActivities);
        Assert.Equal(3, restored.Activities.Count);

        var note = restored.Activities[0];
        Assert.True(note.IsNote);
        Assert.Equal("Reading the project…", note.Text);

        var read = restored.Activities[1];
        Assert.False(read.IsNote);
        Assert.False(read.IsRunning);                   // restored rows are finished, not running
        Assert.Equal("Read file", read.Title);
        Assert.Equal("README.md", read.Detail);
        Assert.Equal("read 120 lines", read.Result);

        var run = restored.Activities[2];
        Assert.True(run.Failed);
        Assert.False(run.IsRunning);
    }

    [Fact]
    public void ExportActivities_ReturnsNull_WhenEmpty() => Assert.Null(NewAssistant().ExportActivities());

    // ---- Delegation (subagent) round-trip -----------------------------------------------------

    [Fact]
    public void Delegations_ExportRestore_PreservesSubagentOutputAndActions()
    {
        var src = NewAssistant();
        src.StartDelegation(0, "Tester", "🧪", "Write unit tests");
        src.ApplyDelegationActivity(0, Started(0, "✏️", "Write file", "tests/Foo.cs"));
        src.ApplyDelegationActivity(0, Finished(0, "wrote 30 lines"));
        src.FinishDelegation(0, "Added 5 tests, all passing.");

        var exported = src.ExportDelegations();
        Assert.NotNull(exported);

        var restored = NewAssistant();
        restored.RestoreDelegations(exported!);

        Assert.True(restored.HasDelegations);
        var card = Assert.Single(restored.Delegations);
        Assert.Equal("Tester", card.AgentName);
        Assert.Equal("🧪", card.Glyph);
        Assert.Equal("Write unit tests", card.Task);
        Assert.Equal("Added 5 tests, all passing.", card.Result);
        Assert.False(card.IsRunning);                   // restored cards are finished

        Assert.True(card.HasActivities);
        var step = Assert.Single(card.Activities);
        Assert.Equal("Write file", step.Title);
        Assert.Equal("wrote 30 lines", step.Result);
        Assert.False(step.IsRunning);
    }

    // ---- JSON round-trip (the on-disk schema) -------------------------------------------------

    [Fact]
    public void ChatTurn_JsonRoundTrip_PreservesStructuredFields_WithStringEnums()
    {
        var src = NewAssistant();
        src.SetPlan(new PlanUpdate(new[] { new PlanStep("Do it", PlanStepStatus.Active) }));
        src.StartDelegation(0, "Tester", "🧪", "Write tests");
        src.FinishDelegation(0, "done");

        var turn = new ChatTurn
        {
            Role = ChatRole.Assistant,
            Text = "answer",
            Plan = src.ExportPlan(),
            Delegations = src.ExportDelegations()
        };

        var json = JsonSerializer.Serialize(turn);
        Assert.Contains("\"Active\"", json);            // PlanStepStatus persists as a readable string, not an int

        var back = JsonSerializer.Deserialize<ChatTurn>(json)!;
        Assert.Equal("answer", back.Text);
        Assert.Equal(PlanStepStatus.Active, back.Plan!.Steps.Single().Status);
        Assert.Equal("Tester", back.Delegations!.Single().AgentName);
        Assert.Equal("done", back.Delegations!.Single().Result);
    }
}
