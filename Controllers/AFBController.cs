using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;
using AfbGenerator.Api.Data;
using AfbGenerator.Api.Services;
using System.Data;
using Microsoft.EntityFrameworkCore;
namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AFBController : ControllerBase
{
    private readonly Afb120Service _Afb120Service;
    private readonly BankTemplateService _templateService; 
    private readonly AppDbContext _context; 
    private readonly FileService _fileService = new FileService();
    public AFBController(Afb120Service Afb120Service, BankTemplateService templateService ,AppDbContext context)
    {
        _Afb120Service = Afb120Service;
        _templateService = templateService;
        _context= context ;
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
public async Task<IActionResult> ProcessAndGenerateAfb(
    IFormFile file, 
    [FromForm] decimal? soldeInitial = null,
    [FromForm] string? numCompte = null,
    [FromForm] string? outputPath = null) // 💡 AJOUT : Le chemin où le fichier AFB doit être généré
{
    if (file == null || file.Length == 0)
        return BadRequest("Le fichier est obligatoire.");

    // 1. Récupérer la vraie extension d'origine (.csv, .xlsx, etc.)
    string originalExtension = Path.GetExtension(file.FileName).ToLower();
    
    // 2. Générer un nom unique AVEC la bonne extension d'origine
    string tempFileName = $"{Guid.NewGuid()}{originalExtension}";
    string tempPath = Path.Combine(Path.GetTempPath(), tempFileName);

    try 
    {
        // 🔥 LOG CONSOLE : Suivi complet des paramètres reçus
        Console.WriteLine($"[ProcessAndGenerateAfb] Fichier : {file.FileName} | Solde Initial : {(soldeInitial.HasValue ? soldeInitial.Value.ToString() : "null")} | Num Compte : {numCompte ?? "null"} | Output Path : {outputPath ?? "null"}");

        // Écriture du fichier physique sur le disque
        using (var stream = new FileStream(tempPath, FileMode.Create)) 
        { 
            await file.CopyToAsync(stream); 
        }
        
        // DÉTECTION AUTOMATIQUE DU DÉLIMITEUR
        string autoDelimiter = (originalExtension == ".csv") ? "," : ";";
        
        DataTable fileData = _fileService.LoadFileToDataTable(tempPath, autoDelimiter);
        
        BankTemplate detectedTemplate = await _templateService.DetectTemplateAsync(fileData);
        if (detectedTemplate == null)
        {
            return BadRequest("Impossible de générer l'AFB : Format de fichier non reconnu.");
        }

        // Extraction des données du relevé (avec prise en compte du compte sélectionné)
        // Note: Assurez-vous d'appeler le bon nom de méthode (ExtractDataAsync ou ExtractData selon votre interface)
        ExtractedAccountStatement finalData = await _templateService.ExtractData(
            fileData, 
            detectedTemplate, 
            soldeInitial, 
            numCompte, 
            HttpContext.RequestAborted);

        // 3. Génération de l'AFB
        try
        {
            // 💡 MODIFICATION : Passage de "outputPath" et du jeton d'annulation au service
            var result = await _Afb120Service.GenerateFromExtractedDataAsync(
                finalData, 
                outputPath, 
                HttpContext.RequestAborted);
                
            return Ok(result);
        }
        catch (MissingMappingsException ex)
        {
            return UnprocessableEntity(ex.MissingKeywords);
        }
    }
    catch (Exception ex)
    {
        return StatusCode(500, $"Une erreur interne est survenue : {ex.Message}");
    }
    finally
    {
        // NETTOYAGE : Toujours supprimer le fichier temporaire à la fin
        if (System.IO.File.Exists(tempPath))
        {
            System.IO.File.Delete(tempPath);
        }
    }
}

[HttpPost("summary")]
public async Task<IActionResult> GetFileSummary(IFormFile file)
{
    if (file == null || file.Length == 0)
    {
        return BadRequest("Aucun fichier n'a été fourni.");
    }

    string originalExtension = Path.GetExtension(file.FileName).ToLower();
    string tempFileName = $"{Guid.NewGuid()}{originalExtension}";
    string tempPath = Path.Combine(Path.GetTempPath(), tempFileName);
    
    try
    {
        using (var stream = new FileStream(tempPath, FileMode.Create)) 
        { 
            await file.CopyToAsync(stream); 
        }
        
        string autoDelimiter = (originalExtension == ".csv") ? "," : ";";
        DataTable fileData = _fileService.LoadFileToDataTable(tempPath, autoDelimiter);

        // 1. Détection du modèle de la banque
        BankTemplate detectedTemplate = await _templateService.DetectTemplateAsync(fileData);
        if (detectedTemplate == null)
        {
            return NotFound(new { message = "Structure inconnue. Aucun modèle de banque ne correspond à ce fichier." });
        }

        // 2. 🔥 CORRECTION : Appel asynchrone "await" pour éviter l'erreur CS0029
        ExtractedAccountStatement extractedData = await _templateService.ExtractData(fileData, detectedTemplate);

        // 3. 🔥 MODIFICATION : On récupère TOUTES les lignes de cette banque (gère le multi-comptes)
        string targetBankCode = detectedTemplate.BankName.Trim().ToLower();
        var banquesEntities = await _context.Banques
            .Where(b => b.CodeBanque.ToLower() == targetBankCode && b.IsActive)
            .ToListAsync();

        string displayBankName = banquesEntities
    .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Libelle))?.Libelle 
    ?? detectedTemplate.BankName;

        // Récupération de la liste distincte des numéros de compte disponibles en BDD
        var listeComptes = banquesEntities
            .Where(b => !string.IsNullOrWhiteSpace(b.Compte))
            .Select(b => b.Compte.Trim())
            .Distinct()
            .ToList();

        // Détermination du compte par défaut à afficher au front
        string accountNameFallback = "Non configuré";
        if (!string.IsNullOrEmpty(extractedData.NumCompte))
        {
            accountNameFallback = extractedData.NumCompte; // Priorité au compte lu dans le fichier
        }
        else if (listeComptes.Count == 1)
        {
            accountNameFallback = listeComptes.First(); // S'il n'y en a qu'un seul en BDD
        }
        else if (listeComptes.Count > 1)
        {
            accountNameFallback = "Multi-comptes : Veuillez sélectionner"; 
        }

        // 4. Construction du récapitulatif
        var summary = new
        {
            BankName = detectedTemplate.BankName,
            Libelle = displayBankName,
            AccountNumber = accountNameFallback, 
            AvailableAccounts = listeComptes, // 🔥 AJOUT : Le front-end reçoit la liste pour générer le composant Select
            Currency = banquesEntities.FirstOrDefault()?.Devise ?? "XOF", 
            InitialBalance = extractedData.SoldeInitial,
            StartDate = extractedData.DateDebut,
            EndDate = extractedData.DateFin
        };

        return Ok(summary);
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { error = "Erreur lors de la génération du récapitulatif : " + ex.Message });
    }
    finally
    {
        if (System.IO.File.Exists(tempPath))
        {
            System.IO.File.Delete(tempPath);
        }
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