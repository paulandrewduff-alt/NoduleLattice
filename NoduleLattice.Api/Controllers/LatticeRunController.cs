// ============================================================================
// FILE: NoduleLattice.Api/Controllers/LatticeRunController.cs
// PURPOSE:
//   Runner endpoints used by the Blazor UI.
// ROUTES:
//   POST /api/lattice/run/start
//   POST /api/lattice/run/stop
//   GET  /api/lattice/run/status
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using NoduleLattice.Api.Dtos;
using NoduleLattice.Api.Services;

namespace NoduleLattice.Api.Controllers;

[ApiController]
[Route("api/lattice/run")]
public sealed class LatticeRunController : ControllerBase
{
    private readonly LatticeRunnerService _runner;

    public LatticeRunController(LatticeRunnerService runner)
    {
        _runner = runner;
    }

    [HttpPost("start")]
    public ActionResult<RunStatusDto> Start([FromBody] RunRequest req)
        => Ok(_runner.Start(req));

    [HttpPost("stop")]
    public ActionResult<RunStatusDto> Stop()
        => Ok(_runner.Stop());

    [HttpGet("status")]
    public ActionResult<RunStatusDto> Status()
        => Ok(_runner.Status());
}
