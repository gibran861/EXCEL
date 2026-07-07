using Microsoft.AspNetCore.Mvc;
using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

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

    private bool IsPasswordValid(PurgeRequest request)
    {
        return request != null && request.Password == PurgePassword;
    }

    private IActionResult UnauthorizedResponse()
    {
        return Unauthorized(new { success = false, message = "Mot de passe incorrect. Opération refusée." });
    }

    // ==========================================
    // 1. PURGE LIBELLES
    // ==========================================
    [HttpDelete("purge/libelles")]
    public async Task<IActionResult> PurgeLibelles([FromBody] PurgeRequest request)
    {
        if (!IsPasswordValid(request)) return UnauthorizedResponse();
        _context.Database.SetCommandTimeout(120);

        try
        {
            int rows = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Libelles]");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[Libelles]', RESEED, 0)");
            return Ok(new { success = true, table = "Libelles", rowsDeleted = rows });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    // ==========================================
    // 2. PURGE FLUX MAPPINGS
    // ==========================================
    [HttpDelete("purge/flux-mappings")]
    public async Task<IActionResult> PurgeFluxMappings([FromBody] PurgeRequest request)
    {
        if (!IsPasswordValid(request)) return UnauthorizedResponse();
        _context.Database.SetCommandTimeout(120);

        try
        {
            int rows = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[FluxMappings]");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[FluxMappings]', RESEED, 0)");
            return Ok(new { success = true, table = "FluxMappings", rowsDeleted = rows });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    // ==========================================
    // 3. PURGE FLUXES
    // ==========================================
    [HttpDelete("purge/fluxes")]
    public async Task<IActionResult> PurgeFluxes([FromBody] PurgeRequest request)
    {
        if (!IsPasswordValid(request)) return UnauthorizedResponse();
        _context.Database.SetCommandTimeout(120);

        try
        {
            int rows = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Fluxes]");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[Fluxes]', RESEED, 0)");
            return Ok(new { success = true, table = "Fluxes", rowsDeleted = rows });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    // ==========================================
    // 4. PURGE FLUX (NEWFlux)
    // ==========================================
    [HttpDelete("purge/flux")]
    public async Task<IActionResult> PurgeFlux([FromBody] PurgeRequest request)
    {
        if (!IsPasswordValid(request)) return UnauthorizedResponse();
        _context.Database.SetCommandTimeout(120);

        try
        {
            int rows = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Flux]");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[Flux]', RESEED, 0)");
            return Ok(new { success = true, table = "Flux", rowsDeleted = rows });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    // ==========================================
    // 5. PURGE CIB
    // ==========================================
    [HttpDelete("purge/cib")]
    public async Task<IActionResult> PurgeCib([FromBody] PurgeRequest request)
    {
        if (!IsPasswordValid(request)) return UnauthorizedResponse();
        _context.Database.SetCommandTimeout(120);

        try
        {
            int rows = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Cib]");
            // Note : Pas de CHECKIDENT ici car Cib n'a pas forcément de colonne Identity (auto-increment)
            return Ok(new { success = true, table = "Cib", rowsDeleted = rows });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    // ==========================================
    // 6. PURGE BANQUES
    // ==========================================
    [HttpDelete("purge/banques")]
    public async Task<IActionResult> PurgeBanques([FromBody] PurgeRequest request)
    {
        if (!IsPasswordValid(request)) return UnauthorizedResponse();
        _context.Database.SetCommandTimeout(120);

        try
        {
            int rows = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Banques]");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[Banques]', RESEED, 0)");
            return Ok(new { success = true, table = "Banques", rowsDeleted = rows });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }
    [HttpDelete("purge/templates")]
    public async Task<IActionResult> PurgeTemplates([FromBody] PurgeRequest request)
    {
        if (!IsPasswordValid(request)) return UnauthorizedResponse();
        _context.Database.SetCommandTimeout(120);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // ORDRE CRUCIAL : Supprimer d'abord les champs enfants liés aux structures
            int fieldsRows = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[TemplateFields]");
            int templatesRows = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[BankTemplates]");

            // Réinitialisation des clés
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[TemplateFields]', RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[BankTemplates]', RESEED, 0)");

            await transaction.CommitAsync();

            return Ok(new { 
                success = true, 
                message = "Modèles bancaires et champs supprimés avec succès.",
                templatesDeleted = templatesRows, 
                fieldsDeleted = fieldsRows 
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

  [HttpDelete("purge/all")]
    public async Task<IActionResult> PurgeAllTables([FromBody] PurgeRequest request)
    {
        if (!IsPasswordValid(request)) return UnauthorizedResponse();
        
        _context.Database.SetCommandTimeout(300);
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Ordre strict des dépendances (Enfants puis Parents)
            int libelles      = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Libelles]");
            int fluxMappings  = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[FluxMappings]");
            int fluxes        = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Fluxes]");
            int flux          = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Flux]");
            int cib           = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Cib]");
            int banques       = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[Banques]");
            
            // 🔥 AJOUT ICI : Suppression des templates (Champs enfants d'abord, puis structure parente)
            int templateFields = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[TemplateFields]");
            int bankTemplates  = await _context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[BankTemplates]");

            // Réinitialisation de toutes les clés d'auto-incrément
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[Libelles]',     RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[FluxMappings]', RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[Fluxes]',       RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[Flux]',         RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[Banques]',      RESEED, 0)");
            
            // 🔥 AJOUT ICI : Reseed des nouvelles tables
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[TemplateFields]', RESEED, 0)");
            await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[dbo].[BankTemplates]',  RESEED, 0)");

            await transaction.CommitAsync();

            return Ok(new
            {
                success = true,
                message = "Toutes les tables (y compris les modèles de banques) ont été purgées avec succès.",
                details = new { 
                    Libelles = libelles, 
                    FluxMappings = fluxMappings, 
                    Fluxes = fluxes, 
                    Flux = flux, 
                    Cib = cib, 
                    Banques = banques,
                    TemplateFields = templateFields,
                    BankTemplates = bankTemplates
                },
                purgedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { success = false, message = "Erreur lors de la purge globale.", error = ex.Message });
        }
    }
}

public class PurgeRequest
{
    public string Password { get; set; } = string.Empty;
}