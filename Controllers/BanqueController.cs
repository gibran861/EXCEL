using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AfbGenerator.Api.Services;
using ExcelDataReader;
using AfbGenerator.Api.Entities.Xrt;

namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BanqueController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly XrtDbContext _XRTContext;
    private readonly IImportService _importService;

    public BanqueController(
        AppDbContext dbContext,
        XrtDbContext xrtContext,
        IImportService importService)
    {
        _dbContext = dbContext;
        _XRTContext = xrtContext;
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

    // probleme de ducplication du code banque 5 permmier du Rib
    //  2. POST : Ajouter une nouvelle configuration de banque
   /* [HttpPost]
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
    }*/


[HttpGet("xrt-accounts")]
[ProducesResponseType(typeof(IEnumerable<RibAccountView>), StatusCodes.Status200OK)]
public async Task<ActionResult<IEnumerable<RibAccountView>>> GetXrtRibAccounts(
    [FromQuery] string? search,
    CancellationToken cancellationToken)
{
    var query = _XRTContext.RibAccountViews.AsNoTracking();

    // Filtrage optionnel par numéro de compte RIB ou description
    if (!string.IsNullOrWhiteSpace(search))
    {
        string term = search.Trim().ToUpper();
        query = query.Where(v => 
            (v.AccountLocalRib != null && v.AccountLocalRib.ToUpper().Contains(term)) ||
            (v.Description != null && v.Description.ToUpper().Contains(term)) ||
            (v.AccountCode != null && v.AccountCode.ToUpper().Contains(term))
        );
    }

    var resultats = await query.ToListAsync(cancellationToken);

    return Ok(resultats);
}
// 2. POST : Ajouter une nouvelle configuration de banque
[HttpPost]
[ProducesResponseType(typeof(Banque), StatusCodes.Status201Created)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<Banque>> Add([FromBody] Banque request, CancellationToken cancellationToken)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);

    // 1. Nettoyage des chaînes en mémoire C# (ToUpperInvariant est parfait ici)
    string compteNettoye = request.Compte?.Trim().ToUpperInvariant() ?? string.Empty;
    string codeBanqueNettoye = request.CodeBanque?.Trim().ToUpperInvariant() ?? string.Empty;

    // 2. Validation du Compte obligatoire
    if (string.IsNullOrWhiteSpace(compteNettoye))
    {
        return BadRequest(new { message = "Le numéro de compte est obligatoire pour identifier la banque avec certitude." });
    }

    // 3. Validation d'unicité (Utilisation de .ToUpper() pour la traduction LINQ/SQL)
    bool compteExiste = await _dbContext.Banques
        .AnyAsync(b => b.Compte != null && b.Compte.Trim().ToUpper() == compteNettoye, cancellationToken);

    if (compteExiste)
    {
        return BadRequest(new { message = $"Un enregistrement avec le numéro de compte '{compteNettoye}' existe déjà." });
    }

    // 4. Instanciation de la nouvelle banque
    var nouvelleBanque = new Banque
    {
        CodeBanque  = codeBanqueNettoye,
        Filiale     = request.Filiale?.Trim().ToUpperInvariant() ?? string.Empty,
        TypeFichier = request.TypeFichier?.Trim().ToUpperInvariant(),
        Libelle     = request.Libelle?.Trim() ?? string.Empty,
        Devise      = request.Devise?.Trim().ToUpperInvariant(),
        IsActive    = true,
        CreatedAt   = DateTime.UtcNow,
        Compte      = compteNettoye
    };

    await _dbContext.Banques.AddAsync(nouvelleBanque, cancellationToken);
    await _dbContext.SaveChangesAsync(cancellationToken);

    return CreatedAtAction(nameof(GetAll), new { id = nouvelleBanque.Id }, nouvelleBanque);
}

[HttpGet("sync-banques")]
[ProducesResponseType(typeof(List<Banque>), StatusCodes.Status200OK)]
public async Task<IActionResult> GetBanqueSynchro(CancellationToken cancellationToken)
{
    // 1. Charger les numéros de comptes déjà existants en BDD locale
    var comptesExistants = await _dbContext.Banques
        .Where(b => b.Compte != null)
        .Select(b => b.Compte!.Trim().ToUpper())
        .ToListAsync(cancellationToken);

    var hashSetComptesExistants = new HashSet<string>(comptesExistants, StringComparer.OrdinalIgnoreCase);

    // 2. Charger les comptes depuis la vue XRT RIBAccount
    var comptesXrt = await _XRTContext.RibAccountViews
        .Where(v => !string.IsNullOrEmpty(v.AccountLocalRib))
        .ToListAsync(cancellationToken);

    // Helper pour extraire le CodeBanque (5 premiers chiffres du RIB)
    string ExtraireCodeBanque(string rib)
    {
        string ribClean = rib.Trim();
        return ribClean.Length >= 5 ? ribClean.Substring(0, 5).ToUpperInvariant() : string.Empty;
    }

    // 3. Filtrer et grouper pour éviter les doublons dans le flux XRT
    var nouvellesBanquesXrt = comptesXrt
        .Select(v => new
        {
            CompteClean      = v.AccountLocalRib!.Trim().ToUpperInvariant(),
            DescriptionClean = v.Description?.Trim() ?? string.Empty,
            DeviseClean      = !string.IsNullOrWhiteSpace(v.Devise) 
                                ? v.Devise.Trim().ToUpperInvariant() 
                                : "XOF", // Fallback sur XOF si nul ou vide
            Source           = v
        })
        .Where(x => !string.IsNullOrWhiteSpace(x.CompteClean) && !hashSetComptesExistants.Contains(x.CompteClean))
        .GroupBy(x => x.CompteClean)
        .Select(g => g.First())
        .ToList();

    // 4. Instancier et ajouter les nouvelles banques si présente(s) dans XRT
    if (nouvellesBanquesXrt.Any())
    {
        var aAjouter = nouvellesBanquesXrt.Select(x => new Banque
        {
            CodeBanque  = ExtraireCodeBanque(x.CompteClean),
            Compte      = x.CompteClean,
            Libelle     = !string.IsNullOrWhiteSpace(x.DescriptionClean) ? x.DescriptionClean : $"Banque {x.CompteClean}",
            Filiale     = "GROUPE",
            TypeFichier = "EXCEL",
            Devise      = x.DeviseClean, // 👈 Utilisation de la devise issue de la vue
            IsActive    = true,
            CreatedAt   = DateTime.UtcNow
        }).ToList();

        await _dbContext.Banques.AddRangeAsync(aAjouter, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // 5. Charger et retourner la liste complète des banques à jour
    var listeCompleteBanques = await _dbContext.Banques
        .AsNoTracking()
        .OrderBy(b => b.CodeBanque)
        .ToListAsync(cancellationToken);

    return Ok(listeCompleteBanques);
}    // 2a. POST : Import Excel
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

    // 3. Si le code de la banque a changé, on met à jour toutes les tables dépendantes
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

        // B. Mise à jour de FluxMappings (BankCode)
        var mappingsAssocies = await _dbContext.FluxMappings
            .Where(m => m.BankCode == ancienCodeBanque)
            .ToListAsync(cancellationToken);

        foreach (var mapping in mappingsAssocies)
        {
            mapping.BankCode = nouveauCodeBanque;
        }

        // C. 🔥 NOUVEAU : Mise à jour de Flux (BankCode)
        var fluxAssocies = await _dbContext.Fluxes
            .Where(f => f.BankCode == ancienCodeBanque)
            .ToListAsync(cancellationToken);

        foreach (var f in fluxAssocies)
        {
            f.BankCode = nouveauCodeBanque;
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
        // Sauvegarde globale de toutes les entités traquées et modifiées
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