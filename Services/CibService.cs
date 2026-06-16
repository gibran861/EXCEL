using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using ExcelDataReader;

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

        var exists = await _dbContext.Cibs.AnyAsync(x => x.Code == normalizedCode, cancellationToken);
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

        _dbContext.Cibs.Add(cib);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(cib);
    }

    public async Task<List<CibResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Cibs
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

    public async Task<(int inserted, int updated, List<string> errors)> ImportCibFromExcelAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        int insertedCount = 0;
        int updatedCount = 0;

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        using var stream = file.OpenReadStream();
        using var reader = ExcelReaderFactory.CreateReader(stream);

        int rowIndex = 0;
        int colCode = -1, colTypeOp = -1, colType = -1;

        while (reader.Read())
        {
            rowIndex++;

            // 1. Lecture du Header
            if (rowIndex == 1)
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var headerValue = reader.GetValue(i)?.ToString()?.Trim().ToUpperInvariant();
                    if (headerValue == "CODE") colCode = i;
                    else if (headerValue == "TYPE OPERATION" || headerValue == "TYPE D'OPERATION") colTypeOp = i;
                    else if (headerValue == "TYPE") colType = i;
                }

                if (colCode == -1 || colTypeOp == -1 || colType == -1)
                {
                    errors.Add("Le fichier Excel ne contient pas tous les headers requis : 'Code', 'Type Operation', 'Type'.");
                    return (0, 0, errors);
                }
                continue;
            }

            // 2. Extraction des données
            var code = reader.GetValue(colCode)?.ToString()?.Trim().ToUpperInvariant();
            var typeOperation = reader.GetValue(colTypeOp)?.ToString()?.Trim();
            var type = reader.GetValue(colType)?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(code)) continue; // Ignore les lignes vides

            // 3. Upsert (Mise à jour ou Insertion)
            var existingCib = await _dbContext.Cibs.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

            if (existingCib == null)
            {
                var newCib = new Cib
                {
                    Code = code,
                    TypeOperation = typeOperation ?? string.Empty,
                    Type = type ?? string.Empty,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _dbContext.Cibs.AddAsync(newCib, cancellationToken);
                insertedCount++;
            }
            else
            {
                existingCib.TypeOperation = typeOperation ?? string.Empty;
                existingCib.Type = type ?? string.Empty;
                updatedCount++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (insertedCount, updatedCount, errors);
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