namespace AI_Interface.Models;

// Structured progress events emitted by the single-agent project loop for each tool call it runs
// (plus lightweight "note" lines for the model's interim narration). The UI groups Started/Finished
// by Index into one row per tool call; mirrors DelegationUpdate (the orchestrator's delegation cards).

/// <summary>The lifecycle stage of one entry in the single-agent activity feed.</summary>
public enum ActivityPhase
{
    /// <summary>A tool call is about to run; <see cref="ActivityUpdate.Icon"/>/<see cref="ActivityUpdate.Title"/>/<see cref="ActivityUpdate.Detail"/> describe it.</summary>
    Started,

    /// <summary>The tool finished (or failed); <see cref="ActivityUpdate.Text"/> is the result and <see cref="ActivityUpdate.Failed"/> flags failure.</summary>
    Finished,

    /// <summary>A line of the model's interim narration (not a tool call); <see cref="ActivityUpdate.Text"/> carries it.</summary>
    Note
}

/// <summary>
/// One structured step in the single-agent project activity feed (mirrors <see cref="DelegationUpdate"/>).
/// <paramref name="Index"/> correlates the <see cref="ActivityPhase.Started"/> and
/// <see cref="ActivityPhase.Finished"/> of one tool call; <paramref name="Icon"/>/<paramref name="Title"/>/
/// <paramref name="Detail"/> are set on Started; <paramref name="Text"/> is the result (Finished) or the
/// narration (Note); <paramref name="Failed"/> applies to Finished only.
/// </summary>
public record ActivityUpdate(
    ActivityPhase Phase,
    int Index,        // correlates Started/Finished of one tool call
    string Icon,      // tool glyph (Started); "" otherwise
    string Title,     // e.g. "Write file" (Started); "" otherwise
    string Detail,    // target/command e.g. "src/App.jsx" (Started); "" otherwise
    string Text,      // result text (Finished) or narration (Note)
    bool Failed);     // Finished only
