using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AfbGenerator.Api.Entities;

[Table("Devises")] // Mappe explicitement le nom de la table SQL
public class Devise
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "Le code de la devise est obligatoire.")]
    [StringLength(10)]
    public string Code { get; set; } = string.Empty; // ex: "XOF", "EUR", "USD"

    [Required(ErrorMessage = "Le libellé de la devise est obligatoire.")]
    [StringLength(100)]
    public string Libelle { get; set; } = string.Empty; // ex: "Franc CFA (BCEAO)", "Euro"

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}