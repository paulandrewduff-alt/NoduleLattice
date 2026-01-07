namespace NoduleLattice.Abstractions.Time;

public interface IResettableTimebase : ITimebase
{
    void Reset(long stepIndex);
}