namespace AfbGenerator.Api.Models;


public class BanqueImportResult
{
    public int TotalRowsProcessed { get; set; }
    public int InsertedCount { get; set; }
    public int SkippedExistingCount { get; set; }
}