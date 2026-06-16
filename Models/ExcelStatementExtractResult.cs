namespace AfbGenerator.Api.Models;

public class ExcelStatementExtractResult
{
    public int? FluxId { get; set; }
    public string CompteCourant { get; set; } = string.Empty;
    public string MontantInitial { get; set; } = string.Empty;
    public string MontantFinal { get; set; } = string.Empty;
    public string DateDebut { get; set; } = string.Empty;
    public string DateFin { get; set; } = string.Empty;
    public int TransactionsCount { get; set; }
    public int InsertedCount { get; set; }
    public int SkippedExistingCount { get; set; }
    public List<ExcelTransactionItem> Transactions { get; set; } = new();
    public List<string> DistinctLibelles { get; set; } = new();
}

public class ExcelTransactionItem
{
    public string DateOperation { get; set; } = string.Empty;
    public string Montant { get; set; } = string.Empty;
    public string Devise { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string InfoComplementaire { get; set; } = string.Empty;
    public string DateValeur { get; set; } = string.Empty;
}
