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

    foreach (var template in allTemplates)
    {
        bool isModelMatch = true;
        int headerFieldsCount = 0;
        
        // Variables pour mémoriser le décalage dynamique trouvé sur ce fichier
        int detectedRowOffset = 0; 
        bool offsetCalculated = false;

        // On ne teste d'abord que les en-têtes fixes
        var headerFields = template.Fields.Where(f => f.IsHeaderField).ToList();

        foreach (var field in headerFields)
        {
            headerFieldsCount++;
            bool anchorFoundInFile = false;

            // Au lieu de viser une cellule fixe (1,1), on cherche l'ancre dans les 50 premières lignes
            // et les 5 premières colonnes du fichier pour absorber les décalages !
            int maxRows = Math.Min(fileData.Rows.Count, 50);
            int maxCols = Math.Min(fileData.Columns.Count, 5);

            for (int r = 0; r < maxRows; r++)
            {
                for (int c = 0; c < maxCols; c++)
                {
                    string cellValue = fileData.Rows[r][c]?.ToString()?.Trim();

                    if (!string.IsNullOrEmpty(cellValue) && 
                        cellValue.Contains(field.AnchorTextValue.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        // L'ancre a été localisée !
                        anchorFoundInFile = true;

                        // Si c'est la première ancre trouvée, on calcule l'écart de lignes par rapport à la BDD
                        if (!offsetCalculated)
                        {
                            detectedRowOffset = r - field.AnchorRowIndex;
                            offsetCalculated = true;
                        }

                        // On réajuste à la volée les coordonnées cibles pour l'extraction des données
                        field.AnchorRowIndex = r;
                        field.AnchorColumnIndex = c; // Si l'IHM s'est trompée de colonne, on corrige
                        
                        // Décalage de la cible de la valeur (ex: si la valeur était initialement sur la même ligne)
                        field.TargetRowIndex = field.TargetRowIndex + detectedRowOffset;
                        break;
                    }
                }
                if (anchorFoundInFile) break;
            }

            // Si une seule ancre essentielle (ex: "Période du") n'est pas trouvée, ce modèle est rejeté
            if (!anchorFoundInFile)
            {
                isModelMatch = false;
                break;
            }
        }

        // Si toutes nos ancres ont matché avec succès, on applique l'ajustement aux lignes du tableau
        if (headerFieldsCount > 0 && isModelMatch)
        {
            foreach (var tableField in template.Fields.Where(f => !f.IsHeaderField))
            {
                tableField.TargetRowIndex = tableField.TargetRowIndex + detectedRowOffset;
            }

            return template; // Modèle validé !
        }
    }

    return null; // Aucun modèle correspondant trouvé
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
    // ÉTAPE 1 : Extraction des champs d'en-tête fixes (Période, Compte...)
    // =========================================================================
    foreach (var field in template.Fields.Where(f => f.IsHeaderField && f.FieldKey != "SOLDE_INIT"))
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
        }
    }

    // =========================================================================
    // ÉTAPE 2 : Extraction Générique basée STRICTEMENT sur la structure du Modèle
    // =========================================================================
    var colDateConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_DATE");
    var colLibelleConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_LIBELLE");
    var colMontantConfig = template.Fields.FirstOrDefault(f => f.FieldKey == "TX_MONTANT");

    if (colMontantConfig != null)
    {
        // On commence la boucle juste après la ligne d'en-tête du tableau configurée
        int startRowIndex = colMontantConfig.TargetRowIndex + 1;

        for (int i = startRowIndex; i < fileData.Rows.Count; i++)
        {
            // Récupération dynamique selon les index de colonnes configurés en BDD
            string dateVal = colDateConfig != null && colDateConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colDateConfig.TargetColumnIndex]?.ToString()?.Trim() : null;
            string libelleVal = colLibelleConfig != null && colLibelleConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colLibelleConfig.TargetColumnIndex]?.ToString()?.Trim() : null;
            string montantVal = colMontantConfig != null && colMontantConfig.TargetColumnIndex < fileData.Columns.Count ? fileData.Rows[i][colMontantConfig.TargetColumnIndex]?.ToString()?.Trim() : null;

            // --- CRITÈRE 1 : Interception dynamique du Solde Initial ---
            if (!string.IsNullOrEmpty(libelleVal) && libelleVal.Contains("Solde initial", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(libelleVal, @"-?\d+");
                statement.SoldeInitial = match.Success ? match.Value : string.Empty;
                continue; // Ligne traitée, on passe à la suivante
            }

            // --- CRITÈRE 2 : Vérification de la présence de la structure minimale exigée ---
            // Si l'un des trois piliers obligatoires de la transaction (Date, Libellé) est complètement vide,
            // ce n'est pas une ligne de transaction conforme au modèle (ex: lignes de commentaires sous-jacentes).
            if (string.IsNullOrEmpty(dateVal) || string.IsNullOrEmpty(libelleVal))
            {
                continue;
            }

            // --- CRITÈRE 3 : Élimination de la répétition des en-têtes ---
            // Si la valeur lue est identique au libellé de l'ancre (ex: le mot "Date" ou le mot "Libellé"), on l'ignore.
            if ((colDateConfig != null && dateVal.Equals(colDateConfig.AnchorTextValue, StringComparison.OrdinalIgnoreCase)) ||
                (colLibelleConfig != null && libelleVal.Equals(colLibelleConfig.AnchorTextValue, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // --- CRITÈRE 4 : Élimination des lignes de fin de fichier (Totaux / Synthèses) ---
            // Si le montant ou le libellé correspond à un résumé qui casse la structure d'une transaction,
            // (Par exemple une ligne "Total" n'a pas de date valide, ou si l'IHM a configuré un montant mais qu'ici c'est du texte)
            // Pour valider la structure complète, on s'assure juste que la ligne ne réécrira pas des mots d'ancres connus.
            if (libelleVal.Contains("Total", StringComparison.OrdinalIgnoreCase) || libelleVal.Contains("Solde", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Si la ligne possède bien des données dans les colonnes configurées par le modèle, elle est ajoutée
            statement.Transactions.Add(new TransactionLine
            {
                DateOp = dateVal,
                Libelle = libelleVal,
                Montant = montantVal
            });
        }
    }

    return statement;
}

private string CleanRawDate(string rawInput)
{
    if (string.IsNullOrEmpty(rawInput)) return string.Empty;
    return rawInput.Replace("Période du", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Du", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();
}
}
}