using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using ExcelDataReader;

namespace AfbGenerator.Api.Services;

public interface IImportService
{
    List<List<string>> ReadFile(IFormFile file);

    Task<ImportPreviewResult> PreviewBanqueAsync(IFormFile file, CancellationToken ct);
 int FindColumnIndex(List<string> headerRow, string columnName);
}