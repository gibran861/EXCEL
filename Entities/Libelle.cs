namespace AfbGenerator.Api.Entities;

public class Libelle
{
    public int Id { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public int? FluxId { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public Flux? Flux { get; set; }
}
