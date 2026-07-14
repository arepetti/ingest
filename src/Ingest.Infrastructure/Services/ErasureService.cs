using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IErasureService"/>. Orchestrates the cross-collection removal/redaction
/// needed to satisfy a right-to-erasure request, then records a single audit entry naming only
/// the pseudonym so the action is accountable without re-introducing the identity.
/// </summary>
public sealed class ErasureService : IErasureService
{
    private readonly IAccountRepository _accounts;
    private readonly IApiKeyRepository _apiKeys;
    private readonly ISubmissionRepository _submissions;
    private readonly ISampleRepository _samples;
    private readonly IEmailQueue _emails;
    private readonly IAuditLogRepository _auditLogs;
    private readonly INotificationLogRepository _notificationLogs;
    private readonly IAuditLogService _audit;

    /// <summary>Create a new <see cref="ErasureService"/>.</summary>
    public ErasureService(
        IAccountRepository accounts,
        IApiKeyRepository apiKeys,
        ISubmissionRepository submissions,
        ISampleRepository samples,
        IEmailQueue emails,
        IAuditLogRepository auditLogs,
        INotificationLogRepository notificationLogs,
        IAuditLogService audit)
    {
        _accounts = accounts;
        _apiKeys = apiKeys;
        _submissions = submissions;
        _samples = samples;
        _emails = emails;
        _auditLogs = auditLogs;
        _notificationLogs = notificationLogs;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<ErasureResult> EraseAccountAsync(Guid accountId, ErasureMode mode, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, includeDeleted: true, ct);
        if (account is null) throw new NotFoundException("Account");

        var pseudonym = $"erased-{Guid.NewGuid():N}"[..14];
        var targetType = account.Kind == AccountKind.User ? AuditTargetType.User : AuditTargetType.Account;
        var email = account.Email;

        // Credentials go in both modes — an erased subject must not retain a way in.
        var apiKeysRemoved = await _apiKeys.HardDeleteByAccountAsync(accountId, ct);
        var emailsRemoved = await _emails.HardDeleteForAccountAsync(accountId, email, ct);

        ErasureResult result;
        if (mode == ErasureMode.Anonymise)
        {
            var submissions = await _submissions.ListByServiceAsync(accountId, includeDeleted: true, ct);
            foreach (var submission in submissions)
            {
                RedactSubmission(submission, pseudonym);
                await _submissions.UpdateAsync(submission, ct);
            }
            var samplesAffected = await _samples.RedactByServiceAsync(accountId, pseudonym, ct);
            var auditAffected = await _auditLogs.AnonymiseAccountAsync(accountId, pseudonym, ct);

            account.Name = pseudonym;
            account.Label = null;
            account.Description = null;
            account.Email = null;
            account.Area = null;
            account.Enabled = false;
            account.ExternalLogins = new();
            await _accounts.UpdateAsync(account, ct);

            result = new ErasureResult(accountId, pseudonym, mode, submissions.Count, samplesAffected, emailsRemoved, auditAffected, apiKeysRemoved);
        }
        else
        {
            var submissionsRemoved = await _submissions.HardDeleteByServiceAsync(accountId, ct);
            var samplesRemoved = await _samples.HardDeleteByServiceAsync(accountId, ct);
            var auditRemoved = await _auditLogs.HardDeleteForAccountAsync(accountId, ct);
            await _notificationLogs.HardDeleteForServiceAsync(accountId, ct);
            await _accounts.HardDeleteAsync(accountId, ct);

            result = new ErasureResult(accountId, pseudonym, mode, (int)submissionsRemoved, samplesRemoved, emailsRemoved, auditRemoved, apiKeysRemoved);
        }

        // Recorded last so the entry survives the audit purge in Delete mode. The target name only
        // ever carries the pseudonym + mode, never the original identity.
        await _audit.RecordAsync(targetType, AuditChangeType.Delete, accountId, $"{pseudonym} (erased: {mode.ToString().ToLowerInvariant()})", ct);
        return result;
    }

    /// <summary>Strip identity-bearing free-text from a submission while keeping its numeric/date/bool samples.</summary>
    private static void RedactSubmission(Submission submission, string pseudonym)
    {
        submission.ServiceName = pseudonym;
        submission.Warnings = new();
        foreach (var sample in submission.Samples)
        {
            sample.Note = null;
            if (sample.Value is string) sample.Value = null;
        }
    }
}
