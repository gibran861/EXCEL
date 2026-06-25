using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
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
private List<List<string>> ReadCsv(IFormFile file)
{
    var rows = new List<List<string>>();

    using var reader = new StreamReader(file.OpenReadStream());

    while (!reader.EndOfStream)
    {
        var line = reader.ReadLine();

        if (string.IsNullOrWhiteSpace(line))
            continue;

        rows.Add(SplitSmart(line));
    }

    return rows;
}
private List<string> SplitSmart(string line)
{
    var result = new List<string>();
    var current = new StringBuilder();
    bool inQuotes = false;

    for (int i = 0; i < line.Length; i++)
    {
        char c = line[i];

        if (c == '"')
        {
            inQuotes = !inQuotes;
            continue;
        }

        // TAB = séparation principale (Excel export)
        if (c == '\t' && !inQuotes)
        {
            result.Add(current.ToString().Trim());
            current.Clear();
            continue;
        }

        // VIRGULE CSV mais attention aux nombres
        if (c == ',' && !inQuotes)
        {
            bool isDecimalOrThousand =
                i > 0 && i < line.Length - 1 &&
                char.IsDigit(line[i - 1]) &&
                char.IsDigit(line[i + 1]);

            if (!isDecimalOrThousand)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
        }

        current.Append(c);
    }

    result.Add(current.ToString().Trim());
    return result;
}
private List<string> SplitCsvLine(
    string line,
    char separator)
{
    var result = new List<string>();

    bool insideQuote = false;
    var current = new StringBuilder();


    foreach(char c in line)
    {
        if(c == '"')
        {
            insideQuote = !insideQuote;
            continue;
        }


        if(c == separator && !insideQuote)
        {
            result.Add(current.ToString().Trim());
            current.Clear();
        }
        else
        {
            current.Append(c);
        }
    }


    result.Add(current.ToString().Trim());


    return result;
}

private List<string> ParseCsvLine(string line)
{
    var result = new List<string>();

    bool insideQuote = false;
    var current = new StringBuilder();


    for(int i = 0; i < line.Length; i++)
    {
        char c = line[i];


        if(c == '"')
        {
            insideQuote = !insideQuote;
            continue;
        }


        // séparateur uniquement si on n'est pas dans une valeur
        if((c == '\t' || c == ';') && !insideQuote)
        {
            result.Add(current.ToString().Trim());
            current.Clear();
            continue;
        }


        // cas CSV séparé par virgule
        // mais on garde les nombres comme 18,000,000.00
        if(c == ',' && !insideQuote)
        {
            bool isNumberSeparator = 
                i > 0 &&
                i < line.Length - 1 &&
                char.IsDigit(line[i-1]) &&
                char.IsDigit(line[i+1]);


            if(isNumberSeparator)
            {
                current.Append(c);
                continue;
            }


            result.Add(current.ToString().Trim());
            current.Clear();
            continue;
        }


        current.Append(c);
    }


    result.Add(current.ToString().Trim());


    return result;
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