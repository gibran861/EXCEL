using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using AfbGenerator.Api.Services;
using System.Data;
using AfbGenerator.Api.Models;
namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BankTemplatesController : ControllerBase
{
    private readonly BankTemplateService _templateService;
    private readonly FileService _fileService = new FileService(); // Notre service de lecture Excel/CSV

    public BankTemplatesController(BankTemplateService templateService)
    {
        _templateService = templateService;
    }

    // POST: api/banktemplates/save
    [HttpPost("save")]
    public async Task<IActionResult> SaveTemplate([FromBody] BankTemplate template)
    {
        if (template == null)
        {
            return BadRequest("Les données du modèle de relevé sont invalides ou vides.");
        }

        try
        {
            // Appel du service pour valider et enregistrer en BDD
            var result = await _templateService.CreateTemplateAsync(template);
            
            // Retourne un code HTTP 201 (Created) avec l'objet mis à jour (contenant son nouvel ID généré)
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            // Retourne une erreur HTTP 400 si la validation métier échoue
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            // Retourne une erreur HTTP 500 en cas de problème technique sur le serveur ou la base
            return StatusCode(500, new { error = "Erreur interne : " + ex.Message });
        }
    }

    // GET: api/banktemplates/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var template = await _templateService.GetTemplateByIdAsync(id);
        if (template == null)
        {
            return NotFound(new { message = $"Modèle introuvable pour l'ID {id}." });
        }
        return Ok(template);
    }

    // GET: api/banktemplates
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var templates = await _templateService.GetAllTemplatesAsync();
        return Ok(templates);
    }

[HttpPost("process-file")]
public async Task<IActionResult> ProcessFileForAfb(IFormFile file)
{
    // 1. Lecture physique du fichier temporaire via le FileService
    var tempPath = Path.GetTempFileName();
    using (var stream = new FileStream(tempPath, FileMode.Create)) { await file.CopyToAsync(stream); }
    
    DataTable fileData = _fileService.LoadFileToDataTable(tempPath, ";");
  

    // 2. Détection automatique du modèle (Matching d'ancres)
    BankTemplate detectedTemplate = await _templateService.DetectTemplateAsync(fileData);
    if (detectedTemplate == null)
    {
        return BadRequest("Impossible de générer l'AFB : Format de fichier non reconnu.");
    }

    // 3. Extraction dynamique des données mappées
    ExtractedAccountStatement finalData = _templateService.ExtractData(fileData, detectedTemplate);

    // Vos données sont prêtes à être envoyées à votre ancienne logique AFB 120 !
    // Vous avez : finalData.NumCompte, finalData.DateDebut, et la liste finalData.Transactions
    return Ok(finalData);
}
    [HttpPost("detect")]
    public async Task<IActionResult> DetectUploadedFile(IFormFile file, [FromForm] string delimiter = ";")
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Aucun fichier n'a été fourni.");
        }

        try
        {
            // 1. Sauvegarder temporairement le fichier reçu pour le lire
            var tempPath = Path.GetTempFileName();
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // 2. Convertir le fichier en DataTable
            DataTable fileData = _fileService.LoadFileToDataTable(tempPath, delimiter);
            
            // Supprimer le fichier temporaire
            

            // 3. Lancer le moteur de détection
            BankTemplate detectedTemplate = await _templateService.DetectTemplateAsync(fileData);

            if (detectedTemplate == null)
            {
                return NotFound(new { message = "Structure inconnue. Aucun modèle de banque ne correspond à ce fichier." });
            }

            // 4. On retourne le modèle trouvé. Le front-end sait maintenant quel format appliquer !
            return Ok(detectedTemplate);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Erreur lors de l'analyse : " + ex.Message });
        }
    }
}