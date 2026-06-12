using Ingest.Core.Abstractions;
using Ingest.Core.Common;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IPersonalDataService"/>. Gathers every collection tied to a subject into a
/// single bundle, stripping API-key secrets and including the outbox emails the registry backup
/// leaves out.
/// </summary>
public sealed class PersonalDataService : IPersonalDataService
{
    private readonly IAccountRepository _accounts;
    private readonly IApiKeyRepository _apiKeys;
    private readonly ISubmissionRepository _submissions;
    private readonly ISampleRepository _samples;
    private readonly IEmailQueue _emails;
    private readonly IAuditLogRepository _auditLogs;
    private readonly TimeProvider _clock;

    /// <summary>Create a new <see cref="PersonalDataService"/>.</summary>
    public PersonalDataService(
        IAccountRepository accounts,
        IApiKeyRepository apiKeys,
        ISubmissionRepository submissions,
        ISampleRepository samples,
        IEmailQueue emails,
        IAuditLogRepository auditLogs,
        TimeProvider clock)
    {
        _accounts = accounts;
        _apiKeys = apiKeys;
        _submissions = submissions;
        _samples = samples;
        _emails = emails;
        _auditLogs = auditLogs;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<PersonalDataBundle> ExportForAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, includeDeleted: true, ct);
        if (account is null) throw new NotFoundException("Account");

        var keys = await _apiKeys.ListByAccountAsync(accountId, ct);
        var apiKeys = keys
            .Select(k => new PersonalDataApiKey(k.KeyId, k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.IsDeleted))
            .ToList();

        var submissions = await _submissions.ListByServiceAsync(accountId, includeDeleted: true, ct);
        var samples = await _samples.ListByServiceAsync(accountId, includeDeleted: true, ct);
        var emails = await _emails.ListForAccountAsync(accountId, account.Email, ct);
        var audit = await _auditLogs.ListForAccountAsync(accountId, ct);

        return new PersonalDataBundle(
            _clock.GetUtcNow().UtcDateTime,
            account,
            apiKeys,
            submissions,
            samples,
            emails,
            audit);
    }
}
