using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AfbGenerator.Api.Data;
using AfbGenerator.Api.Models;
using System.Data;
namespace AfbGenerator.Api.Services
{
public class BankTemplateService
{
    private readonly AppDbContext _context;

    public BankTemplateService(AppDbContext context)
    {
        _context = context;
    }

    // 1. Enregistrer un nouveau modèle de banque avec ses champs
    public async Task<BankTemplate> CreateTemplateAsync(BankTemplate template)
    {
        // Validations de sécurité avant insertion
        if (string.IsNullOrWhiteSpace(template.BankName))
        {
            throw new ArgumentException("Le nom de la banque est obligatoire.");
        }

        if (template.Fields == null || template.Fields.Count == 0)
        {
            throw new ArgumentException("Le modèle doit contenir au moins un champ mappé.");
        }

        foreach (var field in template.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.FieldKey))
            {
                throw new ArgumentException("Chaque champ mappé doit avoir une clé AFB (ex: PERIODE).");
            }
        }

        // Ajout direct dans le contexte (EF Core comprend la relation un-à-plusieurs)
        await _context.BankTemplates.AddAsync(template);
        await _context.SaveChangesAsync();

        return template;
    }

    // 2. Récupérer tous les modèles (Utile pour le matching ou pour l'IHM)
    public async Task<List<BankTemplate>> GetAllTemplatesAsync()
    {
        return await _context.BankTemplates
            .Include(t => t.Fields) // Inclut automatiquement les détails du mapping
            .ToListAsync();
    }

    // 3. Récupérer un modèle spécifique par son ID
    public async Task<BankTemplate> GetTemplateByIdAsync(int id)
    {
        return await _context.BankTemplates
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == id);
    }
public async Task<BankTemplate> DetectTemplateAsync(DataTable fileData)
{
    // 1. Récupérer tous les modèles de la BDD
    var allTemplates = await _context.BankTemplates
        .Include(t => t.Fields)
        .ToListAsync();

    // Fonction locale pour normaliser et nettoyer le texte pour la comparaison (ignore \r, \n et espaces multiples)
    string CleanText(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        string cleaned = input.Replace("\r", "").Replace("\n", " ").Replace("\t", " ");
        return System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim().ToLower();
    }

    foreach (var template in allTemplates)
    {
        bool isModelMatch = true;
        int headerFieldsCount = 0;
        
        int detectedRowOffset = 0; 
        bool offsetCalculated = false;

        // On ne teste d'abord que les en-têtes fixes
        var headerFields = template.Fields.Where(f => f.IsHeaderField).ToList();

        foreach (var field in headerFields)
        {
            headerFieldsCount++;
            bool anchorFoundInFile = false;

            string cleanedAnchorText = CleanText(field.AnchorTextValue);
            if (string.IsNullOrEmpty(cleanedAnchorText)) continue;

            // CORRECTIF 1 : Augmenter maxCols à 10 au lieu de 5 pour inclure le Crédit (index 5) et le Solde (index 6)
            int maxRows = Math.Min(fileData.Rows.Count, 50);
            int maxCols = Math.Min(fileData.Columns.Count, 10);

            for (int r = 0; r < maxRows; r++)
            {
                for (int c = 0; c < maxCols; c++)
                {
                    string cleanedCellValue = CleanText(fileData.Rows[r][c]?.ToString());

                    if (!string.IsNullOrEmpty(cleanedCellValue) && 
                        cleanedCellValue.Contains(cleanedAnchorText, StringComparison.OrdinalIgnoreCase))
                    {
                        // CORRECTIF 2 : Si le texte est générique comme "Mouvement" ou "Solde", 
                        // on s'assure qu'on est au moins proche de la colonne initialement prévue en BDD
                        // pour éviter que le Crédit ne vienne écraser/voler l'index du Débit.
                        if ((cleanedAnchorText == "mouvement" || cleanedAnchorText == "solde") && Math.Abs(c - field.AnchorColumnIndex) > 1)
                        {
                            continue; // Ce n'est probablement pas la bonne colonne pour cette ancre spécifique
                        }

                        anchorFoundInFile = true;

                        if (!offsetCalculated)
                        {
                            detectedRowOffset = r - field.AnchorRowIndex;
                            offsetCalculated = true;
                        }

                        // Réajustement des coordonnées
                        field.AnchorRowIndex = r;
                        field.AnchorColumnIndex = c; 
                        
                        field.TargetRowIndex = field.TargetRowIndex + detectedRowOffset;
                        break;
                    }
                }
                if (anchorFoundInFile) break;
            }

            if (!anchorFoundInFile)
            {
                isModelMatch = false;
                break;
            }
        }

        if (headerFieldsCount > 0 && isModelMatch)
        {
            foreach (var tableField in template.Fields.Where(f => !f.IsHeaderField))
            {
                tableField.TargetRowIndex = tableField.TargetRowIndex + detectedRowOffset;
            }

            return template; 
        }
    }

    return null; 
}


/// <summary>
    /// Extrait les données brutes d'un fichier selon la configuration d'un modèle validé
    /// </summary>

public ExtractedAccountStatement ExtractData(DataTable fileData, BankTemplate template)
{
    var statement = new ExtractedAccountStatement
    {
        BankName = template.BankName
    };

    // =========================================================================
    // ÉTAPE 1 : Extraction des champs d'en-tête fixes
    // =========================================================================
    foreach (var field in template.Fields.Where(f => f.IsHeaderField))
    {
        if (field.TargetRowIndex >= fileData.Rows.Count || field.TargetColumnIndex >= fileData.Columns.Count)
            continue;

        string rawValue = fileData.Rows[field.TargetRowIndex][field.TargetColumnIndex]?.ToString()?.Trim();

        switch (field.FieldKey)
        {
            case "DATE_DEBUT":
            case "PERIODE":
                if (!string.IsNullOrEmpty(rawValue) && rawValue.Contains(" au "))
                {
                    var parts = rawValue.Split(new[] { " au " }, StringSplitOptions.None);
                    statement.DateDebut = CleanRawDate(parts[0]);
                    statement.DateFin = CleanRawDate(parts[1]);
                }
                else
                {
                    statement.DateDebut = rawValue;
                }
                break;

            case "DATE_FIN":
                if (string.IsNullOrEmpty(statement.DateFin))
                {
                    statement.DateFin = rawValue;
                }
                break;

            case "NUM_COMPTE":
                statement.NumCompte = rawValue;
                break;

            case "SOLDE_INIT":
                if (!string.IsNullOrEmpty(rawValue))
                {
                    // Extrait le premier bloc numérique trouvé (gère les nombres négatifs avec le signe "-")
                    var match = System.Text.RegularExpressions.Regex.Match(rawValue.Replace(" ", ""), @"-?\d+");
                    statement.SoldeInitial = match.Success ? match.Value : rawValue;
                }
                break;
        }
    }

    // =========================================================================
    // SÉCURITÉ / FALLBACK : Si le Solde Initial est toujours vide ou introuvable via l'index fixe,
    // on scanne le fichier entier à la recherche d'une ligne textuelle "Solde initial"
    // =========================================================================
    if (string.IsNullOrEmpty(statement.SoldeInitial))
    {
        bool soldeFound = false;
        for (int r = 0; r < fileData.Rows.Count; r++)
        {
            for (int c = 0; c < fileData.Columns.Count; c++)
            {
                string cellText = fileData.Rows[r][c]?.ToString()?.Trim();
                
                if (!string.IsNullOrEmpty(cellText) && cellText.Contains("Solde initial", StringComparison.OrdinalIgnoreCase))
                {
                    // Analyse de la chaîne (ex: "Solde initial (XOF) : -34921450")
                    // On extrait le nombre incluant un éventuel signe négatif
                    var match = System.Text.RegularExpressions.Regex.Match(cellText.Replace(" ", ""), @"-?\d+");
                    if (match.Success)
                    {
                        statement.SoldeInitial = match.Value;
                        soldeFound = true;
                        break;
                    }
                }
            }
            if (soldeFound) break;
        }
    }

    // =========================================================================
    // ÉTAPE 2 : Chargement dynamique des configurations de colonnes
    // =========================================================================
    var colDateConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_DATE");
    var colLibelleConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_LIBELLE");
    var colDateValConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_DATE_VALEUR");
    
    var colMontantConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_MONTANT");
    var colDebitConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_DEBIT");
    var colCreditConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_CREDIT");
    var colSensConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_SENS");

    var anyTableField = template.Fields.FirstOrDefault(f => !f.IsHeaderField);
    if (anyTableField != null)
    {
        int startRowIndex = anyTableField.TargetRowIndex + 1;

        for (int i = startRowIndex; i < fileData.Rows.Count; i++)
        {
            string dateVal = colDateConfig != null && colDateConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colDateConfig.TargetColumnIndex]?.ToString()?.Trim() : null;
            string libelleVal = colLibelleConfig != null && colLibelleConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colLibelleConfig.TargetColumnIndex]?.ToString()?.Trim() : null;
            string dateValeurVal = colDateValConfig != null && colDateValConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colDateValConfig.TargetColumnIndex]?.ToString()?.Trim() : null;
            
            string montantVal = colMontantConfig != null && colMontantConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colMontantConfig.TargetColumnIndex]?.ToString()?.Trim() : null;
            string debitVal = colDebitConfig != null && colDebitConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colDebitConfig.TargetColumnIndex]?.ToString()?.Trim() : null;
            string creditVal = colCreditConfig != null && colCreditConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colCreditConfig.TargetColumnIndex]?.ToString()?.Trim() : null;
            string sensVal = colSensConfig != null && colSensConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colSensConfig.TargetColumnIndex]?.ToString()?.Trim() : null;

            if (string.IsNullOrEmpty(dateVal) && string.IsNullOrEmpty(libelleVal))
            {
                continue;
            }

            // Élimination de la ligne si elle contient le texte global du solde initial (évite les doublons dans les transactions)
            if (!string.IsNullOrEmpty(libelleVal) && libelleVal.Contains("Solde initial", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if ((colDateConfig != null && dateVal.Equals(colDateConfig.AnchorTextValue, StringComparison.OrdinalIgnoreCase)) ||
                (colLibelleConfig != null && libelleVal.Equals(colLibelleConfig.AnchorTextValue, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(libelleVal) && (libelleVal.Contains("Total", StringComparison.OrdinalIgnoreCase) || libelleVal.Contains("Solde", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // =========================================================================
            // ÉTAPE 3 : ALGORITHME DE NORMALISATION DU MONTANT (CORRIGÉ & BLINDÉ)
            // =========================================================================
            string finalDebit = "";
            string finalCredit = "";

            // Fonction utilitaire locale pour nettoyer et standardiser les chaînes de montants
            string CleanAmountStr(string val)
            {
                if (string.IsNullOrEmpty(val)) return "";
                string cleaned = val.Replace(" ", "").Replace("\u00A0", "").Trim(); // Supprime espaces et espaces insécables
                if (cleaned == "-" || cleaned == "0" || cleaned == "0,00" || cleaned == "0.00") return "";
                return cleaned;
            }

            // Nettoyage préalable des variables reçues du DataTable
            debitVal = CleanAmountStr(debitVal);
            creditVal = CleanAmountStr(creditVal);
            montantVal = CleanAmountStr(montantVal);
            string cleanSens = string.IsNullOrEmpty(sensVal) ? "" : sensVal.Trim().ToUpper();

            // -------------------------------------------------------------------------
            // SCÉNARIO 1 : Deux colonnes distinctes Débit et Crédit
            // -------------------------------------------------------------------------
            if (colDebitConfig != null && colCreditConfig != null)
            {
                finalDebit = debitVal;
                finalCredit = creditVal;
            }
            // -------------------------------------------------------------------------
            // SCÉNARIO 2 : Une seule colonne Montant + Une colonne de Sens (C, D, DR, CR, Débit...)
            // -------------------------------------------------------------------------
            else if (colMontantConfig != null && colSensConfig != null && !string.IsNullOrEmpty(cleanSens))
            {
                if (cleanSens.StartsWith("D") || cleanSens.Contains("DEBIT") || cleanSens.Contains("DÉBIT"))
                {
                    finalDebit = montantVal;
                }
                else if (cleanSens.StartsWith("C") || cleanSens.Contains("CREDIT") || cleanSens.Contains("CRÉDIT"))
                {
                    finalCredit = montantVal;
                }
            }
            // -------------------------------------------------------------------------
            // SCÉNARIO 3 : Une seule colonne Montant Signé (Négatif = Débit, Positif = Crédit)
            // -------------------------------------------------------------------------
            else if (colMontantConfig != null && !string.IsNullOrEmpty(montantVal))
            {
                // Remplacement temporaire de la virgule par un point pour l'analyse au format US/Standard
                string parsingTarget = montantVal.Replace(",", ".");

                // Gestion du cas où le signe moins est à la fin (ex: "1500.00-") ou s'il y a des parenthèses "(1500)"
                bool isNegative = parsingTarget.StartsWith("-") || 
                                 parsingTarget.EndsWith("-") || 
                                 (parsingTarget.StartsWith("(") && parsingTarget.EndsWith(")"));

                // Nettoyer les caractères de signe pour ne garder que la valeur absolue
                string absoluteAmount = montantVal.Replace("-", "").Replace("(", "").Replace(")", "").Trim();

                if (isNegative)
                {
                    finalDebit = absoluteAmount;
                    finalCredit = "";
                }
                else
                {
                    finalDebit = "";
                    finalCredit = absoluteAmount;
                }
            }

            statement.Transactions.Add(new TransactionLine
            {
                DateOp = dateVal,
                DateValeur = dateValeurVal,
                Libelle = libelleVal,
                Debit = finalDebit,
                Credit = finalCredit,
                Montant = montantVal
            });
        }
    }

    // =========================================================================
    // ÉTAPE 4 : Application de la formule sur le Solde Initial (Si REVERSE_FIRST_TX)
    // =========================================================================
    var soldeInitConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "SOLDE_INIT");
    
    if (soldeInitConfig != null && !string.IsNullOrEmpty(statement.SoldeInitial) && statement.Transactions.Any())
    {
        // Extraction sécurisée de la propriété de calcul de votre BDD (valeur par défaut : "DIRECT")
        string calculationMethod = soldeInitConfig.CalculationMethod ?? "DIRECT"; 

        if (calculationMethod.Equals("REVERSE_FIRST_TX", StringComparison.OrdinalIgnoreCase))
        {
            // 1. Conversion sécurisée du solde extrait en format décimal
            string formattedSolde = statement.SoldeInitial.Replace(",", ".");
            if (decimal.TryParse(formattedSolde, System.Globalization.CultureInfo.InvariantCulture, out decimal soldeLu))
            {
                // 2. Récupération de la première transaction exécutée
                var firstTx = statement.Transactions.First();
                
                decimal.TryParse(firstTx.Debit?.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture, out decimal firstDebit);
                decimal.TryParse(firstTx.Credit?.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture, out decimal firstCredit);

                // 3. Calcul de l'opération inverse pour obtenir le solde de départ
                // (Solde Initial = Solde après transaction + Débit - Crédit)
                decimal vraiSoldeInitial = soldeLu + firstDebit - firstCredit;

                // 4. Ré-affectation au format chaîne standardisé
                statement.SoldeInitial = vraiSoldeInitial.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    return statement;
}private string CleanRawDate(string rawInput)
{
    if (string.IsNullOrEmpty(rawInput)) return string.Empty;
    return rawInput.Replace("Période du", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Du", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();
}

private string CleanTextForComparison(string input)
{
    if (string.IsNullOrEmpty(input)) return string.Empty;
    
    // Enlever les retours à la ligne, retours chariot et tabulations
    string cleaned = input.Replace("\r", "").Replace("\n", " ").Replace("\t", " ");
    
    // Remplacer les espaces multiples par un seul espace et nettoyer les bords
    return System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim().ToLower();
}
}
}