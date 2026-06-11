using System;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.ViewModels;

/// <summary>
/// Raised by <see cref="MainWindowViewModel"/> when the project agent reaches a phase boundary and
/// <c>AutoFlowPhases</c> is off. The view confirms with the user and reports the decision via
/// <see cref="Completion"/> (true = continue to the next phase, false = stop the run).
/// </summary>
public sealed class PhaseGateEventArgs : EventArgs
{
    public PhaseGateEventArgs(PhaseGate gate, TaskCompletionSource<bool> completion)
    {
        Gate = gate;
        Completion = completion;
    }

    public PhaseGate Gate { get; }

    /// <summary>Set to true to continue to the next phase, false to stop the run.</summary>
    public TaskCompletionSource<bool> Completion { get; }
}
