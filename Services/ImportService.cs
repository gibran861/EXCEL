using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using ExcelDataReader;

namespace AfbGenerator.Api.Services;

public class ImportService : IImportService
{
    private readonly AppDbContext _dbContext;

    public ImportService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<List<string>> ReadFile(IFormFile file)
{
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

    if (extension == ".csv")
        return ReadCsv(file);

    if (extension == ".xlsx" || extension == ".xls")
        return ReadExcel(file);

    throw new Exception("Format non supporté");
}

public List<List<string>> ReadCsv(IFormFile file)
{
    var rows = new List<List<string>>();

    using var reader = new StreamReader(file.OpenReadStream());

    while (!reader.EndOfStream)
    {
        var line = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line)) continue;

        var separator = line.Contains(";") ? ';' : ',';

        rows.Add(line.Split(separator)
            .Select(x => x.Trim())
            .ToList());
    }

    return rows;
}

public List<List<string>> ReadExcel(IFormFile file)
{
    System.Text.Encoding.RegisterProvider(
        System.Text.CodePagesEncodingProvider.Instance);

    using var stream = file.OpenReadStream();
    using var reader = ExcelReaderFactory.CreateReader(stream);

    var rows = new List<List<string>>();

    while (reader.Read())
    {
        var row = new List<string>();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            row.Add(reader.GetValue(i)?.ToString() ?? "");
        }

        rows.Add(row);
    }

    return rows;
}

public async Task<ImportPreviewResult> PreviewBanqueAsync(
    IFormFile file,
    CancellationToken ct)
{
    var rows = ReadFile(file);

    var header = rows[0];

    int codeCol = FindColumnIndex(header, "CodeBanque");
int libCol  = FindColumnIndex(header, "Libelle");

    var existing = await _dbContext.Banques
        .ToListAsync(ct);

    var existingSet = existing
        .Select(x => x.CodeBanque)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var result = new ImportPreviewResult();

    for (int i = 1; i < rows.Count; i++)
    {
        var code = rows[i][codeCol].Trim();
        var lib = rows[i][libCol].Trim();

        if (existingSet.Contains(code))
        {
            result.ExistingRows.Add(new
            {
                Code = code,
                Old = existing.First(x => x.CodeBanque == code).Libelle,
                New = lib
            });
        }
        else
        {
            result.NewRows.Add(new { Code = code, Libelle = lib });
        }
    }

    result.NewCount = result.NewRows.Count;
    result.ExistingCount = result.ExistingRows.Count;

    return result;
} 
public List<List<string>> ReadCsvRows(IFormFile file)
{
    var rows = new List<List<string>>();


    using(var reader = new StreamReader(file.OpenReadStream()))
    {
        while(!reader.EndOfStream)
        {
            var line = reader.ReadLine();


            if(string.IsNullOrWhiteSpace(line))
                continue;


            // accepte CSV avec ; ou ,
            var separator =
                line.Contains(";") ? ';' : ',';


            var columns =
                line.Split(separator)
                .Select(x => x.Trim())
                .ToList();


            rows.Add(columns);
        }
    }


    return rows;
}
    public List<List<string>> ReadWorksheetRows(IFormFile file)
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
}