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


[HttpPost("generateAFB")]
public async Task<IActionResult> GenerateFromExcelgenerique(
    [FromForm] AfbUploadRequest request,
    CancellationToken cancellationToken)
{
    try
    {
        // 1. Validation de la présence de l'objet et du fichier (sécurité)
        if (request == null || request.File == null || request.File.Length == 0)
        {
            return BadRequest(new { message = "Le fichier et les paramètres requis sont obligatoires." });
        }

        // 2. Validation du type en entrée (double sécurité)
        var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv"))
        {
            return BadRequest(new { message = "Format de fichier non supporté. Fichiers acceptés : .xlsx, .xls, .csv" });
        }

        // 3. Appel de la méthode mise à jour avec les paramètres requis (Fichier, Compte, Devise, OutputPath)
        var result = await _Afb120Service.GenerateFromFilegeneriqueAsync(
            request.File, 
            request.CompteCourant, 
            request.Devise, 
            request.OutputPath, 
            cancellationToken);

        return Ok(result);
    }
    catch (MissingMappingsException ex)
    {
        // Retourne un code 422 si des libellés de transactions n'ont pas de correspondance de flux
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