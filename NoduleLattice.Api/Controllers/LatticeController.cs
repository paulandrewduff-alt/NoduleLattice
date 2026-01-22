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


    /// <summary>
    /// Canon snapshot endpoint.
    /// Supports thin mode to keep Blazor real-time.
    /// </summary>
    [HttpGet("snapshot")]
    public ActionResult<LatticeSnapshotDto> Snapshot(
    [FromQuery] bool thin = true,
    [FromQuery] int maxEdges = 12000,
    [FromQuery] float maxLen = 7f)
    {
        if (thin)
            return Ok(_host.GetSnapshotThin(maxEdges, maxLen));


        return Ok(_host.GetSnapshot());
    }
}