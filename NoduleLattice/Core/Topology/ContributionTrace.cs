namespace NoduleLattice.Core.Topology;

/// <summary>
/// Rolling correlation traces for a probationary synapse.
/// Uses EMA of:
/// - x = synapse output (signed)
/// - y = post activity (rate)
/// - xy for correlation proxy
/// </summary>
public sealed class ContributionTrace
{
    public float Ex;   // E[x]
    public float Ey;   // E[y]
    public float Exy;  // E[x*y]

    public void Update(float x, float y, float decay)
    {
        Ex = Ex * decay + x * (1f - decay);
        Ey = Ey * decay + y * (1f - decay);
        Exy = Exy * decay + (x * y) * (1f - decay);
    }

    /// <summary>
    /// Correlation proxy in [-1, +1] (not a Pearson coefficient, but a stable signed measure).
    /// </summary>
    public float CorrProxy()
    {
        float c = Exy - Ex * Ey;

        // Soft normalisation (avoid division instability)
        float denom = 0.10f + System.MathF.Abs(Exy) + System.MathF.Abs(Ex) + System.MathF.Abs(Ey);
        return Clamp(c / denom, -1f, 1f);
    }

    private static float Clamp(float x, float lo, float hi) => x < lo ? lo : (x > hi ? hi : x);
}