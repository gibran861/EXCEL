namespace AfbGenerator.Api.Models;

public class FluxImportResult
{
    /// <summary>
    /// Nombre total de lignes de données trouvées dans le fichier Excel (hors en-tête).
    /// </summary>
    public int TotalRowsProcessed { get; set; }

    /// <summary>
    /// Nombre de nouveaux flux insérés avec succès en base de données.
    /// </summary>
    public int InsertedCount { get; set; }

    /// <summary>
    /// Nombre de lignes ignorées (doublons déjà présents en BDD ou lignes dupliquées au sein du fichier Excel).
    /// </summary>
    public int SkippedExistingCount { get; set; }
}