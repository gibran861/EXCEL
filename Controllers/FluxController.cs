using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FluxController : ControllerBase
{
	private readonly FluxService _fluxService;
    private readonly LibelleService _libelleService;
	public FluxController(FluxService fluxService,LibelleService libelleService)
	{
		_fluxService = fluxService;
		_libelleService = libelleService;
	}
[HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            // Appelle le service en lui passant simplement l'ID
            await _fluxService.DeleteAsync(id, cancellationToken);
            return NoContent(); // Renvoie un statut 204 (Succès, pas de contenu)
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
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

[HttpPut("bank/{bankCode}/flux/{fluxCode}/cib")]
[ProducesResponseType(typeof(FluxResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<FluxResponse>> SetCibByBankAndFlux(
    string bankCode, 
    string fluxCode, 
    [FromBody] UpdateFluxCibRequest request, 
    CancellationToken cancellationToken)
{
    try
    {
        // On passe les deux codes au service
        var updated = await _fluxService.SetCibByBankAndFluxAsync(bankCode, fluxCode, request.Cib1, request.Cib2, cancellationToken);
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


	[HttpPost("extract-flux-from-excel")]
[Consumes("multipart/form-data")]
public async Task<ActionResult<ExcelFluxExtractResult>> ExtractFluxFromExcel(
    [FromForm] FluxExtractRequest request, 
    CancellationToken cancellationToken)
{
    try
    {
        // On passe request.File au lieu de file directement
        var result = await _libelleService.ExtractAndSaveFluxFromExcelAsync(request.File, cancellationToken);
        return Ok(result);
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
}
