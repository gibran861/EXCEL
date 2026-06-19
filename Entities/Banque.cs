using System.ComponentModel.DataAnnotations;

namespace AfbGenerator.Api.Entities;

public class Banque
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string CodeBanque { get; set; } = string.Empty; // ex: "AFB"

    
    [Required]
[StringLength(250)]
public string Libelle { get; set; } = string.Empty; // <-- Ajoutez cette ligne


    [StringLength(100)]
    public string? Filiale { get; set; } = string.Empty; // ex: "MALI"


    [StringLength(100)]
    public string? TypeFichier { get; set; } = string.Empty; // ex: "EXCEL_STD" ou "MT940"
    [StringLength(50)]
    public string? Compte { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}