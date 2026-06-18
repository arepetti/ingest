using System.Text;
using System.Text.Json;
using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Admin-only, convenience-grade export/import of the whole registry as a single JSON file.
/// <b>This is not the primary backup mechanism</b> — it exists for tiny deployments and quick
/// environment copies. For anything real, take a database-level backup (see the hosting guide).
/// Importing <b>replaces all current data</b>.
/// </summary>
[ApiController]
[Route("api/admin/backup")]
[Authorize(Policy = Capabilities.BackupRead)]
public sealed class AdminBackupController(IBackupService backup, IAuditLogService audit) : ControllerBase
{
    /// <summary>Download a full backup of every collection as one JSON file.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The backup file (an attachment download).</response>
    /// <response code="403">Caller is not an Admin.</response>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var json = await backup.ExportAsync(ct);
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileName = $"ingest-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(bytes, "application/json", fileName);
    }

    /// <summary>Restore the registry from a backup file, replacing all current data.</summary>
    /// <remarks>
    /// Destructive: every collection in the file is emptied and repopulated from the backup. The
    /// operation is not transactional across collections — if it fails part-way the database is
    /// left partially restored. Intended for small databases only.
    /// </remarks>
    /// <param name="body">The backup JSON, posted as the request body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Restore completed; the body reports per-collection counts.</response>
    /// <response code="400">The file is empty, not valid JSON, not an Ingest backup, or an unsupported version.</response>
    /// <response code="403">Caller is not an Admin.</response>
    [HttpPost("import")]
    [Authorize(Policy = Capabilities.BackupManage)]
    [ProducesResponseType(typeof(BackupImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Import([FromBody] JsonElement body, CancellationToken ct)
    {
        var result = await backup.ImportAsync(body.GetRawText(), ct);
        await audit.RecordAsync(AuditTargetType.Backup, AuditChangeType.Edit, AuditTargets.DataBackup, "Data backup", RestoreNote(result), ct);
        return Ok(result);
    }

    /// <summary>
    /// Download a configuration backup (Settings-page data: approval policy + rules, email +
    /// notification settings and templates, webhooks, integrations and the Teams connection) as one
    /// JSON file, for copying configuration between environments or recovering it after a disaster.
    /// </summary>
    /// <remarks>
    /// Encrypted secrets (SMTP password, webhook signing secrets, the Teams bot secret) are exported
    /// as stored and only decrypt on a deployment using the same <c>ApiKey:Pepper</c>.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The configuration backup file (an attachment download).</response>
    /// <response code="403">Caller lacks the backup capability.</response>
    [HttpGet("config/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportConfig(CancellationToken ct)
    {
        var json = await backup.ExportConfigAsync(ct);
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileName = $"ingest-config-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(bytes, "application/json", fileName);
    }

    /// <summary>Restore configuration from a configuration backup file, replacing all current settings.</summary>
    /// <remarks>
    /// Destructive: every configuration collection in the file is emptied and repopulated. A stored
    /// secret is preserved when the incoming document omits it. Not transactional across collections.
    /// </remarks>
    /// <param name="body">The configuration backup JSON, posted as the request body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Restore completed; the body reports per-collection counts.</response>
    /// <response code="400">The file is empty, not valid JSON, not an Ingest configuration backup, or an unsupported version.</response>
    /// <response code="403">Caller lacks the backup-manage capability.</response>
    [HttpPost("config/import")]
    [Authorize(Policy = Capabilities.BackupManage)]
    [ProducesResponseType(typeof(BackupImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ImportConfig([FromBody] JsonElement body, CancellationToken ct)
    {
        var result = await backup.ImportConfigAsync(body.GetRawText(), ct);
        await audit.RecordAsync(AuditTargetType.Backup, AuditChangeType.Edit, AuditTargets.ConfigBackup, "Configuration backup", RestoreNote(result), ct);
        return Ok(result);
    }

    /// <summary>Short human summary of a restore, stamped onto the audit entry's note.</summary>
    private static string RestoreNote(BackupImportResult result) =>
        $"Restored {result.Restored.Count} collection(s), {result.Restored.Values.Sum()} document(s).";
}
