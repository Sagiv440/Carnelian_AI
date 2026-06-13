using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AI_Interface.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Interface.ViewModels;

/// <summary>One selectable option (checkbox) in a clarify question.</summary>
public sealed partial class ClarifyOptionViewModel : ObservableObject
{
    public string Text { get; init; } = "";

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// One question in the clarify popup (its own tab when there's more than one): the question text, its options
/// as checkboxes, and an "Other" free-text choice. Raises <see cref="IsAnswered"/>/<see cref="Header"/> as the
/// user picks so the parent can gate Send and mark the tab.
/// </summary>
public sealed partial class ClarifyQuestionViewModel : ObservableObject
{
    public ClarifyQuestionViewModel(int index, ClarificationQuestion question)
    {
        Index = index;
        Question = string.IsNullOrWhiteSpace(question.Question) ? "Which option do you prefer?" : question.Question;
        foreach (var o in question.Options)
        {
            var item = new ClarifyOptionViewModel { Text = o };
            item.PropertyChanged += (_, _) => RaiseAnswered();
            Options.Add(item);
        }
    }

    public int Index { get; }
    public string Question { get; }
    public ObservableCollection<ClarifyOptionViewModel> Options { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnswered))]
    [NotifyPropertyChangedFor(nameof(Header))]
    private bool _otherSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnswered))]
    [NotifyPropertyChangedFor(nameof(Header))]
    private string _otherText = "";

    /// <summary>This question has an answer (a ticked option, or "Other" with non-blank text).</summary>
    public bool IsAnswered =>
        Options.Any(o => o.IsSelected) || (OtherSelected && !string.IsNullOrWhiteSpace(OtherText));

    /// <summary>Tab header: a number, with a ✓ once answered.</summary>
    public string Header => (IsAnswered ? "✓ " : "") + $"Question {Index + 1}";

    /// <summary>This question's answer (selected options + any "Other" text), or null when unanswered.</summary>
    public string? BuildAnswer()
    {
        var parts = Options.Where(o => o.IsSelected).Select(o => o.Text).ToList();
        if (OtherSelected && !string.IsNullOrWhiteSpace(OtherText))
            parts.Add(OtherText.Trim());
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private void RaiseAnswered()
    {
        OnPropertyChanged(nameof(IsAnswered));
        OnPropertyChanged(nameof(Header));
    }
}

/// <summary>
/// Backs the clarify popup (<see cref="Views.ClarifyWindow"/>): one or more <see cref="Questions"/>, each a tab.
/// The window returns the combined answer via ShowDialog (a single question → just its answer; multiple → each
/// question labelled with its answer), or null if the user dismissed it. Send is gated until every question is
/// answered (<see cref="CanSubmit"/>).
/// </summary>
public sealed partial class ClarifyViewModel : ViewModelBase
{
    public ClarifyViewModel(UserClarificationRequest request)
    {
        var i = 0;
        foreach (var q in request.Questions)
        {
            var item = new ClarifyQuestionViewModel(i++, q);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ClarifyQuestionViewModel.IsAnswered))
                    OnPropertyChanged(nameof(CanSubmit));
            };
            Questions.Add(item);
        }
    }

    /// <summary>Design-time constructor (XAML previewer) — two sample tabs.</summary>
    public ClarifyViewModel()
        : this(new UserClarificationRequest(new[]
        {
            new ClarificationQuestion("Which UI framework?", new[] { "Avalonia", "WinForms", "WPF" }),
            new ClarificationQuestion("Target platform?", new[] { "Windows only", "Cross-platform" })
        }))
    {
    }

    public ObservableCollection<ClarifyQuestionViewModel> Questions { get; } = new();

    /// <summary>True when there's more than one question (the popup shows the tab strip).</summary>
    public bool HasMultipleQuestions => Questions.Count > 1;

    /// <summary>OK is enabled once every question has been answered.</summary>
    public bool CanSubmit => Questions.Count > 0 && Questions.All(q => q.IsAnswered);

    /// <summary>The combined answer to return to the agent, or null when nothing usable was chosen.</summary>
    public string? BuildAnswer()
    {
        var answered = Questions.Where(q => q.IsAnswered).ToList();
        if (answered.Count == 0)
            return null;
        if (Questions.Count == 1)
            return answered[0].BuildAnswer(); // single question → just the answer
        return string.Join("\n\n", answered.Select(q => $"{q.Question}\n→ {q.BuildAnswer()}"));
    }
}
