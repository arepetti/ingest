using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
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
[Authorize(Policy = AuthConstants.OperatorPolicy)]
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
}
