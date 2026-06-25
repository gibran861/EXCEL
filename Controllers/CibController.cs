using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CibController : ControllerBase
{
    private readonly CibService _cibService;

    public CibController(CibService cibService)
    {
        _cibService = cibService;
    }

    [HttpGet]
    public async Task<ActionResult<List<CibResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _cibService.GetAllAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CibResponse>> Create([FromBody] CreateCibRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _cibService.CreateAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

  [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CibImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CibImportResult>> ImportCibExcel(IFormFile file, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _cibService.ImportCibDataAsync(file, cancellationToken);
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