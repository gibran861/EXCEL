using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;
using AfbGenerator.Api.Services;
using System.Data;
namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AFBController : ControllerBase
{
    private readonly Afb120Service _Afb120Service;
    private readonly BankTemplateService _templateService;
    private readonly FileService _fileService = new FileService();
    public AFBController(Afb120Service Afb120Service, BankTemplateService templateService)
    {
        _Afb120Service = Afb120Service;
        _templateService = templateService;
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
[HttpPost("process-and-generate-afb")]
public async Task<IActionResult> ProcessAndGenerateAfb(IFormFile file)
{
    if (file == null || file.Length == 0)
        return BadRequest("Le fichier est obligatoire.");

    var tempPath = Path.GetTempFileName();
    try 
    {
        // 1. Lecture physique et extraction (Stable)
        using (var stream = new FileStream(tempPath, FileMode.Create)) 
        { 
            await file.CopyToAsync(stream); 
        }
        
        DataTable fileData = _fileService.LoadFileToDataTable(tempPath, ";");
        
        BankTemplate detectedTemplate = await _templateService.DetectTemplateAsync(fileData);
        if (detectedTemplate == null)
        {
            return BadRequest("Impossible de générer l'AFB : Format de fichier non reconnu.");
        }

        ExtractedAccountStatement finalData = _templateService.ExtractData(fileData, detectedTemplate);

        // 2. Génération de l'AFB
        try
        {
            var result = await _Afb120Service.GenerateFromExtractedDataAsync(finalData);
            return Ok(result);
        }
        catch (MissingMappingsException ex)
        {
            // 🔥 CORRECTION ICI : On renvoie un statut 422 (UnprocessableEntity) 
            // et on passe DIRECTEMENT la liste des mots-clés sans objet enveloppe.
            return UnprocessableEntity(ex.MissingKeywords);
        }
    }
    catch (Exception ex)
    {
        return StatusCode(500, $"Une erreur interne est survenue : {ex.Message}");
    }
    finally
    {
        
    }
}
// [HttpPost("generateAFB")]
// public async Task<IActionResult> GenerateFromExcelgenerique(
//     [FromForm] AfbUploadRequest request,
//     CancellationToken cancellationToken)
// {
//     try
//     {
//         // 1. Validation de la présence de l'objet et du fichier (sécurité)
//         if (request == null || request.File == null || request.File.Length == 0)
//         {
//             return BadRequest(new { message = "Le fichier et les paramètres requis sont obligatoires." });
//         }

//         // 2. Validation du type en entrée (double sécurité)
//         var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
//         if (ext is not (".xlsx" or ".xls" or ".csv"))
//         {
//             return BadRequest(new { message = "Format de fichier non supporté. Fichiers acceptés : .xlsx, .xls, .csv" });
//         }

//         // 3. Appel de la méthode mise à jour avec les paramètres requis (Fichier, Compte, Devise, OutputPath)
//         var result = await _Afb120Service.GenerateFromFilegeneriqueAsync(
//             request.File, 
//             request.CompteCourant, 
//             request.Devise, 
//             request.OutputPath, 
//             cancellationToken);

//         return Ok(result);
//     }
//     catch (MissingMappingsException ex)
//     {
//         // Retourne un code 422 si des libellés de transactions n'ont pas de correspondance de flux
//         return StatusCode(StatusCodes.Status422UnprocessableEntity, ex.MissingKeywords);
//     }
//     catch (ArgumentException ex)
//     {
//         return BadRequest(new { message = ex.Message });
//     }
//     catch (InvalidOperationException ex)
//     {
//         return BadRequest(new { message = ex.Message });
//     }
//     catch (Exception ex)
//     {
//         return StatusCode(StatusCodes.Status500InternalServerError,
//             new { message = "Une erreur système est survenue.", detail = ex.Message });
//     }
// }
    
}