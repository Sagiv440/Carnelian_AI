using AI_Interface.Models;

namespace AI_Interface.ViewModels;

/// <summary>Pairs an <see cref="AppMode"/> with a friendly label for the mode selector.</summary>
public sealed record ModeOption(AppMode Mode, string Label, string Description)
{
    public override string ToString() => Label;
}
