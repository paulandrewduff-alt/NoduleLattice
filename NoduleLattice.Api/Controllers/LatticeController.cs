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

    [HttpGet("snapshot")]
    public ActionResult<LatticeSnapshotDto> Snapshot(
        [FromQuery] string? mode = null,
        [FromQuery] int maxEdges = 12000,
        [FromQuery] float maxLen = 7f)
    {
        if (string.Equals(mode, "thin", StringComparison.OrdinalIgnoreCase))
            return Ok(_host.GetSnapshotThin(Math.Max(0, maxEdges), Math.Max(0.1f, maxLen)));

        return Ok(_host.GetSnapshot());
    }

    [HttpPost("step")]
    public IActionResult Step([FromBody] StepRequest req)
    {
        _host.Step(Math.Max(1, req.Steps));
        return Ok();
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

    [HttpPost("create")]
    public ActionResult<LatticeSnapshotDto> Create([FromBody] CreateLatticeRequest req)
        => Ok(_host.Create(req));

    [HttpPost("archive/save")]
    public ActionResult<ArchiveDto> SaveArchive()
        => Ok(_host.SaveArchive());

    [HttpPost("archive/load")]
    public IActionResult LoadArchive([FromBody] ArchiveDto dto)
    {
        _host.LoadArchive(dto);
        return Ok();
    }

    [HttpGet("validate")]
    public ActionResult<object> Validate()
        => Ok(_host.Validate());
}
