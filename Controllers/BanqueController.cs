using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExcelDataReader; // 💡 AJOUTEZ CETTE LIGNE ICI
namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BanqueController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public BanqueController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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

    // 2. POST : Ajouter une nouvelle configuration de banque
    [HttpPost]
    [ProducesResponseType(typeof(Banque), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Banque>> Add([FromBody] Banque request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Création de l'entité à partir du modèle reçu
        // Création de l'entité à partir du modèle reçu
            var nouvelleBanque = new Banque
            {
                CodeBanque = request.CodeBanque.Trim().ToUpperInvariant(),
                Filiale = request.Filiale.Trim().ToUpperInvariant(),
                TypeFichier = request.TypeFichier?.Trim().ToUpperInvariant(),
                Libelle = request.Libelle.Trim(), // <-- IL MANQUAIT CETTE LIGNE !
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

        // Sauvegarde en base de données
        await _dbContext.Banques.AddAsync(nouvelleBanque, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Retourne un statut 201 Created avec l'objet contenant son nouvel ID
        return CreatedAtAction(nameof(GetAll), new { id = nouvelleBanque.Id }, nouvelleBanque);
    }

    // 3. POST : Importer des banques depuis un fichier Excel (Colonnes requises : CodeBanque, Libelle)
    [HttpPost("import-excel")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(BanqueImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BanqueImportResult>> ImportExcel(IFormFile file, CancellationToken cancellationToken)
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

            // 2. Recherche de la ligne d'en-tête (index 0 ou dynamique)
            var headerRow = rows[0];
            var codeBanqueCol = FindColumnIndex(headerRow, "CodeBanque");
            var libelleCol = FindColumnIndex(headerRow, "Libelle");

            // Fallbacks si les en-têtes ont des espaces ou des variations de casse
            if (codeBanqueCol < 0) codeBanqueCol = FindColumnIndex(headerRow, "Code Banque");
            if (libelleCol < 0) libelleCol = FindColumnIndex(headerRow, "Libellé");

            if (codeBanqueCol < 0 || libelleCol < 0)
            {
                return BadRequest(new { message = "Une ou plusieurs colonnes requises (CodeBanque, Libelle) sont manquantes dans les en-têtes." });
            }

            // 3. Extraction et normalisation des lignes de données
            var excelBanqueItems = new List<Banque>();

            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var codeBanque = GetCell(row, codeBanqueCol).Trim().ToUpperInvariant();
                var libelle = GetCell(row, libelleCol).Trim();

                // Ignorer la ligne si elle est totalement vide
                if (string.IsNullOrWhiteSpace(codeBanque) && string.IsNullOrWhiteSpace(libelle))
                {
                    continue;
                }

                // Clé métier obligatoire
                if (string.IsNullOrWhiteSpace(codeBanque))
                {
                    continue;
                }

                excelBanqueItems.Add(new Banque
                {
                    CodeBanque = codeBanque,
                    Libelle = libelle,
                    Filiale = "STANDARD",  // Valeurs par défaut pour les colonnes non présentes
                    TypeFichier = "AFB120", // Valeurs par défaut adaptées à votre contexte
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Nettoyage des doublons à l'intérieur du fichier Excel lui-même
            var uniqueExcelItems = excelBanqueItems
                .GroupBy(b => b.CodeBanque, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // 4. Récupération des codes banques existants en BDD pour filtrer
            var existingCodes = await _dbContext.Banques
                .Select(b => b.CodeBanque)
                .ToListAsync(cancellationToken);

            var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
            var toInsert = new List<Banque>();
            var skippedExistingCount = 0;

            // 5. Comparaison et isolation des nouveautés
            foreach (var item in uniqueExcelItems)
            {
                if (existingSet.Contains(item.CodeBanque))
                {
                    skippedExistingCount++;
                    continue;
                }

                toInsert.Add(item);
            }

            // 6. Insertion en Base de Données
            int insertedCount = 0;
            if (toInsert.Count > 0)
            {
                await _dbContext.Banques.AddRangeAsync(toInsert, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                insertedCount = toInsert.Count;
            }

            // 7. Renvoi du rapport d'intégration
            return Ok(new BanqueImportResult
            {
                TotalRowsProcessed = excelBanqueItems.Count,
                InsertedCount = insertedCount,
                SkippedExistingCount = skippedExistingCount + (excelBanqueItems.Count - uniqueExcelItems.Count)
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Erreur lors de l'import : {ex.Message}" });
        }
    }

    // --- EN-BAS : RECOPIE DES MÉTHODES UTILITAIRES EXCELDATAREADER ---

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