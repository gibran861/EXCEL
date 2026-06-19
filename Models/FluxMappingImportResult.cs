namespace AfbGenerator.Api.Models;


public class FluxMappingImportResult
{
    public int TotalRows { get; set; }
    public int ImportedCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
}