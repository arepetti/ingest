using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Approvals;

/// <summary>
/// Database-backed global default approval policy. There is at most one document; an absent one is
/// treated as <see cref="ApprovalMode.None"/> so fresh and legacy deployments are back-compatible.
/// </summary>
public sealed class ApprovalSettingsService : IApprovalSettingsService
{
    private readonly MongoContext _ctx;
    private readonly IAccountRepository _accounts;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="ApprovalSettingsService"/>.</summary>
    public ApprovalSettingsService(MongoContext ctx, IAccountRepository accounts, IAuditContext audit)
    {
        _ctx = ctx;
        _accounts = accounts;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<ApprovalPolicy> GetDefaultAsync(CancellationToken ct = default)
    {
        var existing = await _ctx.ApprovalSettings.Find(FilterDefinition<ApprovalSettings>.Empty).FirstOrDefaultAsync(ct);
        return existing?.Default ?? new ApprovalPolicy();
    }

    /// <inheritdoc />
    public async Task<ApprovalPolicy> UpdateDefaultAsync(ApprovalPolicy policy, CancellationToken ct = default)
    {
        await ApprovalPolicyValidator.ValidateAsync(policy, allowUseGlobalDefault: false, _accounts, ct);

        var existing = await _ctx.ApprovalSettings.Find(FilterDefinition<ApprovalSettings>.Empty).FirstOrDefaultAsync(ct);
        var now = _audit.UtcNow;
        var settings = existing ?? new ApprovalSettings { CreatedAt = now, CreatedBy = _audit.UserName };
        settings.Default = policy;
        settings.ModifiedAt = now;
        settings.ModifiedBy = _audit.UserName;

        await _ctx.ApprovalSettings.ReplaceOneAsync(
            Builders<ApprovalSettings>.Filter.Eq(s => s.Id, settings.Id),
            settings,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return settings.Default;
    }
}
