using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;
using AfbGenerator.Api.Services;
namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AFBController : ControllerBase
{
    private readonly Afb120Service _Afb120Service;

    public AFBController(Afb120Service Afb120Service)
    {
        _Afb120Service = Afb120Service;
    }

[HttpPost("generate")]
public async Task<IActionResult> GenerateFromExcel(
    [FromForm] AfbUploadRequest request,
    CancellationToken cancellationToken)
{
    try
    {
        // Validation du type en entrée (double sécurité)
        var ext = Path.GetExtension(request.File?.FileName ?? "").ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv"))
            return BadRequest(new { message = "Format non supporté. Fichiers acceptés : .xlsx, .xls, .csv" });

        var result = await _Afb120Service.GenerateFromFileAsync(   // ← nouveau nom
            request.File, request.OutputPath, cancellationToken);

        return Ok(result);
    }
    catch (MissingMappingsException ex)
    {
        return StatusCode(StatusCodes.Status422UnprocessableEntity, ex.MissingKeywords);
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
    catch (Exception ex)
    {
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "Une erreur système est survenue.", detail = ex.Message });
    }
}
    
}