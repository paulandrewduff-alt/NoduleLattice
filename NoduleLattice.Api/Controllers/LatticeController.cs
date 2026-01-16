// ============================================================================
// FILE: NoduleLattice.Api/Controllers/LatticeController.cs
// PURPOSE:
//   - All request bodies use NoduleLattice.Api.Dtos (avoid ambiguity)
//   - Snapshot supports thin mode via query params (non-breaking)
// ROUTES:
//   GET  /api/lattice/snapshot?mode=thin&maxEdges=12000&maxLen=7
//   GET  /api/lattice/snapshot             (full)
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using NoduleLattice.Api.Dtos;
using NoduleLattice.Api.Services;

namespace NoduleLattice.Api.Controllers;

[ApiController]
[Route("api/lattice")]
public sealed class LatticeController : ControllerBase
{
    private readonly LatticeHostService _host;

    public LatticeController(LatticeHostService host)
    {
        _host = host;
    }

    [HttpPost("create")]
    public ActionResult<LatticeSnapshotDto> Create([FromBody] CreateLatticeRequest req)
        => Ok(_host.Create(req));

    [HttpPost("step")]
    public IActionResult Step([FromBody] StepRequest req)
    {
        _host.Step(req.Steps);
        return Ok();
    }

    // Non-breaking: default is full snapshot
    // Thin snapshot: mode=thin
    [HttpGet("snapshot")]
    public ActionResult<LatticeSnapshotDto> Snapshot(
        [FromQuery] string? mode = null,
        [FromQuery] int? maxEdges = null,
        [FromQuery] float? maxLen = null)
    {
        bool thin = string.Equals(mode, "thin", StringComparison.OrdinalIgnoreCase);

        if (!thin)
            return Ok(_host.GetSnapshot());

        int edges = maxEdges is null ? 12_000 : Math.Clamp(maxEdges.Value, 100, 250_000);
        float len = maxLen is null ? 7f : Math.Clamp(maxLen.Value, 0.5f, 200f);

        return Ok(_host.GetSnapshotThin(edges, len));
    }

    [HttpPost("inject")]
    public IActionResult Inject([FromBody] InjectRequest req)
    {
        _host.Inject(req);
        return Ok();
    }

    [HttpPost("modulators")]
    public IActionResult Modulators([FromBody] ModulatorsRequest req)
    {
        _host.SetModulators(req);
        return Ok();
    }

    [HttpPost("sleep-replay")]
    public IActionResult SleepReplay([FromBody] SleepReplayRequest req)
    {
        _host.SleepReplay(req.Run);
        return Ok();
    }

    [HttpPost("stimulus")]
    public IActionResult Stimulus([FromBody] StimulusRequest req)
    {
        _host.Stimulus(req);
        return Ok();
    }

    [HttpPost("thalamus")]
    public IActionResult Thalamus([FromBody] ThalamusGatesRequest req)
    {
        _host.SetThalamusGates(req);
        return Ok();
    }

    [HttpGet("archive")]
    public ActionResult<ArchiveDto> GetArchive()
        => Ok(_host.SaveArchive());

    [HttpPost("archive")]
    public IActionResult LoadArchive([FromBody] ArchiveDto dto)
    {
        _host.LoadArchive(dto);
        return Ok();
    }

    [HttpPost("validate")]
    public IActionResult Validate()
        => Ok(_host.Validate());
}
