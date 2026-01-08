using System.Net;
using System.Net.Http.Json;
using NoduleLattice.Blazor.Models;

namespace NoduleLattice.Blazor.Services;

/// <summary>
/// Snapshot-only client to NoduleLattice.Api.
/// Includes simple snapshot endpoint discovery and explicit action calls.
/// </summary>
public sealed class LatticeApiClient
{
    private readonly HttpClient _http;

    public LatticeApiClient(HttpClient http) => _http = http;

    private static readonly string[] SnapshotCandidates =
    [
        "api/lattice/snapshot",
        "api/snapshot",
        "lattice/snapshot",
        "snapshot"
    ];

    public string? LastResolvedSnapshotPath { get; private set; }
    public string? LastError { get; private set; }

    public async Task<LatticeSnapshotDto?> GetSnapshot()
    {
        LastError = null;

        foreach (var path in SnapshotCandidates)
        {
            var result = await TryGetSnapshot(path);
            if (result is not null)
            {
                LastResolvedSnapshotPath = path;
                return result;
            }
        }

        LastResolvedSnapshotPath = null;
        LastError ??= "No known snapshot endpoint responded successfully (404/connection).";
        return null;
    }

    private async Task<LatticeSnapshotDto?> TryGetSnapshot(string path)
    {
        try
        {
            using var resp = await _http.GetAsync(path);

            if (resp.StatusCode == HttpStatusCode.NotFound)
                return null;

            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<LatticeSnapshotDto>();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    public async Task Step(int steps)
        => await _http.PostAsJsonAsync("api/lattice/step", new StepRequest { Steps = steps });

    public async Task Inject(UiInject inject)
        => await _http.PostAsJsonAsync("api/lattice/inject", new InjectRequest { NodeId = inject.NodeId, Exc = inject.Exc, Inh = inject.Inh });

    public async Task SetModulators(UiModulators mods)
        => await _http.PostAsJsonAsync("api/lattice/modulators", new ModulatorsRequest
        {
            Reward = mods.Reward,
            Salience = mods.Salience,
            Stability = mods.Stability,
            Alerting = mods.Alerting,
            Curiosity = mods.Curiosity,
            Goal = mods.Goal
        });

    public async Task SetThalamusGates(ThalamusGatesRequest req)
        => await _http.PostAsJsonAsync("api/lattice/thalamus", req);

    public async Task SleepReplay()
        => await _http.PostAsJsonAsync("api/lattice/sleep-replay", new SleepReplayRequest { Run = true });

    public async Task Stimulus(StimulusRequest req)
        => await _http.PostAsJsonAsync("api/lattice/stimulus", req);

    public async Task<ArchiveDto> GetArchive()
        => (await _http.GetFromJsonAsync<ArchiveDto>("api/lattice/archive")) ?? new ArchiveDto();

    public async Task LoadArchive(ArchiveDto dto)
        => await _http.PostAsJsonAsync("api/lattice/archive", dto);
}