namespace AfbGenerator.Api.Models;

public class DetectCategorieResult
{
    public string Libelle { get; set; } = "";
    public bool IsDetected { get; set; }
    public string? Categorie { get; set; }
    public string? Flux { get; set; }
    public string? MotCleDetecte { get; set; }
}