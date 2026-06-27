using System.Text.Json;
using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AfbGenerator.Api.Services
{
    public class BankFileFormatService
    {
        private readonly AppDbContext _context;

        public BankFileFormatService(AppDbContext context)
        {
            _context = context;
        }

        // Détecte le format en cherchant HeaderPattern dans les premières lignes
       public async Task<BankFileFormat?> DetectFormatAsync(
    List<string> rawLines,
    CancellationToken ct = default)
{
    var formats = await _context.BankFileFormats
        .Where(f => f.IsActive)
        .ToListAsync(ct);

    foreach (var format in formats)
    {
        // On inspecte les 25 premières lignes pour être sûr de couvrir les gros en-têtes
        var match = rawLines
            .Take(25)
            .Any(line => 
            {
                if (string.IsNullOrWhiteSpace(line)) return false;

                // 1. Nettoyage de la ligne brute (on enlève les guillemets et espaces superflus)
                string cleanedLine = line.Replace("\"", "").Trim();

                // 2. Test direct avec le pattern configuré
                if (cleanedLine.Contains(format.HeaderPattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 3. Sécurité additionnelle : Si le pattern contient des virgules (ex: "Montants,Solde"),
                // on teste aussi en remplaçant par des points-virgules au cas où le fichier aurait changé de séparateur.
                var alternativePattern = format.HeaderPattern.Replace(",", ";");
                if (cleanedLine.Contains(alternativePattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            });

        if (match) return format;
    }

    return null; // format inconnu
}

        public ColumnMapping DeserializeMapping(BankFileFormat format)
        {
            return JsonSerializer.Deserialize<ColumnMapping>(
                format.ColumnMappingsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new ColumnMapping();
        }
    }
}