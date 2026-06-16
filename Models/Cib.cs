namespace AfbGenerator.Api.Models;

public class CreateCibRequest
{
    public string Code { get; set; } = string.Empty;
    public string TypeOperation { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class CibResponse
{
    public string Code { get; set; } = string.Empty;
    public string TypeOperation { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}