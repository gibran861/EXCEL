using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text;

namespace AfbGenerator.Api.Services;

public class LibelleService
{
	private readonly AppDbContext _dbContext;
	private static bool _encodingProviderRegistered;

	public LibelleService(AppDbContext dbContext)
	{
		_dbContext = dbContext;
		EnsureEncodingProviderRegistered();
	}

	public async Task<LibelleImportResult> ImportFromExcelAsync(IFormFile file, int? fluxId, CancellationToken cancellationToken = default)
	{
		if (file == null || file.Length == 0)
		{
			throw new ArgumentException("Excel file is required.");
		}

		if (fluxId.HasValue)
		{
			var fluxExists = await _dbContext.Fluxes.AnyAsync(x => x.Id == fluxId.Value, cancellationToken);
			if (!fluxExists)
			{
				throw new KeyNotFoundException($"Flux with id {fluxId.Value} does not exist.");
			}
		}

		var rows = ReadWorksheetRows(file);

		var headerRowIndex = FindHeaderRowIndex(rows);
		if (headerRowIndex < 0)
		{
			throw new InvalidOperationException("Header row with LIBELLE column was not found.");
		}

		var libelleColumn = FindColumnIndex(rows[headerRowIndex], "LIBELLE");
		if (libelleColumn < 0)
		{
			throw new InvalidOperationException("LIBELLE column was not found.");
		}

		var detectedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var rowsRead = 0;

		foreach (var row in rows.Skip(headerRowIndex + 1))
		{
			var keyword = NormalizeKeyword(GetCell(row, libelleColumn));
			if (string.IsNullOrWhiteSpace(keyword))
			{
				continue;
			}

			detectedKeywords.Add(keyword);
			rowsRead++;
		}

		var existingKeywords = await _dbContext.Libelles
			.Where(x => x.FluxId == fluxId)
			.Select(x => x.Keyword)
			.ToListAsync(cancellationToken);

		var existingSet = new HashSet<string>(existingKeywords.Select(NormalizeKeyword), StringComparer.OrdinalIgnoreCase);
		var toInsert = new List<Libelle>();

		foreach (var keyword in detectedKeywords)
		{
			if (existingSet.Contains(keyword))
			{
				continue;
			}

			toInsert.Add(new Libelle
			{
				Keyword = keyword,
				FluxId = fluxId,
				Priority = 1,
				IsActive = true,
				CreatedAt = DateTime.UtcNow
			});
		}

		if (toInsert.Count > 0)
		{
			await _dbContext.Libelles.AddRangeAsync(toInsert, cancellationToken);
			await _dbContext.SaveChangesAsync(cancellationToken);
		}

		return new LibelleImportResult
		{
			FluxId = fluxId,
			RowsRead = rowsRead,
			DistinctLibellesFound = detectedKeywords.Count,
			InsertedCount = toInsert.Count,
			SkippedExistingCount = detectedKeywords.Count - toInsert.Count
		};
	}

	public async Task<ExcelStatementExtractResult> ExtractImportantDataAsync(IFormFile file, int? fluxId, CancellationToken cancellationToken = default)
	{
		if (file == null || file.Length == 0)
		{
			throw new ArgumentException("Excel file is required.");
		}

		if (fluxId.HasValue)
		{
			var fluxExists = await _dbContext.Fluxes.AnyAsync(x => x.Id == fluxId.Value, cancellationToken);
			if (!fluxExists)
			{
				throw new KeyNotFoundException($"Flux with id {fluxId.Value} does not exist.");
			}
		}

		var rows = ReadWorksheetRows(file);

		var compteRowIndex = FindRowContaining(rows, "Compte courant");
		if (compteRowIndex < 0)
		{
			throw new InvalidOperationException("Row containing 'Compte courant' was not found.");
		}

		var headerRowIndex = FindHeaderRowIndex(rows);
		if (headerRowIndex < 0)
		{
			throw new InvalidOperationException("Header row with transaction columns was not found.");
		}

		var headerRow = rows[headerRowIndex];
		var dateOperationCol = FindColumnIndex(headerRow, "DATE OPERATION");
		var montantCol = FindColumnIndex(headerRow, "MONTANT");
		var deviseCol = FindColumnIndex(headerRow, "DEVISE");
		var libelleCol = FindColumnIndex(headerRow, "LIBELLE");
		var infoComplementaireCol = FindColumnIndex(headerRow, "INFO COMPLEMENTAIRE");
		var dateValeurCol = FindColumnIndex(headerRow, "DATE VALEUR");

		if (dateOperationCol < 0 || montantCol < 0 || deviseCol < 0 || libelleCol < 0 || infoComplementaireCol < 0 || dateValeurCol < 0)
		{
			throw new InvalidOperationException("One or more required columns are missing in the transaction header.");
		}

		var transactions = new List<ExcelTransactionItem>();
		foreach (var row in rows.Skip(headerRowIndex + 1))
		{
			var libelle = NormalizeKeyword(GetCell(row, libelleCol));
			var montant = GetCell(row, montantCol).Trim();
			var dateOperation = GetCell(row, dateOperationCol).Trim();

			if (string.IsNullOrWhiteSpace(libelle) && string.IsNullOrWhiteSpace(montant) && string.IsNullOrWhiteSpace(dateOperation))
			{
				continue;
			}

			transactions.Add(new ExcelTransactionItem
			{
				DateOperation = dateOperation,
				Montant = montant,
				Devise = GetCell(row, deviseCol).Trim(),
				Libelle = libelle,
				InfoComplementaire = GetCell(row, infoComplementaireCol).Trim(),
				DateValeur = GetCell(row, dateValeurCol).Trim()
			});
		}

		var distinctLibelles = transactions
			.Select(x => x.Libelle)
			.Where(x => !string.IsNullOrWhiteSpace(x))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(x => x)
			.ToList();

		var insertedCount = 0;
		var skippedExistingCount = 0;

		var existingKeywords = await _dbContext.Libelles
			.Where(x => x.FluxId == fluxId)
			.Select(x => x.Keyword)
			.ToListAsync(cancellationToken);

		var existingSet = new HashSet<string>(existingKeywords.Select(NormalizeKeyword), StringComparer.OrdinalIgnoreCase);
		var toInsert = new List<Libelle>();

		foreach (var keyword in distinctLibelles)
		{
			if (existingSet.Contains(keyword))
			{
				skippedExistingCount++;
				continue;
			}

			toInsert.Add(new Libelle
			{
				Keyword = keyword,
				FluxId = fluxId,
				Priority = 1,
				IsActive = true,
				CreatedAt = DateTime.UtcNow
			});
		}

		if (toInsert.Count > 0)
		{
			await _dbContext.Libelles.AddRangeAsync(toInsert, cancellationToken);
			await _dbContext.SaveChangesAsync(cancellationToken);
		}

		insertedCount = toInsert.Count;

		return new ExcelStatementExtractResult
		{
			FluxId = fluxId,
			CompteCourant = GetCell(rows[compteRowIndex], 1).Trim(),
			MontantInitial = GetCell(rows[compteRowIndex], 2).Trim(),
			MontantFinal = GetCell(rows[compteRowIndex], 3).Trim(),
			DateDebut = GetCell(rows[compteRowIndex], 4).Trim(),
			DateFin = GetCell(rows[compteRowIndex], 5).Trim(),
			TransactionsCount = transactions.Count,
			InsertedCount = insertedCount,
			SkippedExistingCount = skippedExistingCount,
			Transactions = transactions,
			DistinctLibelles = distinctLibelles
		};
	}
	public async Task<List<Libelle>> GetAllLibellesAsync(CancellationToken cancellationToken = default)
{
    return await _dbContext.Libelles
        .AsNoTracking()
        .Include(x => x.Flux) // Permet d'avoir les détails du Flux associé
        .OrderBy(x => x.Keyword)
        .ToListAsync(cancellationToken);
}

	public async Task<LibelleFluxMappingImportResult> ImportFluxMappingFromExcelAsync(IFormFile file, CancellationToken cancellationToken = default)
	{
		if (file == null || file.Length == 0)
		{
			throw new ArgumentException("Excel file is required.");
		}

		var rows = ReadWorksheetRows(file);

		var headerRowIndex = FindHeaderRowIndexContainingColumns(rows, "LIBELLE", "CODE FLUX");
		if (headerRowIndex < 0)
		{
			throw new InvalidOperationException("Header row with LIBELLE and CODE FLUX columns was not found.");
		}

		var headerRow = rows[headerRowIndex];
		var libelleColumn = FindColumnIndex(headerRow, "LIBELLE");
		var codeFluxColumn = FindColumnIndex(headerRow, "CODE FLUX");

		if (libelleColumn < 0 || codeFluxColumn < 0)
		{
			throw new InvalidOperationException("LIBELLE or CODE FLUX column was not found.");
		}

		var fluxes = await _dbContext.Fluxes
			.AsNoTracking()
			.ToDictionaryAsync(x => x.FluxCode, StringComparer.OrdinalIgnoreCase, cancellationToken);

		var existingLibelles = await _dbContext.Libelles.ToListAsync(cancellationToken);

		var rowsRead = 0;
		var insertedCount = 0;
		var updatedCount = 0;
		var skippedCount = 0;

		foreach (var row in rows.Skip(headerRowIndex + 1))
		{
			var keyword = NormalizeKeyword(GetCell(row, libelleColumn));
			var fluxCode = NormalizeKeyword(GetCell(row, codeFluxColumn));

			if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrWhiteSpace(fluxCode))
			{
				skippedCount++;
				continue;
			}

			rowsRead++;

			if (!fluxes.TryGetValue(fluxCode, out var flux))
			{
				skippedCount++;
				continue;
			}

			var existing = existingLibelles.FirstOrDefault(x =>
				string.Equals(NormalizeKeyword(x.Keyword), keyword, StringComparison.OrdinalIgnoreCase));

			if (existing == null)
			{
				var libelle = new Libelle
				{
					Keyword = keyword,
					FluxId = flux.Id,
					Priority = 1,
					IsActive = true,
					CreatedAt = DateTime.UtcNow
				};

				_dbContext.Libelles.Add(libelle);
				existingLibelles.Add(libelle);
				insertedCount++;
				continue;
			}

			if (existing.FluxId != flux.Id)
			{
				existing.FluxId = flux.Id;
				updatedCount++;
			}
			else
			{
				skippedCount++;
			}
		}

		if (insertedCount > 0 || updatedCount > 0)
		{
			await _dbContext.SaveChangesAsync(cancellationToken);
		}

		return new LibelleFluxMappingImportResult
		{
			RowsRead = rowsRead,
			InsertedCount = insertedCount,
			UpdatedCount = updatedCount,
			SkippedCount = skippedCount
		};
	}

	public async Task<ResolveFluxByLibelleResult> ResolveFluxByLibelleAsync(string libelle, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(libelle))
		{
			throw new ArgumentException("Libelle is required.");
		}

		var normalizedLibelle = NormalizeKeyword(libelle).ToUpperInvariant();

		var candidates = await _dbContext.Libelles
			.AsNoTracking()
			.Where(x => x.IsActive)
			.Include(x => x.Flux)
			.OrderByDescending(x => x.Priority)
			.ThenByDescending(x => x.Keyword.Length)
			.ToListAsync(cancellationToken);

		var match = candidates.FirstOrDefault(x =>
			!string.IsNullOrWhiteSpace(x.Keyword) &&
			normalizedLibelle.Contains(NormalizeKeyword(x.Keyword).ToUpperInvariant(), StringComparison.Ordinal));

		if (match == null || match.FluxId == null || match.Flux == null)
		{
			return new ResolveFluxByLibelleResult
			{
				Libelle = libelle,
				IsMatched = false
			};
		}

		return new ResolveFluxByLibelleResult
		{
			Libelle = libelle,
			IsMatched = true,
			MatchedKeyword = match.Keyword,
			FluxId = match.FluxId,
			FluxCode = match.Flux.FluxCode,
			FluxLabel = match.Flux.FluxLabel
		};
	}

	private static List<List<string>> ReadWorksheetRows(IFormFile file)
	{
		try
		{
			using var stream = file.OpenReadStream();
			using var reader = ExcelReaderFactory.CreateReader(stream);
			var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
			{
				ConfigureDataTable = _ => new ExcelDataTableConfiguration
				{
					UseHeaderRow = false
				}
			});

			if (dataSet.Tables.Count == 0)
			{
				throw new InvalidOperationException("Excel file does not contain any worksheet.");
			}

			var table = dataSet.Tables[0];
			var rows = new List<List<string>>();
			foreach (DataRow dataRow in table.Rows)
			{
				var cells = new List<string>();
				for (var i = 0; i < table.Columns.Count; i++)
				{
					cells.Add(dataRow[i]?.ToString()?.Trim() ?? string.Empty);
				}

				rows.Add(cells);
			}

			return rows;
		}
		catch (Exception ex) when (
			ex.GetType().Name.Contains("HeaderException", StringComparison.OrdinalIgnoreCase) ||
			ex.GetType().Name.Contains("ExcelReaderException", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Invalid Excel format. Please upload a valid .xls or .xlsx file.");
		}
		catch (IOException)
		{
			throw new InvalidOperationException("The uploaded file cannot be read as an Excel document. Please verify the file and try again.");
		}
		catch (Exception ex) when (ex.GetType().Name.Contains("FileFormatException", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Invalid Excel format. Please upload a valid .xls or .xlsx file.");
		}
	}

	private static int FindHeaderRowIndex(List<List<string>> rows)
	{
		for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
		{
			var row = rows[rowIndex];
			var hasLibelle = row.Any(c => string.Equals(c, "LIBELLE", StringComparison.OrdinalIgnoreCase));
			var hasDateOperation = row.Any(c => string.Equals(c, "DATE OPERATION", StringComparison.OrdinalIgnoreCase));

			if (hasLibelle && hasDateOperation)
			{
				return rowIndex;
			}
		}

		return -1;
	}

	private static int FindHeaderRowIndexContainingColumns(List<List<string>> rows, string firstHeader, string secondHeader)
	{
		for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
		{
			var row = rows[rowIndex];
			var hasFirst = row.Any(c => string.Equals(c, firstHeader, StringComparison.OrdinalIgnoreCase));
			var hasSecond = row.Any(c => string.Equals(c, secondHeader, StringComparison.OrdinalIgnoreCase));

			if (hasFirst && hasSecond)
			{
				return rowIndex;
			}
		}

		return -1;
	}

	private static int FindRowContaining(List<List<string>> rows, string searchText)
	{
		for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
		{
			var found = rows[rowIndex].Any(c =>
				string.Equals(c, searchText, StringComparison.OrdinalIgnoreCase));

			if (found)
			{
				return rowIndex;
			}
		}

		return -1;
	}

	private static int FindColumnIndex(List<string> row, string expectedHeader)
	{
		for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
		{
			if (string.Equals(row[columnIndex], expectedHeader, StringComparison.OrdinalIgnoreCase))
			{
				return columnIndex;
			}
		}

		return -1;
	}

	private static string GetCell(List<string> row, int columnIndex)
	{
		if (columnIndex < 0 || columnIndex >= row.Count)
		{
			return string.Empty;
		}

		return row[columnIndex];
	}

	private static void EnsureEncodingProviderRegistered()
	{
		if (_encodingProviderRegistered)
		{
			return;
		}

		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		_encodingProviderRegistered = true;
	}

	private static string NormalizeKeyword(string value)
	{
		return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
	}

	public async Task<DetectCategorieResult> DetectCategorieAsync(
    string libelle,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(libelle))
        throw new ArgumentException("Libelle obligatoire");


    var text = NormalizeKeyword(libelle).ToUpperInvariant();


    var categories = new List<(string Categorie, string Flux, string[] Keywords)>
    {
        (
            "CHEQUE EMIS",
            "DECA",
            new[]
            {
                "CHQ",
                "CHEQUE",
                "REMISE CHQ",
                "REGL CHQ",
                "RETRAIT CHQ",
                "RTRT CHQ",
                "DEPOT CHQ",
                "CERTIF CHQ"
            }
        ),

        (
            "VIREMENT SALAIRE",
            "DECA",
            new[]
            {
                "VIREMENT SALAIRE",
                "VIREMENT SALAIRES",
                "SALAIRE"
            }
        ),

        (
            "APPRO COMPTE",
            "DECA",
            new[]
            {
                "APPRO COMPTE",
                "APPRO CPTE"
            }
        ),

        (
            "FRAIS BANCAIRES",
            "DECA",
            new[]
            {
                "FRAIS GESTION",
                "FRAIS TENUE",
                "TAF-FRAI",
                "TAF FRAIS",
                "TARIF FRAIS",
                "FRAIS OMNI"
            }
        ),

        (
            "FRAIS VIREMENT",
            "DECA",
            new[]
            {
                "FRAIS VRT",
                "FRAIS VIRMNT",
                "FRAIS INTERBANCAIRE",
                "FRAIS / VIREMENT",
                "DEBIT FRAIS VRT"
            }
        ),

        (
            "FRAIS CHEQUE CERTIF",
            "DECA",
            new[]
            {
                "COMMISS°CERTIF CHQ",
                "TAXE CERTIF CHQ",
                "NIF TRSF CHQ",
                "ANNULAT°RM CHQ"
            }
        ),

        (
            "VIREMENT TIERS",
            "DECA",
            new[]
            {
                "VIRT",
                "VIRMNT",
                "VIREMENT",
                "TRF DE FONDS",
                "TRANSFERT DE FONDS",
                "TRSF",
                "COMMISSION/PAIEMENT",
                "SOLDE TT COMPTE"
            }
        ),

        (
            "INDEMNITE CONGES",
            "DECA",
            new[]
            {
                "INDEMNITE CONGES",
                "CONGE"
            }
        ),

        (
            "CASH MOBILE",
            "DECA",
            new[]
            {
                "CASH CLIENT PR MOBILE",
                "CASH MOBILE"
            }
        ),

        (
            "VERSEMENT ESPECES",
            "ENC",
            new[]
            {
                "VERSEMENT ESPECE",
                "VERSEMENT ESP",
                "VERS ESP",
                "VERS ESPECES",
                "VERST ESPECE"
            }
        ),

        (
            "CHEQUE RECU",
            "ENC",
            new[]
            {
                "REMISE CHEQUE",
                "REMISE CHQ",
                "REMISE CHEQ"
            }
        ),

        (
            "VIREMENT RECU",
            "ENC",
            new[]
            {
                "TRANSF PR MOBILE",
                "TRSF MOBILE",
                "TRF FOND",
                "VIREMENT RECU"
            }
        ),

        (
            "REJET CHQ RECU",
            "ENC",
            new[]
            {
                "REJET COMPENSE"
            }
        )
    };


    foreach(var item in categories)
    {
        foreach(var keyword in item.Keywords)
        {
            if(text.Contains(keyword.ToUpperInvariant()))
            {
                return new DetectCategorieResult
                {
                    Libelle = libelle,
                    IsDetected = true,
                    Categorie = item.Categorie,
                    Flux = item.Flux,
                    MotCleDetecte = keyword
                };
            }
        }
    }


    return new DetectCategorieResult
    {
        Libelle = libelle,
        IsDetected = false
    };
}
}
