namespace NoduleLattice.Abstractions.Time;

public interface ITimebase
{
    long StepIndex { get; }
    float DeltaTime { get; }
    void Advance();
}
