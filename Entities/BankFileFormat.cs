namespace AfbGenerator.Api.Entities
{
    public class BankFileFormat
    {
        public int Id { get; set; }

        // Nom lisible (ex: "UBA NEW AFCM", "ECOBANK CM")
        public string Name { get; set; } = string.Empty;

        // Code banque associé (peut être null = format générique)
        public string? BankCode { get; set; }
        public string? InitialBalanceColumnName { get; set; }
        // Séparateur de colonnes (";", ",", "\t")
        public string? StartDateColumnName { get; set; }
        public string? EndDateColumnName { get; set; }
        public string Separator { get; set; } = ";";

        // Nombre de lignes à sauter avant la ligne d'entête
        public int SkipRowsBeforeHeader { get; set; } = 0;

        // Ligne d'entête servant à identifier ce format
        // (on cherche cette chaîne dans le fichier)
        public string HeaderPattern { get; set; } = string.Empty;

        // JSON : mapping logique → nom de colonne dans le fichier source
        // Ex: {"DateOp":"Transaction Date","DateVal":"Value Date",
        //      "Libelle":"Transaction Remarks","Montant":"Amount",
        //      "Sens":"Credit(Cr)/Debit(Dr)","Devise":null}
        public string ColumnMappingsJson { get; set; } = "{}";

        // "SIGNED"      : colonne Montant contient un nombre signé (+/-)
        // "CR_DR"       : colonne Sens contient "Cr"/"Dr" (ou "C"/"D")
        // "TWO_COLUMNS" : deux colonnes séparées Débit / Crédit
        public string AmountMode { get; set; } = "SIGNED";

        // Si AmountMode = "TWO_COLUMNS"
        public string? DebitColumnName { get; set; }
        public string? CreditColumnName { get; set; }

        // Valeur textuelle pour "Crédit" dans la colonne Sens (ex: "Cr", "C", "CR")
        public string? CreditMarker { get; set; } = "Cr";

        // Compte courant : colonne ou valeur fixe contenant le numéro de compte
        // null => on cherche "Compte courant" comme avant
        public string? AccountNumberColumnName { get; set; }

        // Devise fixe si non présente dans le fichier (ex: "XOF")
        public string? DefaultCurrency { get; set; }

        public bool IsActive { get; set; } = true;
    }
}