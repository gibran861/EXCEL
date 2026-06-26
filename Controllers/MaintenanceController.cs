using Microsoft.AspNetCore.Mvc;
using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using AfbGenerator.Api.Entities;

namespace AfbGenerator.Api.Controllers;
[ApiController]
[Route("api/[controller]")]
public class MaintenanceController : ControllerBase
{
    private readonly AppDbContext _context;
    private const string PurgePassword = "mariem";

    public MaintenanceController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Purge toutes les tables de la base de données.
    /// Nécessite le mot de passe de confirmation.
    /// </summary>
    [HttpDelete("purge")]
    public async Task<IActionResult> PurgeAllTables([FromBody] PurgeRequest request)
    {
        // Vérification du mot de passe
        if (request == null || request.Password != PurgePassword)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Mot de passe incorrect. Opération refusée."
            });
        }

        try
        {
            // Désactiver les contraintes FK temporairement
            await _context.Database.ExecuteSqlRawAsync("EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'");

            // Vider dans l'ordre (enfants avant parents)
            int libelles      = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Libelles]");
            int fluxMappings  = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[FluxMappings]");
            int fluxes        = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Fluxes]");
            int flux          = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Flux]");
            int cib           = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Cib]");
            int banques       = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Banques]");

            // Réinitialiser les IDENTITY (auto-increment repart à 1)
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('Libelles',     RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('FluxMappings', RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('Fluxes',       RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('Flux',         RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('Banques',      RESEED, 0)");

            // Réactiver les contraintes FK
            await _context.Database.ExecuteSqlRawAsync("EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL'");

            return Ok(new
            {
                success = true,
                message = "Purge effectuée avec succès.",
                details = new
                {
                    Libelles     = libelles,
                    FluxMappings = fluxMappings,
                    Fluxes       = fluxes,
                    Flux         = flux,
                    Cib          = cib,
                    Banques      = banques
                },
                purgedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            // Réactiver les contraintes même en cas d'erreur
            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL'"
                );
            }
            catch { /* ignorer */ }

            return StatusCode(500, new
            {
                success = false,
                message = "Erreur lors de la purge.",
                error   = ex.Message
            });
        }
    }
}

// DTO de la requête
public class PurgeRequest
{
    public string Password { get; set; } = string.Empty;
}