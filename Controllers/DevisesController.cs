using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AfbGenerator.Api.Data; // À adapter selon votre namespace de DbContext
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;

namespace AfbGenerator.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DevisesController : ControllerBase
    {
        private readonly AppDbContext _context; // Remplacez AppDbContext par votre classe de contexte de BDD

        public DevisesController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Récupère la liste de toutes les devises actives
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllActive()
        {
            try
            {
                var devises = await _context.Devises
                    .Where(d => d.IsActive)
                    .OrderBy(d => d.Code)
                    .ToListAsync();

                return Ok(devises);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erreur interne lors de la récupération des devises : {ex.Message}");
            }
        }

        /// <summary>
        /// Ajoute une nouvelle devise dans la base de données
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] CreateDeviseDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // Mettre le code en majuscules pour garantir l'uniformité (ex: xof -> XOF)
                string cleanCode = dto.Code.Trim().ToUpperInvariant();

                // Vérifier si le code existe déjà en BDD
                bool codeExists = await _context.Devises
                    .AnyAsync(d => d.Code.ToUpper() == cleanCode);

                if (codeExists)
                {
                    return BadRequest($"La devise avec le code '{cleanCode}' existe déjà.");
                }

                // Instanciation de l'entité
                var newDevise = new Devise
                {
                    Code = cleanCode,
                    Libelle = dto.Libelle.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Devises.Add(newDevise);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetAllActive), new { id = newDevise.Id }, newDevise);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erreur interne lors de l'ajout de la devise : {ex.Message}");
            }
        }
    }
}