using NoduleLattice.Api.Dtos;

namespace NoduleLattice.Api.Services;

public sealed class LatticeRunnerService : IAsyncDisposable
{
    private readonly LatticeHostService _host;

    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;

    private bool _running;
    private int _targetHz = 30;
    private int _stepsPerTick = 2;
    private long _ticks;
    private string? _lastError;

    public LatticeRunnerService(LatticeHostService host)
    {
        _host = host;
    }

    public RunStatusDto Start(RunRequest req)
    {
        lock (_gate)
        {
            _targetHz = Math.Clamp(req.TargetHz, 1, 240);
            _stepsPerTick = Math.Clamp(req.StepsPerTick, 1, 4096);

            if (_running)
                return StatusUnsafe();

            _running = true;
            _lastError = null;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            _loop = Task.Run(() => Loop(_cts.Token), _cts.Token);

            return StatusUnsafe();
        }
    }

    public RunStatusDto Stop()
    {
        lock (_gate)
        {
            if (!_running)
                return StatusUnsafe();

            _running = false;

            _cts?.Cancel();
            return StatusUnsafe();
        }
    }

    public RunStatusDto Status()
    {
        lock (_gate) return StatusUnsafe();
    }

    private RunStatusDto StatusUnsafe()
        => new()
        {
            Running = _running,
            TargetHz = _targetHz,
            StepsPerTick = _stepsPerTick,
            Ticks = _ticks,
            LastError = _lastError
        };

    private async Task Loop(CancellationToken ct)
    {
        try
        {
            var period = TimeSpan.FromMilliseconds(1000.0 / _targetHz);
            using var timer = new PeriodicTimer(period);

            while (!ct.IsCancellationRequested)
            {
                var ok = await timer.WaitForNextTickAsync(ct);
                if (!ok) break;

                // Step the host (host locks internally)
                _host.Step(_stepsPerTick);

                lock (_gate) _ticks++;
            }
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _lastError = ex.Message;
                _running = false;
            }
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
            _running = false;
        }

        try { cts?.Cancel(); } catch { }
        try { if (loop is not null) await loop; } catch { }
        try { cts?.Dispose(); } catch { }
    }
}
