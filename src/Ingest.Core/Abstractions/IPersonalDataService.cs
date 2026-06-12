using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>Non-secret metadata about an API key, for inclusion in a DSAR export.</summary>
/// <param name="KeyId">Public key id (never the secret).</param>
/// <param name="CreatedAt">When the key was issued.</param>
/// <param name="ExpiresAt">Optional expiry.</param>
/// <param name="RevokedAt">When the key was revoked, if it was.</param>
/// <param name="IsDeleted">Whether the key row is soft-deleted.</param>
public sealed record PersonalDataApiKey(string KeyId, DateTime CreatedAt, DateTime? ExpiresAt, DateTime? RevokedAt, bool IsDeleted);

/// <summary>
/// Everything the system holds about a single subject, assembled for a UK GDPR Article 15 ("right
/// of access") request. Deliberately includes the outbox emails the registry backup omits.
/// </summary>
/// <param name="GeneratedAt">When the bundle was produced (UTC).</param>
/// <param name="Account">The account record (labels, contact email, SSO links).</param>
/// <param name="ApiKeys">Key metadata only — no secrets.</param>
/// <param name="Submissions">Raw submission batches owned by the subject.</param>
/// <param name="Samples">Flat sample projections owned by the subject.</param>
/// <param name="Emails">Outbox messages related to the subject or sent to their address.</param>
/// <param name="AuditEntries">Audit entries where the subject is target or actor.</param>
public sealed record PersonalDataBundle(
    DateTime GeneratedAt,
    Account Account,
    IReadOnlyList<PersonalDataApiKey> ApiKeys,
    IReadOnlyList<Submission> Submissions,
    IReadOnlyList<SampleProjection> Samples,
    IReadOnlyList<EmailMessage> Emails,
    IReadOnlyList<AuditLog> AuditEntries);

/// <summary>
/// Assembles the per-subject data-access (DSAR) bundle for an account. Read-only: it gathers, it
/// never mutates.
/// </summary>
public interface IPersonalDataService
{
    /// <summary>Collect everything tied to a subject into one bundle.</summary>
    /// <param name="accountId">Subject account id (soft-deleted accounts are eligible too).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assembled bundle.</returns>
    /// <exception cref="Ingest.Core.Common.NotFoundException">No account with that id exists.</exception>
    Task<PersonalDataBundle> ExportForAccountAsync(Guid accountId, CancellationToken ct = default);
}
