using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Retention;
using Microsoft.Extensions.Options;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IRetentionService"/>. Translates the configured day-count windows into
/// "older than" cutoffs and issues the matching bulk deletes. A window of <c>0</c> means "keep
/// forever" and is skipped, so the feature is safe to leave registered even with everything off.
/// </summary>
public sealed class RetentionService : IRetentionService
{
    private readonly IEmailQueue _emails;
    private readonly IAccountRepository _accounts;
    private readonly ISchemaRepository _schemas;
    private readonly ISubmissionRepository _submissions;
    private readonly ISampleRepository _samples;
    private readonly IReportRepository _reports;
    private readonly IAuditLogRepository _auditLogs;
    private readonly INotificationLogRepository _notificationLogs;
    private readonly TimeProvider _clock;
    private readonly RetentionOptions _options;

    /// <summary>Create a new <see cref="RetentionService"/>.</summary>
    public RetentionService(
        IEmailQueue emails,
        IAccountRepository accounts,
        ISchemaRepository schemas,
        ISubmissionRepository submissions,
        ISampleRepository samples,
        IReportRepository reports,
        IAuditLogRepository auditLogs,
        INotificationLogRepository notificationLogs,
        TimeProvider clock,
        IOptions<RetentionOptions> options)
    {
        _emails = emails;
        _accounts = accounts;
        _schemas = schemas;
        _submissions = submissions;
        _samples = samples;
        _reports = reports;
        _auditLogs = auditLogs;
        _notificationLogs = notificationLogs;
        _clock = clock;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<RetentionRunResult> PurgeAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        long emails = 0;
        if (_options.SentEmailsDays > 0)
            emails = await _emails.PurgeProcessedOlderThanAsync(now.AddDays(-_options.SentEmailsDays), ct);

        long softDeleted = 0;
        if (_options.SoftDeletedDays > 0)
        {
            var cutoff = now.AddDays(-_options.SoftDeletedDays);
            softDeleted += await _accounts.PurgeSoftDeletedAsync(cutoff, ct);
            softDeleted += await _schemas.PurgeSoftDeletedAsync(cutoff, ct);
            softDeleted += await _submissions.PurgeSoftDeletedAsync(cutoff, ct);
            softDeleted += await _samples.PurgeSoftDeletedAsync(cutoff, ct);
            softDeleted += await _reports.PurgeSoftDeletedAsync(cutoff, ct);
        }

        long audit = 0;
        if (_options.AuditLogDays > 0)
            audit = await _auditLogs.PurgeOlderThanAsync(now.AddDays(-_options.AuditLogDays), ct);

        long markers = 0;
        if (_options.NotificationLogDays > 0)
            markers = await _notificationLogs.PurgeOlderThanAsync(now.AddDays(-_options.NotificationLogDays), ct);

        return new RetentionRunResult(emails, softDeleted, audit, markers);
    }
}
