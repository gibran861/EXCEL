using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;
using  AfbGenerator.Api.Data;
using ExcelDataReader;
using  AfbGenerator.Api.Entities;
using Microsoft.EntityFrameworkCore;
namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FluxController : ControllerBase
{
	private readonly FluxService _fluxService;
    private readonly LibelleService _libelleService;
    private readonly AppDbContext _dbContext;
	public FluxController(FluxService fluxService,LibelleService libelleService,AppDbContext dbContext)
	{
		_fluxService = fluxService;
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

	[HttpGet]
	[ProducesResponseType(typeof(List<FluxResponse>), StatusCodes.Status200OK)]
	public async Task<ActionResult<List<FluxResponse>>> GetAll(CancellationToken cancellationToken)
	{
		var fluxes = await _fluxService.GetAllAsync(cancellationToken);
		return Ok(fluxes);
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
    public async Task<ActionResult<FluxImportResult>> ImportExcel(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Le fichier Excel est requis." });
        }

        try
        {
            // 1. Lecture de toutes les lignes du fichier Excel via ExcelDataReader
            var rows = ReadWorksheetRows(file);
            if (rows == null || rows.Count == 0)
            {
                return BadRequest(new { message = "Le fichier Excel ne contient aucune donnée." });
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
                
                // CIB1 et CIB2 sont optionnels dans l'extraction (on remplace par vide si absents)
                var cib1 = cib1Col >= 0 ? GetCell(row, cib1Col).Trim() : string.Empty;
                var cib2 = cib2Col >= 0 ? GetCell(row, cib2Col).Trim() : string.Empty;

                // Ignorer la ligne si elle est complètement blanche
                if (string.IsNullOrWhiteSpace(fluxCode) && string.IsNullOrWhiteSpace(fluxLabel))
                {
                    continue;
                }

                // Validation des clés obligatoires
                if (string.IsNullOrWhiteSpace(fluxCode) || string.IsNullOrWhiteSpace(bankCode))
                {
                    continue;
                }

                excelFluxItems.Add(new Flux
                {
                    FluxCode = fluxCode,
                    FluxLabel = fluxLabel,
                    BankCode = bankCode,
                    Cib1 = string.IsNullOrWhiteSpace(cib1) ? "  " : cib1,   // Espace par défaut AFB
                    Cib2 = string.IsNullOrWhiteSpace(cib2) ? "    " : cib2  // Espace par défaut AFB
                });
            }

            // Nettoyage des doublons stricts présents au sein du fichier Excel lui-même (clé : FluxCode + BankCode)
            var uniqueExcelItems = excelFluxItems
                .GroupBy(f => $"{f.FluxCode}_{f.BankCode}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // 4. Récupération des Flux existants en BDD pour filtrage
            var existingFlux = await _dbContext.Fluxes
                .Select(f => $"{f.FluxCode.Trim().ToUpperInvariant()}_{f.BankCode.Trim().ToUpperInvariant()}")
                .ToListAsync(cancellationToken);

            var existingSet = new HashSet<string>(existingFlux, StringComparer.OrdinalIgnoreCase);
            var toInsert = new List<Flux>();
            var skippedExistingCount = 0;

            // 5. Comparaison pour isoler les nouvelles règles
            foreach (var item in uniqueExcelItems)
            {
                var key = $"{item.FluxCode}_{item.BankCode}";
                
                if (existingSet.Contains(key))
                {
                    skippedExistingCount++;
                    continue;
                }

                toInsert.Add(item);
            }

            // 6. Insertion groupée en base de données
            int insertedCount = 0;
            if (toInsert.Count > 0)
            {
await _dbContext.Fluxes.AddRangeAsync(toInsert, cancellationToken);                await _dbContext.SaveChangesAsync(cancellationToken);
                insertedCount = toInsert.Count;
            }

            // 7. Retour du rapport
            return Ok(new FluxImportResult
            {
                TotalRowsProcessed = excelFluxItems.Count,
                InsertedCount = insertedCount,
                SkippedExistingCount = skippedExistingCount + (excelFluxItems.Count - uniqueExcelItems.Count)
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Erreur lors de l'importation : {ex.Message}" });
        }
    }

    // ── MÉTHODES UTILITAIRES EXCEL DATA READER ───────────────────────────────────────

    private List<List<string>> ReadWorksheetRows(IFormFile file)
    {
        var rows = new List<List<string>>();
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        using var stream = file.OpenReadStream();
        using var reader = ExcelReaderFactory.CreateReader(stream);

        while (reader.Read())
        {
            var row = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row.Add(reader.GetValue(i)?.ToString() ?? string.Empty);
            }
            rows.Add(row);
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
