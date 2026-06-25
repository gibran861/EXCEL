namespace AfbGenerator.Api.Entities.Xrt;

public class XrtFlow
{
    public string FlowCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? FlowsctsId { get; set; }
    public byte? Direction { get; set; }
}
