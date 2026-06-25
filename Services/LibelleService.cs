using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text;
using System.Globalization;
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

	public  List<List<string>> ReadWorksheetRows(IFormFile file)
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

	public  int FindHeaderRowIndex(List<List<string>> rows)
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

	private List<List<string>> ReadCsv(IFormFile file)
{
    var rows = new List<List<string>>();

    using var reader = new StreamReader(file.OpenReadStream());

    while (!reader.EndOfStream)
    {
        var line = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line)) continue;

        var separator = line.Contains(";") ? ';' : ',';

        rows.Add(line.Split(separator)
            .Select(x => x.Trim())
            .ToList());
    }

    return rows;
}
public List<List<string>> ReadCsvRows(IFormFile file, char delimiter = ',')
{
    var rows = new List<List<string>>();
    using var stream = file.OpenReadStream();
    using var reader = new StreamReader(stream, Encoding.UTF8);

    while (!reader.EndOfStream)
    {
        var line = reader.ReadLine();
        if (line == null) continue;
        var cells = ParseCsvLine(line, delimiter);
        rows.Add(cells);
    }
    return rows;
}

// Parser CSV robuste qui gère les champs entre guillemets
private List<string> ParseCsvLine(string line, char delimiter = ',')
{
    var cells = new List<string>();
    var sb = new StringBuilder();
    bool inQuotes = false;

    for (int i = 0; i < line.Length; i++)
    {
        char c = line[i];

        if (c == '"')
        {
            inQuotes = !inQuotes;
        }
        else if (c == delimiter && !inQuotes)
        {
            cells.Add(sb.ToString().Trim());
            sb.Clear();
        }
        else
        {
            sb.Append(c);
        }
    }
    cells.Add(sb.ToString().Trim());
    return cells;
}
public decimal ParseFlexibleAmount(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return 0;

    // Supprime les espaces insécables et normaux (séparateurs de milliers style "13 500 000")
    raw = raw.Replace("\u00A0", "").Replace(" ", "");

    // Si virgule ET point présents → format "18,000,000.00" (virgule = milliers, point = décimale)
    if (raw.Contains(',') && raw.Contains('.'))
        raw = raw.Replace(",", "");
    // Si seulement virgule → décimale française "1500,50"
    else if (raw.Contains(','))
        raw = raw.Replace(",", ".");

    decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result);
    return result;
}
private List<List<string>> ReadExcel(IFormFile file)
{
    System.Text.Encoding.RegisterProvider(
        System.Text.CodePagesEncodingProvider.Instance);

    using var stream = file.OpenReadStream();
    using var reader = ExcelReaderFactory.CreateReader(stream);

    var rows = new List<List<string>>();

    while (reader.Read())
    {
        var row = new List<string>();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            row.Add(reader.GetValue(i)?.ToString() ?? "");
        }

        rows.Add(row);
    }

    return rows;
}
public List<List<string>> ReadFile(IFormFile file)
{
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

    if (extension == ".csv")
        return ReadCsv(file);

    if (extension == ".xlsx" || extension == ".xls")
        return ReadExcel(file);

    throw new Exception("Format non supporté");
}

	public  int FindHeaderRowIndexContainingColumns(List<List<string>> rows, string firstHeader, string secondHeader)
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

	public  int FindRowContaining(List<List<string>> rows, string searchText)
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

	public  int FindColumnIndex(List<string> row, string expectedHeader)
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

	public  string GetCell(List<string> row, int columnIndex)
	{
		if (columnIndex < 0 || columnIndex >= row.Count)
		{
			return string.Empty;
		}

		return row[columnIndex];
	}

	public  void EnsureEncodingProviderRegistered()
	{
		if (_encodingProviderRegistered)
		{
			return;
		}

		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		_encodingProviderRegistered = true;
	}

	public  string NormalizeKeyword(string value)
	{
		return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
	}

public async Task<DetectCategorieResult> DetectCategorieAsync(
    string libelle,
    decimal transactionAmount,
    string currentBankCode,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(libelle))
        throw new ArgumentException("Libelle obligatoire");

    var text = NormalizeKeyword(libelle).ToUpperInvariant();

    var databaseMappings = await _dbContext.FluxMappings
        .Where(m => m.IsActive)
        .ToListAsync(cancellationToken);

    var candidates = new List<(FluxMapping item, int score, int position)>();

    foreach (var item in databaseMappings)
    {
        var keywordUpper = item.Keyword.ToUpperInvariant();

        if (!text.Contains(keywordUpper))
            continue;

        // Vérification BankCode
        bool isBankValid = string.IsNullOrWhiteSpace(item.BankCode) ||
                           item.BankCode.Equals(currentBankCode, StringComparison.OrdinalIgnoreCase);
        if (!isBankValid)
            continue;

        // Vérification Montant
        string op = string.IsNullOrWhiteSpace(item.Operator) ? "ANY" : item.Operator.ToUpperInvariant();
        decimal targetAmt = item.TargetAmount ?? 0;
        bool isAmountValid = op == "ANY" || op switch
        {
            "<=" => transactionAmount <= targetAmt,
            "==" => transactionAmount == targetAmt,
            ">=" => transactionAmount >= targetAmt,
            "<"  => transactionAmount < targetAmt,
            ">"  => transactionAmount > targetAmt,
            _    => true
        };

        if (!isAmountValid)
            continue;

        // Score de spécificité (BankCode + Operator)
        int score = 0;
        score += string.IsNullOrWhiteSpace(item.BankCode) ? 0 : 20;
        score += (op == "ANY") ? 0 : 30;

        // ✅ Position du mot-clé dans le libellé
        int position = text.IndexOf(keywordUpper);

        candidates.Add((item, score, position));
    }

    if (candidates.Count == 0)
        return new DetectCategorieResult { Libelle = libelle, IsDetected = false };

    // ✅ Tri final : spécificité d'abord, puis position, puis longueur du mot-clé
    var best = candidates
        .OrderByDescending(c => c.score)                           // 1. Règle la plus spécifique (BankCode + Operator)
        .ThenBy(c => c.position)                                   // 2. Mot-clé le plus tôt dans le libellé
        .ThenByDescending(c => c.item.Keyword.Length)              // 3. Mot-clé le plus long
        .First().item;

    return new DetectCategorieResult
    {
        Libelle = libelle,
        IsDetected = true,
        Flux = best.Flux,
        MotCleDetecte = best.Keyword
    };
}

public async Task<ExcelFluxExtractResult> ExtractAndSaveFluxFromExcelAsync(IFormFile file, CancellationToken cancellationToken = default)
{
    if (file == null || file.Length == 0)
        throw new ArgumentException("Le fichier Excel est obligatoire.");

    // 1. Lecture du fichier et extraction du compte courant (BankCode)
    var rows = ReadWorksheetRows(file);

    var compteRowIndex = FindRowContaining(rows, "Compte courant");
    if (compteRowIndex < 0)
        throw new InvalidOperationException("La ligne contenant 'Compte courant' est introuvable.");

    // Récupération du numéro de compte (index 1 d'après ton exemple)
   
	string bankCode = 
    GetCell(rows[compteRowIndex],1)
    .Trim()
    .Replace(" ","")
    .Replace(",","")
    .Replace("E+19","");
    if (string.IsNullOrWhiteSpace(bankCode))
        throw new InvalidOperationException("Le numéro de compte (BankCode) n'a pas pu être extrait.");

    // 2. Recherche de l'entête des transactions
    var headerRowIndex = FindHeaderRowIndex(rows);
    if (headerRowIndex < 0)
        throw new InvalidOperationException("L'entête des transactions est introuvable.");

    var headerRow = rows[headerRowIndex];
    var libelleCol = FindColumnIndex(headerRow, "LIBELLE");
    if (libelleCol < 0)
        throw new InvalidOperationException("La colonne 'LIBELLE' est manquante.");

    // 3. Extraction et dédoublonnement des Libellés du fichier Excel
    var distinctLibelles = rows
        .Skip(headerRowIndex + 1)
        .Select(row => NormalizeKeyword(GetCell(row, libelleCol)))
        .Where(libelle => !string.IsNullOrWhiteSpace(libelle))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    // 4. Détection des codes Flux uniques pour ce fichier
    var uniqueFluxCodesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    
    foreach (var libelle in distinctLibelles)
    {
        // On appelle ta méthode existante de détection de catégorie
        var detectionResult = await DetectCategorieAsync(libelle,10, "dd",cancellationToken);
        
        if (detectionResult != null && detectionResult.IsDetected && !string.IsNullOrWhiteSpace(detectionResult.Flux))
        {
            // On nettoie la chaîne au cas où il y aurait des espaces (ex: "CLVIR  ")
            uniqueFluxCodesInFile.Add(detectionResult.Flux.Trim());
        }
    }

    // 5. Vérification de l'existence en Base de Données pour éviter les doublons
    // On cherche les flux déjà existants pour cette banque spécifique
    var existingFluxCodesInDb = await _dbContext.Fluxes
        .Where(f => f.BankCode == bankCode)
        .Select(f => f.FluxCode)
        .ToListAsync(cancellationToken);

    var existingFluxSet = new HashSet<string>(existingFluxCodesInDb, StringComparer.OrdinalIgnoreCase);

    // 6. Préparation des entités à insérer
    var fluxesToInsert = new List<Flux>();
    int skippedCount = 0;

    foreach (var fluxCode in uniqueFluxCodesInFile)
    {
        if (existingFluxSet.Contains(fluxCode))
        {
            skippedCount++;
            continue;
        }

        fluxesToInsert.Add(new Flux
        {
            BankCode = bankCode,
            FluxCode = fluxCode,
            FluxLabel = $"Flux {fluxCode}", // Label générique par défaut ou à adapter
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
    }

    // 7. Enregistrement en Base de Données
    if (fluxesToInsert.Count > 0)
    {
        await _dbContext.Fluxes.AddRangeAsync(fluxesToInsert, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // 8. Retour du résultat
    return new ExcelFluxExtractResult
    {
        BankCode = bankCode,
        InsertedFluxCount = fluxesToInsert.Count,
        SkippedFluxCount = skippedCount,
        DetectedFluxCodes = uniqueFluxCodesInFile.ToList()
    };
}
}
