namespace AfbGenerator.Api.Models;

public class CreateFluxRequest
{
    public string FluxCode { get; set; } = string.Empty;
    public string FluxLabel { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string Cib1 { get; set; } = string.Empty;
    public string Cib2 { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class UpdateFluxCibRequest
{
    public string Cib1 { get; set; } = string.Empty;
    public string Cib2 { get; set; } = string.Empty;
}

public class FluxResponse
{
    public int Id { get; set; }
    public string FluxCode { get; set; } = string.Empty;
    public string FluxLabel { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string Cib1 { get; set; } = string.Empty;
    public string Cib2 { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}