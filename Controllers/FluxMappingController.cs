using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Data;
namespace AfbGenerator.Api.Controllers;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class FluxMappingController : ControllerBase
{
    private readonly AppDbContext _context; // Votre DbContext

    public FluxMappingController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateMapping([FromBody] CreateFluxMappingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword))
            return BadRequest("Le mot-clé est obligatoire.");

        // On évite les doublons de mots-clés
        var keywordUpper = request.Keyword.Trim().ToUpperInvariant();
        bool exists = await _context.FluxMappings.AnyAsync(m => m.Keyword.ToUpper() == keywordUpper);
        
        if (exists)
            return Conflict("Ce mot-clé est déjà configuré pour une règle de détection.");

        var mapping = new FluxMapping
        {
            Flux = request.Flux.Trim(),
            Keyword = request.Keyword.Trim()
        };

        _context.FluxMappings.Add(mapping);
        await _context.SaveChangesAsync();

        return Ok(mapping);
    }
}