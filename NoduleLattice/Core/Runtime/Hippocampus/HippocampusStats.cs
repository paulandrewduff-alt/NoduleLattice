// ============================================================================
// FILE: NoduleLattice/Core/Runtime/Hippocampus/HippocampusStats.cs
// PURPOSE:
//   Lightweight stats for UI/debugging.
// ============================================================================

namespace NoduleLattice.Core.Runtime.Hippocampus;

public readonly record struct HippocampusStats(
    int Episodes,
    long OldestStep,
    long NewestStep,
    long LastStoredStep,
    float LastStoredScore,
    float RecentCaptureRatePerKSteps
);
