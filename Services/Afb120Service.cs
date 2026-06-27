// using System;
// using System.IO;
// using System.Text;
// using System.Linq;
// using System.Collections.Generic;
// using System.Threading;
// using System.Threading.Tasks;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using Microsoft.AspNetCore.Http;
// using AfbGenerator.Api.Data;
// using AfbGenerator.Api.Entities;
// using AfbGenerator.Api.Models;

// namespace AfbGenerator.Api.Services
// {
//     public class Afb120MovementRow
//     {
//         public string? BankCode { get; set; }
//         public string? Guichet { get; set; }
//         public string? Rib2 { get; set; }
//         public DateTime? OperationDate { get; set; }
//         public DateTime? ValueDate { get; set; }
//         public decimal Amount { get; set; }
//         public string? Currency { get; set; }
//         public string? Sens { get; set; }
//         public string? Label { get; set; }
//         public string? Reference { get; set; }
//         public string? Cib1 { get; set; }
//         public string? Cib2 { get; set; }
//         public int DecimalsNumber { get; set; } = 2;
//     }

//     public class MissingMappingItem
//     {
//         public string OriginalLabel { get; set; } = string.Empty;
//         public decimal Amount { get; set; }
//         public string Reason { get; set; } = "Non configuré";
//         // Code banque utilisé pour la recherche CIB1/CIB2 (ex: "AFB", "BNA"…)
//         public string BankCode { get; set; } = string.Empty;
//     }

//     public class MissingMappingsException : Exception
//     {
//         public List<MissingMappingItem> MissingKeywords { get; }
//         public MissingMappingsException(List<MissingMappingItem> missingKeywords)
//             : base("Certains libellés partagent le même flux ou n'ont pas de mapping défini.")
//         {
//             MissingKeywords = missingKeywords;
//         }
//     }

//     public class Afb120Service
//     {
//         private readonly AppDbContext _context;
//         private readonly LibelleService _libelleService;
//         private readonly CurrencyService _currencyService;
//         private readonly ILogger<Afb120Service> _logger;

//         public Afb120Service(AppDbContext context, LibelleService libelleService, CurrencyService currencyService, ILogger<Afb120Service> logger)
//         {
//             _context = context;
//             _libelleService = libelleService;
//             _currencyService = currencyService;
//             _logger = logger;
//         }

//         public async Task<GenerateResultDto> GenerateFromExcelAsync(IFormFile file, string? outputPath = null, CancellationToken cancellationToken = default)
//         {
//             if (file == null || file.Length == 0)
//                 throw new ArgumentException("Le fichier Excel est obligatoire.");

//             var rows = _libelleService.ReadWorksheetRows(file);
//             var compteRowIndex = _libelleService.FindRowContaining(rows, "Compte courant");
//             if (compteRowIndex < 0) throw new InvalidOperationException("Ligne 'Compte courant' introuvable.");
//             var compteRow = rows[compteRowIndex];

//             string bankCode = _libelleService.GetCell(compteRow, 1).Trim().Replace(" ", "");

//             decimal.TryParse(_libelleService.GetCell(compteRow, 2).Trim(), out decimal initialBalance);
//             string rawFileStartDate = _libelleService.GetCell(compteRow, 4).Trim();
//             string rawFileEndDate   = _libelleService.GetCell(compteRow, 5).Trim();
//             DateTime fileStartDate  = DateTime.TryParse(rawFileStartDate, out var parsedStart) ? parsedStart : DateTime.Now;
//             DateTime fileEndDate    = DateTime.TryParse(rawFileEndDate,   out var parsedEnd)   ? parsedEnd   : DateTime.Now;

//             var headerRowIndex = _libelleService.FindHeaderRowIndex(rows);
//             if (headerRowIndex < 0) throw new InvalidOperationException("Entête des transactions introuvable.");
//             var headerRow        = rows[headerRowIndex];
//             var dateOperationCol = _libelleService.FindColumnIndex(headerRow, "DATE OPERATION");
//             var montantCol       = _libelleService.FindColumnIndex(headerRow, "MONTANT");
//             var deviseCol        = _libelleService.FindColumnIndex(headerRow, "DEVISE");
//             var libelleCol       = _libelleService.FindColumnIndex(headerRow, "LIBELLE");
//             var dateValeurCol    = _libelleService.FindColumnIndex(headerRow, "DATE VALEUR");

//             var banqueEntity = await _context.Banques
//                 .FirstOrDefaultAsync(b => b.Compte == bankCode && b.IsActive, cancellationToken);

//             if (banqueEntity == null)
//                 throw new InvalidOperationException($"Aucune banque active configurée avec le compte '{bankCode}' n'a été trouvée dans la base de données.");

//             // currentBankCode = code utilisé pour la recherche CIB1/CIB2 (ex: "AFB")
//             string currentBankCode = banqueEntity.CodeBanque;

//             var bankFluxConfig = await _context.Fluxes
//                 .Where(f => f.BankCode == currentBankCode)
//                 .ToDictionaryAsync(f => f.FluxCode.Trim().ToUpperInvariant(), f => f, cancellationToken);

//             var activeCurrencies    = await _currencyService.GetAllAsync(cancellationToken);
//             var currencyDecimalsMap = activeCurrencies.ToDictionary(
//                 c => c.CUR_ID.Trim().ToUpperInvariant(),
//                 c => (int)c.DECIMALSNUMBER);

//             string cleanBank    = bankCode.Length >= 5  ? bankCode[0..5]  : bankCode.PadRight(5);
//             string cleanGuichet = bankCode.Length >= 10 ? bankCode[5..10] : "00000";
//             string cleanRib     = bankCode.Length >= 11 ? bankCode[10..]  : "0";

//             var missingKeywords = new List<MissingMappingItem>();

//             // ──────────────────────────────────────────────────────────────────
//             // fluxOccurrenceCount : nombre de fois que chaque code flux apparaît.
//             //                       Si count > 1 → conflit → rejet.
//             // temporaryMovements  : liste provisoire pour le tri final.
//             // ──────────────────────────────────────────────────────────────────
//             var fluxOccurrenceCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
//             var temporaryMovements  = new List<(Afb120MovementRow Row, string DetectedFlux, string FullOriginalLabel)>();

//             foreach (var row in rows.Skip(headerRowIndex + 1))
//             {
//                 var originalLibelle = _libelleService.GetCell(row, libelleCol).Trim();
//                 var rawMontant      = _libelleService.GetCell(row, montantCol).Trim();
//                 var rawDateOp       = _libelleService.GetCell(row, dateOperationCol).Trim();
//                 var rawDateVal      = _libelleService.GetCell(row, dateValeurCol).Trim();
//                 var devise          = _libelleService.GetCell(row, deviseCol).Trim();

//                 if (string.IsNullOrWhiteSpace(originalLibelle) && string.IsNullOrWhiteSpace(rawMontant)) continue;

//                 decimal.TryParse(rawMontant, out decimal amount);
//                 string sens = amount >= 0 ? "C" : "D";
//                 amount = Math.Abs(amount);
//                 DateTime? opDate  = DateTime.TryParse(rawDateOp,  out var d1) ? d1 : (DateTime?)null;
//                 DateTime? valDate = DateTime.TryParse(rawDateVal, out var d2) ? d2 : (DateTime?)null;

//                 string cib1 = "  ";
//                 string cib2 = "    ";

//                 var normalizedLibelle = _libelleService.NormalizeKeyword(originalLibelle);
//                 var detection = await _libelleService.DetectCategorieAsync(normalizedLibelle, amount, currentBankCode, cancellationToken);

//                 if (detection != null && detection.IsDetected && !string.IsNullOrWhiteSpace(detection.Flux))
//                 {
//                     string cleanFluxCode = detection.Flux.Trim().ToUpperInvariant();

//                     // Incrémenter le compteur d'occurrences pour ce flux
//                     if (!fluxOccurrenceCount.ContainsKey(cleanFluxCode))
//                         fluxOccurrenceCount[cleanFluxCode] = 0;
//                     fluxOccurrenceCount[cleanFluxCode]++;

//                     if (bankFluxConfig.TryGetValue(cleanFluxCode, out var fluxSetup))
//                     {
//                         cib1 = fluxSetup.Cib1 ?? "  ";
//                         cib2 = fluxSetup.Cib2 ?? "    ";
//                     }

//                     string cleanDevise     = devise.Trim().ToUpperInvariant();
//                     int    dynamicDecimals = currencyDecimalsMap.TryGetValue(cleanDevise, out var dbDecimals) ? dbDecimals : 2;

//                     var movementRow = new Afb120MovementRow
//                     {
//                         BankCode       = cleanBank,
//                         Guichet        = cleanGuichet,
//                         Rib2           = cleanRib,
//                         OperationDate  = opDate,
//                         ValueDate      = valDate,
//                         Amount         = amount,
//                         Currency       = devise,
//                         Sens           = sens,
//                         Label          = originalLibelle.Length > 33 ? originalLibelle[..33] : originalLibelle,
//                         Cib1           = cib1,
//                         Cib2           = cib2,
//                         DecimalsNumber = dynamicDecimals
//                     };

//                     temporaryMovements.Add((movementRow, cleanFluxCode, originalLibelle));
//                 }
//                 else
//                 {
//                     if (!string.IsNullOrWhiteSpace(originalLibelle))
//                     {
//                         missingKeywords.Add(new MissingMappingItem
//                         {
//                             OriginalLabel = originalLibelle,
//                             Amount        = amount,
//                             BankCode      = currentBankCode,   // ← code banque CIB
//                             Reason        = "Aucun mapping flux trouvé pour ce libellé."
//                         });
//                     }
//                 }
//             }

//             // ──────────────────────────────────────────────────────────────────
//             // DEUXIÈME PASSE : tout flux apparu plus d'une fois → rejet
//             // ──────────────────────────────────────────────────────────────────
//             var movements = new List<Afb120MovementRow>();

//             foreach (var item in temporaryMovements)
//             {
//                 if (fluxOccurrenceCount.TryGetValue(item.DetectedFlux, out int count) && count > 1)
//                 {
//                     missingKeywords.Add(new MissingMappingItem
//                     {
//                         OriginalLabel = item.FullOriginalLabel,
//                         Amount        = item.Row.Amount,
//                         BankCode      = currentBankCode,       // ← code banque CIB
//                         Reason        = $"Conflit : Le flux '{item.DetectedFlux}' est attribué à {count} ligne(s) différente(s) dans le fichier."
//                     });
//                 }
//                 else
//                 {
//                     movements.Add(item.Row);
//                 }
//             }

//             // Bloquer si des erreurs ou conflits ont été détectés
//             if (missingKeywords.Any())
//             {
//                 throw new MissingMappingsException(missingKeywords.OrderBy(k => k.OriginalLabel).ToList());
//             }

//             if (!movements.Any())
//                 return new GenerateResultDto { Message = "Aucune transaction valide trouvée dans le fichier Excel.", TotalMouvements = 0 };

//             movements = movements
//                 .OrderBy(m => m.OperationDate ?? DateTime.MinValue)
//                 .ThenBy(m => m.Label)
//                 .ThenBy(m => m.Amount)
//                 .ToList();

//             string finalOutputDir = string.IsNullOrWhiteSpace(outputPath) ? @"C:\BankFiles\AFB120\" : outputPath;
//             Directory.CreateDirectory(finalOutputDir);

//             var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
//             var sb        = new StringBuilder();
//             var first     = movements.First();

//             string openSens = initialBalance >= 0 ? "C" : "D";
//             var line01 = BuildLine01(first.BankCode, first.Guichet, first.Rib2, first.Currency, first.DecimalsNumber, fileStartDate, Math.Abs(initialBalance), openSens);
//             AssertLength(line01, "01", bankCode);
//             sb.AppendLine(line01);

//             var seenLine04 = new HashSet<string>();
//             foreach (var row in movements)
//             {
//                 var line04 = BuildLine04(row);
//                 AssertLength(line04, "04", row.Label);
//                 if (seenLine04.Add(line04)) sb.AppendLine(line04);
//             }

//             var totalCredit    = movements.Where(r => r.Sens == "C").Sum(r => r.Amount);
//             var totalDebit     = movements.Where(r => r.Sens == "D").Sum(r => r.Amount);
//             var closingBalance = initialBalance + totalCredit - totalDebit;
//             string closingSens = closingBalance >= 0 ? "C" : "D";

//             var line07 = BuildLine07(first.BankCode, first.Guichet, first.Rib2, first.Currency, first.DecimalsNumber, fileEndDate, Math.Abs(closingBalance), closingSens);
//             AssertLength(line07, "07", bankCode);
//             sb.AppendLine(line07);

//             var fileName = $"AFB120_{bankCode}_{timestamp}.txt";
//             var filePath = Path.Combine(finalOutputDir, fileName);
//             await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

//             var result = new GenerateResultDto
//             {
//                 Message         = $"Génération AFB120 réussie dans {finalOutputDir}",
//                 NombreFichiers  = 1,
//                 TotalMouvements = seenLine04.Count
//             };
//             result.Fichiers.Add(filePath);
//             result.Detail.Add(new GenerateDetailDto
//             {
//                 AccountId      = bankCode,
//                 Currency       = first.Currency,
//                 NbMouvements   = seenLine04.Count,
//                 TotalCredit    = totalCredit,
//                 TotalDebit     = totalDebit,
//                 SoldeOuverture = initialBalance,
//                 SoldeFinal     = closingBalance,
//                 Fichier        = fileName
//             });

//             return result;
//         }

//         // ── Helpers ───────────────────────────────────────────────────────────

//         private static string FormatDecimals(int? decimals) => (decimals ?? 2).ToString();

//         private static string FormatAmountAfb120(decimal amount, string sens, int decimals = 2)
//         {
//             if (decimals < 0) decimals = 2;
//             decimal multiplier = (decimal)Math.Pow(10, decimals);
//             var scaled     = decimal.Round(Math.Abs(amount) * multiplier, 0, MidpointRounding.AwayFromZero);
//             long amountInt = (long)scaled;

//             string amountStr = amountInt.ToString().PadLeft(14, '0');
//             char last        = amountStr[^1];

//             char encoded = sens == "C"
//                 ? last switch { '0'=>'{','1'=>'A','2'=>'B','3'=>'C','4'=>'D','5'=>'E','6'=>'F','7'=>'G','8'=>'H','9'=>'I', _=>'{' }
//                 : last switch { '0'=>'}','1'=>'J','2'=>'K','3'=>'L','4'=>'M','5'=>'N','6'=>'O','7'=>'P','8'=>'Q','9'=>'R', _=>'}' };

//             return amountStr[..13] + encoded;
//         }

//         private static string FixedLength(string? value, int length, char pad = ' ')
//         {
//             if (string.IsNullOrEmpty(value)) return new string(pad, length);
//             if (value.Length >= length)      return value[..length];
//             return value.PadRight(length, pad);
//         }

//         private static string BuildLine01(string? bankCode, string? guichet, string? rib2, string? currency, int? decimals, DateTime date, decimal balance, string sens)
//             => "01" + FixedLength(bankCode, 5) + "    " + FixedLength(guichet, 5) + FixedLength(currency, 3) + FormatDecimals(decimals) + " " + FixedLength(rib2, 11, '0') + "  " + date.ToString("ddMMyy") + string.Empty.PadRight(50) + FormatAmountAfb120(balance, sens, decimals ?? 2) + string.Empty.PadRight(15) + "*";

//         private static string BuildLine07(string? bankCode, string? guichet, string? rib2, string? currency, int? decimals, DateTime date, decimal balance, string sens)
//             => "07" + FixedLength(bankCode, 5) + "    " + FixedLength(guichet, 5) + FixedLength(currency, 3) + FormatDecimals(decimals) + " " + FixedLength(rib2, 11, '0') + "  " + date.ToString("ddMMyy") + string.Empty.PadRight(50) + FormatAmountAfb120(balance, sens, decimals ?? 2) + string.Empty.PadRight(15) + "*";

//         private static string BuildLine04(Afb120MovementRow row)
//             => "04"
//              + FixedLength(row.BankCode, 5)
//              + FixedLength(row.Cib2, 4)
//              + FixedLength(row.Guichet, 5)
//              + FixedLength(row.Currency, 3) + FormatDecimals(row.DecimalsNumber)
//              + " "
//              + FixedLength(row.Rib2, 11, '0')
//              + FixedLength(row.Cib1, 2)
//              + (row.OperationDate?.ToString("ddMMyy") ?? "      ")
//              + "  "
//              + (row.ValueDate?.ToString("ddMMyy") ?? "      ")
//              + FixedLength(row.Label, 33)
//              + FixedLength(row.Reference, 7)
//              + "  "
//              + FormatAmountAfb120(row.Amount, row.Sens ?? "C", row.DecimalsNumber)
//              + string.Empty.PadRight(15)
//              + "*";

//         private void AssertLength(string line, string code, string? ctx)
//         {
//             if (line.Length != 120)
//                 throw new InvalidOperationException($"Ligne {code} invalide: longueur={line.Length} (attendu 120), contexte={ctx}");
//         }
//     }
// }

using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;

namespace AfbGenerator.Api.Services
{
    public class Afb120MovementRow
    {
        public string? BankCode { get; set; }
        public string? Guichet { get; set; }
        public string? Rib2 { get; set; }
        public DateTime? OperationDate { get; set; }
        public DateTime? ValueDate { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public string? Sens { get; set; }
        public string? Label { get; set; }
        public string? Reference { get; set; }
        public string? Cib1 { get; set; }
        public string? Cib2 { get; set; }
        public int DecimalsNumber { get; set; } = 2;
    }

    public class MissingMappingItem
    {
        public string OriginalLabel { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Reason { get; set; } = "Non configuré";
        public string BankCode { get; set; } = string.Empty;
    }

    public class MissingMappingsException : Exception
    {
        public List<MissingMappingItem> MissingKeywords { get; }
        public MissingMappingsException(List<MissingMappingItem> missingKeywords)
            : base("Certains libellés n'ont pas de mapping défini.")
        {
            MissingKeywords = missingKeywords;
        }
    }

    public class Afb120Service
    {
        private readonly AppDbContext _context;
        private readonly LibelleService _libelleService;
        private readonly CurrencyService _currencyService;
        private readonly ILogger<Afb120Service> _logger;
        private readonly BankFileFormatService _formatService;
        public Afb120Service(AppDbContext context, LibelleService libelleService, CurrencyService currencyService, ILogger<Afb120Service> logger ,BankFileFormatService formatService)
        {
            _context = context;
            _libelleService = libelleService;
            _currencyService = currencyService;
            _logger = logger;
             _formatService = formatService;
        }
public async Task<GenerateResultDto> GenerateFromFileAsync3(IFormFile file, string? outputPath = null, CancellationToken cancellationToken = default)
{
    if (file == null || file.Length == 0)
        throw new ArgumentException("Le fichier est obligatoire.");

    // ── 1. DÉTECTION DYNAMIQUE DU FORMAT BANCAIRE ───────────────────────────
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    List<string> rawLinesForDetection = new List<string>();

    if (extension is ".xlsx" or ".xls")
    {
        rawLinesForDetection = BuildRawLinesFromExcel(file);
    }
    else
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true);
        int lineCount = 0;
        while (!reader.EndOfStream && lineCount < 25) // Poussé à 25 pour couvrir les gros en-têtes
        {
            var line = await reader.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line))
            {
                var cleanedLine = line.Replace("\r", "").Replace("\n", "").Trim();
                rawLinesForDetection.Add(cleanedLine);
                lineCount++;
            }
        }
    }

    var matchedFormat = await _formatService.DetectFormatAsync(rawLinesForDetection, cancellationToken);
    if (matchedFormat == null)
        throw new InvalidOperationException("Impossible de générer le fichier : format de fichier bancaire inconnu.");

    var mapping = _formatService.DeserializeMapping(matchedFormat);

    // ── 2. LECTURE COMPLÈTE DES LIGNES EN TABLEAU DE CHAÎNES ────────────────
    List<List<string>> rows = extension switch
    {
        ".csv" => _libelleService.ReadCsvRows(file),
        ".xlsx" or ".xls" => _libelleService.ReadWorksheetRows(file),
        _ => throw new ArgumentException($"Format de fichier non supporté : '{extension}'.")
    };

    // Récupération immédiate de l'index de la ligne d'en-tête d'après la configuration
    var headerRowIndex = _libelleService.FindRowContaining(rows, matchedFormat.HeaderPattern);
    if (headerRowIndex < 0) headerRowIndex = _libelleService.FindHeaderRowIndex(rows); // Fallback
    if (headerRowIndex < 0) throw new InvalidOperationException("Entête des transactions introuvable.");
    
    var headerRow = rows[headerRowIndex];

    // ── 3. EXTRACTION ADAPTATIVE ET 100% GÉNÉRIQUE DES MÉTADONNÉES ──────────
    string bankCode = string.Empty;
    decimal initialBalance = 0;
    DateTime fileStartDate = DateTime.Now;
    DateTime fileEndDate = DateTime.Now;

    // Concaténation de toutes les lignes pour faciliter les recherches par Regex sur l'ensemble du fichier
    var flatRowsText = rows.Select(r => string.Join(" ", r)).ToList();

    // 3.1 Extraction du Numéro de Compte
    string accountIndicator = matchedFormat.AccountNumberColumnName ?? "Numéro de compte"; 
    int accountRowIdx = _libelleService.FindRowContaining(rows, accountIndicator);
    if (accountRowIdx >= 0)
    {
        var rawLine = string.Join(" ", rows[accountRowIdx]);
        // Extraction du premier bloc numérique long (RIB / Compte) ou nettoyage global si non trouvé
        var matchCompte = System.Text.RegularExpressions.Regex.Match(rawLine.Replace(" ", ""), @"\d{10,25}");
        bankCode = matchCompte.Success ? matchCompte.Value : System.Text.RegularExpressions.Regex.Replace(rawLine, @"[^\d]", "");
    }
    if (string.IsNullOrEmpty(bankCode)) 
        throw new InvalidOperationException($"Impossible d'extraire le numéro de compte avec l'indicateur '{accountIndicator}'.");

    // 3.2 Extraction des Dates (Période)
    string startIndicator = matchedFormat.StartDateColumnName ?? "Période";
    int dateRowIdx = _libelleService.FindRowContaining(rows, startIndicator);
    if (dateRowIdx >= 0)
    {
        var rawLine = string.Join(" ", rows[dateRowIdx]);
        // Regex adaptative qui attrape le format ISO (2026-05-01) ou FR (01/05/2026)
        var dateMatches = System.Text.RegularExpressions.Regex.Matches(rawLine, @"\d{4}-\d{2}-\d{2}|\d{2}/\d{2}/\d{4}");
        if (dateMatches.Count >= 2)
        {
            DateTime.TryParse(dateMatches[0].Value, out fileStartDate);
            DateTime.TryParse(dateMatches[1].Value, out fileEndDate);
        }
    }

    // 3.3 Extraction du Solde Initial
    string balanceIndicator = matchedFormat.InitialBalanceColumnName ?? "Solde initial";
    int balanceRowIdx = _libelleService.FindRowContaining(rows, balanceIndicator);
    if (balanceRowIdx >= 0)
    {
        var rawLine = string.Join(" ", rows[balanceRowIdx]);
        if (rawLine.Contains(":"))
        {
            var parts = rawLine.Split(':');
            if (parts.Length > 1) initialBalance = _libelleService.ParseFlexibleAmount(parts[1].Trim());
        }
        else
        {
            // Fallback : Si l'indicateur est sur la même ligne qu'un tableau (ex: BNDA), on cherche le premier montant de la ligne
            var matchAmount = System.Text.RegularExpressions.Regex.Match(rawLine, @"\d+[\s,.]?\d*");
            if (matchAmount.Success) initialBalance = _libelleService.ParseFlexibleAmount(matchAmount.Value);
        }
    }
    else
    {
        // Fallback BICICI : extraction depuis la première ligne de données dans la colonne "Solde"
        var soldeColIndex = _libelleService.FindColumnIndex(headerRow, "Solde");
        if (soldeColIndex >= 0 && rows.Count > (headerRowIndex + 1))
        {
            string rawSolde = _libelleService.GetCell(rows[headerRowIndex + 1], soldeColIndex).Trim();
            initialBalance = _libelleService.ParseFlexibleAmount(rawSolde);
        }
    }

    // ── 4. MAPPING DYNAMIQUE DES COLONNES DEPUIS LE JSON ─────────────────────
    var dateOperationCol = !string.IsNullOrEmpty(mapping.DateOp) ? _libelleService.FindColumnIndex(headerRow, mapping.DateOp) : -1;
    var montantCol       = !string.IsNullOrEmpty(mapping.Montant) ? _libelleService.FindColumnIndex(headerRow, mapping.Montant) : -1;
    var deviseCol        = !string.IsNullOrEmpty(mapping.Devise) ? _libelleService.FindColumnIndex(headerRow, mapping.Devise) : -1;
    var libelleCol       = !string.IsNullOrEmpty(mapping.Libelle) ? _libelleService.FindColumnIndex(headerRow, mapping.Libelle) : -1;
    var dateValeurCol    = !string.IsNullOrEmpty(mapping.DateVal) ? _libelleService.FindColumnIndex(headerRow, mapping.DateVal) : -1;

    if (dateOperationCol < 0 || montantCol < 0 || libelleCol < 0)
    {
        throw new InvalidOperationException("Une ou plusieurs colonnes requises (Configuration JSON vs Fichier) sont introuvables.");
    }

    // ── 5. TRAITEMENT DES LOGIQUES MÉTIER ───────────────────────────────────
    var banqueEntity = await _context.Banques
        .FirstOrDefaultAsync(b => b.Compte == bankCode && b.IsActive, cancellationToken);

    if (banqueEntity == null)
        throw new InvalidOperationException($"Aucune banque active configurée avec le compte '{bankCode}' n'a été trouvée.");

    string currentBankCode = banqueEntity.CodeBanque;

    var bankFluxConfig = await _context.Fluxes
        .Where(f => f.BankCode == currentBankCode)
        .ToDictionaryAsync(f => f.FluxCode.Trim().ToUpperInvariant(), f => f, cancellationToken);

    var activeCurrencies    = await _currencyService.GetAllAsync(cancellationToken);
    var currencyDecimalsMap = activeCurrencies.ToDictionary(
        c => c.CUR_ID.Trim().ToUpperInvariant(),
        c => (int)c.DECIMALSNUMBER);

    string cleanBank    = bankCode.Length >= 5  ? bankCode[0..5]  : bankCode.PadRight(5);
    string cleanGuichet = bankCode.Length >= 10 ? bankCode[5..10] : "00000";
    string cleanRib     = bankCode.Length >= 11 ? bankCode[10..]  : "0";

    var missingKeywords = new List<MissingMappingItem>();
    var movements = new List<Afb120MovementRow>();

    foreach (var row in rows.Skip(headerRowIndex + 1))
    {
        var originalLibelle = _libelleService.GetCell(row, libelleCol).Trim();
        var rawMontant      = _libelleService.GetCell(row, montantCol).Trim();
        var rawDateOp       = dateOperationCol >= 0 ? _libelleService.GetCell(row, dateOperationCol).Trim() : string.Empty;
        var rawDateVal      = dateValeurCol >= 0 ? _libelleService.GetCell(row, dateValeurCol).Trim() : string.Empty;
        
        // 🛡️ FILTRE GÉNÉRIQUE DE SÉCURITÉ CONTRE LES LIGNES DE FIN / TOTALISATIONS
        if (string.IsNullOrWhiteSpace(rawDateOp) || !DateTime.TryParse(rawDateOp, out _) ||
            originalLibelle.StartsWith("Total", StringComparison.OrdinalIgnoreCase) || 
            originalLibelle.Contains("Solde", StringComparison.OrdinalIgnoreCase))
        {
            continue; // On passe la ligne inutile ou résumé de fin de page
        }

        var devise = deviseCol >= 0 ? _libelleService.GetCell(row, deviseCol).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(devise)) devise = matchedFormat.DefaultCurrency ?? "XOF";

        if (string.IsNullOrWhiteSpace(originalLibelle) && string.IsNullOrWhiteSpace(rawMontant)) continue;

        decimal amount = _libelleService.ParseFlexibleAmount(rawMontant);

        string sens = amount >= 0 ? "C" : "D";
        amount = Math.Abs(amount);
        DateTime? opDate  = DateTime.TryParse(rawDateOp,  out var d1) ? d1 : (DateTime?)null;
        DateTime? valDate = DateTime.TryParse(rawDateVal, out var d2) ? d2 : (DateTime?)null;

        string cib1 = "  ";
        string cib2 = "    ";

        var normalizedLibelle = _libelleService.NormalizeKeyword(originalLibelle);
        var detection = await _libelleService.DetectCategorieAsync(normalizedLibelle, amount, currentBankCode, cancellationToken);

        if (detection != null && detection.IsDetected && !string.IsNullOrWhiteSpace(detection.Flux))
        {
            string cleanFluxCode = detection.Flux.Trim().ToUpperInvariant();

            if (bankFluxConfig.TryGetValue(cleanFluxCode, out var fluxSetup))
            {
                cib1 = fluxSetup.Cib1 ?? "  ";
                cib2 = fluxSetup.Cib2 ?? "    ";
            }

            string cleanDevise     = devise.Trim().ToUpperInvariant();
            int    dynamicDecimals = currencyDecimalsMap.TryGetValue(cleanDevise, out var dbDecimals) ? dbDecimals : 2;

            var movementRow = new Afb120MovementRow
            {
                BankCode       = cleanBank,
                Guichet        = cleanGuichet,
                Rib2           = cleanRib,
                OperationDate  = opDate,
                ValueDate      = valDate,
                Amount         = amount,
                Currency       = devise,
                Sens           = sens,
                Label          = originalLibelle.Length > 33 ? originalLibelle[..33] : originalLibelle,
                Cib1           = cib1,
                Cib2           = cib2,
                DecimalsNumber = dynamicDecimals
            };

            movements.Add(movementRow);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(originalLibelle))
            {
                missingKeywords.Add(new MissingMappingItem
                {
                    OriginalLabel = originalLibelle,
                    Amount        = amount,
                    BankCode      = currentBankCode,
                    Reason        = "Aucun mapping flux trouvé pour ce libellé."
                });
            }
        }
    }

    // ── 6. GÉNÉRATION DU FICHIER DE SORTIE ───────────────────────────────────
    if (missingKeywords.Any())
    {
        throw new MissingMappingsException(missingKeywords.OrderBy(k => k.OriginalLabel).ToList());
    }

    if (!movements.Any())
        return new GenerateResultDto { Message = "Aucune transaction valide trouvée.", TotalMouvements = 0 };

    movements = movements
        .OrderBy(m => m.OperationDate ?? DateTime.MinValue)
        .ThenBy(m => m.Label)
        .ThenBy(m => m.Amount)
        .ToList();

    string finalOutputDir = string.IsNullOrWhiteSpace(outputPath) ? @"C:\BankFiles\AFB120\" : outputPath;
    Directory.CreateDirectory(finalOutputDir);

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var sb        = new StringBuilder();
    var first     = movements.First();

    string openSens = initialBalance >= 0 ? "C" : "D";
    var line01 = BuildLine01(first.BankCode, first.Guichet, first.Rib2, first.Currency, first.DecimalsNumber, fileStartDate, Math.Abs(initialBalance), openSens);
    AssertLength(line01, "01", bankCode);
    sb.AppendLine(line01);

    var line04Count = 0;
    foreach (var row in movements)
    {
        var line04 = BuildLine04(row);
        AssertLength(line04, "04", row.Label);
        sb.AppendLine(line04);
        line04Count++;
    }

    var totalCredit    = movements.Where(r => r.Sens == "C").Sum(r => r.Amount);
    var totalDebit     = movements.Where(r => r.Sens == "D").Sum(r => r.Amount);
    var closingBalance = initialBalance + totalCredit - totalDebit;
    string closingSens = closingBalance >= 0 ? "C" : "D";

    var line07 = BuildLine07(first.BankCode, first.Guichet, first.Rib2, first.Currency, first.DecimalsNumber, fileEndDate, Math.Abs(closingBalance), closingSens);
    AssertLength(line07, "07", bankCode);
    sb.AppendLine(line07);

    var fileName = $"AFB120_{bankCode}_{timestamp}.txt";
    var filePath = Path.Combine(finalOutputDir, fileName);
    await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

    var result = new GenerateResultDto
    {
        Message         = $"Génération AFB120 réussie dans {finalOutputDir}",
        NombreFichiers  = 1,
        TotalMouvements = line04Count
    };
    result.Fichiers.Add(filePath);
    result.Detail.Add(new GenerateDetailDto
    {
        AccountId      = bankCode,
        Currency       = first.Currency,
        NbMouvements   = line04Count,
        TotalCredit    = totalCredit,
        TotalDebit     = totalDebit,
        SoldeOuverture = initialBalance,
        SoldeFinal     = closingBalance,
        Fichier        = fileName
    });

    return result;
}
public async Task<GenerateResultDto> GenerateFromFileAsync(IFormFile file, string? outputPath = null, CancellationToken cancellationToken = default)
{
    if (file == null || file.Length == 0)
        throw new ArgumentException("Le fichier est obligatoire.");

    // ── 1. DÉTECTION DYNAMIQUE DU FORMAT BANCAIRE ───────────────────────────
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    List<string> rawLinesForDetection = new List<string>();

    if (extension is ".xlsx" or ".xls")
    {
        rawLinesForDetection = BuildRawLinesFromExcel(file);
    }
    else
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true);
        int lineCount = 0;
        while (!reader.EndOfStream && lineCount < 20)
        {
            var line = await reader.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line))
            {
                var cleanedLine = line.Replace("\r", "").Replace("\n", "").Trim();
                rawLinesForDetection.Add(cleanedLine);
                lineCount++;
            }
        }
    }

    var matchedFormat = await _formatService.DetectFormatAsync(rawLinesForDetection, cancellationToken);
    if (matchedFormat == null)
        throw new InvalidOperationException("Impossible de générer le fichier : format de fichier bancaire inconnu.");

    var mapping = _formatService.DeserializeMapping(matchedFormat);

    // ── 2. LECTURE COMPLÈTE DES LIGNES EN TABLEAU DE CHAÎNES ────────────────
    List<List<string>> rows;
    if (extension == ".csv")
    {
        rows = _libelleService.ReadCsvRows(file);
    }
    else if (extension is ".xlsx" or ".xls")
    {
        rows = _libelleService.ReadWorksheetRows(file);
    }
    else
    {
        throw new ArgumentException($"Format de fichier non supporté : '{extension}'.");
    }

    // Récupération immédiate de l'index de la ligne d'en-tête d'après la configuration
    var headerRowIndex = _libelleService.FindRowContaining(rows, matchedFormat.HeaderPattern);
    if (headerRowIndex < 0) headerRowIndex = _libelleService.FindHeaderRowIndex(rows); // Fallback
    if (headerRowIndex < 0) throw new InvalidOperationException("Entête des transactions introuvable.");
    
    var headerRow = rows[headerRowIndex];

    // ── 3. EXTRACTION ADAPTATIVE ET DYNAMIQUE DU BLOC EN-TÊTE ────────────────
    string bankCode = string.Empty;
    decimal initialBalance = 0;
    DateTime fileStartDate = DateTime.Now;
    DateTime fileEndDate = DateTime.Now;

    if (matchedFormat.BankCode == "BNDA")
    {
        var compteRowIndex = _libelleService.FindRowContaining(rows, "Compte courant");
        if (compteRowIndex < 0) throw new InvalidOperationException("Ligne 'Compte courant' introuvable pour la BNDA.");
        var compteRow = rows[compteRowIndex];

        bankCode = _libelleService.GetCell(compteRow, 1).Trim().Replace(" ", "");
        string rawInitialBalance = _libelleService.GetCell(compteRow, 2).Trim();
        initialBalance = _libelleService.ParseFlexibleAmount(rawInitialBalance);
        
        string rawFileStartDate = _libelleService.GetCell(compteRow, 4).Trim();
        string rawFileEndDate   = _libelleService.GetCell(compteRow, 5).Trim();
        
        fileStartDate = DateTime.TryParse(rawFileStartDate, out var parsedStart) ? parsedStart : DateTime.Now;
        fileEndDate   = DateTime.TryParse(rawFileEndDate,   out var parsedEnd)   ? parsedEnd   : DateTime.Now;
    }
    else if (matchedFormat.BankCode == "BICICI")
    {
        int compteRowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            string fullLineText = string.Join(" ", rows[i]).Trim();
            if (fullLineText.Contains("Compte", StringComparison.OrdinalIgnoreCase) || 
                fullLineText.Contains("Cpt", StringComparison.OrdinalIgnoreCase))
            {
                compteRowIndex = i;
                break;
            }
        }

        if (compteRowIndex < 0) 
            throw new InvalidOperationException("Ligne d'en-tête contenant les informations du 'Compte' introuvable pour la BICICI.");
        
        var rawCompteLine = string.Join(" ", rows[compteRowIndex]);
        var matchCompte = System.Text.RegularExpressions.Regex.Match(rawCompteLine, @"\d{5,}");
        bankCode = matchCompte.Success ? matchCompte.Value : System.Text.RegularExpressions.Regex.Match(rawCompteLine, @"\d+").Value;

        int periodeRowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            string fullLineText = string.Join(" ", rows[i]).Trim();
            if (fullLineText.Contains("Période", StringComparison.OrdinalIgnoreCase) || 
                fullLineText.Contains("Du", StringComparison.OrdinalIgnoreCase))
            {
                periodeRowIndex = i;
                break;
            }
        }

        if (periodeRowIndex >= 0)
        {
            var rawPeriodeLine = string.Join(" ", rows[periodeRowIndex]);
            var dateMatches = System.Text.RegularExpressions.Regex.Matches(rawPeriodeLine, @"\d{2}/\d{2}/\d{4}");
            if (dateMatches.Count >= 2)
            {
                DateTime.TryParse(dateMatches[0].Value, out fileStartDate);
                DateTime.TryParse(dateMatches[1].Value, out fileEndDate);
            }
        }

        var soldeColIndex = _libelleService.FindColumnIndex(headerRow, "Solde");
        if (soldeColIndex >= 0 && rows.Count > (headerRowIndex + 1))
        {
            var firstDataRow = rows[headerRowIndex + 1];
            string rawSolde = _libelleService.GetCell(firstDataRow, soldeColIndex).Trim();
            initialBalance = _libelleService.ParseFlexibleAmount(rawSolde);
        }
    }
    else if (matchedFormat.BankCode == "MANSA")
    {
        int compteRowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            string fullLineText = string.Join(" ", rows[i]).Trim();
            if (fullLineText.Contains("Numéro de compte", StringComparison.OrdinalIgnoreCase))
            {
                compteRowIndex = i;
                break;
            }
        }

        if (compteRowIndex < 0) 
            throw new InvalidOperationException("Ligne 'Numéro de compte' introuvable pour la banque MANSA.");
        
        var rawCompteLine = string.Join(" ", rows[compteRowIndex]);
        bankCode = System.Text.RegularExpressions.Regex.Replace(rawCompteLine, @"[^\d]", "");

        int periodeRowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            string fullLineText = string.Join(" ", rows[i]).Trim();
            if (fullLineText.Contains("Période du", StringComparison.OrdinalIgnoreCase))
            {
                periodeRowIndex = i;
                break;
            }
        }

        if (periodeRowIndex >= 0)
        {
            var rawPeriodeLine = string.Join(" ", rows[periodeRowIndex]);
            var dateMatches = System.Text.RegularExpressions.Regex.Matches(rawPeriodeLine, @"\d{4}-\d{2}-\d{2}");
            if (dateMatches.Count >= 2)
            {
                DateTime.TryParse(dateMatches[0].Value, out fileStartDate);
                DateTime.TryParse(dateMatches[1].Value, out fileEndDate);
            }
        }

        int soldeInitialRowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            string fullLineText = string.Join(" ", rows[i]).Trim();
            if (fullLineText.Contains("Solde initial", StringComparison.OrdinalIgnoreCase))
            {
                soldeInitialRowIndex = i;
                break;
            }
        }

        if (soldeInitialRowIndex >= 0)
        {
            var rawSoldeLine = string.Join(" ", rows[soldeInitialRowIndex]);
            var parts = rawSoldeLine.Split(':');
            if (parts.Length > 1)
            {
                initialBalance = _libelleService.ParseFlexibleAmount(parts[1].Trim());
            }
        }
    }
    else
    {
        throw new InvalidOperationException($"La stratégie d'extraction d'en-tête pour la banque '{matchedFormat.BankCode}' n'est pas encore implémentée.");
    }

    // ── 4. MAPPING DYNAMIQUE DES COLONNES DEPUIS LE JSON ─────────────────────
    var dateOperationCol = !string.IsNullOrEmpty(mapping.DateOp) ? _libelleService.FindColumnIndex(headerRow, mapping.DateOp) : -1;
    var montantCol       = !string.IsNullOrEmpty(mapping.Montant) ? _libelleService.FindColumnIndex(headerRow, mapping.Montant) : -1;
    var deviseCol        = !string.IsNullOrEmpty(mapping.Devise) ? _libelleService.FindColumnIndex(headerRow, mapping.Devise) : -1;
    var libelleCol       = !string.IsNullOrEmpty(mapping.Libelle) ? _libelleService.FindColumnIndex(headerRow, mapping.Libelle) : -1;
    var dateValeurCol    = !string.IsNullOrEmpty(mapping.DateVal) ? _libelleService.FindColumnIndex(headerRow, mapping.DateVal) : -1;

    if (dateOperationCol < 0 || montantCol < 0 || libelleCol < 0)
    {
        throw new InvalidOperationException("Une ou plusieurs colonnes requises (Configuration JSON vs Fichier) sont introuvables.");
    }

    // ── 5. TRAITEMENT DES LOGIQUES MÉTIER ───────────────────────────────────
    var banqueEntity = await _context.Banques
        .FirstOrDefaultAsync(b => b.Compte == bankCode && b.IsActive, cancellationToken);

    if (banqueEntity == null)
        throw new InvalidOperationException($"Aucune banque active configurée avec le compte '{bankCode}' n'a été trouvée dans la base de données.");

    string currentBankCode = banqueEntity.CodeBanque;

    var bankFluxConfig = await _context.Fluxes
        .Where(f => f.BankCode == currentBankCode)
        .ToDictionaryAsync(f => f.FluxCode.Trim().ToUpperInvariant(), f => f, cancellationToken);

    var activeCurrencies    = await _currencyService.GetAllAsync(cancellationToken);
    var currencyDecimalsMap = activeCurrencies.ToDictionary(
        c => c.CUR_ID.Trim().ToUpperInvariant(),
        c => (int)c.DECIMALSNUMBER);

    string cleanBank    = bankCode.Length >= 5  ? bankCode[0..5]  : bankCode.PadRight(5);
    string cleanGuichet = bankCode.Length >= 10 ? bankCode[5..10] : "00000";
    string cleanRib     = bankCode.Length >= 11 ? bankCode[10..]  : "0";

    var missingKeywords = new List<MissingMappingItem>();
    var movements = new List<Afb120MovementRow>();

    foreach (var row in rows.Skip(headerRowIndex + 1))
    {
        var originalLibelle = _libelleService.GetCell(row, libelleCol).Trim();
        var rawMontant      = _libelleService.GetCell(row, montantCol).Trim();
        var rawDateOp       = dateOperationCol >= 0 ? _libelleService.GetCell(row, dateOperationCol).Trim() : string.Empty;
        var rawDateVal      = dateValeurCol >= 0 ? _libelleService.GetCell(row, dateValeurCol).Trim() : string.Empty;
        
        // 🛑 SÉCURITÉ SPÉCIFIQUE MANSA (Ignorer les lignes de totaux et résumés de fin de fichier)
        if (matchedFormat.BankCode == "MANSA")
        {
            // Si la ligne commence par "Total" ou si le libellé contient "Solde (XOF) au"
            if (originalLibelle.StartsWith("Total", StringComparison.OrdinalIgnoreCase) || 
                originalLibelle.Contains("Solde", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(rawDateOp) || 
                !DateTime.TryParse(rawDateOp, out _))
            {
                continue; // On ignore complètement cette ligne et on passe à la suivante
            }
        }

        var devise          = deviseCol >= 0 ? _libelleService.GetCell(row, deviseCol).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(devise)) devise = matchedFormat.DefaultCurrency ?? "XOF";

        if (string.IsNullOrWhiteSpace(originalLibelle) && string.IsNullOrWhiteSpace(rawMontant)) continue;

        decimal amount = _libelleService.ParseFlexibleAmount(rawMontant);

        string sens = amount >= 0 ? "C" : "D";
        amount = Math.Abs(amount);
        DateTime? opDate  = DateTime.TryParse(rawDateOp,  out var d1) ? d1 : (DateTime?)null;
        DateTime? valDate = DateTime.TryParse(rawDateVal, out var d2) ? d2 : (DateTime?)null;

        string cib1 = "  ";
        string cib2 = "    ";

        var normalizedLibelle = _libelleService.NormalizeKeyword(originalLibelle);
        var detection = await _libelleService.DetectCategorieAsync(normalizedLibelle, amount, currentBankCode, cancellationToken);

        if (detection != null && detection.IsDetected && !string.IsNullOrWhiteSpace(detection.Flux))
        {
            string cleanFluxCode = detection.Flux.Trim().ToUpperInvariant();

            if (bankFluxConfig.TryGetValue(cleanFluxCode, out var fluxSetup))
            {
                cib1 = fluxSetup.Cib1 ?? "  ";
                cib2 = fluxSetup.Cib2 ?? "    ";
            }

            string cleanDevise     = devise.Trim().ToUpperInvariant();
            int    dynamicDecimals = currencyDecimalsMap.TryGetValue(cleanDevise, out var dbDecimals) ? dbDecimals : 2;

            var movementRow = new Afb120MovementRow
            {
                BankCode       = cleanBank,
                Guichet        = cleanGuichet,
                Rib2           = cleanRib,
                OperationDate  = opDate,
                ValueDate      = valDate,
                Amount         = amount,
                Currency       = devise,
                Sens           = sens,
                Label          = originalLibelle.Length > 33 ? originalLibelle[..33] : originalLibelle,
                Cib1           = cib1,
                Cib2           = cib2,
                DecimalsNumber = dynamicDecimals
            };

            movements.Add(movementRow);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(originalLibelle))
            {
                missingKeywords.Add(new MissingMappingItem
                {
                    OriginalLabel = originalLibelle,
                    Amount        = amount,
                    BankCode      = currentBankCode,
                    Reason        = "Aucun mapping flux trouvé pour ce libellé."
                });
            }
        }
    }

    // ── 6. GÉNÉRATION DU FICHIER DE SORTIE ───────────────────────────────────
    if (missingKeywords.Any())
    {
        throw new MissingMappingsException(missingKeywords.OrderBy(k => k.OriginalLabel).ToList());
    }

    if (!movements.Any())
        return new GenerateResultDto { Message = "Aucune transaction valide trouvée.", TotalMouvements = 0 };

    movements = movements
        .OrderBy(m => m.OperationDate ?? DateTime.MinValue)
        .ThenBy(m => m.Label)
        .ThenBy(m => m.Amount)
        .ToList();

    string finalOutputDir = string.IsNullOrWhiteSpace(outputPath) ? @"C:\BankFiles\AFB120\" : outputPath;
    Directory.CreateDirectory(finalOutputDir);

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var sb        = new StringBuilder();
    var first     = movements.First();

    string openSens = initialBalance >= 0 ? "C" : "D";
    var line01 = BuildLine01(first.BankCode, first.Guichet, first.Rib2, first.Currency, first.DecimalsNumber, fileStartDate, Math.Abs(initialBalance), openSens);
    AssertLength(line01, "01", bankCode);
    sb.AppendLine(line01);

    var line04Count = 0;
    foreach (var row in movements)
    {
        var line04 = BuildLine04(row);
        AssertLength(line04, "04", row.Label);
        sb.AppendLine(line04);
        line04Count++;
    }

    var totalCredit    = movements.Where(r => r.Sens == "C").Sum(r => r.Amount);
    var totalDebit     = movements.Where(r => r.Sens == "D").Sum(r => r.Amount);
    var closingBalance = initialBalance + totalCredit - totalDebit;
    string closingSens = closingBalance >= 0 ? "C" : "D";

    var line07 = BuildLine07(first.BankCode, first.Guichet, first.Rib2, first.Currency, first.DecimalsNumber, fileEndDate, Math.Abs(closingBalance), closingSens);
    AssertLength(line07, "07", bankCode);
    sb.AppendLine(line07);

    var fileName = $"AFB120_{bankCode}_{timestamp}.txt";
    var filePath = Path.Combine(finalOutputDir, fileName);
    await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

    var result = new GenerateResultDto
    {
        Message         = $"Génération AFB120 réussie dans {finalOutputDir}",
        NombreFichiers  = 1,
        TotalMouvements = line04Count
    };
    result.Fichiers.Add(filePath);
    result.Detail.Add(new GenerateDetailDto
    {
        AccountId      = bankCode,
        Currency       = first.Currency,
        NbMouvements   = line04Count,
        TotalCredit    = totalCredit,
        TotalDebit     = totalDebit,
        SoldeOuverture = initialBalance,
        SoldeFinal     = closingBalance,
        Fichier        = fileName
    });

    return result;
}    private static string FormatDecimals(int? decimals) => (decimals ?? 2).ToString();

        private static string FormatAmountAfb120(decimal amount, string sens, int decimals = 2)
        {
            if (decimals < 0) decimals = 2;
            decimal multiplier = (decimal)Math.Pow(10, decimals);
            var scaled     = decimal.Round(Math.Abs(amount) * multiplier, 0, MidpointRounding.AwayFromZero);
            long amountInt = (long)scaled;

            string amountStr = amountInt.ToString().PadLeft(14, '0');
            char last        = amountStr[^1];

            char encoded = sens == "C"
                ? last switch { '0'=>'{','1'=>'A','2'=>'B','3'=>'C','4'=>'D','5'=>'E','6'=>'F','7'=>'G','8'=>'H','9'=>'I', _=>'{' }
                : last switch { '0'=>'}','1'=>'J','2'=>'K','3'=>'L','4'=>'M','5'=>'N','6'=>'O','7'=>'P','8'=>'Q','9'=>'R', _=>'}' };

            return amountStr[..13] + encoded;
        }

        private static string FixedLength(string? value, int length, char pad = ' ')
        {
            if (string.IsNullOrEmpty(value)) return new string(pad, length);
            if (value.Length >= length)      return value[..length];
            return value.PadRight(length, pad);
        }

        private static string BuildLine01(string? bankCode, string? guichet, string? rib2, string? currency, int? decimals, DateTime date, decimal balance, string sens)
            => "01" + FixedLength(bankCode, 5) + "    " + FixedLength(guichet, 5) + FixedLength(currency, 3) + FormatDecimals(decimals) + " " + FixedLength(rib2, 11, '0') + "  " + date.ToString("ddMMyy") + string.Empty.PadRight(50) + FormatAmountAfb120(balance, sens, decimals ?? 2) + string.Empty.PadRight(15) + "*";

        private static string BuildLine07(string? bankCode, string? guichet, string? rib2, string? currency, int? decimals, DateTime date, decimal balance, string sens)
            => "07" + FixedLength(bankCode, 5) + "    " + FixedLength(guichet, 5) + FixedLength(currency, 3) + FormatDecimals(decimals) + " " + FixedLength(rib2, 11, '0') + "  " + date.ToString("ddMMyy") + string.Empty.PadRight(50) + FormatAmountAfb120(balance, sens, decimals ?? 2) + string.Empty.PadRight(15) + "*";

        private static string BuildLine04(Afb120MovementRow row)
            => "04"
             + FixedLength(row.BankCode, 5)
             + FixedLength(row.Cib2, 4)
             + FixedLength(row.Guichet, 5)
             + FixedLength(row.Currency, 3) + FormatDecimals(row.DecimalsNumber)
             + " "
             + FixedLength(row.Rib2, 11, '0')
             + FixedLength(row.Cib1, 2)
             + (row.OperationDate?.ToString("ddMMyy") ?? "      ")
             + "  "
             + (row.ValueDate?.ToString("ddMMyy") ?? "      ")
             + FixedLength(row.Label, 33)
             + FixedLength(row.Reference, 7)
             + "  "
             + FormatAmountAfb120(row.Amount, row.Sens ?? "C", row.DecimalsNumber)
             + string.Empty.PadRight(15)
             + "*";

        private void AssertLength(string line, string code, string? ctx)
        {
            if (line.Length != 120)
                throw new InvalidOperationException($"Ligne {code} invalide: longueur={line.Length} (attendu 120), contexte={ctx}");
        }
    
    private List<string> BuildRawLinesFromExcel(IFormFile file)
{
    var lines = new List<string>();

    // Réinitialiser le stream au cas où il aurait déjà été lu
    file.OpenReadStream().Seek(0, SeekOrigin.Begin);

    // Utiliser ReadWorksheetRows que tu as déjà dans LibelleService
    var rows = _libelleService.ReadWorksheetRows(file);

    foreach (var row in rows.Take(20))
    {
        // Joindre toutes les cellules avec TAB → ligne comparable à du texte
        var line = string.Join("\t", row.Select(c => c?.Trim() ?? ""));
        if (!string.IsNullOrWhiteSpace(line))
            lines.Add(line);
    }

    return lines;
}
}
}

