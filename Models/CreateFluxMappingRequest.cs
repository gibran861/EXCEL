namespace AfbGenerator.Api.Models;

public class CreateFluxMappingRequest
{
    public string Flux { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty;
}

public class UpdateFluxMappingRequest
    {
        public string Flux { get; set; } = string.Empty;
        public string Keyword { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }