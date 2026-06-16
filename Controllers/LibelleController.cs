using Microsoft.AspNetCore.Mvc;
using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using AfbGenerator.Api.Entities;

namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LibelleController : ControllerBase
{
	private readonly LibelleService _libelleService;

	public LibelleController(LibelleService libelleService)
	{
		_libelleService = libelleService;
	}

	[HttpPost("import-excel")]
	[Consumes("multipart/form-data")]
	[ProducesResponseType(typeof(LibelleImportResult), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<LibelleImportResult>> ImportExcel([FromForm] LibelleImportExcelRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var result = await _libelleService.ImportFromExcelAsync(request.File, request.FluxId, cancellationToken);
			return Ok(result);
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(new { message = ex.Message });
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

	[HttpPost("extract-excel")]
	[Consumes("multipart/form-data")]
	[ProducesResponseType(typeof(ExcelStatementExtractResult), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<ExcelStatementExtractResult>> ExtractExcel([FromForm] ExcelExtractRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var result = await _libelleService.ExtractImportantDataAsync(request.File, request.FluxId, cancellationToken);
			return Ok(result);
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(new { message = ex.Message });
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

	[HttpPost("import-flux-mapping")]
	[Consumes("multipart/form-data")]
	[ProducesResponseType(typeof(LibelleFluxMappingImportResult), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<LibelleFluxMappingImportResult>> ImportFluxMapping([FromForm] LibelleFluxMappingImportRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var result = await _libelleService.ImportFluxMappingFromExcelAsync(request.File, cancellationToken);
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

[HttpGet]
[ProducesResponseType(typeof(List<Libelle>), StatusCodes.Status200OK)]
public async Task<ActionResult<List<Libelle>>> GetAll(CancellationToken cancellationToken = default)
{
    var libelles = await _libelleService.GetAllLibellesAsync(cancellationToken);
    return Ok(libelles);
}
	[HttpPost("resolve-flux")]
	[ProducesResponseType(typeof(ResolveFluxByLibelleResult), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<ResolveFluxByLibelleResult>> ResolveFlux([FromBody] ResolveFluxByLibelleRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var result = await _libelleService.ResolveFluxByLibelleAsync(request.Libelle, cancellationToken);
			return Ok(result);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(new { message = ex.Message });
		}
	}

	[HttpGet("detect-categorie")]
public async Task<IActionResult> DetectCategorie(string libelle)
{
    var result = await _libelleService.DetectCategorieAsync(libelle);
    return Ok(result);
}
}
