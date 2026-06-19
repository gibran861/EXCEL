 using Microsoft.AspNetCore.Http;

namespace AfbGenerator.Api.Models;
 
 public class GenerateResultDto
    {
        public string Message { get; set; } = string.Empty;
        public int TotalMouvements { get; set; }
        public int NombreFichiers { get; set; }
        public List<string> Fichiers { get; set; } = new();
        public List<GenerateDetailDto> Detail { get; set; } = new();
    }

     public class GenerateDetailDto
    {
        public string? AccountId { get; set; }
        public string? Currency { get; set; }
        public int NbMouvements { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal SoldeOuverture { get; set; }
        public decimal SoldeFinal { get; set; }
        public string? Fichier { get; set; }
    }

public class AfbUploadRequest
{
    public IFormFile File { get; set; } = null!;
    public string? OutputPath { get; set; } // <-- AJOUT DU CHEMIN DESTINATION
}