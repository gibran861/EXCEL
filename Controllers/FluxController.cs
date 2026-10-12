using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;
using  AfbGenerator.Api.Data;
using ExcelDataReader;
using  AfbGenerator.Api.Entities;
using Microsoft.EntityFrameworkCore;
using AfbGenerator.Api.Entities.Xrt;
namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FluxController : ControllerBase
{
	private readonly FluxService _fluxService;
    private readonly LibelleService _libelleService;
    private readonly AppDbContext _dbContext;
    private readonly XrtDbContext _XRTContext;
	public FluxController(FluxService fluxService, XrtDbContext xrtContext, LibelleService libelleService, AppDbContext dbContext)
	{
		_fluxService = fluxService;
		_XRTContext = xrtContext;
		_libelleService = libelleService;
        _dbContext = dbContext;
	}
[HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            // Appelle le service en lui passant simplement l'ID
            await _fluxService.DeleteAsync(id, cancellationToken);
            return NoContent(); // Renvoie un statut 204 (Succès, pas de contenu)
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
	[HttpPost]
	[ProducesResponseType(typeof(FluxResponse), StatusCodes.Status201Created)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<FluxResponse>> Create([FromBody] CreateFluxRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var created = await _fluxService.CreateAsync(request, cancellationToken);
			return CreatedAtAction(nameof(GetAll), new { id = created.Id }, created);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(new { message = ex.Message });
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(new { message = ex.Message });
		}
	}
[HttpGet("sync-cib")]
[ProducesResponseType(typeof(List<FluxResponse>), StatusCodes.Status200OK)]
public async Task<IActionResult> GetCibSynchro(CancellationToken cancellationToken)
{
    // 1. Charger tous les flux depuis NEWFlux (_dbContext.Flux) pour récupérer les Libelle
    var dictionnaireFluxLocaux = await _dbContext.Flux
        .ToDictionaryAsync(
            f => f.Code.Trim().ToUpperInvariant(), 
            f => f.Libelle, 
            cancellationToken
        );

    // 2. Charger les paires (BankCode + FluxCode) déjà existantes dans la table CIB (_dbContext.Fluxes)
    var combinaisonsCibExistantes = await _dbContext.Fluxes
        .Select(f => new { 
            BankCode = f.BankCode.Trim().ToUpperInvariant(), 
            FluxCode = f.FluxCode.Trim().ToUpperInvariant() 
        })
        .ToListAsync(cancellationToken);

    // 3. Charger les éléments depuis la vue XRT
    var vueXrt = await _XRTContext.FluxCibViews
        .Where(v => !string.IsNullOrEmpty(v.Flux))
        .ToListAsync(cancellationToken);

    // Helper pour récupérer le Code Banque le plus fiable disponible
    string ObtenirCodeBanque(FluxCibView item)
    {
        // Priorité 1 : La nouvelle colonne dédiée CODE_BANQUE_RIB
        if (!string.IsNullOrWhiteSpace(item.CodeBanqueRib))
            return item.CodeBanqueRib.Trim().ToUpperInvariant();

        // Priorité 2 : Extraction des 5 premiers caractères du RIB
        string ribClean = item.Rib?.Trim() ?? string.Empty;
        if (ribClean.Length >= 5)
            return ribClean.Substring(0, 5).ToUpperInvariant();

        // Priorité 3 : Fallback sur BanqueCode
        return item.BanqueCode?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    // 4. Filtrer les paires absentes
    var nouveauxXrt = vueXrt
        .Select(v => new
        {
            Source = v,
            BankCodeFinal = ObtenirCodeBanque(v),
            FluxCodeClean = v.Flux!.Trim().ToUpperInvariant()
        })
        .Where(x => !string.IsNullOrEmpty(x.BankCodeFinal) &&
                    !combinaisonsCibExistantes.Any(e => 
                        e.BankCode == x.BankCodeFinal && 
                        e.FluxCode == x.FluxCodeClean))
        .GroupBy(x => new { x.BankCodeFinal, x.FluxCodeClean })
        .Select(g => g.First())
        .ToList();

    var nouveauxACreer = new List<AfbGenerator.Api.Entities.Flux>();
    var fluxNonTrouves = new HashSet<string>();

    // 5. Parcourir et instancier les entités BDD
    foreach (var item in nouveauxXrt)
    {
        if (dictionnaireFluxLocaux.TryGetValue(item.FluxCodeClean, out var libelleExistant))
        {
            nouveauxACreer.Add(new AfbGenerator.Api.Entities.Flux
            {
                BankCode  = item.BankCodeFinal, // Utilise la valeur issue de CODE_BANQUE_RIB
                FluxCode  = item.FluxCodeClean,
                FluxLabel = libelleExistant,
                Cib1      = item.Source.Cib1?.Trim() ?? string.Empty,
                Cib2      = item.Source.Cib2?.Trim() ?? string.Empty,
                IsActive  = true,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            fluxNonTrouves.Add(item.FluxCodeClean);
        }
    }

    // 6. Sauvegarder les entités BDD
    if (nouveauxACreer.Any())
    {
        await _dbContext.Fluxes.AddRangeAsync(nouveauxACreer, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // 7. Charger et mapper la liste complète des CIB vers le DTO FluxResponse
    var listeComplete = await _dbContext.Fluxes
        .Select(f => new FluxResponse
        {
            Id        = f.Id,
            BankCode  = f.BankCode,
            FluxCode  = f.FluxCode,
            FluxLabel = f.FluxLabel,
            Cib1      = f.Cib1,
            Cib2      = f.Cib2,
            IsActive  = f.IsActive,
            CreatedAt = f.CreatedAt
        })
        .ToListAsync(cancellationToken);

    // 8. Retourner le résultat
    if (fluxNonTrouves.Any())
    {
        return Ok(new
        {
            Data = listeComplete,
            Warning = $"Certains flux de la vue XRT n'existent pas dans la table NEWFlux ({string.Join(", ", fluxNonTrouves)}). Veuillez d'abord exécuter la synchronisation des flux (/sync).",
            MissingFluxCodes = fluxNonTrouves
        });
    }

    return Ok(listeComplete);
}
	[HttpGet]
	[ProducesResponseType(typeof(List<FluxResponse>), StatusCodes.Status200OK)]
	public async Task<ActionResult<List<FluxResponse>>> GetAll(CancellationToken cancellationToken)
	{
		var fluxes = await _fluxService.GetAllAsync(cancellationToken);
		return Ok(fluxes);
	}

/// <summary>
    /// Récupère directement les données depuis la vue SQL Flux_Cib
    /// </summary>
    [HttpGet("xrt-cib")]
    [ProducesResponseType(typeof(List<FluxCibResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FluxCibResponse>>> GetFromView([FromQuery] string? codeBanque, CancellationToken cancellationToken)
    {
        IQueryable<FluxCibView> query = _XRTContext.FluxCibViews.AsNoTracking();

        // Filtre optionnel par Banque (champ BQ dans la vue)
        if (!string.IsNullOrWhiteSpace(codeBanque))
        {
            string cleanBank = codeBanque.Trim().ToLower();
            query = query.Where(v => v.BanqueCode != null && v.BanqueCode.ToLower() == cleanBank);
        }

        var viewData = await query.ToListAsync(cancellationToken);

        // Projection directe vers le DTO de réponse
        var response = viewData.Select(v => new FluxCibResponse
        {
            Code = v.Code,
            Description = v.Description,
            Flux = v.Flux,
            Cib1 = v.Cib1,
            Cib2 = v.Cib2,
            Acc = v.Acc,
            Rib = v.Rib,
            CodeBanqueRib = v.CodeBanqueRib,
            BanqueCode = v.BanqueCode
        }).ToList();

        return Ok(response);
    }
[HttpPut("bank/{bankCode}/flux/{fluxCode}/cib")]
[ProducesResponseType(typeof(FluxResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<FluxResponse>> SetCibByBankAndFlux(
    string bankCode, 
    string fluxCode, 
    [FromBody] UpdateFluxCibRequest request, 
    CancellationToken cancellationToken)
{
    try
    {
        // On passe les deux codes au service
        var updated = await _fluxService.SetCibByBankAndFluxAsync(bankCode, fluxCode, request.Cib1, request.Cib2, cancellationToken);
        return Ok(updated);
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        return NotFound(new { message = ex.Message });
    }
}


	[HttpPost("extract-flux-from-excel")]
[Consumes("multipart/form-data")]
public async Task<ActionResult<ExcelFluxExtractResult>> ExtractFluxFromExcel(
    [FromForm] FluxExtractRequest request, 
    CancellationToken cancellationToken)
{
    try
    {
        // On passe request.File au lieu de file directement
        var result = await _libelleService.ExtractAndSaveFluxFromExcelAsync(request.File, cancellationToken);
        return Ok(result);
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
}

[HttpPost("import-excel")]
[Consumes("multipart/form-data")]
[ProducesResponseType(typeof(FluxImportResult), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<FluxImportResult>> ImportExcel(
    IFormFile file, 
    [FromForm] string? codesAMettreAJour = null, // <- Ajout du paramètre venant du Front-End
    CancellationToken cancellationToken = default)
{
    if (file == null || file.Length == 0)
    {
        return BadRequest(new { message = "Le fichier Excel ou CSV est requis." });
    }

    try
    {
        // 1. Lecture de toutes les lignes du fichier (Excel ou CSV) via ExcelDataReader
        var rows = ReadWorksheetRows(file);
        if (rows == null || rows.Count == 0)
        {
            return BadRequest(new { message = "Le fichier ne contient aucune donnée." });
        }

        // Désérialiser la liste des codes que l'utilisateur a choisi de mettre à jour
        var codesToUpdate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(codesAMettreAJour))
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(codesAMettreAJour);
            if (parsed != null)
            {
                foreach (var c in parsed) codesToUpdate.Add(c.Trim());
            }
        }

        // 2. Recherche de la ligne d'en-tête (Ligne index 0)
        var headerRow = rows[0];
        var fluxCodeCol = FindColumnIndex(headerRow, "FluxCode");
        var fluxLabelCol = FindColumnIndex(headerRow, "FluxLabel");
        var bankCodeCol = FindColumnIndex(headerRow, "BankCode");
        var cib1Col = FindColumnIndex(headerRow, "Cib1");
        var cib2Col = FindColumnIndex(headerRow, "Cib2");

        // Fallbacks de sécurité si l'en-tête contient des variantes d'écriture
        if (fluxCodeCol < 0) fluxCodeCol = FindColumnIndex(headerRow, "Code Flux");
        if (fluxLabelCol < 0) fluxLabelCol = FindColumnIndex(headerRow, "Libellé Flux");
        if (bankCodeCol < 0) bankCodeCol = FindColumnIndex(headerRow, "Code Banque");

        if (fluxCodeCol < 0 || fluxLabelCol < 0 || bankCodeCol < 0)
        {
            return BadRequest(new { message = "Une ou plusieurs colonnes obligatoires (FluxCode, FluxLabel, BankCode) sont manquantes." });
        }

        // 3. Extraction et normalisation des lignes de données
        var excelFluxItems = new List<Flux>();

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var fluxCode = GetCell(row, fluxCodeCol).Trim().ToUpperInvariant();
            var fluxLabel = GetCell(row, fluxLabelCol).Trim();
            var bankCode = GetCell(row, bankCodeCol).Trim().ToUpperInvariant();
            
            var cib1 = cib1Col >= 0 ? GetCell(row, cib1Col).Trim() : string.Empty;
            var cib2 = cib2Col >= 0 ? GetCell(row, cib2Col).Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(fluxCode) && string.IsNullOrWhiteSpace(fluxLabel)) continue;
            if (string.IsNullOrWhiteSpace(fluxCode) || string.IsNullOrWhiteSpace(bankCode)) continue;

            excelFluxItems.Add(new Flux
            {
                FluxCode = fluxCode,
                FluxLabel = fluxLabel,
                BankCode = bankCode,
                Cib1 = string.IsNullOrWhiteSpace(cib1) ? "  " : cib1,
                Cib2 = string.IsNullOrWhiteSpace(cib2) ? "    " : cib2
            });
        }

        // Nettoyage des doublons stricts présents au sein du fichier lui-même (Clé unique composite)
        var uniqueExcelItems = excelFluxItems
            .GroupBy(f => $"{f.FluxCode}_{f.BankCode}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // 4. Récupération des entités existantes complètes pour pouvoir les modifier
        var distinctKeys = uniqueExcelItems.Select(f => $"{f.FluxCode}_{f.BankCode}").ToList();
        
        var existingFluxList = await _dbContext.Fluxes
            .ToListAsync(cancellationToken);

        // Map vers un dictionnaire indexé par la clé unique composite "FluxCode_BankCode"
        var existingDict = existingFluxList
            .ToDictionary(f => $"{f.FluxCode.Trim().ToUpperInvariant()}_{f.BankCode.Trim().ToUpperInvariant()}", StringComparer.OrdinalIgnoreCase);

        var toInsert = new List<Flux>();
        var duplicates = new List<object>(); // Liste renvoyée au Front-End pour arbitrage
        int updatedCount = 0;
        int skippedCount = 0;

        // 5. Comparaison et aiguillage (Insert / Update / Skip)
        foreach (var item in uniqueExcelItems)
        {
            var key = $"{item.FluxCode}_{item.BankCode}";
            
            if (existingDict.TryGetValue(key, out var existingEntity))
            {
                // Si l'utilisateur a explicitement demandé la mise à jour pour cette clé composite
                if (codesToUpdate.Contains(key))
                {
                    existingEntity.FluxLabel = item.FluxLabel;
                    existingEntity.Cib1 = item.Cib1;
                    existingEntity.Cib2 = item.Cib2;
                    updatedCount++;
                }
                else
                {
                    // Premier appel : On stocke l'information pour le Front-End
                    // Note: On utilise la clé composite comme "Code" pour que le front puisse ré-identifier la ligne
                    duplicates.Add(new { code = key, libelle = $"{item.FluxLabel} (Banque: {item.BankCode})" });
                    skippedCount++;
                }
                continue;
            }

            // Nouvel enregistrement
            toInsert.Add(item);
        }

        // 6. Sauvegarde en Base de données
        if (toInsert.Count > 0)
        {
            await _dbContext.Fluxes.AddRangeAsync(toInsert, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 7. Retour du rapport harmonisé
        return Ok(new
        {
            TotalRowsProcessed = excelFluxItems.Count,
            InsertedCount = toInsert.Count,
            UpdatedCount = updatedCount,
            SkippedExistingCount = skippedCount + (excelFluxItems.Count - uniqueExcelItems.Count),
            Duplicates = duplicates // <- Envoyé au Front
        });
    }
    catch (Exception ex)
    {
        return BadRequest(new { message = $"Erreur lors de l'importation : {ex.Message}" });
    }
}

// ── MÉTHODES UTILITAIRES MODIFIÉE POUR SUPPORTER LE CSV ───────────────────────────────────────

private List<List<string>> ReadWorksheetRows(IFormFile file)
{
    var rows = new List<List<string>>();
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    using var stream = file.OpenReadStream();
    IExcelDataReader reader;

    // Utilisation du lecteur approprié selon l'extension du fichier
    if (extension == ".csv")
    {
        reader = ExcelReaderFactory.CreateCsvReader(stream);
    }
    else
    {
        reader = ExcelReaderFactory.CreateReader(stream);
    }

    using (reader)
    {
        while (reader.Read())
        {
            var row = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row.Add(reader.GetValue(i)?.ToString() ?? string.Empty);
            }
            rows.Add(row);
        }
    }
    return rows;
}

    private int FindColumnIndex(List<string> headerRow, string columnName)
    {
        if (headerRow == null) return -1;
        for (int i = 0; i < headerRow.Count; i++)
        {
            if (headerRow[i] != null && headerRow[i].Trim().Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private string GetCell(List<string> row, int columnIndex)
    {
        if (row == null || columnIndex < 0 || columnIndex >= row.Count)
        {
            return string.Empty;
        }
        return row[columnIndex] ?? string.Empty;
    }
}
