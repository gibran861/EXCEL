namespace AfbGenerator.Api.Models;

public class CreateFluxMappingRequest
{
    public string Flux { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty;
    public string? Operator { get; set; } = "ANY";
    public decimal? TargetAmount { get; set; } = 0;
    public string? BankCode { get; set; }
}

public class UpdateFluxMappingRequest
    {
        public string Flux { get; set; } = string.Empty;
        public string Keyword { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? Operator { get; set; } = "ANY";
        public decimal? TargetAmount { get; set; } = 0;
        public string? BankCode { get; set; }
    }