using System.Collections.Generic;
namespace AfbGenerator.Api.Models;

public class ExtractedAccountStatement
{
    public string BankName { get; set; }
    public string DateDebut { get; set; }
    public string DateFin { get; set; }
    public string NumCompte { get; set; }
    public string SoldeInitial { get; set; }
    public List<TransactionLine> Transactions { get; set; } = new List<TransactionLine>();
}

public class TransactionLine
{
    public string DateOp { get; set; }
    public string Libelle { get; set; }
    public string Montant { get; set; }
}