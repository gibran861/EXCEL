namespace AfbGenerator.Api.Entities;

public class FluxMapping
{
    public int Id { get; set; }
    public string Flux { get; set; } = string.Empty;      // Ex: "DEP1"
    public string Keyword { get; set; } = string.Empty;   // Ex: "CHQ"
    public bool IsActive { get; set; } = true;
    // NOUVELLES COLONNES
    public string? Operator { get; set; } = "ANY"; // "ANY", "<=", ">=", "=="
    public decimal? TargetAmount { get; set; } = 0;
    public string? BankCode { get; set; }
}