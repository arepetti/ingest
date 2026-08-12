using System.Text;
using System.Text.Json;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Bulk export/import of registry accounts as a single JSON file — a convenience for cloning an
/// environment or seeding one. <b>API keys are never exported</b> (they don't live on the account
/// and can't be reversed from their hashes), so imported accounts start with no key and must have
/// one re-generated. Import matches on the account name: existing accounts are updated, unknown
/// names are created.
/// </summary>
[ApiController]
[Route("api/admin/accounts/backup")]
[Authorize(Policy = Capabilities.AccountsRead)]
public sealed class AccountsBackupController(IAccountService accounts, IAuditLogService audit) : ControllerBase
{
    private const string Marker = "ingest-accounts";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Download every account as one JSON file (no API keys).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The accounts file (an attachment download).</response>
    /// <response code="403">Caller lacks the accounts-read capability.</response>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var entries = await accounts.ExportAsync(ct);
        var file = new AccountsBackupFileDto(
            Marker, CurrentVersion, DateTime.UtcNow,
            entries.Select(AccountBackupEntryDto.From).ToList());

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(file, JsonOptions));
        var fileName = $"ingest-accounts-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(bytes, "application/json", fileName);
    }

    /// <summary>Import accounts from an accounts file, creating new ones and updating existing ones by name.</summary>
    /// <remarks>
    /// Not destructive: accounts absent from the file are left untouched. API keys are not part of
    /// the file, so newly created accounts have none — generate one for each afterwards.
    /// </remarks>
    /// <param name="body">The accounts JSON, posted as the request body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Import completed; the body reports created/updated counts and any per-account errors.</response>
    /// <response code="400">The file is empty, not valid JSON, not an Ingest accounts file, or an unsupported version.</response>
    /// <response code="403">Caller lacks the accounts-manage capability.</response>
    [HttpPost("import")]
    [Authorize(Policy = Capabilities.AccountsManage)]
    [ProducesResponseType(typeof(AccountsImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Import([FromBody] JsonElement body, CancellationToken ct)
    {
        AccountsBackupFileDto? file;
        try
        {
            file = body.Deserialize<AccountsBackupFileDto>(JsonOptions);
        }
        catch (JsonException ex)
        {
            var message = $"Invalid accounts file: {ex.Message}";
            return BadRequest(DiagnosticProblem.BadRequest(
                Diagnostic.Create(
                    DiagnosticCodes.Imports.AccountFileInvalidJson,
                    message,
                    ("detail", ex.Message),
                    ("fileType", "accounts")),
                "Invalid accounts file",
                message));
        }

        if (file is null || !string.Equals(file.Format, Marker, StringComparison.Ordinal))
        {
            const string message = "This file is not an Ingest accounts export.";
            return BadRequest(DiagnosticProblem.BadRequest(Diagnostic.Create(
                DiagnosticCodes.Imports.AccountFileMarker,
                message,
                ("expectedFormat", Marker),
                ("actualFormat", file?.Format))));
        }
        if (file.Version != CurrentVersion)
        {
            var message = $"Unsupported accounts file version {file.Version}; this server expects version {CurrentVersion}.";
            return BadRequest(DiagnosticProblem.BadRequest(Diagnostic.Create(
                DiagnosticCodes.Imports.AccountFileVersion,
                message,
                ("actualVersion", file.Version),
                ("expectedVersion", CurrentVersion))));
        }
        if (file.Accounts is null || file.Accounts.Count == 0)
        {
            const string message = "The accounts file contains no accounts.";
            return BadRequest(DiagnosticProblem.BadRequest(
                new Diagnostic(DiagnosticCodes.Imports.AccountFileEmpty, message)));
        }

        var result = await accounts.ImportAsync(file.Accounts.Select(a => a.ToEntry()).ToList(), ct);
        await audit.RecordAsync(
            AuditTargetType.Backup, AuditChangeType.Edit, AuditTargets.AccountsBackup, "Accounts backup",
            $"Imported accounts: {result.Created} created, {result.Updated} updated, {result.Errors.Count} skipped.", ct);
        return Ok(AccountsImportResultDto.From(result));
    }
}
