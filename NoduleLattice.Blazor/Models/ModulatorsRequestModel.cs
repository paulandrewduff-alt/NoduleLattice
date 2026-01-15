// ============================================================================
// FILE: NoduleLattice.Blazor/Models/ModulatorsRequestModel.cs
// PURPOSE:
//   Blazor-side request model for /api/lattice/modulators
//   Uses the canon ModulatorVector field set you’re using in ThalamusBranch.
// ============================================================================
namespace NoduleLattice.Blazor.Models;

public sealed class ModulatorsRequestModel
{
    public float Reward { get; set; } = 0f;
    public float Salience { get; set; } = 0f;
    public float Stability { get; set; } = 0f;
    public float Alerting { get; set; } = 0f;
    public float Curiosity { get; set; } = 0f;
    public float Goal { get; set; } = 0f;
}
