using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Admin management of miscellaneous server-wide configuration. Currently exposes the list of
/// selectable "areas" an account can be tagged with (used to group/label services in the UI and
/// exports).
/// </summary>
[ApiController]
[Route("api/admin/configuration")]
[Authorize(Policy = Capabilities.SettingsRead)]
public sealed class AdminConfigurationController(IAppConfigurationService config, IAuditLogService audit) : ControllerBase
{
    /// <summary>Fetch the configured areas.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The current list of areas (possibly empty).</response>
    [HttpGet("areas")]
    [ProducesResponseType(typeof(AreasConfigurationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAreas(CancellationToken ct)
    {
        var areas = await config.GetAreasAsync(ct);
        return Ok(new AreasConfigurationDto(areas.ToList()));
    }

    /// <summary>Replace the configured areas.</summary>
    /// <param name="body">The new areas. Entries are trimmed, blanks dropped and duplicates removed while preserving order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The stored list of areas.</response>
    [HttpPut("areas")]
    [Authorize(Policy = Capabilities.SettingsManage)]
    [ProducesResponseType(typeof(AreasConfigurationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAreas([FromBody] AreasConfigurationDto body, CancellationToken ct)
    {
        var updated = await config.UpdateAreasAsync(body?.Areas ?? new List<string>(), ct);
        await audit.RecordAsync(AuditTargetType.Settings, AuditChangeType.Edit, AuditTargets.AppConfiguration, "Areas", ct);
        return Ok(new AreasConfigurationDto(updated.ToList()));
    }
}
