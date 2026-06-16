using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FluxController : ControllerBase
{
	private readonly FluxService _fluxService;

	public FluxController(FluxService fluxService)
	{
		_fluxService = fluxService;
	}

	[HttpPost]
	[ProducesResponseType(typeof(FluxResponse), StatusCodes.Status201Created)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<FluxResponse>> Create([FromBody] CreateFluxRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var created = await _fluxService.CreateAsync(request, cancellationToken);
			return CreatedAtAction(nameof(GetAll), new { id = created.Id }, created);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(new { message = ex.Message });
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(new { message = ex.Message });
		}
	}

	[HttpGet]
	[ProducesResponseType(typeof(List<FluxResponse>), StatusCodes.Status200OK)]
	public async Task<ActionResult<List<FluxResponse>>> GetAll(CancellationToken cancellationToken)
	{
		var fluxes = await _fluxService.GetAllAsync(cancellationToken);
		return Ok(fluxes);
	}

	[HttpPut("{fluxCode}/cib")]
	[ProducesResponseType(typeof(FluxResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<FluxResponse>> SetCibByFluxCode(string fluxCode, [FromBody] UpdateFluxCibRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var updated = await _fluxService.SetCibByFluxCodeAsync(fluxCode, request.Cib1, request.Cib2, cancellationToken);
			return Ok(updated);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(new { message = ex.Message });
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(new { message = ex.Message });
		}
	}
}
