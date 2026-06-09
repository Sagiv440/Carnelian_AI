using System.Linq;
using AI_Interface.Models;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the orchestrator (lead) delegation-card logic on <see cref="MessageViewModel"/> and
/// <see cref="DelegationStepViewModel"/>. Both are plain <c>ObservableObject</c> view models with no I/O,
/// so they are directly unit-testable. These cover the start/append/finish lifecycle, the index-based
/// routing of streamed updates, the null-safe lookup for stray indices, and the card-header glyph rule.
/// </summary>
public class MessageViewModelDelegationTests
{
    private static MessageViewModel NewAssistant() => new(ChatRole.Assistant);

    // --- StartDelegation -----------------------------------------------------------------------

    [Fact]
    public void StartDelegation_AddsCard_SetsRunningStateAndHeader()
    {
        var msg = NewAssistant();

        msg.StartDelegation(0, "Code Buddy", "🛠", "do the thing");

        Assert.Single(msg.Delegations);
        Assert.True(msg.HasDelegations);

        var card = msg.Delegations[0];
        Assert.Equal(0, card.Index);
        Assert.True(card.IsRunning);
        Assert.Equal("do the thing", card.Task);
        // Header glues glyph + name with TWO spaces.
        Assert.Equal("🛠  Code Buddy", card.Header);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void StartDelegation_BlankGlyph_HeaderFallsBackToAgentName(string glyph)
    {
        var msg = NewAssistant();

        msg.StartDelegation(0, "Researcher", glyph, "find sources");

        // With a null/empty/whitespace glyph the header is just the agent name (no leading spaces).
        Assert.Equal("Researcher", msg.Delegations[0].Header);
    }

    // --- ApplyDelegationActivity (structured specialist steps, Phase 3B) -----------------------

    [Fact]
    public void ApplyDelegationActivity_StartedThenFinished_AddsResolvedStructuredRow()
    {
        var msg = NewAssistant();
        msg.StartDelegation(0, "Code Buddy", "🛠", "do the thing");

        // The specialist's structured steps land in THIS card's own feed (its own per-run index space).
        msg.ApplyDelegationActivity(0, new ActivityUpdate(ActivityPhase.Started, 0, "✏️", "Write file", "App.jsx", "", false));
        msg.ApplyDelegationActivity(0, new ActivityUpdate(ActivityPhase.Finished, 0, "", "", "", "Wrote 10 chars.", false));

        var card = msg.Delegations[0];
        Assert.True(card.HasActivities);
        var row = Assert.Single(card.Activities);
        Assert.Equal("Write file", row.Title);
        Assert.Equal("App.jsx", row.Detail);
        Assert.Equal("Wrote 10 chars.", row.Result);
        Assert.False(row.IsRunning);
        Assert.False(row.Failed);
    }

    [Fact]
    public void ApplyDelegationActivity_Note_AddsMutedNoteRow()
    {
        var msg = NewAssistant();
        msg.StartDelegation(0, "Code Buddy", "🛠", "do the thing");

        msg.ApplyDelegationActivity(0, new ActivityUpdate(ActivityPhase.Note, 0, "", "", "", "planning the edit", false));

        var card = msg.Delegations[0];
        Assert.True(card.HasActivities);
        var row = Assert.Single(card.Activities);
        Assert.True(row.IsNote);
        Assert.Equal("planning the edit", row.Text);
    }

    [Fact]
    public void ApplyDelegationActivity_UnknownIndex_IsNoOp_DoesNotThrow()
    {
        var msg = NewAssistant();
        msg.StartDelegation(0, "Code Buddy", "🛠", "do the thing");

        // No card at index 99 -> FindDelegation returns null and the call is a no-op (null-conditional).
        msg.ApplyDelegationActivity(99, new ActivityUpdate(ActivityPhase.Started, 0, "✏️", "Write file", "x", "", false));

        Assert.Single(msg.Delegations);
        Assert.False(msg.Delegations[0].HasActivities);
        Assert.Empty(msg.Delegations[0].Activities);
    }

    // --- FinishDelegation ----------------------------------------------------------------------

    [Fact]
    public void FinishDelegation_RecordsResult_StopsRunning()
    {
        var msg = NewAssistant();
        msg.StartDelegation(0, "Code Buddy", "🛠", "do the thing");

        msg.FinishDelegation(0, "result");

        Assert.Equal("result", msg.Delegations[0].Result);
        Assert.False(msg.Delegations[0].IsRunning);
    }

    [Fact]
    public void FinishDelegation_NullResult_CoalescesToEmpty()
    {
        var msg = NewAssistant();
        msg.StartDelegation(0, "Code Buddy", "🛠", "do the thing");

        msg.FinishDelegation(0, null!);

        Assert.Equal("", msg.Delegations[0].Result);
        Assert.False(msg.Delegations[0].IsRunning);
    }

    [Fact]
    public void FinishDelegation_UnknownIndex_DoesNotThrow()
    {
        var msg = NewAssistant();
        msg.StartDelegation(0, "Code Buddy", "🛠", "do the thing");

        // No card at index 99 -> early return, no exception.
        msg.FinishDelegation(99, "x");

        Assert.True(msg.Delegations[0].IsRunning);
        Assert.Equal("", msg.Delegations[0].Result);
    }

    // --- Index routing across multiple cards ---------------------------------------------------

    [Fact]
    public void FinishDelegation_OnlyAffectsMatchingIndex()
    {
        var msg = NewAssistant();
        msg.StartDelegation(0, "Code Buddy", "🛠", "task A");
        msg.StartDelegation(1, "Researcher", "🔎", "task B");

        msg.FinishDelegation(0, "done A");

        var first = msg.Delegations.Single(d => d.Index == 0);
        var second = msg.Delegations.Single(d => d.Index == 1);

        Assert.False(first.IsRunning);
        Assert.Equal("done A", first.Result);

        // The other card is untouched -> proves updates route by index, not by position/last-added.
        Assert.True(second.IsRunning);
        Assert.Equal("", second.Result);
    }

    // --- DelegationStepViewModel.Header (standalone) -------------------------------------------

    [Fact]
    public void Header_WithGlyph_GluesGlyphAndNameWithTwoSpaces()
    {
        var card = new DelegationStepViewModel { AgentName = "Code Buddy", Glyph = "🛠" };
        Assert.Equal("🛠  Code Buddy", card.Header);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Header_BlankGlyph_IsJustAgentName(string glyph)
    {
        var card = new DelegationStepViewModel { AgentName = "Code Buddy", Glyph = glyph };
        Assert.Equal("Code Buddy", card.Header);
    }
}
