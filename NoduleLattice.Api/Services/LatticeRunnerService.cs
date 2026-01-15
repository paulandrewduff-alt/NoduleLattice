using System.Diagnostics;
using NoduleLattice.Api.Dtos;

namespace NoduleLattice.Api.Services;

public sealed class LatticeRunnerService : BackgroundService
{
    private readonly LatticeHostService _host;

    private readonly object _gate = new();

    private bool _running;
    private int _targetHz = 30;
    private int _stepsPerTick = 2;

    private long _ticks;
    private string? _lastError;

    public LatticeRunnerService(LatticeHostService host)
    {
        _host = host;
    }

    public void Start(int targetHz, int stepsPerTick)
    {
        lock (_gate)
        {
            _targetHz = Math.Clamp(targetHz, 1, 240);
            _stepsPerTick = Math.Clamp(stepsPerTick, 1, 4096);
            _running = true;
            _lastError = null;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
        }
    }

    public RunStatusDto Status()
    {
        lock (_gate)
        {
            return new RunStatusDto
            {
                Running = _running,
                TargetHz = _targetHz,
                StepsPerTick = _stepsPerTick,
                Ticks = _ticks,
                LastError = _lastError
            };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sw = new Stopwatch();

        while (!stoppingToken.IsCancellationRequested)
        {
            bool run;
            int hz;
            int steps;

            lock (_gate)
            {
                run = _running;
                hz = _targetHz;
                steps = _stepsPerTick;
            }

            if (!run)
            {
                await Task.Delay(100, stoppingToken);
                continue;
            }

            sw.Restart();

            try
            {
                // Host handles internal locking and uses parallel engine inside.
                _host.Step(steps);

                lock (_gate) { _ticks++; }
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _running = false;
                    _lastError = ex.Message;
                }
            }

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var targetFrameMs = 1000.0 / Math.Max(1, hz);
            var delayMs = (int)Math.Max(0, targetFrameMs - elapsedMs);

            if (delayMs > 0)
                await Task.Delay(delayMs, stoppingToken);
            else
                await Task.Yield();
        }
    }
}
