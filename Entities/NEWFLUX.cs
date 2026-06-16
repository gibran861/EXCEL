using System.ComponentModel.DataAnnotations;

namespace AfbGenerator.Api.Entities;

public class NEWFlux
{
    [Key]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty; // ex: "CHQ_EMIS"

    [Required]
    [StringLength(100)]
    public string TypeFlux { get; set; } = string.Empty; // ex: "CHEQUE"

    [Required]
    [StringLength(250)]
    public string Libelle { get; set; } = string.Empty; // ex: "CHEQUE EMIS"

    [Required]
    [StringLength(10)]
    public string Sens { get; set; } = string.Empty; // "ENC" ou "DECA"

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}