using Microsoft.AspNetCore.Http;

namespace AfbGenerator.Api.Models;

public class LibelleFluxMappingImportRequest
{
    public IFormFile File { get; set; } = null!;
}

public class LibelleFluxMappingImportResult
{
    public int RowsRead { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
}

public class ResolveFluxByLibelleRequest
{
    public string Libelle { get; set; } = string.Empty;
}

public class ResolveFluxByLibelleResult
{
    public string Libelle { get; set; } = string.Empty;
    public bool IsMatched { get; set; }
    public string? MatchedKeyword { get; set; }
    public int? FluxId { get; set; }
    public string? FluxCode { get; set; }
    public string? FluxLabel { get; set; }
}