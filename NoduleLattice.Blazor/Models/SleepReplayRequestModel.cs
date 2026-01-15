// ============================================================================
// FILE: NoduleLattice.Blazor/Models/SleepReplayRequestModel.cs
// PURPOSE:
//   Blazor-side request model for /api/lattice/sleep-replay
// ============================================================================
namespace NoduleLattice.Blazor.Models;

public sealed class SleepReplayRequestModel
{
    public bool Run { get; set; } = false;
}
