using Microsoft.AspNetCore.Mvc;
using NoduleLattice.Api.Dtos;


//using NoduleLattice.Api.Models;
using NoduleLattice.Api.Services;

namespace NoduleLattice.Api.Controllers;

[ApiController]
[Route("api/lattice")]
public sealed class LatticeController : ControllerBase
{
    private readonly LatticeHostService _host;
    private readonly LatticeRunnerService _runner;

    public LatticeController(LatticeHostService host, LatticeRunnerService runner)
    {
        _host = host;
        _runner = runner;
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

    [HttpGet("snapshot")]
    public ActionResult<LatticeSnapshotDto> Snapshot()
        => Ok(_host.GetSnapshot());

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

    [HttpPost("sleep-replay")]
    public IActionResult SleepReplay([FromBody] SleepReplayRequest req)
    {
        _host.SleepReplay(req.Run);
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

    // ---------------- Runner ----------------

    [HttpPost("run/start")]
    public ActionResult<RunStatusDto> RunStart([FromBody] RunRequest req)
    {
        _runner.Start(req.TargetHz, req.StepsPerTick);
        return Ok(_runner.Status());
    }

    [HttpPost("run/stop")]
    public ActionResult<RunStatusDto> RunStop()
    {
        _runner.Stop();
        return Ok(_runner.Status());
    }

    [HttpGet("run/status")]
    public ActionResult<RunStatusDto> RunStatus()
        => Ok(_runner.Status());
}
