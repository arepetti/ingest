using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Cross-service status reports for back-office users. Mirrors the per-service
/// <c>GET /api/services/{name}/status</c> endpoint but aggregates across every service-role
/// account so operators can see what's currently missing at a glance.
/// </summary>
[ApiController]
[Route("api/admin/status")]
[Authorize(Policy = Capabilities.StatusRead)]
public sealed class AdminStatusController(IStatusService statuses) : ControllerBase
{
    /// <summary>
    /// Return the registry-wide "missing submissions" report, bucketed by cadence. Only cadences
    /// with at least one (service, schema) tuple short of its required values appear in the
    /// response, so the caller can render one card per cadence that actually warrants attention.
    /// </summary>
    /// <remarks>
    /// Disabled accounts, disabled schemas, and disabled or optional values are skipped. The
    /// report is informational and intended for the operator dashboard; for per-service detail
    /// fall back to <c>GET /api/services/{name}/status</c>.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The missing-submissions report. <see cref="MissingByCadenceDto"/>.</response>
    [HttpGet("missing")]
    [ProducesResponseType(typeof(List<MissingByCadenceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMissing(CancellationToken ct)
    {
        var report = await statuses.GetMissingAsync(ct);
        return Ok(StatusMapper.ToDto(report));
    }

    /// <summary>
    /// Detailed missing-submissions report for a single cadence and a single window. The window
    /// is addressed by <paramref name="offset"/> (0 = current, -1 = previous, -N = N periods
    /// ago), letting the analytics page page back through history.
    /// </summary>
    /// <param name="cadence">Cadence to evaluate (e.g. <c>Monthly</c>).</param>
    /// <param name="offset">Signed bucket offset from "now". Defaults to -1 (the previous, overdue, window).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The per-period missing report. <see cref="MissingPeriodReportDto"/>.</response>
    [HttpGet("missing/period")]
    [ProducesResponseType(typeof(MissingPeriodReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMissingPeriod([FromQuery] Cadence cadence, [FromQuery] int offset, CancellationToken ct)
    {
        var report = await statuses.GetMissingForPeriodAsync(cadence, offset, ct);
        return Ok(StatusMapper.ToDto(report));
    }

    /// <summary>
    /// "Missing submissions over time" trend for a single cadence: the total count of missing
    /// required values for each of the last <paramref name="periods"/> windows, oldest first and
    /// ending with the current window.
    /// </summary>
    /// <param name="cadence">Cadence to evaluate (e.g. <c>Monthly</c>).</param>
    /// <param name="periods">Number of windows to include (clamped server-side; defaults to 12).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The trend. <see cref="MissingHistoryDto"/>.</response>
    [HttpGet("missing/history")]
    [ProducesResponseType(typeof(MissingHistoryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMissingHistory([FromQuery] Cadence cadence, [FromQuery] int periods, CancellationToken ct)
    {
        var history = await statuses.GetMissingHistoryAsync(cadence, periods <= 0 ? 12 : periods, ct);
        return Ok(StatusMapper.ToDto(history));
    }
}
