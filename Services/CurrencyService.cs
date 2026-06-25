using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AfbGenerator.Api.Data;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using AfbGenerator.Api.Entities.Xrt;
namespace AfbGenerator.Api.Services
{
    public class CurrencyService
    {
        private readonly XrtDbContext _context;

        public CurrencyService(XrtDbContext context)
        {
            _context = context;
        }

        // Récupère toutes les devises activement configurées
        public async Task<List<GS_CUR>> GetAllAsync(CancellationToken cancellationToken)
        {
            return await _context.GS_CUR.AsNoTracking().ToListAsync(cancellationToken);
        }

        // Récupère uniquement le nombre de décimales pour un CUR_ID donné
        public async Task<byte> GetDecimalsByCurIdAsync(string curId, CancellationToken cancellationToken)
        {
            var result = await _context.GS_CUR
                .AsNoTracking()
                .Where(c => c.CUR_ID == curId)
                .Select(c => (byte?)c.DECIMALSNUMBER) // Cast temporaire en nullable pour détecter si absent
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
            {
                throw new KeyNotFoundException($"La devise avec l'identifiant '{curId}' n'existe pas.");
            }

            return result.Value;
        }
    }
}