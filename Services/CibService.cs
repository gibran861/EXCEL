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

namespace AfbGenerator.Api.Services;

public class CibService
{
    private readonly AppDbContext _dbContext;

    public CibService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CibResponse> CreateAsync(CreateCibRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("Le code est obligatoire.");

        if (string.IsNullOrWhiteSpace(request.TypeOperation))
            throw new ArgumentException("Le type d'opération est obligatoire.");

        if (string.IsNullOrWhiteSpace(request.Type))
            throw new ArgumentException("Le type est obligatoire.");

        var normalizedCode = request.Code.Trim().ToUpperInvariant();

        var exists = await _dbContext.Cib.AnyAsync(x => x.Code == normalizedCode, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException($"La CIB avec le code '{normalizedCode}' existe déjà.");
        }

        var cib = new Cib
        {
            Code = normalizedCode,
            TypeOperation = request.TypeOperation.Trim(),
            Type = request.Type.Trim(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Cib.Add(cib);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(cib);
    }

    public async Task<List<CibResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Cib
            .OrderBy(x => x.Code)
            .Select(x => new CibResponse
            {
                Code = x.Code,
                TypeOperation = x.TypeOperation,
                Type = x.Type,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CibImportResult> ImportCibDataAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("Le fichier Excel est requis.");
        }

        // 1. Lecture de toutes les lignes du fichier Excel via ExcelDataReader
        var rows = ReadWorksheetRows(file);

        // 2. Recherche de la ligne d'en-tête spécifique à la table CIB
        var headerRowIndex = FindCibHeaderRowIndex(rows);
        if (headerRowIndex < 0)
        {
            throw new InvalidOperationException("La ligne d'en-tête avec les colonnes CIB n'a pas été trouvée.");
        }

        var headerRow = rows[headerRowIndex];
        var codeCol = FindColumnIndex(headerRow, "Code");
        var typeOperationCol = FindColumnIndex(headerRow, "Type d'opération");
        var typeCol = FindColumnIndex(headerRow, "Type");

        // Fallback si "Type d'opération" est écrit sans accent dans l'Excel
        if (typeOperationCol < 0)
        {
            typeOperationCol = FindColumnIndex(headerRow, "Type d'operation");
        }

        if (codeCol < 0 || typeOperationCol < 0 || typeCol < 0)
        {
            throw new InvalidOperationException("Une ou plusieurs colonnes requises (Code, Type d'opération, Type) sont manquantes.");
        }

        // 3. Extraction et normalisation des données du fichier Excel
        var excelCibItems = new List<Cib>();
        
        foreach (var row in rows.Skip(headerRowIndex + 1))
        {
            var code = GetCell(row, codeCol).Trim().ToUpperInvariant();
            var typeOperation = GetCell(row, typeOperationCol).Trim();
            var type = GetCell(row, typeCol).Trim();

            // Si la ligne est vide, on passe à la suivante
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(typeOperation) && string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            // Le Code étant la clé primaire, il ne doit pas être vide
            if (string.IsNullOrWhiteSpace(code))
            {
                continue; 
            }

            excelCibItems.Add(new Cib
            {
                Code = code,
                TypeOperation = typeOperation,
                Type = type,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Regroupement par Code pour nettoyer les doublons présents au sein du fichier Excel lui-même
        var uniqueExcelItems = excelCibItems
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // 4. Récupération des Codes existants en Base de Données pour filtrage
        var existingCodes = await _dbContext.Cib
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var toInsert = new List<Cib>();
        var skippedExistingCount = 0;

        // 5. Comparaison et filtrage
        foreach (var item in uniqueExcelItems)
        {
            if (existingSet.Contains(item.Code))
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
            await _dbContext.Cib.AddRangeAsync(toInsert, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            insertedCount = toInsert.Count;
        }

        // 7. Retour du résultat d'analyse et d'insertion
        return new CibImportResult
        {
            TotalRowsProcessed = excelCibItems.Count,
            InsertedCount = insertedCount,
            SkippedExistingCount = skippedExistingCount + (excelCibItems.Count - uniqueExcelItems.Count)
        };
    }

    // --- MÉTHODES UTILITAIRES CORRIGÉES ---

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

    public string GetCell(List<string> row, int columnIndex)
    {
        if (row == null || columnIndex < 0 || columnIndex >= row.Count)
        {
            return string.Empty;
        }
        return row[columnIndex] ?? string.Empty;
    }

    private static CibResponse Map(Cib cib)
    {
        return new CibResponse
        {
            Code = cib.Code,
            TypeOperation = cib.TypeOperation,
            Type = cib.Type,
            IsActive = cib.IsActive,
            CreatedAt = cib.CreatedAt
        };
    }
}