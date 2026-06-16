using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ingest.Api.Auth;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Admin-only data-rights endpoints backing UK GDPR obligations: per-subject erasure
/// (Art. 17), the per-subject data-access export (Art. 15), and a manual trigger for the
/// retention purge (Art. 5(1)(e)). The retention <em>schedule</em> is driven by the
/// <c>RetentionWorker</c>; this controller only exposes the on-demand run for testing/ops.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = Capabilities.PrivacyManage)]
public sealed class AdminPrivacyController(
    IErasureService erasure,
    IPersonalDataService personalData,
    IRetentionService retention) : ControllerBase
{
    /// <summary>Erase everything tied to an account (anonymise or full delete).</summary>
    /// <remarks>
    /// <b>Irreversible.</b> Anonymise keeps numeric/date/bool KPI values for reporting but strips
    /// identity; Delete removes the account and all of its data. Both record a single audit entry
    /// (naming only the pseudonym) for accountability. Bypasses the ordinary "account has data"
    /// delete guard.
    /// </remarks>
    /// <param name="id">Account id.</param>
    /// <param name="req">Erasure mode (defaults to anonymise when the body is omitted).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The erasure tally (per-collection counts).</response>
    /// <response code="404">No account with that id.</response>
    [HttpPost("accounts/{id:guid}/erase")]
    [ProducesResponseType(typeof(ErasureResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Erase(Guid id, [FromBody] EraseAccountRequest? req, CancellationToken ct)
    {
        var result = await erasure.EraseAccountAsync(id, req?.Mode ?? ErasureMode.Anonymise, ct);
        return Ok(result);
    }

    /// <summary>Download everything the system holds about a subject as one JSON file (DSAR).</summary>
    /// <param name="id">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The data bundle (an attachment download).</response>
    /// <response code="404">No account with that id.</response>
    [HttpGet("accounts/{id:guid}/personal-data/export")]
    [Authorize(Policy = Capabilities.PrivacyRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportPersonalData(Guid id, CancellationToken ct)
    {
        var bundle = await personalData.ExportForAccountAsync(id, ct);
        var json = JsonSerializer.Serialize(bundle, BundleJsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileName = $"personal-data-{id}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(bytes, "application/json", fileName);
    }

    /// <summary>Run the retention purge now. Internal trigger mirroring the worker.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The purge tally (per-target counts).</response>
    [HttpPost("retention/run")]
    [ProducesResponseType(typeof(RetentionRunResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunRetention(CancellationToken ct)
    {
        return Ok(await retention.PurgeAsync(ct));
    }

    private static readonly JsonSerializerOptions BundleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
