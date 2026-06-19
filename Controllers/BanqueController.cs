using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BanqueController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public BanqueController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // 1. GET : Récupérer toutes les banques configurées
    [HttpGet]
    [ProducesResponseType(typeof(List<Banque>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Banque>>> GetAll(CancellationToken cancellationToken)
    {
        var banques = await _dbContext.Banques
            .AsNoTracking()
            .OrderBy(b => b.CodeBanque)
            .ThenBy(b => b.Filiale)
            .ToListAsync(cancellationToken);

        return Ok(banques);
    }

    // 2. POST : Ajouter une nouvelle configuration de banque
    [HttpPost]
    [ProducesResponseType(typeof(Banque), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Banque>> Add([FromBody] Banque request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Création de l'entité à partir du modèle reçu
        // Création de l'entité à partir du modèle reçu
            var nouvelleBanque = new Banque
            {
                CodeBanque = request.CodeBanque.Trim().ToUpperInvariant(),
                Filiale = request.Filiale.Trim().ToUpperInvariant(),
                TypeFichier = request.TypeFichier?.Trim().ToUpperInvariant(),
                Libelle = request.Libelle.Trim(), // <-- IL MANQUAIT CETTE LIGNE !
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Compte = request.Compte?.Trim()
            };

        // Sauvegarde en base de données
        await _dbContext.Banques.AddAsync(nouvelleBanque, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Retourne un statut 201 Created avec l'objet contenant son nouvel ID
        return CreatedAtAction(nameof(GetAll), new { id = nouvelleBanque.Id }, nouvelleBanque);
    }
}