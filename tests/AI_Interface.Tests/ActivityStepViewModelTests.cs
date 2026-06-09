using System.Collections.Generic;
using System.ComponentModel;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the computed properties on <see cref="ActivityStepViewModel"/> — the row view model behind
/// the single-agent project "activity feed". It is a plain <c>ObservableObject</c> with no I/O, so the
/// status glyph / result / done / caret derivations and their change notifications are directly testable.
/// </summary>
public sealed class ActivityStepViewModelTests
{
    // ---- StatusGlyph: ✗ on failure, ⏳ while running, ✓ otherwise ------------------------------

    [Fact]
    public void StatusGlyph_Failed_ReturnsCross()
    {
        var step = new ActivityStepViewModel { IsRunning = false, Failed = true };
        Assert.Equal("✗", step.StatusGlyph);
    }

    [Fact]
    public void StatusGlyph_Running_ReturnsHourglass()
    {
        var step = new ActivityStepViewModel { IsRunning = true, Failed = false };
        Assert.Equal("⏳", step.StatusGlyph);
    }

    [Fact]
    public void StatusGlyph_Done_ReturnsCheck()
    {
        var step = new ActivityStepViewModel { IsRunning = false, Failed = false };
        Assert.Equal("✓", step.StatusGlyph);
    }

    [Fact]
    public void StatusGlyph_FailedAndRunning_FailureWins()
    {
        // The expression is `Failed ? "✗" : IsRunning ? "⏳" : "✓"`, so Failed takes precedence over IsRunning.
        var step = new ActivityStepViewModel { IsRunning = true, Failed = true };
        Assert.Equal("✗", step.StatusGlyph);
    }

    // ---- HasResult: true iff Result has non-whitespace content ---------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void HasResult_EmptyOrWhitespace_IsFalse(string result)
    {
        var step = new ActivityStepViewModel { Result = result };
        Assert.False(step.HasResult);
    }

    [Theory]
    [InlineData("Wrote 42 chars")]
    [InlineData("   x   ")]
    public void HasResult_NonWhitespace_IsTrue(string result)
    {
        var step = new ActivityStepViewModel { Result = result };
        Assert.True(step.HasResult);
    }

    // ---- Done => !IsRunning && !Failed ---------------------------------------------------------

    [Theory]
    [InlineData(true, false, false)]  // running -> not done
    [InlineData(false, true, false)]  // failed -> not done
    [InlineData(true, true, false)]   // running + failed -> not done
    [InlineData(false, false, true)]  // finished & succeeded -> done
    public void Done_ReflectsRunningAndFailed(bool isRunning, bool failed, bool expected)
    {
        var step = new ActivityStepViewModel { IsRunning = isRunning, Failed = failed };
        Assert.Equal(expected, step.Done);
    }

    // ---- ShowCollapsedCaret => HasResult && !IsExpanded ----------------------------------------

    [Fact]
    public void ShowCollapsedCaret_HasResultAndCollapsed_IsTrue()
    {
        var step = new ActivityStepViewModel { Result = "body", IsExpanded = false };
        Assert.True(step.ShowCollapsedCaret);
    }

    [Fact]
    public void ShowCollapsedCaret_HasResultButExpanded_IsFalse()
    {
        var step = new ActivityStepViewModel { Result = "body", IsExpanded = true };
        Assert.False(step.ShowCollapsedCaret);
    }

    [Fact]
    public void ShowCollapsedCaret_NoResult_IsFalse()
    {
        // Resultless rows hide the caret so they don't look clickable.
        var step = new ActivityStepViewModel { Result = "", IsExpanded = false };
        Assert.False(step.ShowCollapsedCaret);
    }

    // ---- PropertyChanged for the dependent computed properties ---------------------------------

    [Fact]
    public void SettingFailed_RaisesStatusGlyphAndDone()
    {
        var step = new ActivityStepViewModel { IsRunning = false };
        var raised = CaptureChanges(step);

        step.Failed = true;

        Assert.Contains(nameof(ActivityStepViewModel.StatusGlyph), raised);
        Assert.Contains(nameof(ActivityStepViewModel.Done), raised);
    }

    [Fact]
    public void SettingIsRunning_RaisesStatusGlyphAndDone()
    {
        var step = new ActivityStepViewModel { IsRunning = true };
        var raised = CaptureChanges(step);

        step.IsRunning = false;

        Assert.Contains(nameof(ActivityStepViewModel.StatusGlyph), raised);
        Assert.Contains(nameof(ActivityStepViewModel.Done), raised);
    }

    [Fact]
    public void SettingResult_RaisesHasResultAndShowCollapsedCaret()
    {
        var step = new ActivityStepViewModel();
        var raised = CaptureChanges(step);

        step.Result = "done";

        Assert.Contains(nameof(ActivityStepViewModel.HasResult), raised);
        Assert.Contains(nameof(ActivityStepViewModel.ShowCollapsedCaret), raised);
    }

    [Fact]
    public void SettingIsExpanded_RaisesShowCollapsedCaret()
    {
        var step = new ActivityStepViewModel { Result = "body" };
        var raised = CaptureChanges(step);

        step.IsExpanded = true;

        Assert.Contains(nameof(ActivityStepViewModel.ShowCollapsedCaret), raised);
    }

    /// <summary>Records every property name reported via <see cref="INotifyPropertyChanged"/>.</summary>
    private static List<string> CaptureChanges(ActivityStepViewModel step)
    {
        var raised = new List<string>();
        step.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } name)
                raised.Add(name);
        };
        return raised;
    }
}
