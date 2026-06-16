using Ingest.Core.Entities;

namespace Ingest.Core.Approvals;

/// <summary>The outcome of resolving an effective approval policy for a submission.</summary>
/// <param name="Required">True when this submission must be approved before going live.</param>
/// <param name="Approvers">
/// The designated approvers to snapshot onto the submission (required and optional). Empty when
/// <paramref name="Required"/> is false.
/// </param>
public readonly record struct ResolvedApproval(bool Required, IReadOnlyList<ApproverSpec> Approvers)
{
    /// <summary>A "no approval needed" outcome.</summary>
    public static ResolvedApproval NotRequired { get; } = new(false, Array.Empty<ApproverSpec>());
}

/// <summary>
/// Pure resolution of the effective approval policy for a submission. Combines the master switch,
/// the schema's own policy (which may defer to the global default), and the submission's source.
/// Kept free of I/O so it's trivially unit-testable; callers fetch the schema + global default and
/// pass them in.
/// </summary>
public static class ApprovalPolicyResolver
{
    /// <summary>Resolve whether a submission needs approval, and which approvers govern it.</summary>
    /// <param name="masterEnabled">The <c>Approval:Enabled</c> master switch.</param>
    /// <param name="schemaPolicy">The schema's own policy (<c>null</c> = no approval).</param>
    /// <param name="globalDefault">The server-wide default policy (used when the schema defers to it).</param>
    /// <param name="source">Where the submission came from.</param>
    public static ResolvedApproval Resolve(
        bool masterEnabled,
        ApprovalPolicy? schemaPolicy,
        ApprovalPolicy? globalDefault,
        SubmissionSource source)
    {
        if (!masterEnabled) return ResolvedApproval.NotRequired;

        var effective = schemaPolicy?.Mode switch
        {
            ApprovalMode.Required => schemaPolicy,
            ApprovalMode.UseGlobalDefault => globalDefault is { Mode: ApprovalMode.Required } ? globalDefault : null,
            _ => null, // None or null
        };

        if (effective is null) return ResolvedApproval.NotRequired;
        if (!AppliesToSource(effective.AppliesToSources, source)) return ResolvedApproval.NotRequired;

        // Misconfiguration guard: an approval policy with no *required* approver has nothing to
        // gate on, so treat it as "not required" rather than leaving submissions stuck forever.
        var hasRequired = effective.Approvers.Any(a => a.Requirement == ApproverRequirement.Required);
        if (!hasRequired) return ResolvedApproval.NotRequired;

        // Snapshot every designated approver (required + optional) so optional approvers can act too.
        // The service-owner kind is carried through unresolved; the caller (which knows the
        // submission's service account) binds it to a concrete account.
        var snapshot = effective.Approvers
            .Select(a => new ApproverSpec { AccountId = a.AccountId, Kind = a.Kind, Requirement = a.Requirement })
            .ToList();
        return new ResolvedApproval(true, snapshot);
    }

    /// <summary>True when a policy scoped to <paramref name="scope"/> applies to a <paramref name="source"/> submission.</summary>
    public static bool AppliesToSource(ApprovalSourceScope scope, SubmissionSource source) => scope switch
    {
        ApprovalSourceScope.ManualOnly => source == SubmissionSource.Manual,
        ApprovalSourceScope.ApiOnly => source == SubmissionSource.Api,
        _ => true, // Both
    };

    /// <summary>
    /// True when every <see cref="ApproverRequirement.Required"/> approver in the snapshot has an
    /// <see cref="ApprovalDecision.Approved"/> decision recorded. Optional approvers don't gate.
    /// </summary>
    public static bool IsComplete(
        IReadOnlyList<ApproverSpec> requiredApprovers,
        IReadOnlyList<SubmissionApproval> approvals)
    {
        var approvedBy = approvals
            .Where(a => a.Decision == ApprovalDecision.Approved)
            .Select(a => a.ApproverAccountId)
            .ToHashSet();

        return requiredApprovers
            .Where(a => a.Requirement == ApproverRequirement.Required)
            .All(a => approvedBy.Contains(a.AccountId));
    }
}
