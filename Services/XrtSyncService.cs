using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AfbGenerator.Api.Services;

public class XrtSyncService
{
    private readonly XrtDbContext _xrtContext;
    private readonly AppDbContext _appContext;

    public XrtSyncService(XrtDbContext xrtContext, AppDbContext appContext)
    {
        _xrtContext = xrtContext;
        _appContext = appContext;
    }

    public async Task<List<XrtFlowDto>> FetchXrtFlowsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var flows = await _xrtContext.XrtFlows
                .AsNoTracking()
                .OrderBy(x => x.FlowCode)
                .Select(x => new XrtFlowDto
                {
                    FlowCode = x.FlowCode,
                    Description = x.Description,
                    FlowsctsId = x.FlowsctsId,
                    Direction = x.Direction.HasValue ? x.Direction.Value.ToString() : null
                })
                .ToListAsync(cancellationToken);

            return flows;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to fetch flows from XRT database: {ex.Message}", ex);
        }
    }

    public async Task<XrtSyncResult> SyncFluxesAsync(CancellationToken cancellationToken = default)
    {
        var result = new XrtSyncResult();

        try
        {
            var xrtFlows = await _xrtContext.XrtFlows
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            result.TotalFlowsInXrt = xrtFlows.Count;

            var existingFluxes = await _appContext.Fluxes
                .ToDictionaryAsync(x => x.FluxCode, cancellationToken);

            foreach (var xrtFlow in xrtFlows)
            {
                if (string.IsNullOrWhiteSpace(xrtFlow.FlowCode))
                {
                    result.SkippedCount++;
                    result.Messages.Add($"Skipped XRT flow with empty FlowCode");
                    continue;
                }

                var normalizedCode = xrtFlow.FlowCode.Trim();

                if (existingFluxes.TryGetValue(normalizedCode, out var existingFlux))
                {
                    var updated = false;

                    if (existingFlux.FluxLabel != (xrtFlow.Description ?? string.Empty))
                    {
                        existingFlux.FluxLabel = xrtFlow.Description ?? string.Empty;
                        updated = true;
                    }

                    if (existingFlux.Cib1 != (xrtFlow.FlowsctsId?.ToString() ?? string.Empty))
                    {
                        existingFlux.Cib1 = xrtFlow.FlowsctsId?.ToString() ?? string.Empty;
                        updated = true;
                    }

                    var directionStr = xrtFlow.Direction.HasValue ? xrtFlow.Direction.Value.ToString() : string.Empty;
                    if (existingFlux.Cib2 != directionStr)
                    {
                        existingFlux.Cib2 = directionStr;
                        updated = true;
                    }

                    if (updated)
                    {
                        result.UpdatedCount++;
                        result.Messages.Add($"Updated flux: {normalizedCode}");
                    }
                    else
                    {
                        result.SkippedCount++;
                    }
                }
                else
                {
                    var newFlux = new Flux
                    {
                        FluxCode = normalizedCode,
                        FluxLabel = xrtFlow.Description ?? string.Empty,
                        BankCode = string.Empty,
                        Cib1 = string.Empty,
                        Cib2 = string.Empty,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    _appContext.Fluxes.Add(newFlux);
                    result.InsertedCount++;
                    result.Messages.Add($"Inserted new flux: {normalizedCode}");
                }
            }

            if (result.InsertedCount > 0 || result.UpdatedCount > 0)
            {
                await _appContext.SaveChangesAsync(cancellationToken);
                result.Messages.Insert(0, $"Sync completed: {result.InsertedCount} inserted, {result.UpdatedCount} updated, {result.SkippedCount} skipped");
            }
            else
            {
                result.Messages.Insert(0, $"Sync completed: No changes needed ({result.SkippedCount} skipped)");
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to sync fluxes: {ex.Message}", ex);
        }
    }
}
