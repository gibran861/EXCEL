using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AfbGenerator.Api.Models;

namespace AfbGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NEWFLUXCONTROLLER : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly FluxService _fluxService;

    public NEWFLUXCONTROLLER(AppDbContext dbContext, FluxService fluxService)
    {
        _dbContext = dbContext;
        _fluxService = fluxService;
    }

    // 1. GET : Récupérer la liste de tous les flux
    [HttpGet]
    [ProducesResponseType(typeof(List<Flux>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Flux>>> GetAll(CancellationToken cancellationToken)
    {
        var fluxList = await _dbContext.Flux
            .AsNoTracking()
            .OrderBy(f => f.Sens)
            .ThenBy(f => f.Code)
            .ToListAsync(cancellationToken);

        return Ok(fluxList);
    }

    [HttpGet("{code}/libelle")]
    public async Task<ActionResult<string>> GetLibelleByCode(string code)
    {
        // Recherche du flux par son code (Key) dans la base de données
        var flux = await _dbContext.Flux.FindAsync(code);

        if (flux == null)
        {
            return NotFound($"Aucun flux trouvé avec le code : {code}");
        }

        // Retourne uniquement le libellé
        return Ok(flux.Libelle);
    }

    [HttpPost]
    [ProducesResponseType(typeof(NEWFlux), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NEWFlux>> Add(
        [FromBody] NEWFlux request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Vérifier si le code existe déjà
        var existe = await _dbContext.Flux
            .AnyAsync(x => x.Code == request.Code, cancellationToken);

        if (existe)
        {
            return BadRequest($"Le flux {request.Code} existe déjà");
        }

        var nouveauFlux = new NEWFlux
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            TypeFlux = request.TypeFlux.Trim().ToUpperInvariant(),
            Libelle = request.Libelle.Trim(),
            Sens = request.Sens.Trim().ToUpperInvariant(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.Flux.AddAsync(nouveauFlux, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetAll),
            new { code = nouveauFlux.Code },
            nouveauFlux
        );
    }


[HttpPut("{id:int}")] // On passe par l'ID numérique maintenant
public async Task<IActionResult> Update(int id, [FromBody] NEWFlux request, CancellationToken cancellationToken)
{
    if (id != request.Id)
    {
        return BadRequest("L'Id dans l'URL ne correspond pas à l'objet fourni.");
    }

    if (!ModelState.IsValid) return BadRequest(ModelState);

    // Recherche par ID
    var fluxExistant = await _dbContext.Flux
        .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    if (fluxExistant == null)
    {
        return NotFound($"Aucun flux trouvé avec l'Id : {id}");
    }

    // Le Code peut potentiellement être modifié maintenant s'il n'est plus clé primaire !
    fluxExistant.Code = request.Code.Trim().ToUpperInvariant(); 
    fluxExistant.TypeFlux = request.TypeFlux.Trim().ToUpperInvariant();
    fluxExistant.Libelle = request.Libelle.Trim();
    fluxExistant.Sens = request.Sens.Trim().ToUpperInvariant();
    fluxExistant.IsActive = request.IsActive;

    await _dbContext.SaveChangesAsync(cancellationToken);
    return NoContent();
}

    
[HttpDelete("{id:int}")] // On cible par ID
public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
{
    var flux = await _dbContext.Flux
        .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    if (flux == null)
    {
        return NotFound($"Aucun flux trouvé avec l'Id : {id}");
    }

    _dbContext.Flux.Remove(flux);
    await _dbContext.SaveChangesAsync(cancellationToken);

    return NoContent();
}

    // 4. POST : Importer les flux via un fichier Excel
[HttpPost("import-excel")]
[Consumes("multipart/form-data")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<IActionResult> ImportExcel(
    IFormFile file,
    [FromForm] string? codesAMettreAJour = null,
    CancellationToken cancellationToken = default)
{
    if (file == null || file.Length == 0)
    {
        return BadRequest(new { message = "Veuillez fournir un fichier Excel ou CSV valide." });
    }

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (extension != ".xlsx" && extension != ".xls" && extension != ".csv")
    {
        return BadRequest(new { message = "Format non supporté. Utilisez un fichier .xlsx, .xls ou .csv" });
    }

    try
    {
        var (inserted, updated, skipped, duplicates, errors) = 
            await _fluxService.ImportFluxFromExcelAsync(file, codesAMettreAJour, cancellationToken);

        if (errors.Any() && inserted == 0 && updated == 0 && skipped == 0)
        {
            return BadRequest(new { message = errors.First() });
        }

        return Ok(new
        {
            message = "Traitement du fichier terminé avec succès.",
            insertedRows = inserted,
            updatedRows = updated,
            skippedExistingCount = skipped,
            duplicates = duplicates, // ← Liste { code, libelle } renvoyée au front
            warningsOrErrors = errors
        });
    }
    catch (Exception ex)
    {
        return BadRequest(new { message = $"Une erreur est survenue lors de l'import : {ex.Message}" });
    }
}
}