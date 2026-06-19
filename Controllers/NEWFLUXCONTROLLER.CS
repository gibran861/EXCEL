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
    // 2. POST : Importer les flux via un fichier Excel
    [HttpPost("import-excel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Veuillez fournir un fichier Excel valide." });
        }

        // Vérification de l'extension du fichier
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".xlsx" && extension != ".xls")
        {
            return BadRequest(new { message = "Format de fichier non supporté. Veuillez fournir un fichier .xlsx ou .xls" });
        }

        try
        {
            var (inserted, updated, errors) = await _fluxService.ImportFluxFromExcelAsync(file, cancellationToken);

            return Ok(new
            {
                message = "Traitement du fichier Excel terminé avec succès.",
                insertedRows = inserted,
                updatedRows = updated,
                warningsOrErrors = errors
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Une erreur est survenue lors de l'import : {ex.Message}" });
        }
    }
}