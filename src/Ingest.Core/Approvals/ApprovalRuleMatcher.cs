using Ingest.Core.Entities;

namespace Ingest.Core.Approvals;

/// <summary>
/// Pure matching of a cross-cutting <see cref="ApprovalRule"/> against a concrete
/// (service, schema) pair. Kept free of I/O so the match logic is trivially unit-testable; the
/// caller decides what to do with a matching rule (resolve its policy and merge approvers).
/// </summary>
public static class ApprovalRuleMatcher
{
    /// <summary>
    /// True when <paramref name="rule"/> applies to the given service and schema. The rule must be
    /// enabled; an empty <see cref="ApprovalRule.ServiceIds"/> matches every service and an empty
    /// <see cref="ApprovalRule.SchemaIds"/> matches every schema. A <paramref name="schemaId"/> of
    /// <c>null</c> (schema not resolvable) still matches an "all schemas" rule.
    /// </summary>
    /// <param name="rule">The rule to test.</param>
    /// <param name="serviceId">The submitting service account.</param>
    /// <param name="schemaId">The schema being submitted, or <c>null</c> when it can't be resolved.</param>
    public static bool Matches(ApprovalRule rule, Guid serviceId, Guid? schemaId)
    {
        if (!rule.Enabled) return false;

        var serviceMatches = rule.ServiceIds.Count == 0 || rule.ServiceIds.Contains(serviceId);
        if (!serviceMatches) return false;

        return rule.SchemaIds.Count == 0 || (schemaId is { } id && rule.SchemaIds.Contains(id));
    }
}
