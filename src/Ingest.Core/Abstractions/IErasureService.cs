namespace Ingest.Core.Abstractions;

/// <summary>How a subject-erasure request should treat the data tied to an account.</summary>
public enum ErasureMode
{
    /// <summary>
    /// Strip identity but keep the statistical record: pseudonymise the account, redact free-text,
    /// drop credentials and emails, and rewrite the audit trail to the pseudonym. Numeric/date/bool
    /// KPI values survive so historical dashboards stay meaningful.
    /// </summary>
    Anonymise = 0,

    /// <summary>
    /// Full erasure: hard-delete the account, its keys, submissions, samples, outbox emails,
    /// audit entries and notification markers. Nothing tied to the subject remains except the single
    /// erasure audit entry recorded for accountability.
    /// </summary>
    Delete = 1,
}

/// <summary>Per-collection tally of what an erasure run touched, for the API response and audit.</summary>
/// <param name="AccountId">The erased account id.</param>
/// <param name="Pseudonym">The stable pseudonym the account was reduced to (anonymise), or assigned in the audit trail (delete).</param>
/// <param name="Mode">The mode that was applied.</param>
/// <param name="SubmissionsAffected">Submissions redacted (anonymise) or removed (delete).</param>
/// <param name="SamplesAffected">Sample projections redacted (anonymise) or removed (delete).</param>
/// <param name="EmailsRemoved">Outbox messages removed.</param>
/// <param name="AuditEntriesAffected">Audit entries rewritten (anonymise) or removed (delete).</param>
/// <param name="ApiKeysRemoved">API keys removed.</param>
public sealed record ErasureResult(
    Guid AccountId,
    string Pseudonym,
    ErasureMode Mode,
    int SubmissionsAffected,
    long SamplesAffected,
    long EmailsRemoved,
    long AuditEntriesAffected,
    long ApiKeysRemoved);

/// <summary>
/// Carries out a UK GDPR Article 17 ("right to erasure") request against a single account, in one
/// of two modes chosen by the admin. Both modes record a dedicated audit entry for the erasure
/// action itself so accountability (Art. 5(2)) survives the data being removed.
/// </summary>
public interface IErasureService
{
    /// <summary>Erase everything tied to an account in the requested mode.</summary>
    /// <param name="accountId">Account to erase (soft-deleted accounts are eligible too).</param>
    /// <param name="mode">Anonymise (keep statistics) or Delete (remove everything).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tally of what was touched.</returns>
    /// <exception cref="Ingest.Core.Common.NotFoundException">No account with that id exists.</exception>
    Task<ErasureResult> EraseAccountAsync(Guid accountId, ErasureMode mode, CancellationToken ct = default);
}
