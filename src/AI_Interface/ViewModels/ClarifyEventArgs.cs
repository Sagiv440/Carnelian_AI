using System;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.ViewModels;

/// <summary>
/// Raised by <see cref="MainWindowViewModel"/> when the project agent asks a clarifying question (the
/// <c>ask_user</c> tool). The view shows a popup and reports the user's answer via <see cref="Completion"/>
/// (the chosen option(s) / free text, or null if the user dismissed it).
/// </summary>
public sealed class ClarifyEventArgs : EventArgs
{
    public ClarifyEventArgs(UserClarificationRequest request, TaskCompletionSource<string?> completion)
    {
        Request = request;
        Completion = completion;
    }

    public UserClarificationRequest Request { get; }

    /// <summary>Set to the user's answer string, or null if they dismissed the question.</summary>
    public TaskCompletionSource<string?> Completion { get; }
}
