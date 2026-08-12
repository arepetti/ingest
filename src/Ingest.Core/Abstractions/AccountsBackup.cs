using Ingest.Core.Entities;
using Ingest.Core.Common;

namespace Ingest.Core.Abstractions;

/// <summary>
/// One account in an accounts export/import. A portable, secret-free projection of an
/// <see cref="Account"/>: it deliberately omits the id, audit stamps and — most importantly — any
/// API keys. Accounts are matched on <see cref="Name"/> when imported (the registry's unique
/// identifier), so a restored or copied account must have its keys re-generated afterwards.
/// </summary>
/// <param name="Name">Unique machine-style name; the match key on import.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Description">Free-form description.</param>
/// <param name="Email">Contact email (may be null).</param>
/// <param name="Area">Informative-only area tag (may be null).</param>
/// <param name="Kind">UI-capable (User) vs API-only (Application).</param>
/// <param name="Role">Authorisation tier.</param>
/// <param name="Enabled">Whether the account is enabled.</param>
/// <param name="Capabilities">Capability overrides (empty = follow the role default bundle).</param>
/// <param name="ExternalLogins">SSO identity links (provider + email); only meaningful for User-kind accounts.</param>
/// <param name="AssignedServiceIds">Assigned-service allowlist (empty = unrestricted, sees every service). Ignored for Admins.</param>
public sealed record AccountBackupEntry(
    string Name,
    string? Label,
    string? Description,
    string? Email,
    string? Area,
    AccountKind Kind,
    AccountRole Role,
    bool Enabled,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<AccountBackupLogin> ExternalLogins,
    IReadOnlyList<Guid> AssignedServiceIds);

/// <summary>A single SSO identity link carried in an accounts export (provider + email; no subject).</summary>
/// <param name="Provider">Provider id (e.g. <c>"Microsoft"</c>).</param>
/// <param name="Email">Verified email that signs the account in via the provider.</param>
public sealed record AccountBackupLogin(string Provider, string Email);

/// <summary>
/// Outcome of an accounts import: how many accounts were created vs updated (matched by name) and
/// any per-account problems that didn't abort the rest of the batch.
/// </summary>
/// <param name="Created">Number of accounts created.</param>
/// <param name="Updated">Number of existing accounts updated.</param>
/// <param name="Errors">Human-readable, per-account errors for entries that were skipped.</param>
public sealed record AccountsImportResult(int Created, int Updated, IReadOnlyList<string> Errors)
{
    /// <summary>Structured counterparts to <see cref="Errors"/>, in the same order.</summary>
    public IReadOnlyList<Diagnostic> ErrorDetails { get; init; } =
        Errors.Select(x => Diagnostic.Create(
            DiagnosticCodes.Imports.AccountEntry,
            x,
            ("domain", "accounts"))).ToList();
}
