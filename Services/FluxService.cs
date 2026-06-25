using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using ExcelDataReader;

namespace AfbGenerator.Api.Services;

public class FluxService
{
	private readonly AppDbContext _dbContext;

	public FluxService(AppDbContext dbContext)
	{
		_dbContext = dbContext;
	}
public async Task<FluxResponse2> CreateFluxAsync(CreateFluxRequest2 request, CancellationToken cancellationToken = default)
    {
        // 1. Validations de base (au cas où la validation automatique du contrôleur ne suffit pas)
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("Le code est obligatoire.");

        var normalizedCode = request.Code.Trim().ToUpperInvariant();

        // 2. Vérification des doublons en BDD (le Code est la clé primaire)
        var exists = await _dbContext.Flux.AnyAsync(x => x.Code == normalizedCode, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException($"Le flux avec le code '{normalizedCode}' existe déjà.");
        }

        // 3. Cartographie (Mapping) vers l'entité
        var newFlux = new NEWFlux
        {
            Code = normalizedCode,
            TypeFlux = request.TypeFlux.Trim(),
            Libelle = request.Libelle.Trim(),
            Sens = request.Sens.Trim().ToUpperInvariant(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        // 4. Ajout et sauvegarde
        _dbContext.Flux.Add(newFlux);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 5. Retour du résultat mappé en FluxResponse
        return new FluxResponse2
        {
            Code = newFlux.Code,
            TypeFlux = newFlux.TypeFlux,
            Libelle = newFlux.Libelle,
            Sens = newFlux.Sens,
            IsActive = newFlux.IsActive,
            CreatedAt = newFlux.CreatedAt
        };
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
{
    var flux = await _dbContext.Fluxes.FindAsync(new object[] { id }, cancellationToken);
    if (flux == null) 
        throw new KeyNotFoundException($"Le flux avec l'ID {id} n'existe pas.");

    _dbContext.Fluxes.Remove(flux);
    await _dbContext.SaveChangesAsync(cancellationToken);
}
	public async Task<FluxResponse> CreateAsync(CreateFluxRequest request, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(request.FluxCode))
    {
        throw new ArgumentException("FluxCode is required.");
    }

    if (string.IsNullOrWhiteSpace(request.BankCode))
    {
        throw new ArgumentException("BankCode is required.");
    }

    var normalizedFluxCode = request.FluxCode.Trim();
    var normalizedBankCode = request.BankCode.Trim();

    // ── CORRECTION ICI : On vérifie si la COMBINAISON existe déjà ──
    var exists = await _dbContext.Fluxes
        .AnyAsync(x => x.FluxCode == normalizedFluxCode && x.BankCode == normalizedBankCode, cancellationToken);

    if (exists)
    {
        throw new InvalidOperationException($"A flux with code '{normalizedFluxCode}' already exists for bank '{normalizedBankCode}'.");
    }

    var flux = new Flux
    {
        FluxCode = normalizedFluxCode,
        BankCode = normalizedBankCode,
        // Sécurisation contre les valeurs nulles venant du Front
        FluxLabel = string.IsNullOrWhiteSpace(request.FluxLabel) ? normalizedFluxCode : request.FluxLabel.Trim(),
        Cib1 = request.Cib1?.Trim() ?? string.Empty,
        Cib2 = request.Cib2?.Trim() ?? string.Empty,
        IsActive = request.IsActive,
        CreatedAt = DateTime.UtcNow
    };

    _dbContext.Fluxes.Add(flux);
    await _dbContext.SaveChangesAsync(cancellationToken);

    return Map(flux);
}

	public async Task<List<FluxResponse>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		return await _dbContext.Fluxes
			.OrderBy(x => x.Id)
			.Select(x => new FluxResponse
			{
				Id = x.Id,
				FluxCode = x.FluxCode,
				FluxLabel = x.FluxLabel,
				BankCode = x.BankCode,
				Cib1 = x.Cib1,
				Cib2 = x.Cib2,
				IsActive = x.IsActive,
				CreatedAt = x.CreatedAt
			})
			.ToListAsync(cancellationToken);
	}
public async Task<FluxResponse> SetCibByBankAndFluxAsync(
    string bankCode, 
    string fluxCode, 
    string cib1, 
    string cib2, 
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(bankCode))
        throw new ArgumentException("Le code banque est obligatoire.");

    if (string.IsNullOrWhiteSpace(fluxCode))
        throw new ArgumentException("Le code flux est obligatoire.");

    // Recherche de la ligne spécifique associant cette banque ET ce flux
    var fluxEntity = await _dbContext.Fluxes
        .FirstOrDefaultAsync(x => x.BankCode == bankCode && x.FluxCode == fluxCode, cancellationToken);

    if (fluxEntity == null)
    {
        throw new KeyNotFoundException($"Le flux '{fluxCode}' pour la banque '{bankCode}' est introuvable.");
    }

    // Mise à jour des valeurs CIB
    fluxEntity.Cib1 = cib1 ?? string.Empty;
    fluxEntity.Cib2 = cib2 ?? string.Empty;

    // Sauvegarde en base de données
    await _dbContext.SaveChangesAsync(cancellationToken);

    // Retour de la réponse mappée (à adapter selon ta structure réelle de FluxResponse)
    return new FluxResponse
    {
        Id = fluxEntity.Id,
        BankCode = fluxEntity.BankCode,
        FluxCode = fluxEntity.FluxCode,
        FluxLabel = fluxEntity.FluxLabel,
        Cib1 = fluxEntity.Cib1,
        Cib2 = fluxEntity.Cib2,
        IsActive = fluxEntity.IsActive
    };
}
	public async Task<FluxResponse> SetCibByFluxCodeAsync(string fluxCode, string cib1, string cib2, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(fluxCode))
		{
			throw new ArgumentException("Flux code is required.");
		}

		if (string.IsNullOrWhiteSpace(cib1) || string.IsNullOrWhiteSpace(cib2))
		{
			throw new ArgumentException("Cib1 and Cib2 are required.");
		}

		var normalizedCode = fluxCode.Trim();

		var flux = await _dbContext.Fluxes
			.FirstOrDefaultAsync(x => x.FluxCode == normalizedCode, cancellationToken);

		if (flux == null)
		{
			throw new KeyNotFoundException($"Flux with code '{normalizedCode}' does not exist.");
		}

		flux.Cib1 = cib1.Trim();
		flux.Cib2 = cib2.Trim();

		await _dbContext.SaveChangesAsync(cancellationToken);

		return Map(flux);
	}

	private static FluxResponse Map(Flux flux)
	{
		return new FluxResponse
		{
			Id = flux.Id,
			FluxCode = flux.FluxCode,
			FluxLabel = flux.FluxLabel,
			BankCode = flux.BankCode,
			Cib1 = flux.Cib1,
			Cib2 = flux.Cib2,
			IsActive = flux.IsActive,
			CreatedAt = flux.CreatedAt
		};
	}

	public async Task<(int inserted, int updated, int skipped, List<object> duplicates, List<string> errors)> ImportFluxFromExcelAsync(
    IFormFile file,
    string? codesAMettreAJour,
    CancellationToken cancellationToken)
{
    var errors = new List<string>();
    var duplicates = new List<object>();
    
    int insertedCount = 0;
    int updatedCount = 0;
    int skippedCount = 0;

    // 1. Désérialiser la liste des codes à mettre à jour
    var codesToUpdate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(codesAMettreAJour))
    {
        var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(codesAMettreAJour);
        if (parsed != null)
        {
            foreach (var c in parsed) codesToUpdate.Add(c.Trim());
        }
    }

    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    using var stream = file.OpenReadStream();
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    IExcelDataReader reader;

    if (extension == ".csv") reader = ExcelReaderFactory.CreateCsvReader(stream);
    else if (extension == ".xls" || extension == ".xlsx") reader = ExcelReaderFactory.CreateReader(stream);
    else
    {
        errors.Add("Format de fichier non supporté.");
        return (0, 0, 0, duplicates, errors);
    }

    // Structure temporaire pour stocker les lignes du fichier lues en mémoire
    var fileRows = new List<(string Code, string? TypeFlux, string? Libelle, string? Sens)>();

    using (reader)
    {
        int rowIndex = 0;
        int colCode = -1, colType = -1, colLibelle = -1, colSens = -1;

        while (reader.Read())
        {
            rowIndex++;

            // Lecture de l'entête
            if (rowIndex == 1)
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var headerValue = reader.GetValue(i)?.ToString()?.Trim().ToUpperInvariant();

                    if (headerValue == "CODE") colCode = i;
                    else if (headerValue == "TYPE FLUX" || headerValue == "TYPEFLUX") colType = i;
                    else if (headerValue == "LIBELLE" || headerValue == "LIBELLÉ") colLibelle = i;
                    else if (headerValue == "SENS") colSens = i;
                }

                if (colCode == -1 || colType == -1 || colLibelle == -1 || colSens == -1)
                {
                    errors.Add("Le fichier doit contenir les colonnes : Code, Type flux, Libelle, SENS");
                    return (0, 0, 0, duplicates, errors);
                }
                continue;
            }

            // Lecture des données
            var code = reader.GetValue(colCode)?.ToString()?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code)) continue;

            var typeFlux = reader.GetValue(colType)?.ToString()?.Trim();
            var libelle = reader.GetValue(colLibelle)?.ToString()?.Trim();
            var sens = reader.GetValue(colSens)?.ToString()?.Trim().ToUpperInvariant();

            fileRows.Add((code, typeFlux, libelle, sens));
        }
    }

    if (!fileRows.Any())
    {
        errors.Add("Le fichier est vide.");
        return (0, 0, 0, duplicates, errors);
    }

    // 2. Collecter tous les codes uniques du fichier pour requêter la BDD une seule fois
    var codesInFile = fileRows.Select(r => r.Code).Distinct().ToList();

    var existingFluxDict = await _dbContext.Flux
        .Where(x => codesInFile.Contains(x.Code))
        .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

    var toInsert = new List<NEWFlux>();

    // 3. Traitement des lignes
    foreach (var row in fileRows)
    {
        if (existingFluxDict.TryGetValue(row.Code, out var existingFlux))
        {
            // Si le code est dans la liste des éléments à écraser/mettre à jour
            if (codesToUpdate.Contains(row.Code))
            {
                existingFlux.TypeFlux = row.TypeFlux;
                existingFlux.Libelle = row.Libelle;
                existingFlux.Sens = row.Sens;
                updatedCount++;
            }
            else
            {
                // Sinon, on ignore et on l'ajoute aux doublons détectés pour le front-end
                duplicates.Add(new { code = row.Code, libelle = row.Libelle });
                skippedCount++;
            }
        }
        else
        {
            // Nouvel enregistrement
            toInsert.Add(new NEWFlux
            {
                Code = row.Code,
                TypeFlux = row.TypeFlux,
                Libelle = row.Libelle,
                Sens = row.Sens,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            insertedCount++;
        }
    }

    // 4. Sauvegarde groupée en base de données
    if (toInsert.Any())
    {
        await _dbContext.Flux.AddRangeAsync(toInsert, cancellationToken);
    }

    await _dbContext.SaveChangesAsync(cancellationToken);

    return (
        insertedCount,
        updatedCount,
        skippedCount,
        duplicates,
        errors
    );
}
}
