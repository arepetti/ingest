using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Ingest.Core.Validation;
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
public sealed class AdminConfigurationController(IAppConfigurationService config, IAuditLogService audit, TimeProvider time) : ControllerBase
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

    /// <summary>Fetch the configured cadence bucket alignment points (defaults when unset).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The current submission-window configuration.</response>
    [HttpGet("submission-window")]
    [ProducesResponseType(typeof(SubmissionWindowDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubmissionWindow(CancellationToken ct)
    {
        var anchors = await config.GetCadenceAnchorsAsync(ct);
        return Ok(SubmissionWindowDto.From(anchors));
    }

    /// <summary>Replace the cadence bucket alignment points.</summary>
    /// <param name="body">The new alignment points; out-of-range values are clamped.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The stored (clamped) submission-window configuration.</response>
    [HttpPut("submission-window")]
    [Authorize(Policy = Capabilities.SettingsManage)]
    [ProducesResponseType(typeof(SubmissionWindowDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSubmissionWindow([FromBody] SubmissionWindowDto body, CancellationToken ct)
    {
        var updated = await config.UpdateCadenceAnchorsAsync(body.ToAnchors(), ct);
        await audit.RecordAsync(AuditTargetType.Settings, AuditChangeType.Edit, AuditTargets.AppConfiguration, "Submission periods", ct);
        return Ok(SubmissionWindowDto.From(updated));
    }

    /// <summary>Fetch the global ingestion kill-switch state.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The current ingestion status.</response>
    [HttpGet("ingestion")]
    [ProducesResponseType(typeof(IngestionStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIngestionStatus(CancellationToken ct)
    {
        var status = await config.GetIngestionStatusAsync(ct);
        return Ok(new IngestionStatusDto(status.Closed, status.Message));
    }

    /// <summary>
    /// Update the global ingestion kill switch. When closed, service-facing ingestion (service
    /// create/replace, bulk import, Teams inbound) is rejected with a 503 carrying the configured
    /// message; admin create/replace and every other operation are unaffected.
    /// </summary>
    /// <param name="body">The new ingestion state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The stored ingestion status.</response>
    [HttpPut("ingestion")]
    [Authorize(Policy = Capabilities.SettingsManage)]
    [ProducesResponseType(typeof(IngestionStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateIngestionStatus([FromBody] IngestionStatusDto body, CancellationToken ct)
    {
        var updated = await config.UpdateIngestionStatusAsync(body.Closed, body.Message, ct);
        await audit.RecordAsync(AuditTargetType.Settings, AuditChangeType.Edit, AuditTargets.AppConfiguration, "Ingestion", ct);
        return Ok(new IngestionStatusDto(updated.Closed, updated.Message));
    }

    /// <summary>
    /// Fetch the per-cadence submission-window offsets (open offset / grace, in hours). Every
    /// cadence defaults to no offset and no grace — the window is exactly the bucket — until an
    /// admin configures otherwise.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The current cadence-window configuration.</response>
    [HttpGet("cadence-windows")]
    [ProducesResponseType(typeof(CadenceWindowsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCadenceWindows(CancellationToken ct)
    {
        var windows = await config.GetCadenceWindowsAsync(ct);
        return Ok(CadenceWindowsDto.From(windows));
    }

    /// <summary>
    /// Replace the per-cadence submission-window offsets. A non-zero open offset delays when a
    /// cadence's window opens; a non-zero grace extends how long after the bucket closes a service
    /// may still create/edit a sample for it.
    /// </summary>
    /// <param name="body">The new per-cadence offsets; each hour value is clamped to a sane, non-negative range.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The stored (clamped) cadence-window configuration.</response>
    [HttpPut("cadence-windows")]
    [Authorize(Policy = Capabilities.SettingsManage)]
    [ProducesResponseType(typeof(CadenceWindowsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCadenceWindows([FromBody] CadenceWindowsDto body, CancellationToken ct)
    {
        var updated = await config.UpdateCadenceWindowsAsync(body.ToDomain(), ct);
        await audit.RecordAsync(AuditTargetType.Settings, AuditChangeType.Edit, AuditTargets.AppConfiguration, "Submission windows", ct);
        return Ok(CadenceWindowsDto.From(updated));
    }

    /// <summary>
    /// Live preview of every cadence's current bucket and resolved submission window, computed at
    /// "now" from the currently configured anchors and windows. Answers "when does e.g. a weekly
    /// submission open and close" directly, using the exact same math the enforcement paths use.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">One entry per cadence, in enum declaration order.</response>
    [HttpGet("cadence-preview")]
    [ProducesResponseType(typeof(List<CadencePreviewEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCadencePreview(CancellationToken ct)
    {
        var anchors = await config.GetCadenceAnchorsAsync(ct);
        var windows = await config.GetCadenceWindowsAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;

        var entries = Enum.GetValues<Cadence>().Select(cadence =>
        {
            var (periodStart, periodEnd) = CadenceCalculator.BucketFor(cadence, now, anchors);
            var (windowStart, windowEnd) = CadenceCalculator.WindowFor(cadence, now, anchors, windows);
            return new CadencePreviewEntryDto(cadence, periodStart, periodEnd, windowStart, windowEnd);
        }).ToList();

        return Ok(entries);
    }
}
