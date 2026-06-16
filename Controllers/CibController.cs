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

    [HttpPost("import")]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Veuillez fournir un fichier Excel valide.");
        }

        var (inserted, updated, errors) = await _cibService.ImportCibFromExcelAsync(file, cancellationToken);

        if (errors.Any())
        {
            return BadRequest(new { Errors = errors });
        }

        return Ok(new { Inserted = inserted, Updated = updated, Message = "Importation réussie." });
    }
}