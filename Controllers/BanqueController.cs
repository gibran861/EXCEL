using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AfbGenerator.Api.Services;
using ExcelDataReader; 

namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BanqueController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IImportService _importService;

    public BanqueController(
        AppDbContext dbContext,
        IImportService importService)
    {
        _dbContext = dbContext;
        _importService = importService;
    }

    // 1. GET : Récupérer toutes les banques configurées
    [HttpGet]
    [ProducesResponseType(typeof(List<Banque>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Banque>>> GetAll(CancellationToken cancellationToken)
    {
        var banques = await _dbContext.Banques
            .AsNoTracking()
            .OrderBy(b => b.CodeBanque)
            .ThenBy(b => b.Filiale)
            .ToListAsync(cancellationToken);

        return Ok(banques);
    }

    // 1b. GET : Récupérer le libellé à partir du code banque
    [HttpGet("code/{code}/libelle")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<string>> GetLibelleByCode(string code, CancellationToken cancellationToken)
    {
        var libelle = await _dbContext.Banques
            .AsNoTracking()
            .Where(b => b.CodeBanque == code.Trim())
            .Select(b => b.Libelle)
            .FirstOrDefaultAsync(cancellationToken);

        if (libelle == null)
            return NotFound(new { message = $"Aucune banque trouvée pour le code '{code}'." });

        return Ok(libelle);
    }

    // 1c. GET : Récupérer le libellé à partir du numéro de compte
    [HttpGet("compte/{compte}/libelle")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<string>> GetLibelleByCompte(string compte, CancellationToken cancellationToken)
    {
        var libelle = await _dbContext.Banques
            .AsNoTracking()
            .Where(b => b.Compte == compte.Trim())
            .Select(b => b.Libelle)
            .FirstOrDefaultAsync(cancellationToken);

        if (libelle == null)
            return NotFound(new { message = $"Aucune banque trouvée pour le compte '{compte}'." });

        return Ok(libelle);
    }

    // 2. POST : Ajouter une nouvelle configuration de banque
    [HttpPost]
    [ProducesResponseType(typeof(Banque), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Banque>> Add([FromBody] Banque request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var nouvelleBanque = new Banque
        {
            CodeBanque = request.CodeBanque.Trim().ToUpperInvariant(),
            Filiale    = request.Filiale.Trim().ToUpperInvariant(),
            TypeFichier = request.TypeFichier?.Trim().ToUpperInvariant(),
            Libelle    = request.Libelle.Trim(),
            IsActive   = true,
            CreatedAt  = DateTime.UtcNow,
            Compte     = request.Compte?.Trim()
        };

        await _dbContext.Banques.AddAsync(nouvelleBanque, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { id = nouvelleBanque.Id }, nouvelleBanque);
    }

    // 2a. POST : Import Excel
  // BanqueController.cs — endpoint import-excel mis à jour
[HttpPost("import-excel")]
[Consumes("multipart/form-data")]
public async Task<IActionResult> ImportBanque(
    IFormFile file,
    [FromForm] string? codesAMettreAJour = null,
    CancellationToken cancellationToken = default)
{
    if (file == null || file.Length == 0)
        return BadRequest(new { message = "Fichier requis" });

    try
    {
        // Désérialiser la liste des codes à mettre à jour
        var codesToUpdate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(codesAMettreAJour))
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(codesAMettreAJour);
            if (parsed != null)
                foreach (var c in parsed) codesToUpdate.Add(c);
        }

        var rows = _importService.ReadFile(file);
        if (rows.Count == 0) return BadRequest(new { message = "Fichier vide" });

        var header   = rows[0];
        int codeCol  = _importService.FindColumnIndex(header, "CodeBanque");
        int libCol   = _importService.FindColumnIndex(header, "Libelle");
        int compteCol = _importService.FindColumnIndex(header, "Compte");

        if (codeCol < 0 || libCol < 0)
            return BadRequest(new { message = "Colonnes manquantes (CodeBanque, Libelle)" });

        // Collecter tous les codes du fichier
        var codesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < rows.Count; i++)
        {
            var c = rows[i][codeCol].Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(c)) codesInFile.Add(c);
        }

        var existingBanques = await _dbContext.Banques
            .Where(x => codesInFile.Contains(x.CodeBanque))
            .ToListAsync(cancellationToken);

        var existingDict = existingBanques
            .ToDictionary(b => b.CodeBanque, StringComparer.OrdinalIgnoreCase);

        var toInsert   = new List<Banque>();
        var duplicates = new List<object>(); // pour le premier appel
        int skipped    = 0;
        int updated    = 0;

        for (int i = 1; i < rows.Count; i++)
        {
            var code   = rows[i][codeCol].Trim().ToUpperInvariant();
            var lib    = rows[i][libCol].Trim();
            string? compte = compteCol >= 0 ? rows[i][compteCol].Trim() : null;

            if (string.IsNullOrWhiteSpace(code)) continue;

            if (existingDict.TryGetValue(code, out var existing))
            {
                if (codesToUpdate.Contains(code))
                {
                    // Mise à jour ciblée
                    existing.Libelle = lib;
                    existing.Compte  = compte;
                    updated++;
                }
                else
                {
                    // Skip + on remonte l'info au front (premier appel)
                    duplicates.Add(new { code, libelle = lib });
                    skipped++;
                }
                continue;
            }

            toInsert.Add(new Banque
            {
                CodeBanque  = code,
                Libelle     = lib,
                Compte      = compte,
                Filiale     = "STANDARD",
                TypeFichier = "AFB120",
                IsActive    = true,
                CreatedAt   = DateTime.UtcNow
            });
        }

        await _dbContext.Banques.AddRangeAsync(toInsert, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            TotalRowsProcessed   = rows.Count - 1,
            InsertedCount        = toInsert.Count,
            SkippedExistingCount = skipped,
            UpdatedCount         = updated,
            Duplicates           = duplicates  // ← liste {code, libelle} pour le front
        });
    }
    catch (Exception ex)
    {
        return BadRequest(new { message = ex.Message });
    }
}

    // 2b. PUT : Modifier une configuration de banque existante
   [HttpPut("{id}")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> Edit(int id, [FromBody] Banque request, CancellationToken cancellationToken)
{
    if (id != request.Id)
        return BadRequest(new { message = "L'ID fourni dans l'URL ne correspond pas à l'ID du corps de la requête." });

    if (!ModelState.IsValid)
        return BadRequest(ModelState);

    var banqueExistante = await _dbContext.Banques
        .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    if (banqueExistante == null)
        return NotFound(new { message = $"Banque avec l'ID {id} introuvable." });

    // 1. On mémorise l'ancien code avant toute modification
    string ancienCodeBanque = banqueExistante.CodeBanque;
    
    // 2. On prépare le nouveau code nettoyé
    string nouveauCodeBanque = request.CodeBanque.Trim().ToUpperInvariant();

    // 3. Si le code de la banque a changé, on met à jour les tables dépendantes
    if (ancienCodeBanque != nouveauCodeBanque)
    {
        // A. Mise à jour de BankTemplates (BankName)
        var templatesAssocies = await _dbContext.BankTemplates
            .Where(t => t.BankName == ancienCodeBanque)
            .ToListAsync(cancellationToken);

        foreach (var template in templatesAssocies)
        {
            template.BankName = nouveauCodeBanque;
        }

        // B. 🔥 NOUVEAU : Mise à jour de FluxMappings (BankCode)
        var mappingsAssocies = await _dbContext.FluxMappings
            .Where(m => m.BankCode == ancienCodeBanque)
            .ToListAsync(cancellationToken);

        foreach (var mapping in mappingsAssocies)
        {
            mapping.BankCode = nouveauCodeBanque;
        }
    }

    // 4. Mise à jour des autres propriétés de la banque
    banqueExistante.CodeBanque  = nouveauCodeBanque;
    banqueExistante.Filiale     = request.Filiale.Trim().ToUpperInvariant();
    banqueExistante.TypeFichier = request.TypeFichier?.Trim().ToUpperInvariant();
    banqueExistante.Libelle     = request.Libelle.Trim();
    banqueExistante.Compte      = request.Compte?.Trim();
    banqueExistante.IsActive    = request.IsActive;

    try
    {
        // Sauvegarde globale de la banque, des templates et des mappings modifiés
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        if (!await BanqueExistsAsync(id, cancellationToken))
            return NotFound();
        throw;
    }

    return NoContent();
}

    // 2c. DELETE : Supprimer une banque
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var banque = await _dbContext.Banques
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (banque == null)
            return NotFound(new { message = $"Banque avec l'ID {id} introuvable." });

        _dbContext.Banques.Remove(banque);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // --- MÉTHODES UTILITAIRES PRIVÉES ---
    private async Task<bool> BanqueExistsAsync(int id, CancellationToken cancellationToken)
    {
        return await _dbContext.Banques.AnyAsync(e => e.Id == id, cancellationToken);
    }
}