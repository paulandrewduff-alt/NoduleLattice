namespace NoduleLattice.Blazor.Models;

public sealed class IdleDriveConfigModel
{
    public bool Enabled { get; set; } = true;

    public float BaselineExc { get; set; } = 0.004f;
    public float NoiseSigma { get; set; } = 0.010f;

    public float OscAmplitude { get; set; } = 0.006f;
    public float OscHz { get; set; } = 1.7f;

    public float BurstProbability { get; set; } = 0.015f;
    public float BurstAmplitude { get; set; } = 0.060f;
    public float BurstRadius { get; set; } = 3.5f;

    public float CortexBoost { get; set; } = 1.0f;
    public float ThalamusBoost { get; set; } = 1.25f;
    public float NucleiBoost { get; set; } = 1.35f;
}
