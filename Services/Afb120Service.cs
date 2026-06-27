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
        public string? BankCode      { get; set; }
        public string? Guichet       { get; set; }
        public string? Rib2          { get; set; }
        public DateTime? OperationDate { get; set; }
        public DateTime? ValueDate   { get; set; }
        public decimal Amount        { get; set; }
        public string? Currency      { get; set; }
        public string? Sens          { get; set; }
        public string? Label         { get; set; }
        public string? Reference     { get; set; }
        public string? Cib1          { get; set; }
        public string? Cib2          { get; set; }
        public int DecimalsNumber    { get; set; } = 2;
    }

    public class MissingMappingItem
    {
        public string OriginalLabel { get; set; } = string.Empty;
        public decimal Amount       { get; set; }
        public string Reason        { get; set; } = "Non configuré";
        public string BankCode      { get; set; } = string.Empty;
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
        private readonly AppDbContext         _context;
        private readonly LibelleService       _libelleService;
        private readonly CurrencyService      _currencyService;
        private readonly BankFileFormatService _formatService;
        private readonly ILogger<Afb120Service> _logger;

        public Afb120Service(
            AppDbContext context,
            LibelleService libelleService,
            CurrencyService currencyService,
            BankFileFormatService formatService,
            ILogger<Afb120Service> logger)
        {
            _context        = context;
            _libelleService = libelleService;
            _currencyService = currencyService;
            _formatService  = formatService;
            _logger         = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // MÉTHODE PRINCIPALE
        // ─────────────────────────────────────────────────────────────────────
        public async Task<GenerateResultDto> GenerateFromFileAsync(
            IFormFile file,
            string? outputPath = null,
            CancellationToken cancellationToken = default)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Le fichier est obligatoire.");

            // ── 1. Vérification extension ─────────────────────────────────────
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".csv" or ".xlsx" or ".xls"))
                throw new ArgumentException(
                    $"Format de fichier non supporté : '{extension}'. Utilisez .xlsx, .xls ou .csv.");

            // ── 2. Lecture des lignes brutes (toujours en mode texte pour la détection) ──
            var rawLines = _libelleService.ReadRawLines(file);

            if (rawLines.Count == 0)
                throw new InvalidOperationException("Le fichier est vide.");

            // ── 3. Détection du format en base ────────────────────────────────
            var format = await _formatService.DetectFormatAsync(rawLines, cancellationToken);
            if (format == null)
                throw new InvalidOperationException(
                    "Format de fichier non reconnu. " +
                    "Ajoutez ce format dans la table BankFileFormats " +
                    $"(premières lignes : '{string.Join(" | ", rawLines.Take(3))}').");

            _logger.LogInformation(
                "Format détecté : {FormatName} (BankCode={BankCode}, AmountMode={AmountMode})",
                format.Name, format.BankCode, format.AmountMode);

            var mapping = _formatService.DeserializeMapping(format);

            // ── 4. Parser les lignes selon le séparateur du format ────────────
            //      Pour Excel on réutilise ReadWorksheetRows car le fichier
            //      doit être relu depuis le début (stream déjà consommé par ReadRawLines).
            //      On relit donc le fichier une deuxième fois.
            List<List<string>> rows;
            if (extension is ".xlsx" or ".xls")
            {
                // Rembobiner le stream avant de le relire
                file.OpenReadStream().Seek(0, SeekOrigin.Begin);
                rows = _libelleService.ReadWorksheetRows(file);
            }
            else
            {
                rows = _libelleService.ParseLinesWithFormat(rawLines, format);
            }

            // ── 5. Trouver la ligne d'entête ──────────────────────────────────
            int headerRowIndex = _libelleService.FindHeaderRowByPattern(rawLines, format.HeaderPattern);
            if (headerRowIndex < 0)
                throw new InvalidOperationException(
                    $"Ligne d'entête introuvable. Pattern attendu : '{format.HeaderPattern}'.");

            var headerCells = rows[headerRowIndex];

            // ── 6. Résoudre les index de colonnes ─────────────────────────────
            int dateOpCol  = mapping.DateOp      != null ? _libelleService.FindColumnIndex(headerCells, mapping.DateOp)      : -1;
            int dateValCol = mapping.DateVal     != null ? _libelleService.FindColumnIndex(headerCells, mapping.DateVal)     : -1;
            int libelleCol = mapping.Libelle     != null ? _libelleService.FindColumnIndex(headerCells, mapping.Libelle)     : -1;
            int montantCol = mapping.Montant     != null ? _libelleService.FindColumnIndex(headerCells, mapping.Montant)     : -1;
            int sensCol    = mapping.Sens        != null ? _libelleService.FindColumnIndex(headerCells, mapping.Sens)        : -1;
            int deviseCol  = mapping.Devise      != null ? _libelleService.FindColumnIndex(headerCells, mapping.Devise)      : -1;
            int refCol     = mapping.Reference   != null ? _libelleService.FindColumnIndex(headerCells, mapping.Reference)   : -1;
            int debitCol   = format.DebitColumnName  != null ? _libelleService.FindColumnIndex(headerCells, format.DebitColumnName)  : -1;
            int creditCol  = format.CreditColumnName != null ? _libelleService.FindColumnIndex(headerCells, format.CreditColumnName) : -1;

            // Validation minimale : on a au moins libellé et montant (ou débit/crédit)
            if (libelleCol < 0)
                throw new InvalidOperationException(
                    $"Colonne libellé '{mapping.Libelle}' introuvable dans l'entête.");

            bool hasMontant = montantCol >= 0 || (debitCol >= 0 && creditCol >= 0);
            if (!hasMontant)
                throw new InvalidOperationException(
                    "Aucune colonne de montant trouvée. " +
                    "Vérifiez les ColumnMappings ou DebitColumnName/CreditColumnName du format.");

            // ── 7. Résoudre le numéro de compte ───────────────────────────────
            string bankAccountNumber = _libelleService.ResolveAccountNumber(
                rows, headerRowIndex, headerCells, format, mapping);

            if (string.IsNullOrWhiteSpace(bankAccountNumber))
                throw new InvalidOperationException(
                    "Numéro de compte introuvable. " +
                    "Configurez AccountNumberColumnName dans le format ou ajoutez une ligne 'Compte courant'.");

            bankAccountNumber = bankAccountNumber.Replace(" ", "");

            // ── 8. Résoudre les dates début/fin du relevé ─────────────────────
            //      Priorité : colonnes dédiées dans le format > ligne "Compte courant"
            DateTime fileStartDate = ResolveFileDate(rows, format.StartDateColumnName, headerRowIndex, true);
            DateTime fileEndDate   = ResolveFileDate(rows, format.EndDateColumnName,   headerRowIndex, false);

            // ── 9. Trouver la banque en base ──────────────────────────────────
            var banqueEntity = await _context.Banques
                .FirstOrDefaultAsync(
                    b => b.Compte == bankAccountNumber && b.IsActive,
                    cancellationToken);

            if (banqueEntity == null)
                throw new InvalidOperationException(
                    $"Aucune banque active configurée avec le compte '{bankAccountNumber}' " +
                    "n'a été trouvée dans la base de données.");

            string currentBankCode = banqueEntity.CodeBanque;

            // ── 10. Charger les configs flux et devises ────────────────────────
            var bankFluxConfig = await _context.Fluxes
                .Where(f => f.BankCode == currentBankCode)
                .ToDictionaryAsync(
                    f => f.FluxCode.Trim().ToUpperInvariant(),
                    f => f,
                    cancellationToken);

            var activeCurrencies    = await _currencyService.GetAllAsync(cancellationToken);
            var currencyDecimalsMap = activeCurrencies.ToDictionary(
                c => c.CUR_ID.Trim().ToUpperInvariant(),
                c => (int)c.DECIMALSNUMBER);

            // ── 11. Décomposer le RIB ─────────────────────────────────────────
            string cleanBank    = bankAccountNumber.Length >= 5  ? bankAccountNumber[0..5]  : bankAccountNumber.PadRight(5);
            string cleanGuichet = bankAccountNumber.Length >= 10 ? bankAccountNumber[5..10] : "00000";
            string cleanRib     = bankAccountNumber.Length >= 11 ? bankAccountNumber[10..]  : "0";

            // ── 12. Résoudre le solde d'ouverture ─────────────────────────────
            decimal initialBalance = ResolveInitialBalance(rows, format, headerCells, headerRowIndex);

            // ── 13. Itérer sur les lignes de données ──────────────────────────
            var missingKeywords = new List<MissingMappingItem>();
            var movements       = new List<Afb120MovementRow>();

            int dataRowNumber = 0;
            foreach (var row in rows.Skip(headerRowIndex + 1))
            {
                dataRowNumber++;

                // Ignorer les lignes vides
                if (row.All(cell => string.IsNullOrWhiteSpace(cell)))
                    continue;

                var originalLibelle = libelleCol >= 0
                    ? _libelleService.GetCell(row, libelleCol).Trim()
                    : string.Empty;

                var rawDateOp  = dateOpCol  >= 0 ? _libelleService.GetCell(row, dateOpCol).Trim()  : string.Empty;
                var rawDateVal = dateValCol >= 0 ? _libelleService.GetCell(row, dateValCol).Trim()  : string.Empty;
                var devise     = deviseCol  >= 0
                    ? _libelleService.GetCell(row, deviseCol).Trim()
                    : format.DefaultCurrency ?? "XOF";
                var reference  = refCol >= 0 ? _libelleService.GetCell(row, refCol).Trim() : string.Empty;

                if (string.IsNullOrWhiteSpace(originalLibelle))
                    continue;

                // ── Résolution du montant selon AmountMode ────────────────────
                decimal amount;
                try
                {
                    amount = ResolveAmount(row, format, montantCol, sensCol, debitCol, creditCol);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Ligne {LineNumber} ignorée — erreur de montant : {Error}",
                        headerRowIndex + 1 + dataRowNumber, ex.Message);
                    continue;
                }

                string sens = amount >= 0 ? "C" : "D";
                amount = Math.Abs(amount);

                DateTime? opDate  = TryParseDate(rawDateOp);
                DateTime? valDate = TryParseDate(rawDateVal);

                // ── Détection du flux ─────────────────────────────────────────
                string cib1 = "  ";
                string cib2 = "    ";

                var normalizedLibelle = _libelleService.NormalizeKeyword(originalLibelle);
                var detection = await _libelleService.DetectCategorieAsync(
                    normalizedLibelle, amount, currentBankCode, cancellationToken);

                if (detection != null && detection.IsDetected && !string.IsNullOrWhiteSpace(detection.Flux))
                {
                    string cleanFluxCode = detection.Flux.Trim().ToUpperInvariant();

                    if (bankFluxConfig.TryGetValue(cleanFluxCode, out var fluxSetup))
                    {
                        cib1 = fluxSetup.Cib1 ?? "  ";
                        cib2 = fluxSetup.Cib2 ?? "    ";
                    }

                    string cleanDevise     = devise.Trim().ToUpperInvariant();
                    int    dynamicDecimals = currencyDecimalsMap.TryGetValue(cleanDevise, out var dbDecimals)
                        ? dbDecimals
                        : 2;

                    movements.Add(new Afb120MovementRow
                    {
                        BankCode      = cleanBank,
                        Guichet       = cleanGuichet,
                        Rib2          = cleanRib,
                        OperationDate = opDate,
                        ValueDate     = valDate,
                        Amount        = amount,
                        Currency      = devise,
                        Sens          = sens,
                        Label         = originalLibelle.Length > 33
                            ? originalLibelle[..33]
                            : originalLibelle,
                        Reference     = reference,
                        Cib1          = cib1,
                        Cib2          = cib2,
                        DecimalsNumber = dynamicDecimals
                    });
                }
                else
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

            // ── 14. Bloquer si des mappings manquent ──────────────────────────
            if (missingKeywords.Any())
                throw new MissingMappingsException(
                    missingKeywords.OrderBy(k => k.OriginalLabel).ToList());

            if (!movements.Any())
                return new GenerateResultDto
                {
                    Message         = "Aucune transaction valide trouvée dans le fichier.",
                    TotalMouvements = 0
                };

            // ── 15. Trier les mouvements ──────────────────────────────────────
            movements = movements
                .OrderBy(m => m.OperationDate ?? DateTime.MinValue)
                .ThenBy(m => m.Label)
                .ThenBy(m => m.Amount)
                .ToList();

            // ── 16. Générer le fichier AFB120 ─────────────────────────────────
            string finalOutputDir = string.IsNullOrWhiteSpace(outputPath)
                ? @"C:\BankFiles\AFB120\"
                : outputPath;
            Directory.CreateDirectory(finalOutputDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var sb        = new StringBuilder();
            var first     = movements.First();

            // Ligne 01 — solde d'ouverture
            string openSens = initialBalance >= 0 ? "C" : "D";
            var line01 = BuildLine01(
                first.BankCode, first.Guichet, first.Rib2,
                first.Currency, first.DecimalsNumber,
                fileStartDate, Math.Abs(initialBalance), openSens);
            AssertLength(line01, "01", bankAccountNumber);
            sb.AppendLine(line01);

            // Lignes 04 — mouvements
            int line04Count = 0;
            foreach (var row in movements)
            {
                var line04 = BuildLine04(row);
                AssertLength(line04, "04", row.Label);
                sb.AppendLine(line04);
                line04Count++;
            }

            // Ligne 07 — solde de clôture
            decimal totalCredit    = movements.Where(r => r.Sens == "C").Sum(r => r.Amount);
            decimal totalDebit     = movements.Where(r => r.Sens == "D").Sum(r => r.Amount);
            decimal closingBalance = initialBalance + totalCredit - totalDebit;
            string  closingSens    = closingBalance >= 0 ? "C" : "D";

            var line07 = BuildLine07(
                first.BankCode, first.Guichet, first.Rib2,
                first.Currency, first.DecimalsNumber,
                fileEndDate, Math.Abs(closingBalance), closingSens);
            AssertLength(line07, "07", bankAccountNumber);
            sb.AppendLine(line07);

            // ── 17. Écrire sur disque ─────────────────────────────────────────
            var fileName = $"AFB120_{bankAccountNumber}_{timestamp}.txt";
            var filePath = Path.Combine(finalOutputDir, fileName);
            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken);

            _logger.LogInformation(
                "Fichier AFB120 généré : {FilePath} ({Count} mouvements)",
                filePath, line04Count);

            // ── 18. Construire et retourner le résultat ───────────────────────
            var result = new GenerateResultDto
            {
                Message         = $"Génération AFB120 réussie — format '{format.Name}' — dossier : {finalOutputDir}",
                NombreFichiers  = 1,
                TotalMouvements = line04Count
            };
            result.Fichiers.Add(filePath);
            result.Detail.Add(new GenerateDetailDto
            {
                AccountId      = bankAccountNumber,
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

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS PRIVÉS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Résout le montant d'une ligne selon le mode configuré dans le format.
        /// SIGNED      : montant signé dans une seule colonne.
        /// CR_DR       : montant absolu + colonne sens "Cr"/"Dr".
        /// TWO_COLUMNS : deux colonnes débit / crédit séparées.
        /// </summary>
        private decimal ResolveAmount(
            List<string> row,
            BankFileFormat format,
            int montantCol, int sensCol,
            int debitCol, int creditCol)
        {
            switch (format.AmountMode.ToUpperInvariant())
            {
                case "SIGNED":
                {
                    var raw = montantCol >= 0
                        ? _libelleService.GetCell(row, montantCol)
                        : "0";
                    return _libelleService.ParseFlexibleAmount(raw);
                }

                case "CR_DR":
                {
                    var rawAmt  = montantCol >= 0 ? _libelleService.GetCell(row, montantCol) : "0";
                    var rawSens = sensCol    >= 0 ? _libelleService.GetCell(row, sensCol).Trim() : string.Empty;
                    decimal absAmt = _libelleService.ParseFlexibleAmount(rawAmt);

                    // Tolérance : Cr / CR / C / Credit / CREDIT
                    bool isCredit = rawSens.StartsWith(
                        format.CreditMarker ?? "Cr",
                        StringComparison.OrdinalIgnoreCase);

                    return isCredit ? absAmt : -absAmt;
                }

                case "TWO_COLUMNS":
                {
                    var rawDebit  = debitCol  >= 0 ? _libelleService.GetCell(row, debitCol)  : "0";
                    var rawCredit = creditCol >= 0 ? _libelleService.GetCell(row, creditCol) : "0";

                    decimal d = _libelleService.ParseFlexibleAmount(rawDebit);
                    decimal c = _libelleService.ParseFlexibleAmount(rawCredit);

                    // Crédit prioritaire si les deux sont remplis (cas rare)
                    if (c != 0) return c;
                    if (d != 0) return -d;
                    return 0;
                }

                default:
                    throw new InvalidOperationException(
                        $"AmountMode inconnu : '{format.AmountMode}'. " +
                        "Valeurs acceptées : SIGNED, CR_DR, TWO_COLUMNS.");
            }
        }

        /// <summary>
        /// Résout la date début ou fin du relevé depuis une colonne dédiée
        /// ou depuis la ligne "Compte courant" (compatibilité ancienne).
        /// </summary>
        private DateTime ResolveFileDate(
            List<List<string>> rows,
            string? columnName,
            int headerRowIndex,
            bool isStartDate)
        {
            // Cas 1 : colonne dédiée dans la config du format
            if (!string.IsNullOrWhiteSpace(columnName))
            {
                // On cherche la valeur dans toutes les lignes avant l'entête
                for (int i = 0; i < headerRowIndex; i++)
                {
                    foreach (var cell in rows[i])
                    {
                        if (DateTime.TryParse(cell.Trim(), out var d))
                            return d;
                    }
                }
            }

            // Cas 2 : ligne "Compte courant" (format Excel legacy)
            int compteRowIndex = _libelleService.FindRowContaining(rows, "Compte courant");
            if (compteRowIndex >= 0)
            {
                var compteRow = rows[compteRowIndex];
                // col 4 = date début, col 5 = date fin (convention existante)
                int col = isStartDate ? 4 : 5;
                var raw = _libelleService.GetCell(compteRow, col).Trim();
                if (DateTime.TryParse(raw, out var parsed))
                    return parsed;
            }

            // Fallback : aujourd'hui (ne devrait pas arriver avec un format bien configuré)
            _logger.LogWarning(
                "Date {Type} du relevé introuvable, utilisation de DateTime.Now.",
                isStartDate ? "début" : "fin");
            return DateTime.Now;
        }

        /// <summary>
        /// Résout le solde d'ouverture :
        /// 1. Depuis une colonne dédiée dans le format.
        /// 2. Depuis la ligne "Compte courant" col 2 (legacy).
        /// 3. Zéro par défaut.
        /// </summary>
        private decimal ResolveInitialBalance(
            List<List<string>> rows,
            BankFileFormat format,
            List<string> headerCells,
            int headerRowIndex)
        {
            // Cas 1 : colonne dédiée dans le format
            if (!string.IsNullOrWhiteSpace(format.InitialBalanceColumnName))
            {
                int col = _libelleService.FindColumnIndex(headerCells, format.InitialBalanceColumnName);
                if (col >= 0 && rows.Count > headerRowIndex + 1)
                {
                    var raw = _libelleService.GetCell(rows[headerRowIndex + 1], col).Trim();
                    return _libelleService.ParseFlexibleAmount(raw);
                }
            }

            // Cas 2 : ligne "Compte courant" col 2 (format Excel legacy)
            int compteRowIndex = _libelleService.FindRowContaining(rows, "Compte courant");
            if (compteRowIndex >= 0)
            {
                var raw = _libelleService.GetCell(rows[compteRowIndex], 2).Trim();
                return _libelleService.ParseFlexibleAmount(raw);
            }

            _logger.LogWarning("Solde d'ouverture introuvable, valeur par défaut = 0.");
            return 0m;
        }

        /// <summary>Tente de parser une date avec plusieurs formats courants.</summary>
        private static DateTime? TryParseDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Formats les plus fréquents dans les relevés bancaires africains
            var formats = new[]
            {
                "dd/MM/yyyy", "d/MM/yyyy", "dd/MM/yy",
                "dd-MM-yyyy", "d-MM-yyyy",
                "yyyy-MM-dd",
                "dd MMM yyyy", "d MMM yyyy",
                "MM/dd/yyyy"
            };

            if (DateTime.TryParseExact(raw, formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var result))
                return result;

            if (DateTime.TryParse(raw, out var fallback))
                return fallback;

            return null;
        }

        // ── Formatage AFB120 ───────────────────────────────────────────────────

        private static string FormatDecimals(int? decimals) => (decimals ?? 2).ToString();

        private static string FormatAmountAfb120(decimal amount, string sens, int decimals = 2)
        {
            if (decimals < 0) decimals = 2;
            decimal multiplier = (decimal)Math.Pow(10, decimals);
            var scaled     = decimal.Round(Math.Abs(amount) * multiplier, 0, MidpointRounding.AwayFromZero);
            long amountInt = (long)scaled;

            string amountStr = amountInt.ToString().PadLeft(14, '0');
            char last        = amountStr[^1];

            char encoded = sens == "C"
                ? last switch
                {
                    '0' => '{', '1' => 'A', '2' => 'B', '3' => 'C', '4' => 'D',
                    '5' => 'E', '6' => 'F', '7' => 'G', '8' => 'H', '9' => 'I',
                    _   => '{'
                }
                : last switch
                {
                    '0' => '}', '1' => 'J', '2' => 'K', '3' => 'L', '4' => 'M',
                    '5' => 'N', '6' => 'O', '7' => 'P', '8' => 'Q', '9' => 'R',
                    _   => '}'
                };

            return amountStr[..13] + encoded;
        }

        private static string FixedLength(string? value, int length, char pad = ' ')
        {
            if (string.IsNullOrEmpty(value)) return new string(pad, length);
            if (value.Length >= length)      return value[..length];
            return value.PadRight(length, pad);
        }

        private static string BuildLine01(
            string? bankCode, string? guichet, string? rib2,
            string? currency, int? decimals,
            DateTime date, decimal balance, string sens)
            => "01"
             + FixedLength(bankCode, 5)
             + "    "
             + FixedLength(guichet, 5)
             + FixedLength(currency, 3)
             + FormatDecimals(decimals)
             + " "
             + FixedLength(rib2, 11, '0')
             + "  "
             + date.ToString("ddMMyy")
             + string.Empty.PadRight(50)
             + FormatAmountAfb120(balance, sens, decimals ?? 2)
             + string.Empty.PadRight(15)
             + "*";

        private static string BuildLine07(
            string? bankCode, string? guichet, string? rib2,
            string? currency, int? decimals,
            DateTime date, decimal balance, string sens)
            => "07"
             + FixedLength(bankCode, 5)
             + "    "
             + FixedLength(guichet, 5)
             + FixedLength(currency, 3)
             + FormatDecimals(decimals)
             + " "
             + FixedLength(rib2, 11, '0')
             + "  "
             + date.ToString("ddMMyy")
             + string.Empty.PadRight(50)
             + FormatAmountAfb120(balance, sens, decimals ?? 2)
             + string.Empty.PadRight(15)
             + "*";

        private static string BuildLine04(Afb120MovementRow row)
            => "04"
             + FixedLength(row.BankCode, 5)
             + FixedLength(row.Cib2, 4)
             + FixedLength(row.Guichet, 5)
             + FixedLength(row.Currency, 3)
             + FormatDecimals(row.DecimalsNumber)
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
                throw new InvalidOperationException(
                    $"Ligne {code} invalide : longueur={line.Length} (attendu 120), contexte='{ctx}'");
        }
    }
}