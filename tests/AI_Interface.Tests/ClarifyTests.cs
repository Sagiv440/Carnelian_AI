using System.Text.Json;
using AI_Interface.Models;
using AI_Interface.Services;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the ask_user clarification flow's pure logic: <see cref="ProjectAgentService.ParseStringArray"/>
/// (value-kind-guarded parse of the tool's <c>options</c>) and <see cref="ClarifyViewModel"/>'s submit gate +
/// combined-answer building (selected checkboxes + the "Other" free-text).
/// </summary>
public sealed class ClarifyTests
{
    // ---- ParseStringArray ----------------------------------------------------------------------

    [Fact]
    public void ParseStringArray_WellFormed_ReturnsTrimmedNonBlankInOrder()
    {
        var args = JsonSerializer.SerializeToElement(new { options = new[] { " A ", "B", "", "  ", "C" } });

        var result = ProjectAgentService.ParseStringArray(args, "options");

        Assert.Equal(new[] { "A", "B", "C" }, result);
    }

    [Fact]
    public void ParseStringArray_MissingOrNonArray_ReturnsEmpty()
    {
        Assert.Empty(ProjectAgentService.ParseStringArray(
            JsonSerializer.SerializeToElement(new { question = "hi" }), "options"));
        Assert.Empty(ProjectAgentService.ParseStringArray(
            JsonSerializer.SerializeToElement(new { options = "nope" }), "options"));
        Assert.Empty(ProjectAgentService.ParseStringArray(default, "options"));
    }

    [Fact]
    public void ParseStringArray_NonStringElements_AreStringified()
    {
        using var doc = JsonDocument.Parse("""{ "options": [1, true, "x"] }""");
        Assert.Equal(new[] { "1", "True", "x" }, ProjectAgentService.ParseStringArray(doc.RootElement, "options"));
    }

    // ---- AskUserAsync (the tool body) ----------------------------------------------------------

    private static JsonElement Args(string question, params string[] options) =>
        JsonSerializer.SerializeToElement(new { question, options });

    [Fact]
    public async System.Threading.Tasks.Task AskUserAsync_BlankQuestion_ReturnsGuidance()
    {
        var result = await ProjectAgentService.AskUserAsync(
            JsonSerializer.SerializeToElement(new { }), _ => System.Threading.Tasks.Task.FromResult<string?>("x"),
            System.Threading.CancellationToken.None);
        Assert.Contains("question", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task AskUserAsync_NoCallback_TellsModelToProceed()
    {
        var result = await ProjectAgentService.AskUserAsync(
            Args("Which?", "A", "B"), askUser: null, System.Threading.CancellationToken.None);
        Assert.Contains("best judgement", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task AskUserAsync_RealAnswer_ReturnedVerbatim()
    {
        var result = await ProjectAgentService.AskUserAsync(
            Args("Which?", "A", "B"), _ => System.Threading.Tasks.Task.FromResult<string?>("A; B"),
            System.Threading.CancellationToken.None);
        Assert.Equal("A; B", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task AskUserAsync_DismissedOrBlank_TellsModelToProceed()
    {
        var result = await ProjectAgentService.AskUserAsync(
            Args("Which?", "A"), _ => System.Threading.Tasks.Task.FromResult<string?>("   "),
            System.Threading.CancellationToken.None);
        Assert.Contains("best judgement", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task AskUserAsync_AlreadyCancelled_ThrowsBeforeAsking()
    {
        var called = false;
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() =>
            ProjectAgentService.AskUserAsync(Args("Which?", "A"),
                _ => { called = true; return System.Threading.Tasks.Task.FromResult<string?>("A"); }, cts.Token));
        Assert.False(called);
    }

    // ---- CapAskUser (per-run popup cap) --------------------------------------------------------

    [Fact]
    public async System.Threading.Tasks.Task CapAskUser_StopsAskingAfterTheCap()
    {
        var calls = 0;
        var capped = ProjectAgentService.CapAskUser(_ =>
        {
            calls++;
            return System.Threading.Tasks.Task.FromResult<string?>("answer");
        })!;

        var req = new UserClarificationRequest("Q", new[] { "A" });
        for (var i = 0; i < ProjectAgentService.MaxUserQuestions; i++)
            Assert.Equal("answer", await capped(req));

        // Past the cap: the real callback isn't invoked and the model is told to proceed.
        var beyond = await capped(req);
        Assert.Contains("best judgement", beyond);
        Assert.Equal(ProjectAgentService.MaxUserQuestions, calls);
    }

    [Fact]
    public void CapAskUser_Null_StaysNull() => Assert.Null(ProjectAgentService.CapAskUser(null));

    // ---- ParseClarificationQuestions -----------------------------------------------------------

    [Fact]
    public void ParseClarificationQuestions_SingleQuestionFallback()
    {
        var qs = ProjectAgentService.ParseClarificationQuestions(Args("Which framework?", "A", "B"));

        var q = Assert.Single(qs);
        Assert.Equal("Which framework?", q.Question);
        Assert.Equal(new[] { "A", "B" }, q.Options);
    }

    [Fact]
    public void ParseClarificationQuestions_MultipleQuestions_SkipsBlankQuestionItems()
    {
        using var doc = JsonDocument.Parse(
            """{ "questions": [ { "question": "Lang?", "options": ["C#","Go"] }, { "options": ["x"] }, { "question": "DB?" } ] }""");

        var qs = ProjectAgentService.ParseClarificationQuestions(doc.RootElement);

        Assert.Collection(qs,
            q => { Assert.Equal("Lang?", q.Question); Assert.Equal(new[] { "C#", "Go" }, q.Options); },
            q => { Assert.Equal("DB?", q.Question); Assert.Empty(q.Options); });
    }

    [Fact]
    public void ParseClarificationQuestions_NeitherForm_ReturnsEmpty() =>
        Assert.Empty(ProjectAgentService.ParseClarificationQuestions(JsonSerializer.SerializeToElement(new { })));

    // ---- ClarifyViewModel: single question -----------------------------------------------------

    private static ClarifyViewModel Vm(params string[] options) =>
        new(new UserClarificationRequest("Pick one", options));

    [Fact]
    public void CanSubmit_FalseUntilAnOptionOrOtherTextIsChosen()
    {
        var vm = Vm("A", "B");
        Assert.False(vm.CanSubmit);

        vm.Questions[0].Options[0].IsSelected = true;
        Assert.True(vm.CanSubmit);
    }

    [Fact]
    public void CanSubmit_OtherRequiresNonBlankText()
    {
        var vm = Vm("A", "B");

        vm.Questions[0].OtherSelected = true;
        Assert.False(vm.CanSubmit);          // ticked but no text yet

        vm.Questions[0].OtherText = "my own answer";
        Assert.True(vm.CanSubmit);
    }

    [Fact]
    public void BuildAnswer_SingleQuestion_JoinsSelectedOptionsAndOther()
    {
        var vm = Vm("A", "B", "C");
        vm.Questions[0].Options[0].IsSelected = true;
        vm.Questions[0].Options[2].IsSelected = true;
        Assert.Equal("A; C", vm.BuildAnswer());

        vm.Questions[0].OtherSelected = true;
        vm.Questions[0].OtherText = "  custom  ";
        Assert.Equal("A; C; custom", vm.BuildAnswer());
    }

    [Fact]
    public void BuildAnswer_NothingChosen_ReturnsNull()
    {
        var vm = Vm("A", "B");
        Assert.Null(vm.BuildAnswer());
    }

    // ---- ClarifyViewModel: multiple questions (tabs) -------------------------------------------

    private static ClarifyViewModel MultiVm() =>
        new(new UserClarificationRequest(new[]
        {
            new ClarificationQuestion("Lang?", new[] { "C#", "Go" }),
            new ClarificationQuestion("DB?", new[] { "Postgres", "SQLite" })
        }));

    [Fact]
    public void MultiQuestion_RequiresEveryQuestionAnswered()
    {
        var vm = MultiVm();
        Assert.True(vm.HasMultipleQuestions);
        Assert.False(vm.CanSubmit);

        vm.Questions[0].Options[0].IsSelected = true;   // only the first answered
        Assert.False(vm.CanSubmit);

        vm.Questions[1].Options[1].IsSelected = true;   // now both
        Assert.True(vm.CanSubmit);
    }

    [Fact]
    public void MultiQuestion_BuildAnswer_LabelsEachQuestion()
    {
        var vm = MultiVm();
        vm.Questions[0].Options[0].IsSelected = true;   // C#
        vm.Questions[1].Options[0].IsSelected = true;   // Postgres

        Assert.Equal("Lang?\n→ C#\n\nDB?\n→ Postgres", vm.BuildAnswer());
    }

    [Fact]
    public void MultiQuestion_TabHeader_MarksAnswered()
    {
        var vm = MultiVm();
        Assert.Equal("Question 1", vm.Questions[0].Header);

        vm.Questions[0].Options[0].IsSelected = true;
        Assert.Equal("✓ Question 1", vm.Questions[0].Header);
    }
}
