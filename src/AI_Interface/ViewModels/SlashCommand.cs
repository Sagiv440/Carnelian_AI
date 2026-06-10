using System;

namespace AI_Interface.ViewModels;

/// <summary>
/// One entry in the composer's slash (<c>/</c>) command palette: a name, a short description, the action to
/// run when chosen, and an availability predicate so the menu is context-aware (e.g. mode switches hide while
/// a project is active, <c>/auto-read</c> hides without a voice). The action maps to an existing VM command.
/// </summary>
public sealed class SlashCommand
{
    /// <summary>Command name WITHOUT the leading slash, e.g. "compact".</summary>
    public required string Name { get; init; }

    /// <summary>One-line description shown beside the name in the menu.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Runs the command (invoked on the UI thread when the user picks it). The palette clears the composer
    /// BEFORE calling this, so a command must not read <c>InputText</c> for arguments.
    /// </summary>
    public required Action Run { get; init; }

    /// <summary>Whether this command currently applies (context-aware). Default: always available.</summary>
    public Func<bool> IsAvailable { get; init; } = () => true;

    /// <summary>Display form with the leading slash, e.g. "/compact".</summary>
    public string Display => "/" + Name;
}
