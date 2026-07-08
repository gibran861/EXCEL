using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;

using AfbGenerator.Api.Services;



namespace AfbGenerator.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FluxMappingController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly fluxMappingService _fluxMappingService;

        public FluxMappingController(AppDbContext context,fluxMappingService fluxMappingService)
        {
            _context = context;
            _fluxMappingService = fluxMappingService;
        }

        // ── 1. GET ALL MAPPINGS ─────────────────────────────────────────────
        [HttpGet]
        public async Task<ActionResult<IEnumerable<FluxMapping>>> GetMappings()
        {
            return await _context.FluxMappings.ToListAsync();
        }

        // ── 2. POST (CREATE) MAPPING ────────────────────────────────────────
       [HttpPost]
public async Task<IActionResult> CreateMapping([FromBody] CreateFluxMappingRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Keyword))
        return BadRequest("Le mot-clé est obligatoire.");

    // 🔥 LOG 1 : Affichage du mot-clé brut reçu par l'API
    Console.WriteLine($"[API] Tentative d'ajout du mot-clé brut reçu : '{request.Keyword}' pour le flux : '{request.Flux}' et la banque : '{request.BankCode}'");

    // Normalisation du mot-clé (remplacement des accents é et è)
    string normalizedKeyword = request.Keyword.Trim()
        .Replace("é", "e")
        .Replace("è", "e")
        .Replace("É", "E")
        .Replace("È", "E");

    // 🔥 LOG 2 : Affichage du mot-clé après nettoyage des accents é/è
    Console.WriteLine($"[API] Mot-clé après nettoyage des accents (é, è -> e) : '{normalizedKeyword}'");

    var keywordUpper = normalizedKeyword.ToUpperInvariant();
    var targetFlux = request.Flux.Trim().ToUpperInvariant();
    var targetBankCode = request.BankCode?.Trim().ToUpperInvariant();
    
    // 🔥 CORRECTION : L'unicité est maintenant vérifiée par le triplet : Mot-clé + Flux + Code Banque
    bool exists = await _context.FluxMappings.AnyAsync(m => 
        m.Keyword.ToUpper() == keywordUpper && 
        m.Flux.ToUpper() == targetFlux && 
        (string.IsNullOrEmpty(targetBankCode) ? string.IsNullOrEmpty(m.BankCode) : m.BankCode.ToUpper() == targetBankCode)
    );
    
    if (exists)
    {
        Console.WriteLine($"[API] [ATTENTION] Le mot-clé '{normalizedKeyword}' existe déjà pour le flux '{request.Flux}' et la banque '{request.BankCode}'.");
        return BadRequest("Ce mot-clé est déjà configuré pour ce flux et cette banque.");
    }

    var mapping = new FluxMapping
    {
        Flux = request.Flux.Trim(),
        Keyword = normalizedKeyword, 
        Operator = request.Operator ?? "ANY",
        TargetAmount = request.TargetAmount,
        BankCode = request.BankCode?.Trim(), // Nettoyage des espaces pour le code banque
        IsActive = true
    };

    _context.FluxMappings.Add(mapping);
    await _context.SaveChangesAsync();

    // 🔥 LOG 3 : Confirmation du succès de l'insertion en base de données
    Console.WriteLine($"[API] [SUCCÈS] Le mot-clé '{mapping.Keyword}' a été correctement enregistré pour le flux '{mapping.Flux}' et la banque '{mapping.BankCode}'.");

    return Ok(mapping);
}
        // ── 3. PUT (EDIT) MAPPING ───────────────────────────────────────────
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMapping(int id, [FromBody] UpdateFluxMappingRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Keyword))
                return BadRequest("Le mot-clé est obligatoire.");

            var mapping = await _context.FluxMappings.FindAsync(id);
            if (mapping == null)
                return NotFound($"Aucun mapping trouvé avec l'Id : {id}");

            // Vérifier si le nouveau mot-clé n'est pas déjà utilisé par un AUTRE mapping
            var keywordUpper = request.Keyword.Trim().ToUpperInvariant();
            bool exists = await _context.FluxMappings.AnyAsync(m => m.Id != id && m.Keyword.ToUpper() == keywordUpper);
            
            if (exists)
                return Conflict("Ce mot-clé est déjà utilisé par une autre règle.");

            // Mise à jour des données
            mapping.Flux = request.Flux.Trim();
            mapping.Keyword = request.Keyword.Trim();
            mapping.IsActive = request.IsActive;

            await _context.SaveChangesAsync();
            return Ok(mapping);
        }

        // ── 4. DELETE MAPPING ───────────────────────────────────────────────
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMapping(int id)
        {
            var mapping = await _context.FluxMappings.FindAsync(id);
            if (mapping == null)
                return NotFound($"Aucun mapping trouvé avec l'Id : {id}");

            _context.FluxMappings.Remove(mapping);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Mapping supprimé avec succès." });
        }

[HttpPost("import-excel")]
[Consumes("multipart/form-data")]
public async Task<ActionResult<FluxMappingImportResult>> ImportExcel(
    IFormFile file,
    [FromForm] List<string>? keysAMettreAJour = null, // Changement ici : de string? à List<string>?
    CancellationToken cancellationToken = default)
{
    // ... vos vérifications de fichier (ex: if (file == null || file.Length == 0) ...)

    // Plus besoin de désérialisation manuelle ! .NET a déjà tout mis dans la liste.
    // On s'assure juste d'avoir une liste vide au lieu de null si rien n'est coché
    var listKeys = keysAMettreAJour ?? new List<string>();

    // Appelez ensuite votre service en lui passant 'listKeys'
    var result = await _fluxMappingService.ImportFluxMappingDataAsync(file, listKeys, cancellationToken);
    
    return Ok(result);
}
    }

    
}