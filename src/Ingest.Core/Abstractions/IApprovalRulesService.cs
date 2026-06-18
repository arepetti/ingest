using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// CRUD access to the cross-cutting <see cref="ApprovalRule"/> set — approval requirements keyed
/// by service and schema that apply additively on top of the per-schema and global-default
/// policies. Rules are soft-deleted so audit history is preserved.
/// </summary>
public interface IApprovalRulesService
{
    /// <summary>List every rule (excluding soft-deleted ones), newest first.</summary>
    Task<IReadOnlyList<ApprovalRule>> ListAsync(CancellationToken ct = default);

    /// <summary>Create a new rule. The embedded policy is validated (>= 1 required approver when Mode is Required).</summary>
    Task<ApprovalRule> CreateAsync(ApprovalRule rule, CancellationToken ct = default);

    /// <summary>Replace an existing rule by id. Throws when the rule doesn't exist; validates the policy.</summary>
    Task<ApprovalRule> UpdateAsync(Guid id, ApprovalRule rule, CancellationToken ct = default);

    /// <summary>Soft-delete a rule by id.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
