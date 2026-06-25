namespace AfbGenerator.Api.Models;


// public class FluxMappingImportResult
// {
//     public int TotalRows { get; set; }
//     public int ImportedCount { get; set; }
//     public int ErrorCount { get; set; }
//     public List<string> Errors { get; set; } = new();
// }
public class FluxMappingImportResult
{
    public int TotalRows { get; set; }
    public int ImportedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int ErrorCount { get; set; }

    public List<FluxMappingDuplicateItem> Duplicates { get; set; } = new();
}

public class FluxMappingDuplicateItem
{
    public string Code { get; set; } = string.Empty;   // Keyword + BankCode clé
    public string Libelle { get; set; } = string.Empty; // Flux
}