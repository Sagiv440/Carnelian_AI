using System.Collections.ObjectModel;
using System.Linq;
using AI_Interface.Models;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for <see cref="ActivityFeed.Apply"/> — the shared, I/O-free logic behind both
/// <c>MessageViewModel.ApplyActivity</c> and <c>DelegationStepViewModel.ApplyActivity</c>. Exercises each
/// <see cref="ActivityPhase"/> directly against a bare <see cref="ObservableCollection{T}"/>: a Note appends
/// a non-resolvable narration row, a Started appends a running tool row, and a Finished resolves the LAST
/// non-note row with the matching index (skipping notes and ignoring a missing match). <c>ActivityFeed</c> is
/// <c>internal static</c>, reachable from the test assembly via <c>[assembly: InternalsVisibleTo]</c>.
/// </summary>
public sealed class ActivityFeedTests
{
    private static ObservableCollection<ActivityStepViewModel> NewFeed() => new();

    private static ActivityUpdate Started(int index, string icon, string title, string detail) =>
        new(ActivityPhase.Started, index, icon, title, detail, "", false);

    private static ActivityUpdate Finished(int index, string result, bool failed = false) =>
        new(ActivityPhase.Finished, index, "", "", "", result, failed);

    private static ActivityUpdate Note(int index, string text) =>
        new(ActivityPhase.Note, index, "", "", "", text, false);

    // ---- Note ---------------------------------------------------------------------------------

    [Fact]
    public void Apply_Note_AppendsNoteRow_WithIndexAndText()
    {
        var feed = NewFeed();

        ActivityFeed.Apply(feed, Note(3, "Planning the next step…"));

        var row = Assert.Single(feed);
        Assert.True(row.IsNote);
        Assert.Equal(3, row.Index);
        Assert.Equal("Planning the next step…", row.Text);
    }

    [Fact]
    public void Apply_Note_IsNotResolvableByFinished()
    {
        var feed = NewFeed();

        ActivityFeed.Apply(feed, Note(0, "narration"));
        ActivityFeed.Apply(feed, Finished(0, "should not land on the note"));

        var row = Assert.Single(feed);
        Assert.True(row.IsNote);
        // The note keeps its narration; Result stays empty because Finished skipped it (no-op).
        Assert.Equal("narration", row.Text);
        Assert.Equal("", row.Result);
        Assert.False(row.Failed);
    }

    // ---- Started ------------------------------------------------------------------------------

    [Fact]
    public void Apply_Started_AppendsRunningToolRow_WithAllFieldsSet()
    {
        var feed = NewFeed();

        ActivityFeed.Apply(feed, Started(0, "✏️", "Write file", "src/App.jsx"));

        var row = Assert.Single(feed);
        Assert.False(row.IsNote);
        Assert.True(row.IsRunning);
        Assert.Equal(0, row.Index);
        Assert.Equal("✏️", row.Icon);
        Assert.Equal("Write file", row.Title);
        Assert.Equal("src/App.jsx", row.Detail);
    }

    // ---- Finished -----------------------------------------------------------------------------

    [Fact]
    public void Apply_Finished_MatchingIndex_SetsResultFailedClearsRunning()
    {
        var feed = NewFeed();
        ActivityFeed.Apply(feed, Started(0, "⌘", "Run command", "npm test"));

        ActivityFeed.Apply(feed, Finished(0, "Error: command failed.", failed: true));

        var row = Assert.Single(feed);
        Assert.Equal("Error: command failed.", row.Result);
        Assert.True(row.Failed);
        Assert.False(row.IsRunning);
    }

    [Fact]
    public void Apply_Finished_ResolvesCorrectRowByIndex_LeavingOthersUntouched()
    {
        var feed = NewFeed();
        ActivityFeed.Apply(feed, Started(0, "📄", "Read file", "a.txt"));
        ActivityFeed.Apply(feed, Started(1, "✏️", "Write file", "b.txt"));

        ActivityFeed.Apply(feed, Finished(1, "wrote b"));

        var first = feed.Single(r => r.Index == 0);
        var second = feed.Single(r => r.Index == 1);

        // Only the index-1 row is resolved; the index-0 row is still running with no result.
        Assert.True(first.IsRunning);
        Assert.Equal("", first.Result);

        Assert.False(second.IsRunning);
        Assert.Equal("wrote b", second.Result);
    }

    [Fact]
    public void Apply_Finished_TwoStartedSameIndex_ResolvesTheLastOne()
    {
        var feed = NewFeed();
        // Two tool rows share an index; FindStarted iterates from the end, so the SECOND one resolves.
        ActivityFeed.Apply(feed, Started(0, "📄", "Read file", "first.txt"));
        ActivityFeed.Apply(feed, Started(0, "📄", "Read file", "second.txt"));

        ActivityFeed.Apply(feed, Finished(0, "result for second"));

        Assert.Equal(2, feed.Count);
        var firstRow = feed[0];
        var lastRow = feed[1];

        // The first (earlier) row stays running; the last row got the result.
        Assert.True(firstRow.IsRunning);
        Assert.Equal("", firstRow.Result);

        Assert.False(lastRow.IsRunning);
        Assert.Equal("result for second", lastRow.Result);
        Assert.Equal("second.txt", lastRow.Detail);
    }

    [Fact]
    public void Apply_Finished_SkipsNoteWithSameIndex_ResolvesToolRowInstead()
    {
        var feed = NewFeed();
        // A note and a tool row share the index; Finished must land on the tool row, never the note.
        ActivityFeed.Apply(feed, Note(0, "thinking"));
        ActivityFeed.Apply(feed, Started(0, "📄", "Read file", "x.txt"));

        ActivityFeed.Apply(feed, Finished(0, "read 10 lines"));

        var note = feed.Single(r => r.IsNote);
        var tool = feed.Single(r => !r.IsNote);

        Assert.Equal("thinking", note.Text);
        Assert.Equal("", note.Result);     // note untouched

        Assert.Equal("read 10 lines", tool.Result);
        Assert.False(tool.IsRunning);
    }

    [Fact]
    public void Apply_Finished_NoteOnlyAtIndex_IsSilentNoOp()
    {
        var feed = NewFeed();
        ActivityFeed.Apply(feed, Note(0, "only a note here"));

        // The single row at index 0 is a note, so FindStarted returns null -> no-op (collection unchanged).
        ActivityFeed.Apply(feed, Finished(0, "stray"));

        var row = Assert.Single(feed);
        Assert.True(row.IsNote);
        Assert.Equal("", row.Result);
    }

    [Fact]
    public void Apply_Finished_NoMatchingStarted_IsSilentNoOp()
    {
        var feed = NewFeed();
        ActivityFeed.Apply(feed, Started(0, "✏️", "Write file", "src/App.jsx"));

        // No row at index 99 -> FindStarted returns null and the call leaves the feed unchanged.
        ActivityFeed.Apply(feed, Finished(99, "stray"));

        var row = Assert.Single(feed);
        Assert.True(row.IsRunning);
        Assert.Equal("", row.Result);
    }

    [Fact]
    public void Apply_Finished_OnEmptyFeed_IsSilentNoOp()
    {
        var feed = NewFeed();

        ActivityFeed.Apply(feed, Finished(0, "stray"));

        Assert.Empty(feed);
    }
}
