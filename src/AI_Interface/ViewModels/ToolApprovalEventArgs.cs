using System;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.ViewModels;

/// <summary>
/// Raised by <see cref="MainWindowViewModel"/> when the project agent needs the user to approve a
/// tool call. The view shows a dialog and reports the decision via <see cref="Completion"/>.
/// </summary>
public sealed class ToolApprovalEventArgs : EventArgs
{
    public ToolApprovalEventArgs(ToolApprovalRequest request, TaskCompletionSource<bool> completion)
    {
        Request = request;
        Completion = completion;
    }

    public ToolApprovalRequest Request { get; }

    /// <summary>Set to true to approve the action, false to decline it.</summary>
    public TaskCompletionSource<bool> Completion { get; }
}
