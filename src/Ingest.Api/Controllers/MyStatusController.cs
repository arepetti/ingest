using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Api.Options;
using Ingest.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>
/// Self-service status snapshot for the calling service account: which schemas it can submit to,
/// which values are required, and whether each value's most recent sample falls inside its current
/// cadence period.
/// </summary>
[ApiController]
[Route("api/me/status")]
[Authorize(Policy = AuthConstants.ServicePolicy)]
public sealed class MyStatusController(
    IStatusService statuses,
    IOptions<IngestOptions> options) : ControllerBase
{
    /// <summary>
    /// Returns the calling account's submission status for the requested period.
    /// </summary>
    /// <remarks>
    /// For each schema visible to the caller the response lists every value with its declared
    /// cadence, the bounds of the current cadence bucket, the latest submission (if any) within
    /// that bucket, and a boolean <c>satisfied</c> flag. Disabled schemas/values are reported but
    /// flagged so callers can filter them out.
    /// </remarks>
    /// <param name="period">
    /// Period hint used to render summary headers — one of <c>day</c>, <c>week</c>, <c>month</c>,
    /// <c>year</c>. Per-value satisfaction is always computed against the value's own cadence
    /// bucket regardless of this hint. When omitted the configured default is used.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Status snapshot for the caller. See <see cref="ServiceStatusDto"/>.</response>
    /// <response code="401">No API key was supplied or it does not resolve to an active account.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ServiceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get([FromQuery] string? period, CancellationToken ct)
    {
        var status = await statuses.GetStatusAsync(
            User.CurrentAccountId(),
            period ?? options.Value.DefaultStatusPeriod,
            ct);
        return Ok(StatusMapper.ToDto(status));
    }
}
