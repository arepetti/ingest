using System.Globalization;
using System.Text;
using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Read-only access to the audit log of create/edit/delete changes. Admin-only. The log itself is
/// written by the domain services as a side effect of the operations it records; this controller
/// only exposes browsing (paged, newest-first) and a full CSV export.
/// </summary>
[ApiController]
[Route("api/admin/audit")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AuditController(IAuditLogService service) : ControllerBase
{
    /// <summary>Page through the audit log, newest change first.</summary>
    /// <param name="page">1-based page number; defaults to 1.</param>
    /// <param name="pageSize">Page size; defaults to 50.</param>
    /// <param name="change">Restrict to a single change type (Create, Edit, Delete).</param>
    /// <param name="targetType">Restrict to a single target type (User, Account, Schema, ApiKey, Submission, Report).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of audit entries.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<AuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] AuditChangeType? change,
        [FromQuery] AuditTargetType? targetType,
        CancellationToken ct)
    {
        var result = await service.ListAsync(
            RequestHelpers.ToPageRequest(page, pageSize, null, false), change, targetType, null, ct);
        return Ok(result.Map(AuditLogDto.From));
    }

    /// <summary>Export the audit log as CSV, streamed and newest-first.</summary>
    /// <remarks>
    /// All filters are optional and ANDed. <paramref name="name"/> is a case-insensitive substring
    /// matched against either the target or actor name — it is intentionally only reachable by
    /// calling this endpoint directly (the UI does not expose name filtering).
    /// </remarks>
    /// <param name="name">Substring matched against either the target or actor name.</param>
    /// <param name="change">Restrict to a single change type.</param>
    /// <param name="targetType">Restrict to a single target type.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("export")]
    [Produces("text/csv")]
    public async Task ExportCsv(
        [FromQuery] string? name,
        [FromQuery] AuditChangeType? change,
        [FromQuery] AuditTargetType? targetType,
        CancellationToken ct)
    {
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = "attachment; filename=\"audit-log.csv\"";

        await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync("Timestamp,Change,TargetType,TargetId,TargetName,ActorId,ActorName");

        await foreach (var entry in service.StreamAsync(change, targetType, name, ct))
        {
            var line = string.Join(',',
                Escape(entry.Timestamp.ToString("o", CultureInfo.InvariantCulture)),
                Escape(entry.Change.ToString()),
                Escape(entry.TargetType.ToString()),
                Escape(entry.TargetId.ToString()),
                Escape(entry.TargetName),
                Escape(entry.ActorId?.ToString()),
                Escape(entry.ActorName));
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync(ct);
    }

    /// <summary>Quote a CSV field per RFC 4180 when it contains a comma, quote or line break.</summary>
    private static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return field;
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
