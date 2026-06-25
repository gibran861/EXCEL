using System.ComponentModel.DataAnnotations;

namespace AfbGenerator.Api.Entities;

public class Cib
{
    [Key]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty; // ex: "CIB_001"

    [Required]
    [StringLength(100)]
    public string TypeOperation { get; set; } = string.Empty; // ex: "RETRAIT"

    [Required]
    [StringLength(100)]
    public string Type { get; set; } = string.Empty; // ex: "VIREMENT"

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}