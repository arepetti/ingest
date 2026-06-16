using Ingest.Core.Approvals;
using Ingest.Core.Entities;

namespace Ingest.Tests;

/// <summary>
/// Unit tests for the pure approval-policy resolution and completion logic
/// (<see cref="ApprovalPolicyResolver"/>). These have no I/O, so they pin down the decision
/// table that drives whether a submission is held for approval and when it goes live.
/// </summary>
public class ApprovalPolicyResolverTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static ApprovalPolicy Required(ApprovalSourceScope scope = ApprovalSourceScope.Both, params (Guid id, ApproverRequirement req)[] approvers) => new()
    {
        Mode = ApprovalMode.Required,
        AppliesToSources = scope,
        Approvers = approvers.Select(a => new ApproverSpec { AccountId = a.id, Requirement = a.req }).ToList(),
    };

    [Fact]
    public void Master_switch_off_means_never_required()
    {
        var policy = Required(approvers: (Alice, ApproverRequirement.Required));
        var r = ApprovalPolicyResolver.Resolve(masterEnabled: false, policy, globalDefault: null, SubmissionSource.Manual);
        Assert.False(r.Required);
    }

    [Fact]
    public void Null_schema_policy_is_not_required()
    {
        var r = ApprovalPolicyResolver.Resolve(true, schemaPolicy: null, globalDefault: null, SubmissionSource.Manual);
        Assert.False(r.Required);
    }

    [Fact]
    public void Mode_none_is_not_required()
    {
        var policy = new ApprovalPolicy { Mode = ApprovalMode.None };
        var r = ApprovalPolicyResolver.Resolve(true, policy, null, SubmissionSource.Api);
        Assert.False(r.Required);
    }

    [Fact]
    public void Required_with_a_required_approver_gates_and_snapshots_all_approvers()
    {
        var policy = Required(approvers: new[] { (Alice, ApproverRequirement.Required), (Bob, ApproverRequirement.Optional) });
        var r = ApprovalPolicyResolver.Resolve(true, policy, null, SubmissionSource.Manual);

        Assert.True(r.Required);
        Assert.Equal(2, r.Approvers.Count);
        Assert.Contains(r.Approvers, a => a.AccountId == Alice && a.Requirement == ApproverRequirement.Required);
        Assert.Contains(r.Approvers, a => a.AccountId == Bob && a.Requirement == ApproverRequirement.Optional);
    }

    [Fact]
    public void ServiceOwner_approver_is_carried_through_the_snapshot_unresolved()
    {
        // The resolver leaves the service-owner kind intact (AccountId unset); the submission
        // service binds it to the sender later.
        var policy = new ApprovalPolicy
        {
            Mode = ApprovalMode.Required,
            Approvers = new() { new ApproverSpec { Kind = ApproverKind.ServiceOwner, Requirement = ApproverRequirement.Required } },
        };
        var r = ApprovalPolicyResolver.Resolve(true, policy, null, SubmissionSource.Manual);

        Assert.True(r.Required);
        Assert.Single(r.Approvers);
        Assert.Equal(ApproverKind.ServiceOwner, r.Approvers[0].Kind);
        Assert.Equal(Guid.Empty, r.Approvers[0].AccountId);
    }

    [Fact]
    public void Required_without_any_required_approver_is_treated_as_not_required()
    {
        // Misconfiguration guard: a policy with only optional approvers has nothing to gate on.
        var policy = Required(approvers: (Bob, ApproverRequirement.Optional));
        var r = ApprovalPolicyResolver.Resolve(true, policy, null, SubmissionSource.Manual);
        Assert.False(r.Required);
    }

    [Theory]
    [InlineData(ApprovalSourceScope.ManualOnly, SubmissionSource.Manual, true)]
    [InlineData(ApprovalSourceScope.ManualOnly, SubmissionSource.Api, false)]
    [InlineData(ApprovalSourceScope.ApiOnly, SubmissionSource.Api, true)]
    [InlineData(ApprovalSourceScope.ApiOnly, SubmissionSource.Manual, false)]
    [InlineData(ApprovalSourceScope.Both, SubmissionSource.Api, true)]
    [InlineData(ApprovalSourceScope.Both, SubmissionSource.Manual, true)]
    public void Source_scope_is_honoured(ApprovalSourceScope scope, SubmissionSource source, bool expectedRequired)
    {
        var policy = Required(scope, (Alice, ApproverRequirement.Required));
        var r = ApprovalPolicyResolver.Resolve(true, policy, null, source);
        Assert.Equal(expectedRequired, r.Required);
    }

    [Fact]
    public void UseGlobalDefault_defers_to_a_required_global_policy()
    {
        var schema = new ApprovalPolicy { Mode = ApprovalMode.UseGlobalDefault };
        var global = Required(approvers: (Alice, ApproverRequirement.Required));

        var r = ApprovalPolicyResolver.Resolve(true, schema, global, SubmissionSource.Manual);

        Assert.True(r.Required);
        Assert.Single(r.Approvers);
        Assert.Equal(Alice, r.Approvers[0].AccountId);
    }

    [Fact]
    public void UseGlobalDefault_with_no_required_global_is_not_required()
    {
        var schema = new ApprovalPolicy { Mode = ApprovalMode.UseGlobalDefault };
        var global = new ApprovalPolicy { Mode = ApprovalMode.None };

        var r = ApprovalPolicyResolver.Resolve(true, schema, global, SubmissionSource.Manual);
        Assert.False(r.Required);
    }

    [Fact]
    public void UseGlobalDefault_with_null_global_is_not_required()
    {
        var schema = new ApprovalPolicy { Mode = ApprovalMode.UseGlobalDefault };
        var r = ApprovalPolicyResolver.Resolve(true, schema, globalDefault: null, SubmissionSource.Manual);
        Assert.False(r.Required);
    }

    [Fact]
    public void IsComplete_true_only_when_every_required_approver_has_approved()
    {
        var required = new List<ApproverSpec>
        {
            new() { AccountId = Alice, Requirement = ApproverRequirement.Required },
            new() { AccountId = Bob, Requirement = ApproverRequirement.Required },
        };

        var onlyAlice = new List<SubmissionApproval>
        {
            new() { ApproverAccountId = Alice, Decision = ApprovalDecision.Approved },
        };
        Assert.False(ApprovalPolicyResolver.IsComplete(required, onlyAlice));

        var both = new List<SubmissionApproval>(onlyAlice)
        {
            new() { ApproverAccountId = Bob, Decision = ApprovalDecision.Approved },
        };
        Assert.True(ApprovalPolicyResolver.IsComplete(required, both));
    }

    [Fact]
    public void IsComplete_ignores_optional_approvers()
    {
        var approvers = new List<ApproverSpec>
        {
            new() { AccountId = Alice, Requirement = ApproverRequirement.Required },
            new() { AccountId = Bob, Requirement = ApproverRequirement.Optional },
        };
        // Only the required approver (Alice) has signed off; Bob is optional and absent.
        var approvals = new List<SubmissionApproval>
        {
            new() { ApproverAccountId = Alice, Decision = ApprovalDecision.Approved },
        };
        Assert.True(ApprovalPolicyResolver.IsComplete(approvers, approvals));
    }

    [Fact]
    public void IsComplete_does_not_count_a_rejection_as_approval()
    {
        var required = new List<ApproverSpec> { new() { AccountId = Alice, Requirement = ApproverRequirement.Required } };
        var approvals = new List<SubmissionApproval> { new() { ApproverAccountId = Alice, Decision = ApprovalDecision.Rejected } };
        Assert.False(ApprovalPolicyResolver.IsComplete(required, approvals));
    }
}
