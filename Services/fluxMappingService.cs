using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AfbGenerator.Api.Models;

namespace AfbGenerator.Api.Services;

public class fluxMappingService
{
     private readonly AppDbContext _dbContext;
      public fluxMappingService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

public async Task<FluxMappingImportResult> ImportFluxMappingDataAsync(IFormFile file, CancellationToken cancellationToken = default)
{
    if (file == null || file.Length == 0)
    {
        throw new ArgumentException("Le fichier Excel est requis.");
    }

    // 1. Lecture de toutes les lignes du fichier Excel via vos méthodes ExcelDataReader existantes
    var rows = ReadWorksheetRows(file);

    if (rows == null || rows.Count == 0)
    {
        throw new InvalidOperationException("Le fichier Excel ne contient aucune donnée.");
    }

    // On suppose ici que la première ligne (index 0) contient vos en-têtes : Flux, Keyword, BankCode
    var headerRow = rows[0];
    var fluxCol = FindColumnIndex(headerRow, "Flux");
    var keywordCol = FindColumnIndex(headerRow, "Keyword");
    var bankCodeCol = FindColumnIndex(headerRow, "BankCode");

    if (fluxCol < 0 || keywordCol < 0)
    {
        throw new InvalidOperationException("Une ou plusieurs colonnes requises ('Flux' ou 'Keyword') sont manquantes dans les en-têtes.");
    }

    var result = new FluxMappingImportResult { TotalRows = rows.Count - 1 };
    var excelItems = new List<FluxMapping>();

    // 2. Extraction et parcours des données (on saute la ligne d'en-tête index 0)
    for (int i = 1; i < rows.Count; i++)
    {
        var row = rows[i];
        int rowNumber = i + 1; // Utile pour cibler la ligne Excel réelle en cas d'erreur

        var flux = GetCell(row, fluxCol)?.Trim();
        var keyword = GetCell(row, keywordCol)?.Trim();
        
        // Le BankCode est optionnel, on gère l'absence de colonne ou la valeur vide
        var bankCode = bankCodeCol >= 0 ? GetCell(row, bankCodeCol)?.Trim() : null;

        // Sauter la ligne si elle est complètement vide
        if (string.IsNullOrWhiteSpace(flux) && string.IsNullOrWhiteSpace(keyword))
        {
            continue;
        }

        // Validation des champs obligatoires
        if (string.IsNullOrWhiteSpace(flux) || string.IsNullOrWhiteSpace(keyword))
        {
            result.ErrorCount++;
            result.Errors.Add($"Ligne {rowNumber} : Les colonnes 'Flux' et 'Keyword' ne peuvent pas être vides.");
            continue;
        }

        excelItems.Add(new FluxMapping
        {
            Flux = flux,
            Keyword = keyword,
            BankCode = string.IsNullOrWhiteSpace(bankCode) ? null : bankCode,
            IsActive = true,
            Operator = "ANY", // Valeur par défaut
            TargetAmount = 0   // Valeur par défaut
        });
    }

    // 3. Récupération des règles existantes en BDD pour éviter les doublons (Clé composite ou Keyword unique)
    var existingMappings = await _dbContext.FluxMappings
        .ToListAsync(cancellationToken);

    // On crée un Set de comparaison (ex: basé sur Keyword + BankCode)
    var existingSet = new HashSet<string>(
        existingMappings.Select(x => $"{x.Keyword?.ToUpperInvariant()}_{x.BankCode?.ToUpperInvariant()}"), 
        StringComparer.OrdinalIgnoreCase
    );

    var toInsert = new List<FluxMapping>();

    // 4. Filtrage
    foreach (var item in excelItems)
    {
        var key = $"{item.Keyword?.ToUpperInvariant()}_{item.BankCode?.ToUpperInvariant()}";
        
        if (existingSet.Contains(key))
        {
            result.ErrorCount++;
            result.Errors.Add($"Le mot-clé '{item.Keyword}' pour la banque '{item.BankCode ?? "TOUTES"}' existe déjà en base de données.");
            continue;
        }

        toInsert.Add(item);
        result.ImportedCount++;
    }

    // 5. Sauvegarde en Base de Données
    if (toInsert.Count > 0)
    {
        await _dbContext.FluxMappings.AddRangeAsync(toInsert, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    return result;
}
   public string GetCell(List<string> row, int columnIndex)
    {
        if (row == null || columnIndex < 0 || columnIndex >= row.Count)
        {
            return string.Empty;
        }
        return row[columnIndex] ?? string.Empty;
    }

    
    private int FindCibHeaderRowIndex(List<List<string>> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            
            bool hasCode = row.Any(cell => cell != null && cell.Trim().Equals("Code", StringComparison.OrdinalIgnoreCase));
            bool hasTypeOp = row.Any(cell => cell != null && (cell.Trim().Equals("Type d'opération", StringComparison.OrdinalIgnoreCase) || cell.Trim().Equals("Type d'operation", StringComparison.OrdinalIgnoreCase)));
            
            if (hasCode && hasTypeOp)
            {
                return i;
            }
        }
        return -1;
    }

    public List<List<string>> ReadWorksheetRows(IFormFile file)
    {
        var rows = new List<List<string>>();

        // Configuration d'ExcelDataReader pour supporter les encodages Windows
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

    public int FindColumnIndex(List<string> headerRow, string columnName)
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

}