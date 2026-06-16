using System.Text;
using System.Text.Json;
using Ingest.Api.Auth;
using Ingest.Core.Abstractions;
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
public sealed class AdminBackupController(IBackupService backup) : ControllerBase
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
        return Ok(result);
    }
}
