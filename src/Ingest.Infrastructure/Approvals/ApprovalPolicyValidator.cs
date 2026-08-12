using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Approvals;

/// <summary>
/// Shared validation for an <see cref="ApprovalPolicy"/>, used by both the schema editor and the
/// global-default settings service so the rules stay in one place.
/// </summary>
public static class ApprovalPolicyValidator
{
    /// <summary>
    /// Validate a policy. Throws <see cref="ValidationException"/> on any problem. A
    /// <see cref="ApprovalMode.Required"/> policy must name at least one required approver, every
    /// referenced approver account must exist, and duplicate approver entries are rejected.
    /// </summary>
    /// <param name="policy">Policy to validate.</param>
    /// <param name="allowUseGlobalDefault">Whether <see cref="ApprovalMode.UseGlobalDefault"/> is permitted (schemas yes, the global default no).</param>
    /// <param name="accounts">Account repository used to confirm approver accounts exist.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ValidateAsync(
        ApprovalPolicy policy,
        bool allowUseGlobalDefault,
        IAccountRepository accounts,
        CancellationToken ct = default)
    {
        var errors = new List<Diagnostic>();

        if (policy.Mode == ApprovalMode.UseGlobalDefault && !allowUseGlobalDefault)
            errors.Add(Diagnostic.Create(
                DiagnosticCodes.Approval.GlobalDefaultNotAllowed,
                "This policy cannot defer to the global default.",
                ("mode", policy.Mode.ToString())));

        if (policy.Mode == ApprovalMode.Required)
        {
            if (policy.Approvers.Count == 0)
                errors.Add(new Diagnostic(
                    DiagnosticCodes.Approval.ApproverRequired,
                    "At least one approver is required when approval is required."));
            else if (!policy.Approvers.Any(a => a.Requirement == ApproverRequirement.Required))
                errors.Add(new Diagnostic(
                    DiagnosticCodes.Approval.RequiredApproverRequired,
                    "At least one approver must be marked as required."));

            var seen = new HashSet<Guid>();
            var seenServiceOwner = false;
            foreach (var spec in policy.Approvers)
            {
                // The service-owner approver has no fixed account — it's bound per submission to the
                // sender — so there's nothing to verify beyond rejecting a duplicate entry.
                if (spec.Kind == ApproverKind.ServiceOwner)
                {
                    if (seenServiceOwner)
                        errors.Add(new Diagnostic(
                            DiagnosticCodes.Approval.DuplicateServiceOwner,
                            "The service owner is listed more than once."));
                    seenServiceOwner = true;
                    continue;
                }

                if (!seen.Add(spec.AccountId))
                {
                    errors.Add(Diagnostic.Create(
                        DiagnosticCodes.Approval.DuplicateApprover,
                        "The same approver is listed more than once.",
                        ("accountId", spec.AccountId)));
                    continue;
                }
                var account = await accounts.GetByIdAsync(spec.AccountId, ct: ct);
                if (account is null)
                    errors.Add(Diagnostic.Create(
                        DiagnosticCodes.Approval.ApproverNotFound,
                        $"Approver account '{spec.AccountId}' does not exist.",
                        ("accountId", spec.AccountId)));
            }
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
