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

public async Task<FluxMappingImportResult> ImportFluxMappingDataAsync(
    IFormFile file,
    List<string>? keysAMettreAJour = null,
    CancellationToken cancellationToken = default)
{
    if (file == null || file.Length == 0)
        throw new ArgumentException("Le fichier est requis.");

    // Log des clés reçues du Front-end
    Console.WriteLine($"[BACK] Nombre de clés reçues pour mise à jour : {keysAMettreAJour?.Count ?? 0}");
    if (keysAMettreAJour != null)
    {
        foreach (var k in keysAMettreAJour)
        {
            Console.WriteLine($"[BACK] Clé à mettre à jour demandée par le Front : '{k}'");
        }
    }

    var rows = ReadFile(file);
    if (rows == null || rows.Count == 0)
        throw new InvalidOperationException("Le fichier est vide.");

    var header = rows[0];
    int fluxCol = FindColumnIndex(header, "Flux");
    int keywordCol = FindColumnIndex(header, "Keyword");
    int bankCodeCol = FindColumnIndex(header, "BankCode");

    if (fluxCol < 0 || keywordCol < 0)
        throw new InvalidOperationException("Colonnes Flux ou Keyword manquantes.");

    var keysToUpdate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (keysAMettreAJour != null)
        foreach (var k in keysAMettreAJour)
            keysToUpdate.Add(k);

    var excelItems = new List<FluxMapping>();
    for (int i = 1; i < rows.Count; i++)
    {
        var row = rows[i];
        var flux = GetCell(row, fluxCol)?.Trim();
        var keyword = GetCell(row, keywordCol)?.Trim();
        var bankCode = bankCodeCol >= 0 ? GetCell(row, bankCodeCol)?.Trim() : null;

        if (string.IsNullOrWhiteSpace(flux) || string.IsNullOrWhiteSpace(keyword))
            continue;

        excelItems.Add(new FluxMapping
        {
            Flux = flux,
            Keyword = keyword,
            BankCode = string.IsNullOrWhiteSpace(bankCode) ? null : bankCode,
            IsActive = true,
            Operator = "ANY",
            TargetAmount = 0
        });
    }

    var existing = await _dbContext.FluxMappings.ToListAsync(cancellationToken);
    var dict = existing
        .GroupBy(x => $"{x.Flux}_{x.Keyword}_{x.BankCode}".ToUpperInvariant())
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    var toInsert = new List<FluxMapping>();
    var duplicates = new List<FluxMappingDuplicateItem>();
    int updated = 0;
    int skipped = 0;

    foreach (var item in excelItems)
    {
        var key = $"{item.Flux}_{item.Keyword}_{item.BankCode}".ToUpperInvariant();

        if (dict.TryGetValue(key, out var existingItem))
        {
            // Vérification si la clé générée correspond EXACTEMENT à une clé reçue
            bool containsKey = keysToUpdate.Contains(key);
            Console.WriteLine($"[BACK] Doublon détecté pour la clé : '{key}'. Présente dans keysToUpdate ? {containsKey}");

            if (containsKey)
            {
                Console.WriteLine($"[BACK] -> ACTION : MISE À JOUR de la clé '{key}'");
                existingItem.Flux = item.Flux;
                existingItem.Keyword = item.Keyword;
                existingItem.BankCode = item.BankCode;
                updated++;
            }
            else
            {
                Console.WriteLine($"[BACK] -> ACTION : IGNORER la clé '{key}'");
                duplicates.Add(new FluxMappingDuplicateItem
                {
                    Code = key,
                    Libelle = item.Flux
                });
                skipped++;
            }
            continue;
        }

        toInsert.Add(item);
    }

    if (toInsert.Any())
        await _dbContext.FluxMappings.AddRangeAsync(toInsert, cancellationToken);

    await _dbContext.SaveChangesAsync(cancellationToken);

    Console.WriteLine($"[BACK] Bilan final -> Insérés: {toInsert.Count}, Modifiés: {updated}, Ignorés: {skipped}");

    return new FluxMappingImportResult
    {
        TotalRows = rows.Count - 1,
        ImportedCount = toInsert.Count,
        UpdatedCount = updated,
        ErrorCount = skipped,
        Duplicates = duplicates
    };
}

private List<List<string>> ReadFile(IFormFile file)
{
    var ext = Path.GetExtension(file.FileName)
                  .ToLowerInvariant();


    if(ext == ".csv")
        return ReadCsv(file);


    if(ext == ".xlsx" || ext == ".xls")
        return ReadWorksheetRows(file);


    throw new Exception("Format non supporté");
}
private List<List<string>> ReadCsv(IFormFile file)
{
    var rows = new List<List<string>>();


    using var reader =
        new StreamReader(file.OpenReadStream());


    while(!reader.EndOfStream)
    {
        var line = reader.ReadLine();


        if(string.IsNullOrWhiteSpace(line))
            continue;


        char separator =
            line.Contains(";") ? ';' : ',';



        rows.Add(
            line.Split(separator)
            .Select(x=>x.Trim())
            .ToList()
        );
    }


    return rows;
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