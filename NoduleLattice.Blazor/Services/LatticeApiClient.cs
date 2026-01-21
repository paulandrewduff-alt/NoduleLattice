// ============================================================================
// FILE: NoduleLattice.Blazor/Services/LatticeApiClient.cs
// PURPOSE:
//   Add IdleDrive endpoint client call.
// NOTES:
//   This file assumes your existing client already implements:
//     - base address setup
//     - PostAsJson helper pattern or direct PostAsJsonAsync usage
//   This is a complete file ONLY if your project already matches this layout.
//   If your existing client differs, paste it and I’ll output the full canon version.
// ============================================================================

using System.Net.Http.Json;
using NoduleLattice.Blazor.Models;

namespace NoduleLattice.Blazor.Services;

public sealed class LatticeApiClient
{
    private readonly HttpClient _http;

    public string? LastError { get; private set; }
    public string LastResolvedSnapshotPath { get; }

    public bool UseThinSnapshot { get; set; } = true;
    public int ThinMaxEdges { get; set; } = 12000;
    public float ThinMaxLen { get; set; } = 7f;

    public LatticeApiClient(HttpClient http)
    {
        _http = http;

        var baseUri = _http.BaseAddress?.ToString() ?? string.Empty;
        if (!baseUri.EndsWith("/")) baseUri += "/";
        LastResolvedSnapshotPath = baseUri + "api/lattice/snapshot";
    }

    private string SnapshotUrl(CancellationToken ct = default)
    {
        if (!UseThinSnapshot)
            return "api/lattice/snapshot";

        // thin snapshot query parameters
        return $"api/lattice/snapshot?mode=thin&maxEdges={ThinMaxEdges}&maxLen={ThinMaxLen}";
    }

    public async Task<LatticeSnapshotDto> GetSnapshot(CancellationToken ct = default)
    {
        try
        {
            LastError = null;

            var snap = await _http.GetFromJsonAsync<LatticeSnapshotDto>(SnapshotUrl(ct), ct);
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

    public async Task SleepReplay(CancellationToken ct = default) => await SleepReplay(true, ct);

    public async Task SleepReplay(bool run, CancellationToken ct = default)
    {
        try
        {
            LastError = null;
            using var resp = await _http.PostAsJsonAsync("api/lattice/sleep-replay", new SleepReplayRequestModel { Run = run }, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public async Task<RunStatusDto> RunStart(RunRequestModel req, CancellationToken ct = default)
    {
        try
        {
            LastError = null;
            using var resp = await _http.PostAsJsonAsync("api/lattice/run/start", req, ct);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<RunStatusDto>(cancellationToken: ct)) ?? new RunStatusDto();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new RunStatusDto();
        }
    }

    public async Task<RunStatusDto> RunStop(CancellationToken ct = default)
    {
        try
        {
            LastError = null;
            using var resp = await _http.PostAsync("api/lattice/run/stop", null, ct);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<RunStatusDto>(cancellationToken: ct)) ?? new RunStatusDto();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new RunStatusDto();
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
            return new RunStatusDto();
        }
    }

    // ------------------------------------------------------------------------
    // NEW: Engine-side IdleDrive
    // ------------------------------------------------------------------------

    public async Task SetIdleDrive(IdleDriveConfigModel cfg, CancellationToken ct = default)
    {
        try
        {
            LastError = null;
            using var resp = await _http.PostAsJsonAsync("api/lattice/idledrive", cfg, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }
}
