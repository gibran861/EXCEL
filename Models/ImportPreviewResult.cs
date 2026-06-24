namespace AfbGenerator.Api.Models;


public class ImportPreviewResult
{
    public List<object> NewRows { get; set; } = new();
    public List<object> ExistingRows { get; set; } = new();

    public int NewCount { get; set; }
    public int ExistingCount { get; set; }
}