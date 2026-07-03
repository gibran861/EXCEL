using System.ComponentModel.DataAnnotations;
namespace AfbGenerator.Api.Models;


public class CreateDeviseDto
    {
        [Required(ErrorMessage = "Le code de la devise est obligatoire.")]
        [StringLength(10, ErrorMessage = "Le code ne doit pas dépasser 10 caractères.")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le libellé de la devise est obligatoire.")]
        [StringLength(100, ErrorMessage = "Le libellé ne doit pas dépasser 100 caractères.")]
        public string Libelle { get; set; } = string.Empty;
    }