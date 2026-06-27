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
                // On cherche la ligne d'entête dans les 20 premières lignes
                var match = rawLines
                    .Take(20)
                    .Any(line => line.Contains(
                        format.HeaderPattern,
                        StringComparison.OrdinalIgnoreCase));

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