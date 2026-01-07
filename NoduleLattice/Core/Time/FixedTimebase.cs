using NoduleLattice.Abstractions.Time;

namespace NoduleLattice.Core.Time;

public sealed class FixedTimebase : IResettableTimebase
{
    public long StepIndex { get; private set; }
    public float DeltaTime { get; }

    public FixedTimebase(float deltaTime = 1.0f, long initialStepIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaTime);
        DeltaTime = deltaTime;
        StepIndex = initialStepIndex;
    }

    public void Advance() => StepIndex++;

    public void Reset(long stepIndex)
    {
        if (stepIndex < 0) stepIndex = 0;
        StepIndex = stepIndex;
    }
}