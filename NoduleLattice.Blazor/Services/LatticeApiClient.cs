// ============================================================================
// FILE: NoduleLattice.Blazor/Services/LatticeApiClient.cs
// PURPOSE:
//   - Adds Thin Snapshot support (mode=thin)
//   - Keeps existing surface area used by Home.razor
// ============================================================================

using System.Net.Http.Json;
using NoduleLattice.Blazor.Models;

namespace NoduleLattice.Blazor.Services;

public sealed class LatticeApiClient
{
    private readonly HttpClient _http;

    public string? LastError { get; private set; }
    public string LastResolvedSnapshotPath { get; }

    // Thin snapshot controls (UI can bind these)
    public bool UseThinSnapshot { get; set; } = true;
    public int ThinMaxEdges { get; set; } = 12_000;
    public float ThinMaxLen { get; set; } = 7f;

    public LatticeApiClient(HttpClient http)
    {
        _http = http;

        var baseUri = _http.BaseAddress?.ToString() ?? string.Empty;
        if (!baseUri.EndsWith("/")) baseUri += "/";

        // Default "resolved path" points to the thin endpoint we call in GetSnapshot()
        LastResolvedSnapshotPath = baseUri + "api/lattice/snapshot";
    }

    public async Task<LatticeSnapshotDto> GetSnapshot(CancellationToken ct = default)
    {
        try
        {
            LastError = null;

            string path = "api/lattice/snapshot";
            if (UseThinSnapshot)
            {
                int edges = Math.Clamp(ThinMaxEdges, 100, 250_000);
                float len = Math.Clamp(ThinMaxLen, 0.5f, 200f);
                path += $"?mode=thin&maxEdges={edges}&maxLen={len}";
            }

            var snap = await _http.GetFromJsonAsync<LatticeSnapshotDto>(path, ct);
            return snap ?? new LatticeSnapshotDto();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new LatticeSnapshotDto();
        }
    }

    public async Task Step(int steps, CancellationToken ct = default)
    {
        try
        {
            LastError = null;
            using var resp = await _http.PostAsJsonAsync("api/lattice/step", new StepRequest { Steps = steps }, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public Task SetModulators(UiModulators ui, CancellationToken ct = default)
        => SetModulators(new ModulatorsRequestModel
        {
            Reward = ui.Reward,
            Salience = ui.Salience,
            Stability = ui.Stability,
            Alerting = ui.Alerting,
            Curiosity = ui.Curiosity,
            Goal = ui.Goal
        }, ct);

    public Task SetModulators(ModulatorsRequestModel req, CancellationToken ct = default)
        => Post("api/lattice/modulators", req, ct);

    public Task SetThalamusGates(ThalamusGatesRequestModel req, CancellationToken ct = default)
        => Post("api/lattice/thalamus", req, ct);

    public Task Inject(UiInject ui, CancellationToken ct = default)
        => Post("api/lattice/inject", new InjectRequest { NodeId = ui.NodeId, Exc = ui.Exc, Inh = ui.Inh }, ct);

    public Task Stimulus(StimulusRequestModel req, CancellationToken ct = default)
        => Post("api/lattice/stimulus", req, ct);

    public Task SleepReplay(CancellationToken ct = default)
        => SleepReplay(true, ct);

    public Task SleepReplay(bool run, CancellationToken ct = default)
        => Post("api/lattice/sleep-replay", new SleepReplayRequestModel { Run = run }, ct);

    // ---------------- Runner ----------------

    public async Task<RunStatusDto> RunStart(RunRequestModel req, CancellationToken ct = default)
    {
        try
        {
            LastError = null;

            using var resp = await _http.PostAsJsonAsync("api/lattice/run/start", req, ct);
            resp.EnsureSuccessStatusCode();

            return (await resp.Content.ReadFromJsonAsync<RunStatusDto>(cancellationToken: ct))
                   ?? new RunStatusDto();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new RunStatusDto { Running = false, LastError = ex.Message };
        }
    }

    public async Task<RunStatusDto> RunStop(CancellationToken ct = default)
    {
        try
        {
            LastError = null;

            using var resp = await _http.PostAsync("api/lattice/run/stop", content: null, ct);
            resp.EnsureSuccessStatusCode();

            return (await resp.Content.ReadFromJsonAsync<RunStatusDto>(cancellationToken: ct))
                   ?? new RunStatusDto();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new RunStatusDto { Running = false, LastError = ex.Message };
        }
    }

    public async Task<RunStatusDto> RunStatus(CancellationToken ct = default)
    {
        try
        {
            LastError = null;

            var dto = await _http.GetFromJsonAsync<RunStatusDto>("api/lattice/run/status", ct);
            return dto ?? new RunStatusDto();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new RunStatusDto { Running = false, LastError = ex.Message };
        }
    }

    private async Task Post<T>(string path, T body, CancellationToken ct)
    {
        try
        {
            LastError = null;

            using var resp = await _http.PostAsJsonAsync(path, body, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }
}
