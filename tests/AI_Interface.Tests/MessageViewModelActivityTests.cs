using System.Linq;
using AI_Interface.Models;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the single-agent structured activity feed on <see cref="MessageViewModel.ApplyActivity"/>.
/// The view model is a plain <c>ObservableObject</c> with no I/O, so the Started/Finished/Note handling,
/// the index-based correlation of a Finished to its Started row, the null-safe lookup for stray indices,
/// and the <c>HasActivities</c>/<c>ShowWorkBlock</c> flag interplay are directly unit-testable.
/// </summary>
public sealed class MessageViewModelActivityTests
{
    private static MessageViewModel NewAssistant() => new(ChatRole.Assistant);

    private static ActivityUpdate Started(int index, string icon, string title, string detail) =>
        new(ActivityPhase.Started, index, icon, title, detail, "", false);

    private static ActivityUpdate Finished(int index, string result, bool failed = false) =>
        new(ActivityPhase.Finished, index, "", "", "", result, failed);

    private static ActivityUpdate Note(int index, string text) =>
        new(ActivityPhase.Note, index, "", "", "", text, false);

    // ---- Started ------------------------------------------------------------------------------

    [Fact]
    public void ApplyActivity_Started_AddsRunningToolRow_SetsHasActivities()
    {
        var msg = NewAssistant();

        msg.ApplyActivity(Started(0, "✏️", "Write file", "src/App.jsx"));

        Assert.True(msg.HasActivities);
        var row = Assert.Single(msg.Activities);
        Assert.False(row.IsNote);
        Assert.True(row.IsRunning);
        Assert.Equal("✏️", row.Icon);
        Assert.Equal("Write file", row.Title);
        Assert.Equal("src/App.jsx", row.Detail);
    }

    [Fact]
    public void ApplyActivity_Started_SuppressesLegacyWorkBlock_EvenWhenHasWorkSet()
    {
        var msg = NewAssistant();

        // The legacy monospace block would normally show once there's work text...
        msg.SetWork("some reasoning");
        Assert.True(msg.ShowWorkBlock);

        // ...but a structured feed (HasActivities) hides it (no duplicate display).
        msg.ApplyActivity(Started(0, "📄", "Read file", "README.md"));

        Assert.True(msg.HasWork);
        Assert.True(msg.HasActivities);
        Assert.False(msg.ShowWorkBlock);
    }

    // ---- Finished -----------------------------------------------------------------------------

    [Fact]
    public void ApplyActivity_Finished_MatchingIndex_SetsResultAndClearsRunning()
    {
        var msg = NewAssistant();
        msg.ApplyActivity(Started(0, "✏️", "Write file", "src/App.jsx"));

        msg.ApplyActivity(Finished(0, "Wrote 42 characters.", failed: false));

        var row = Assert.Single(msg.Activities);
        Assert.Equal("Wrote 42 characters.", row.Result);
        Assert.False(row.Failed);
        Assert.False(row.IsRunning);
    }

    [Fact]
    public void ApplyActivity_Finished_FailedFlag_IsRecordedOnRow()
    {
        var msg = NewAssistant();
        msg.ApplyActivity(Started(0, "⌘", "Run command", "npm test"));

        msg.ApplyActivity(Finished(0, "Error: command failed.", failed: true));

        var row = Assert.Single(msg.Activities);
        Assert.True(row.Failed);
        Assert.False(row.IsRunning);
    }

    [Fact]
    public void ApplyActivity_Finished_UnknownIndex_IsNoOp()
    {
        var msg = NewAssistant();
        msg.ApplyActivity(Started(0, "✏️", "Write file", "src/App.jsx"));

        // No row at index 99 -> ActivityFeed.FindStarted returns null and the call is a no-op (early return).
        msg.ApplyActivity(Finished(99, "stray"));

        var row = Assert.Single(msg.Activities);
        Assert.True(row.IsRunning);
        Assert.Equal("", row.Result);
    }

    // ---- Note ---------------------------------------------------------------------------------

    [Fact]
    public void ApplyActivity_Note_AddsNoteRow_SetsHasActivities()
    {
        var msg = NewAssistant();

        msg.ApplyActivity(Note(0, "Planning the next step…"));

        Assert.True(msg.HasActivities);
        var row = Assert.Single(msg.Activities);
        Assert.True(row.IsNote);
        Assert.Equal("Planning the next step…", row.Text);
    }

    [Fact]
    public void ApplyActivity_Finished_DoesNotMatchNoteRow()
    {
        var msg = NewAssistant();

        // A note shares the index, but Finished only targets non-note rows.
        msg.ApplyActivity(Note(0, "narration"));
        msg.ApplyActivity(Finished(0, "should not land on the note"));

        var note = Assert.Single(msg.Activities);
        Assert.True(note.IsNote);
        // The note carries its narration in Text; its Result stays empty (Finished was a no-op).
        Assert.Equal("narration", note.Text);
        Assert.Equal("", note.Result);
        Assert.False(note.Failed);
    }

    // ---- Index correlation across mixed rows --------------------------------------------------

    [Fact]
    public void ApplyActivity_Finished_LandsOnToolRow_NotNote_WhenInterleaved()
    {
        var msg = NewAssistant();

        // Note at 0, tool Started at 1, then Finished at 1.
        msg.ApplyActivity(Note(0, "thinking out loud"));
        msg.ApplyActivity(Started(1, "📄", "Read file", "src/index.js"));
        msg.ApplyActivity(Finished(1, "read 120 lines"));

        Assert.Equal(2, msg.Activities.Count);

        var note = msg.Activities.Single(a => a.IsNote);
        var tool = msg.Activities.Single(a => !a.IsNote);

        // The note is untouched; the tool row got the result and stopped running.
        Assert.Equal("thinking out loud", note.Text);
        Assert.Equal("", note.Result);

        Assert.Equal("read 120 lines", tool.Result);
        Assert.False(tool.IsRunning);
    }

    [Fact]
    public void ApplyActivity_Finished_OnlyAffectsMatchingToolIndex()
    {
        var msg = NewAssistant();
        msg.ApplyActivity(Started(0, "📄", "Read file", "a.txt"));
        msg.ApplyActivity(Started(1, "✏️", "Write file", "b.txt"));

        msg.ApplyActivity(Finished(0, "done A"));

        var first = msg.Activities.Single(a => !a.IsNote && a.Index == 0);
        var second = msg.Activities.Single(a => !a.IsNote && a.Index == 1);

        Assert.False(first.IsRunning);
        Assert.Equal("done A", first.Result);

        // The other row is untouched -> updates route by index, not by position/last-added.
        Assert.True(second.IsRunning);
        Assert.Equal("", second.Result);
    }
}
