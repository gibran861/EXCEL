namespace AfbGenerator.Api.Models;

public class XrtFlowDto
{
    public string FlowCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? FlowsctsId { get; set; }
    public string? Direction { get; set; }
}

public class XrtSyncResult
{
    public int TotalFlowsInXrt { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Messages { get; set; } = new();
}
