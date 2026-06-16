using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class XrtSyncController : ControllerBase
{
    private readonly XrtSyncService _xrtSyncService;

    public XrtSyncController(XrtSyncService xrtSyncService)
    {
        _xrtSyncService = xrtSyncService;
    }

    [HttpGet("fetch-flows")]
    [ProducesResponseType(typeof(List<XrtFlowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<XrtFlowDto>>> FetchXrtFlows(CancellationToken cancellationToken)
    {
        try
        {
            var flows = await _xrtSyncService.FetchXrtFlowsAsync(cancellationToken);
            return Ok(flows);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(XrtSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<XrtSyncResult>> SyncFluxes(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _xrtSyncService.SyncFluxesAsync(cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }
}
