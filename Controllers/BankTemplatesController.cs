using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using AfbGenerator.Api.Services;
using System.Data;
using AfbGenerator.Api.Data;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BankTemplatesController : ControllerBase
{
    private readonly BankTemplateService _templateService;
     private readonly AppDbContext _context;
    private readonly FileService _fileService = new FileService(); // Notre service de lecture Excel/CSV

    public BankTemplatesController(BankTemplateService templateService,AppDbContext context)
    {
        _templateService = templateService;
        _context = context;
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
public async Task<IActionResult> ProcessFileForAfb(IFormFile file, [FromForm] string delimiter = ";")
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
        
        // 🔥 MODIFICATION ICI : On utilise la variable "delimiter" reçue au lieu du ";" en dur !
        DataTable fileData = _fileService.LoadFileToDataTable(tempPath, delimiter);

        // 2. Détection automatique du modèle
        BankTemplate detectedTemplate = await _templateService.DetectTemplateAsync(fileData);
        if (detectedTemplate == null)
        {
            return BadRequest("Impossible de générer l'AFB : Format de fichier non reconnu.");
        }

        // 3. Extraction dynamique des données mappées
        ExtractedAccountStatement finalData = await _templateService.ExtractData(fileData, detectedTemplate);

        return Ok(finalData);
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { error = "Erreur lors du traitement : " + ex.Message });
    }
    finally
    {
        if (System.IO.File.Exists(tempPath))
        {
            System.IO.File.Delete(tempPath);
        }
    }
}
[HttpPut("template-by-bank/{bankName}")]
public async Task<IActionResult> UpdateTemplateByBankName(string bankName, [FromBody] BankTemplate updatedTemplate)
{
    if (updatedTemplate == null)
    {
        return BadRequest("Les données du modèle sont obligatoires.");
    }

    if (string.IsNullOrWhiteSpace(bankName))
    {
        return BadRequest("Le nom de la banque dans l'URL est obligatoire.");
    }

    try
    {
        // 1. Récupérer le template existant avec ses champs actuels
        // Ajuste '_context.BankTemplates' selon le nom exact dans ton DbContext
        var existingTemplate = await _context.BankTemplates
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.BankName.ToLower() == bankName.ToLower());

        if (existingTemplate == null)
        {
            return NotFound(new { message = $"Aucun modèle trouvé à modifier pour la banque : {bankName}" });
        }

        // 2. Mise à jour des propriétés globales du template
        existingTemplate.BankName = updatedTemplate.BankName;
        existingTemplate.FileExtension = updatedTemplate.FileExtension;
        existingTemplate.CsvDelimiter = updatedTemplate.CsvDelimiter;

        // 3. Nettoyage des anciens champs associés pour éviter les conflits d'IDs
        if (existingTemplate.Fields != null && existingTemplate.Fields.Any())
        {
            // Supprime les anciens enregistrements directement via le DbSet de liaison
            // Si ta table s'appelle autrement (ex: _context.TemplateFields), ajuste le nom ici :
            _context.TemplateFields.RemoveRange(existingTemplate.Fields);
        }

        // 4. Injection des nouveaux champs (TemplateField) reçus
        existingTemplate.Fields = new List<TemplateField>();
        if (updatedTemplate.Fields != null)
        {
            foreach (var field in updatedTemplate.Fields)
            {
                // On réinitialise l'Id à 0 pour que la BDD l'incrémente automatiquement comme une nouveauté
                field.Id = 0; 
                field.BankTemplateId = existingTemplate.Id;
                
                existingTemplate.Fields.Add(field);
            }
        }

        // 5. Sauvegarde des changements
        await _context.SaveChangesAsync();

        // On renvoie le template fraîchement mis à jour
        return Ok(existingTemplate);
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { error = "Erreur lors de la mise à jour du modèle : " + ex.Message });
    }
}

[HttpGet("bank/{bankName}")]
public async Task<IActionResult> GetTemplate(string bankName)
{
    var template = await _templateService.GetByBank(bankName);

    if (template == null)
        return NotFound();

    return Ok(template);
}
    [HttpPost("detect")]
public async Task<IActionResult> DetectUploadedFile(IFormFile file, [FromForm] string delimiter = ";")
{
    if (file == null || file.Length == 0)
    {
        return BadRequest("Aucun fichier n'a été fourni.");
    }

    // 1. Récupérer l'extension d'origine (.csv, .xlsx, etc.)
    string originalExtension = Path.GetExtension(file.FileName).ToLower();
    
    // 2. Générer un chemin temporaire unique AVEC la bonne extension d'origine
    string tempFileName = $"{Guid.NewGuid()}{originalExtension}";
    string tempPath = Path.Combine(Path.GetTempPath(), tempFileName);
    
    try
    {
        // 3. Sauvegarder le fichier reçu sur le disque
        using (var stream = new FileStream(tempPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // 4. Convertir le fichier en DataTable (LoadFileToDataTable verra la vraie extension)
        DataTable fileData = _fileService.LoadFileToDataTable(tempPath, delimiter);

        // 5. Lancer le moteur de détection
        BankTemplate detectedTemplate = await _templateService.DetectTemplateAsync(fileData);

        if (detectedTemplate == null)
        {
            return NotFound(new { message = "Structure inconnue. Aucun modèle de banque ne correspond à ce fichier." });
        }

        return Ok(detectedTemplate);
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { error = "Erreur lors de l'analyse : " + ex.Message });
    }
    finally
    {
        // 🚨 NETTOYAGE : On supprime le fichier du répertoire temporaire
      if (System.IO.File.Exists(tempPath))
    {
        System.IO.File.Delete(tempPath);
    }
    }
}

[HttpGet("template-by-bank/{bankName}")]
public async Task<IActionResult> GetTemplateByBankName(string bankName)
{
    if (string.IsNullOrWhiteSpace(bankName))
    {
        return BadRequest("Le nom de la banque est obligatoire.");
    }

    try
    {
        // Recherche du template par son nom en incluant la liste de ses champs configurés
        // Utilise le bon nom de DbSet pour tes templates (ex: _context.BankTemplates)
        var template = await _context.BankTemplates
            .Include(t => t.Fields) 
            .FirstOrDefaultAsync(t => t.BankName.ToLower() == bankName.ToLower());

        if (template == null)
        {
            return NotFound(new { message = $"Aucun modèle de configuration trouvé pour la banque : {bankName}" });
        }

        // Renvoie exactement l'objet complet (id, bankName, fileExtension, fields, etc.)
        return Ok(template);
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { error = "Erreur lors de la récupération du modèle : " + ex.Message });
    }
}
}