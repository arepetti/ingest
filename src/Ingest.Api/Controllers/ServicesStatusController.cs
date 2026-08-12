using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Api.Options;
using Ingest.Core.Abstractions;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>
/// Operator/admin-facing status snapshot for any specific service, looked up by name. Mirrors the
/// shape of <c>/api/me/status</c> but lets a back-office user inspect a service they don't own —
/// useful for monitoring dashboards and incident reviews.
/// </summary>
[ApiController]
[Route("api/services")]
[Authorize(Policy = Capabilities.StatusRead)]
public sealed class ServicesStatusController(
    IStatusService statuses,
    IOptions<IngestOptions> options) : ControllerBase
{
    /// <summary>Return the submission status of a single service account, identified by name.</summary>
    /// <remarks>
    /// The shape of the response is identical to <c>/api/me/status</c>: per-schema and per-value
    /// summaries, latest sample timestamps, and a <c>satisfied</c> flag computed against the
    /// value's own cadence bucket.
    /// </remarks>
    /// <param name="name">Machine-style account name.</param>
    /// <param name="period">Period hint (<c>day</c>/<c>week</c>/<c>month</c>/<c>year</c>); falls back to the configured default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The service's status. <see cref="ServiceStatusDto"/>.</response>
    /// <response code="404">No service with that name.</response>
    [HttpGet("{name}/status")]
    [ProducesResponseType(typeof(ServiceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetServiceStatus(string name, [FromQuery] string? period, CancellationToken ct)
    {
        var status = await statuses.GetStatusByServiceNameAsync(
            name,
            period ?? options.Value.DefaultStatusPeriod,
            ct);
        // A scoped operator may only inspect services in its allowlist; everything else looks like
        // it doesn't exist.
        if (!User.CanAccessService(status.ServiceId))
            return NotFound(DiagnosticProblem.NotFound("Service", name));
        return Ok(StatusMapper.ToDto(status));
    }
}
