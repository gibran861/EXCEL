using System.Collections.Generic;

public class BankTemplate
{
    public int Id { get; set; }
    public string BankName { get; set; }
    public string FileExtension { get; set; } // ".csv" ou ".xlsx"
    public string CsvDelimiter { get; set; } // ";" ou ","
    
    // Relation un-à-plusieurs : la liste des champs mappés
    public List<TemplateField> Fields { get; set; } = new List<TemplateField>();
}

public class TemplateField
{
    public int Id { get; set; }
    public int BankTemplateId { get; set; }
    
    // Clé unique du champ requis pour l'AFB (ex: "PERIODE", "NUM_COMPTE", "TX_MONTANT")
    public string FieldKey { get; set; }
    public string FieldLabel { get; set; }
    public string CalculationMethod { get; set; } = "DIRECT";
    // Coordonnées de la cellule d'ANCRE (le texte de repère)
    public int AnchorRowIndex { get; set; }
    public int AnchorColumnIndex { get; set; }
    public string AnchorTextValue { get; set; } // Ex: "Période du" ou "Numéro de compte"
    
    // Coordonnées de la cellule CIBLE contenant la donnée réelle (Uniquement pour les en-têtes fixes)
    public int TargetRowIndex { get; set; }
    public int TargetColumnIndex { get; set; }

    // True = Ligne fixe / isolée, False = Colonne entière d'un tableau de transactions
    public bool IsHeaderField { get; set; }
}