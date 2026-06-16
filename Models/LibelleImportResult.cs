namespace AfbGenerator.Api.Models;

public class LibelleImportResult
{
    public int? FluxId { get; set; }
    public int RowsRead { get; set; }
    public int DistinctLibellesFound { get; set; }
    public int InsertedCount { get; set; }
    public int SkippedExistingCount { get; set; }
}
