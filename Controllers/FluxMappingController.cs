using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;

using AfbGenerator.Api.Services;



namespace AfbGenerator.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FluxMappingController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly fluxMappingService _fluxMappingService;

        public FluxMappingController(AppDbContext context,fluxMappingService fluxMappingService)
        {
            _context = context;
            _fluxMappingService = fluxMappingService;
        }

        // ── 1. GET ALL MAPPINGS ─────────────────────────────────────────────
        [HttpGet]
        public async Task<ActionResult<IEnumerable<FluxMapping>>> GetMappings()
        {
            return await _context.FluxMappings.ToListAsync();
        }

        // ── 2. POST (CREATE) MAPPING ────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> CreateMapping([FromBody] CreateFluxMappingRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Keyword))
                return BadRequest("Le mot-clé est obligatoire.");

            var keywordUpper = request.Keyword.Trim().ToUpperInvariant();
            bool exists = await _context.FluxMappings.AnyAsync(m => m.Keyword.ToUpper() == keywordUpper);
            
        
            var mapping = new FluxMapping
            {
                Flux = request.Flux.Trim(),
                Keyword = request.Keyword.Trim(),
                Operator = request.Operator ?? "ANY",
        TargetAmount = request.TargetAmount,
        BankCode = request.BankCode,
                IsActive = true
            };

            _context.FluxMappings.Add(mapping);
            await _context.SaveChangesAsync();

            return Ok(mapping);
        }

        // ── 3. PUT (EDIT) MAPPING ───────────────────────────────────────────
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMapping(int id, [FromBody] UpdateFluxMappingRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Keyword))
                return BadRequest("Le mot-clé est obligatoire.");

            var mapping = await _context.FluxMappings.FindAsync(id);
            if (mapping == null)
                return NotFound($"Aucun mapping trouvé avec l'Id : {id}");

            // Vérifier si le nouveau mot-clé n'est pas déjà utilisé par un AUTRE mapping
            var keywordUpper = request.Keyword.Trim().ToUpperInvariant();
            bool exists = await _context.FluxMappings.AnyAsync(m => m.Id != id && m.Keyword.ToUpper() == keywordUpper);
            
            if (exists)
                return Conflict("Ce mot-clé est déjà utilisé par une autre règle.");

            // Mise à jour des données
            mapping.Flux = request.Flux.Trim();
            mapping.Keyword = request.Keyword.Trim();
            mapping.IsActive = request.IsActive;

            await _context.SaveChangesAsync();
            return Ok(mapping);
        }

        // ── 4. DELETE MAPPING ───────────────────────────────────────────────
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMapping(int id)
        {
            var mapping = await _context.FluxMappings.FindAsync(id);
            if (mapping == null)
                return NotFound($"Aucun mapping trouvé avec l'Id : {id}");

            _context.FluxMappings.Remove(mapping);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Mapping supprimé avec succès." });
        }

        [HttpPost("import-excel")]
[Consumes("multipart/form-data")]
[ProducesResponseType(typeof(FluxMappingImportResult), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<FluxMappingImportResult>> ImportFluxMappingExcel(IFormFile file, CancellationToken cancellationToken)
{
    try
    {
        var result = await _fluxMappingService.ImportFluxMappingDataAsync(file, cancellationToken);
        return Ok(result);
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
}
    }

    
}