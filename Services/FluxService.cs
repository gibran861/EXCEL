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

	public async Task<FluxResponse> CreateAsync(CreateFluxRequest request, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(request.FluxCode))
		{
			throw new ArgumentException("FluxCode is required.");
		}

		if (string.IsNullOrWhiteSpace(request.FluxLabel))
		{
			throw new ArgumentException("FluxLabel is required.");
		}

		if (string.IsNullOrWhiteSpace(request.BankCode))
		{
			throw new ArgumentException("BankCode is required.");
		}

		if (string.IsNullOrWhiteSpace(request.Cib1) || string.IsNullOrWhiteSpace(request.Cib2))
		{
			throw new ArgumentException("Cib1 and Cib2 are required.");
		}

		var normalizedCode = request.FluxCode.Trim();

		var exists = await _dbContext.Fluxes
			.AnyAsync(x => x.FluxCode == normalizedCode, cancellationToken);

		if (exists)
		{
			throw new InvalidOperationException($"Flux with code '{normalizedCode}' already exists.");
		}

		var flux = new Flux
		{
			FluxCode = normalizedCode,
			FluxLabel = request.FluxLabel.Trim(),
			BankCode = request.BankCode.Trim(),
			Cib1 = request.Cib1.Trim(),
			Cib2 = request.Cib2.Trim(),
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

	public async Task<(int inserted, int updated, List<string> errors)> ImportFluxFromExcelAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        int insertedCount = 0;
        int updatedCount = 0;

        // Configuration pour supporter l'encodage des fichiers Excel
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        using var stream = file.OpenReadStream();
        using var reader = ExcelReaderFactory.CreateReader(stream);

        int rowIndex = 0;
        
        // Dictionnaires pour mapper dynamiquement les colonnes par leur header
        int colCode = -1, colType = -1, colLibelle = -1, colSens = -1;

        while (reader.Read())
        {
            rowIndex++;

            // 1. Lecture de la première ligne (Header) pour trouver les indices des colonnes
            if (rowIndex == 1)
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var headerValue = reader.GetValue(i)?.ToString()?.Trim().ToUpperInvariant();
                    if (headerValue == "CODE") colCode = i;
                    else if (headerValue == "TYPE FLUX") colType = i;
                    else if (headerValue == "LIBELLE") colLibelle = i;
                    else if (headerValue == "SENS") colSens = i;
                }

                if (colCode == -1 || colType == -1 || colLibelle == -1 || colSens == -1)
                {
                    errors.Add("Le fichier Excel ne contient pas tous les headers requis : 'Code', 'Type flux', 'Libelle', 'SENS'.");
                    return (0, 0, errors);
                }
                continue;
            }

            // 2. Extraction des valeurs de la ligne
            var code = reader.GetValue(colCode)?.ToString()?.Trim().ToUpperInvariant();
            var typeFlux = reader.GetValue(colType)?.ToString()?.Trim();
            var libelle = reader.GetValue(colLibelle)?.ToString()?.Trim();
            var sens = reader.GetValue(colSens)?.ToString()?.Trim().ToUpperInvariant();

           
            // 3. Vérification de l'existence en base de données
            var existingFlux = await _dbContext.Flux
                .FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

            if (existingFlux == null)
            {
                // INSERTION
                var newFlux = new NEWFlux
                {
                    Code = code,
                    TypeFlux = typeFlux,
                    Libelle = libelle,
                    Sens = sens,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _dbContext.Flux.AddAsync(newFlux, cancellationToken);
                insertedCount++;
            }
            else
            {
                // MISE À JOUR (si des valeurs ont changé)
                existingFlux.TypeFlux = typeFlux;
                existingFlux.Libelle = libelle;
                existingFlux.Sens = sens;
                updatedCount++;
            }
        }

        // Sauvegarde finale de toutes les lignes traitées
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (insertedCount, updatedCount, errors);
    }
}
