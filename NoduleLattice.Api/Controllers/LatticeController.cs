using Microsoft.AspNetCore.Mvc;
using NoduleLattice.Api.Models;
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
    public ActionResult<LatticeSnapshotDto> GetSnapshot()
        => Ok(_host.GetSnapshot());

    [HttpPost("step")]
    public IActionResult Step([FromBody] StepRequest req)
    {
        _host.Step(req.Steps);
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

    [HttpGet("archive")]
    public ActionResult<ArchiveDto> GetArchive()
        => Ok(_host.SaveArchive());

    [HttpPost("archive")]
    public IActionResult LoadArchive([FromBody] ArchiveDto dto)
    {
        _host.LoadArchive(dto);
        return Ok();
    }
}
