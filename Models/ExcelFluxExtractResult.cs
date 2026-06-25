namespace AfbGenerator.Api.Models;


public class ExcelFluxExtractResult
{
    public string BankCode { get; set; } = string.Empty;
    public int InsertedFluxCount { get; set; }
    public int SkippedFluxCount { get; set; }
    public List<string> DetectedFluxCodes { get; set; } = new List<string>();
}

public class FluxExtractRequest
{
    public IFormFile File { get; set; } = null!;
}