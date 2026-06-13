using System;
using System.Threading.Tasks;

namespace AI_Interface.ViewModels;

/// <summary>
/// Raised by <see cref="MainWindowViewModel"/> when the user picks "Delete" on a file in the project file
/// tree. Deleting is destructive, so the view confirms with the user and reports the decision via
/// <see cref="Completion"/> (true = delete the file, false = cancel).
/// </summary>
public sealed class DeleteFileEventArgs : EventArgs
{
    public DeleteFileEventArgs(string fileName, TaskCompletionSource<bool> completion)
    {
        FileName = fileName;
        Completion = completion;
    }

    /// <summary>The file's display name, shown in the confirmation prompt.</summary>
    public string FileName { get; }

    /// <summary>Set to true to delete the file, false to cancel.</summary>
    public TaskCompletionSource<bool> Completion { get; }
}
