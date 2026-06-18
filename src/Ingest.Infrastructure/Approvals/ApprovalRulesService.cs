using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Approvals;

/// <summary>
/// Database-backed CRUD for cross-cutting <see cref="ApprovalRule"/> documents. Each write
/// validates the embedded policy (reusing <see cref="ApprovalPolicyValidator"/>, which permits
/// <see cref="ApprovalMode.UseGlobalDefault"/> here so a rule can defer to the global default) and
/// records an audit entry. Rules are soft-deleted to preserve history.
/// </summary>
public sealed class ApprovalRulesService : IApprovalRulesService
{
    private readonly MongoContext _ctx;
    private readonly IAccountRepository _accounts;
    private readonly IAuditLogService _audit;
    private readonly IAuditContext _auditContext;

    /// <summary>Create a new <see cref="ApprovalRulesService"/>.</summary>
    public ApprovalRulesService(MongoContext ctx, IAccountRepository accounts, IAuditLogService audit, IAuditContext auditContext)
    {
        _ctx = ctx;
        _accounts = accounts;
        _audit = audit;
        _auditContext = auditContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRule>> ListAsync(CancellationToken ct = default)
    {
        var filter = Builders<ApprovalRule>.Filter.Eq(r => r.IsDeleted, false);
        return await _ctx.ApprovalRules
            .Find(filter)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<ApprovalRule> CreateAsync(ApprovalRule rule, CancellationToken ct = default)
    {
        await ApprovalPolicyValidator.ValidateAsync(rule.Policy, allowUseGlobalDefault: true, _accounts, ct);

        var now = _auditContext.UtcNow;
        rule.Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id;
        rule.IsDeleted = false;
        rule.CreatedAt = now;
        rule.CreatedBy = _auditContext.UserName;
        rule.ModifiedAt = now;
        rule.ModifiedBy = _auditContext.UserName;

        await _ctx.ApprovalRules.InsertOneAsync(rule, cancellationToken: ct);
        await _audit.RecordAsync(AuditTargetType.ApprovalRule, AuditChangeType.Create, rule.Id, rule.Label, ct);
        return rule;
    }

    /// <inheritdoc />
    public async Task<ApprovalRule> UpdateAsync(Guid id, ApprovalRule rule, CancellationToken ct = default)
    {
        await ApprovalPolicyValidator.ValidateAsync(rule.Policy, allowUseGlobalDefault: true, _accounts, ct);

        var existing = await _ctx.ApprovalRules
            .Find(Builders<ApprovalRule>.Filter.And(
                Builders<ApprovalRule>.Filter.Eq(r => r.Id, id),
                Builders<ApprovalRule>.Filter.Eq(r => r.IsDeleted, false)))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Approval rule");

        existing.Label = rule.Label;
        existing.Enabled = rule.Enabled;
        existing.ServiceIds = rule.ServiceIds;
        existing.SchemaIds = rule.SchemaIds;
        existing.Policy = rule.Policy;
        existing.ModifiedAt = _auditContext.UtcNow;
        existing.ModifiedBy = _auditContext.UserName;

        await _ctx.ApprovalRules.ReplaceOneAsync(
            Builders<ApprovalRule>.Filter.Eq(r => r.Id, existing.Id), existing, cancellationToken: ct);
        await _audit.RecordAsync(AuditTargetType.ApprovalRule, AuditChangeType.Edit, existing.Id, existing.Label, ct);
        return existing;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _ctx.ApprovalRules
            .Find(Builders<ApprovalRule>.Filter.And(
                Builders<ApprovalRule>.Filter.Eq(r => r.Id, id),
                Builders<ApprovalRule>.Filter.Eq(r => r.IsDeleted, false)))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Approval rule");

        existing.IsDeleted = true;
        existing.ModifiedAt = _auditContext.UtcNow;
        existing.ModifiedBy = _auditContext.UserName;

        await _ctx.ApprovalRules.ReplaceOneAsync(
            Builders<ApprovalRule>.Filter.Eq(r => r.Id, existing.Id), existing, cancellationToken: ct);
        await _audit.RecordAsync(AuditTargetType.ApprovalRule, AuditChangeType.Delete, existing.Id, existing.Label, ct);
    }
}
