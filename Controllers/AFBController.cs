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
public async Task<IActionResult> GenerateFromExcel([FromForm] AfbUploadRequest request, CancellationToken cancellationToken)
{
    if (request.File == null || request.File.Length == 0)
        return BadRequest("Le fichier Excel est obligatoire.");

    // On passe request.OutputPath au service
    var result = await _Afb120Service.GenerateFromExcelAsync(request.File, request.OutputPath, cancellationToken);
    return Ok(result);
}
}