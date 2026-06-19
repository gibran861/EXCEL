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
    }

    public class MissingMappingsException : Exception
    {
        public List<MissingMappingItem> MissingKeywords { get; }
        public MissingMappingsException(List<MissingMappingItem> missingKeywords)
            : base("Certains libellés partagent le même flux ou n'ont pas de mapping défini.")
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

        public Afb120Service(AppDbContext context, LibelleService libelleService, CurrencyService currencyService, ILogger<Afb120Service> logger)
        {
            _context = context;
            _libelleService = libelleService;
            _currencyService = currencyService;
            _logger = logger;
        }

        public async Task<GenerateResultDto> GenerateFromExcelAsync(IFormFile file, string? outputPath = null, CancellationToken cancellationToken = default)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Le fichier Excel est obligatoire.");

            var rows = _libelleService.ReadWorksheetRows(file);
            var compteRowIndex = _libelleService.FindRowContaining(rows, "Compte courant");
            if (compteRowIndex < 0) throw new InvalidOperationException("Ligne 'Compte courant' introuvable.");
            var compteRow = rows[compteRowIndex];

            string bankCode = _libelleService.GetCell(compteRow, 1).Trim().Replace(" ", "");

            decimal.TryParse(_libelleService.GetCell(compteRow, 2).Trim(), out decimal initialBalance);
            string rawFileStartDate = _libelleService.GetCell(compteRow, 4).Trim();
            string rawFileEndDate   = _libelleService.GetCell(compteRow, 5).Trim();
            DateTime fileStartDate  = DateTime.TryParse(rawFileStartDate, out var parsedStart) ? parsedStart : DateTime.Now;
            DateTime fileEndDate    = DateTime.TryParse(rawFileEndDate,   out var parsedEnd)   ? parsedEnd   : DateTime.Now;

            var headerRowIndex = _libelleService.FindHeaderRowIndex(rows);
            if (headerRowIndex < 0) throw new InvalidOperationException("Entête des transactions introuvable.");
            var headerRow       = rows[headerRowIndex];
            var dateOperationCol = _libelleService.FindColumnIndex(headerRow, "DATE OPERATION");
            var montantCol       = _libelleService.FindColumnIndex(headerRow, "MONTANT");
            var deviseCol        = _libelleService.FindColumnIndex(headerRow, "DEVISE");
            var libelleCol       = _libelleService.FindColumnIndex(headerRow, "LIBELLE");
            var dateValeurCol    = _libelleService.FindColumnIndex(headerRow, "DATE VALEUR");

            var banqueEntity = await _context.Banques
                .FirstOrDefaultAsync(b => b.Compte == bankCode && b.IsActive, cancellationToken);

            if (banqueEntity == null)
                throw new InvalidOperationException($"Aucune banque active configurée avec le compte '{bankCode}' n'a été trouvée dans la base de données.");

            string currentBankCode = banqueEntity.CodeBanque;

            var bankFluxConfig = await _context.Fluxes
                .Where(f => f.BankCode == currentBankCode)
                .ToDictionaryAsync(f => f.FluxCode.Trim().ToUpperInvariant(), f => f, cancellationToken);

            var activeCurrencies     = await _currencyService.GetAllAsync(cancellationToken);
            var currencyDecimalsMap  = activeCurrencies.ToDictionary(
                c => c.CUR_ID.Trim().ToUpperInvariant(),
                c => (int)c.DECIMALSNUMBER);

            string cleanBank    = bankCode.Length >= 5  ? bankCode[0..5]   : bankCode.PadRight(5);
            string cleanGuichet = bankCode.Length >= 10 ? bankCode[5..10]  : "00000";
            string cleanRib     = bankCode.Length >= 11 ? bankCode[10..]   : "0";

            var missingKeywords    = new List<MissingMappingItem>();

            // ──────────────────────────────────────────────────────────────────
            // STRUCTURES DE DÉTECTION DE CONFLIT
            //
            // fluxOccurrenceCount : combien de fois chaque code flux apparaît
            //                       dans le fichier (toutes lignes confondues).
            //                       Si count > 1 → conflit → rejet.
            //
            // temporaryMovements  : liste provisoire avec le flux détecté et
            //                       le libellé original, pour le tri final.
            // ──────────────────────────────────────────────────────────────────
            var fluxOccurrenceCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var temporaryMovements  = new List<(Afb120MovementRow Row, string DetectedFlux, string FullOriginalLabel)>();

            foreach (var row in rows.Skip(headerRowIndex + 1))
            {
                var originalLibelle = _libelleService.GetCell(row, libelleCol).Trim();
                var rawMontant      = _libelleService.GetCell(row, montantCol).Trim();
                var rawDateOp       = _libelleService.GetCell(row, dateOperationCol).Trim();
                var rawDateVal      = _libelleService.GetCell(row, dateValeurCol).Trim();
                var devise          = _libelleService.GetCell(row, deviseCol).Trim();

                if (string.IsNullOrWhiteSpace(originalLibelle) && string.IsNullOrWhiteSpace(rawMontant)) continue;

                decimal.TryParse(rawMontant, out decimal amount);
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

                    // Incrémenter le compteur d'occurrences pour ce flux
                    if (!fluxOccurrenceCount.ContainsKey(cleanFluxCode))
                        fluxOccurrenceCount[cleanFluxCode] = 0;
                    fluxOccurrenceCount[cleanFluxCode]++;

                    if (bankFluxConfig.TryGetValue(cleanFluxCode, out var fluxSetup))
                    {
                        cib1 = fluxSetup.Cib1 ?? "  ";
                        cib2 = fluxSetup.Cib2 ?? "    ";
                    }

                    string cleanDevise     = devise.Trim().ToUpperInvariant();
                    int    dynamicDecimals = currencyDecimalsMap.TryGetValue(cleanDevise, out var dbDecimals) ? dbDecimals : 2;

                    var movementRow = new Afb120MovementRow
                    {
                        BankCode      = cleanBank,
                        Guichet       = cleanGuichet,
                        Rib2          = cleanRib,
                        OperationDate = opDate,
                        ValueDate     = valDate,
                        Amount        = amount,
                        Currency      = devise,
                        Sens          = sens,
                        Label         = originalLibelle.Length > 33 ? originalLibelle[..33] : originalLibelle,
                        Cib1          = cib1,
                        Cib2          = cib2,
                        DecimalsNumber = dynamicDecimals
                    };

                    temporaryMovements.Add((movementRow, cleanFluxCode, originalLibelle));
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(originalLibelle))
                    {
                        missingKeywords.Add(new MissingMappingItem
                        {
                            OriginalLabel = originalLibelle,
                            Amount        = amount,
                            Reason        = "Aucun mapping flux trouvé pour ce libellé."
                        });
                    }
                }
            }

            // ──────────────────────────────────────────────────────────────────
            // DEUXIÈME PASSE : tout flux apparu plus d'une fois → rejet
            // (même libellé répété, montants différents, sens différents…
            //  peu importe : un flux = une seule ligne dans le fichier AFB120)
            // ──────────────────────────────────────────────────────────────────
            var movements = new List<Afb120MovementRow>();

            foreach (var item in temporaryMovements)
            {
                if (fluxOccurrenceCount.TryGetValue(item.DetectedFlux, out int count) && count > 1)
                {
                    // Conflit : ce code flux est porté par plusieurs lignes
                    missingKeywords.Add(new MissingMappingItem
                    {
                        OriginalLabel = item.FullOriginalLabel,
                        Amount        = item.Row.Amount,
                        Reason        = $"Conflit : Le flux '{item.DetectedFlux}' est attribué à {count} ligne(s) différente(s) dans le fichier."
                    });
                }
                else
                {
                    movements.Add(item.Row);
                }
            }

            // Bloquer si des erreurs ou conflits ont été détectés
            if (missingKeywords.Any())
            {
                throw new MissingMappingsException(missingKeywords.OrderBy(k => k.OriginalLabel).ToList());
            }

            if (!movements.Any())
                return new GenerateResultDto { Message = "Aucune transaction valide trouvée dans le fichier Excel.", TotalMouvements = 0 };

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

            var seenLine04 = new HashSet<string>();
            foreach (var row in movements)
            {
                var line04 = BuildLine04(row);
                AssertLength(line04, "04", row.Label);
                if (seenLine04.Add(line04)) sb.AppendLine(line04);
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
                TotalMouvements = seenLine04.Count
            };
            result.Fichiers.Add(filePath);
            result.Detail.Add(new GenerateDetailDto
            {
                AccountId      = bankCode,
                Currency       = first.Currency,
                NbMouvements   = seenLine04.Count,
                TotalCredit    = totalCredit,
                TotalDebit     = totalDebit,
                SoldeOuverture = initialBalance,
                SoldeFinal     = closingBalance,
                Fichier        = fileName
            });

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string FormatDecimals(int? decimals) => (decimals ?? 2).ToString();

        private static string FormatAmountAfb120(decimal amount, string sens, int decimals = 2)
        {
            if (decimals < 0) decimals = 2;
            decimal multiplier = (decimal)Math.Pow(10, decimals);
            var scaled    = decimal.Round(Math.Abs(amount) * multiplier, 0, MidpointRounding.AwayFromZero);
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
    }
}