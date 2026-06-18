using System;
using System.Threading.Tasks;

namespace AI_Interface.ViewModels;

/// <summary>
/// Raised by <see cref="MainWindowViewModel"/> for the <c>/saveToDoc</c> action: asks the view to open a
/// native Save dialog (defaulting to the Documents folder) so the user picks where the reply is written.
/// The chosen full path is returned via <see cref="Completion"/> (null when cancelled).
/// </summary>
public sealed class SaveDocPathRequest : EventArgs
{
    public SaveDocPathRequest(string suggestedName, string format, TaskCompletionSource<string?> completion)
    {
        SuggestedName = suggestedName;
        Format = format;
        Completion = completion;
    }

    /// <summary>Suggested base file name (with extension), e.g. "reply-2026-06-18.pdf".</summary>
    public string SuggestedName { get; }

    /// <summary>Chosen format, "pdf" or "docx" — drives the file-type filter.</summary>
    public string Format { get; }

    /// <summary>The chosen full path, or null when the user cancels the dialog.</summary>
    public TaskCompletionSource<string?> Completion { get; }
}
