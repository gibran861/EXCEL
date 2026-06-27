namespace AfbGenerator.Api.Models
{
    // Noms logiques utilisés dans tout le service de génération
    public class ColumnMapping
    {
        public string? DateOp      { get; set; }  // colonne date opération
        public string? DateVal     { get; set; }  // colonne date valeur
        public string? Libelle     { get; set; }  // colonne libellé
        public string? Montant     { get; set; }  // colonne montant (mode SIGNED)
        public string? Sens        { get; set; }  // colonne Cr/Dr (mode CR_DR)
        public string? Devise      { get; set; }  // colonne devise (nullable)
        public string? Reference   { get; set; }  // colonne référence (nullable)
        public string? AccountNumber { get; set; } // colonne n° de compte (nullable)
    }
}