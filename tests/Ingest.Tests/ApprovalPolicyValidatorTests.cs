using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Approvals;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="ApprovalPolicyValidator"/> — the shared rules that guard a saved approval
/// policy (used by both the schema editor and the global-default settings).
/// </summary>
public class ApprovalPolicyValidatorTests
{
    private static readonly Guid Known = Guid.NewGuid();

    private static Task Validate(ApprovalPolicy policy, bool allowUseGlobalDefault = true) =>
        ApprovalPolicyValidator.ValidateAsync(policy, allowUseGlobalDefault, new FakeAccounts(Known));

    [Fact]
    public async Task None_policy_is_always_valid()
    {
        await Validate(new ApprovalPolicy { Mode = ApprovalMode.None });
    }

    [Fact]
    public async Task Required_with_a_known_required_approver_is_valid()
    {
        var policy = new ApprovalPolicy
        {
            Mode = ApprovalMode.Required,
            Approvers = new() { new ApproverSpec { AccountId = Known, Requirement = ApproverRequirement.Required } },
        };
        await Validate(policy);
    }

    [Fact]
    public async Task Required_with_no_approvers_is_rejected()
    {
        var policy = new ApprovalPolicy { Mode = ApprovalMode.Required };
        await Assert.ThrowsAsync<ValidationException>(() => Validate(policy));
    }

    [Fact]
    public async Task Required_with_only_optional_approvers_is_rejected()
    {
        var policy = new ApprovalPolicy
        {
            Mode = ApprovalMode.Required,
            Approvers = new() { new ApproverSpec { AccountId = Known, Requirement = ApproverRequirement.Optional } },
        };
        await Assert.ThrowsAsync<ValidationException>(() => Validate(policy));
    }

    [Fact]
    public async Task Required_referencing_an_unknown_account_is_rejected()
    {
        var policy = new ApprovalPolicy
        {
            Mode = ApprovalMode.Required,
            Approvers = new() { new ApproverSpec { AccountId = Guid.NewGuid(), Requirement = ApproverRequirement.Required } },
        };
        await Assert.ThrowsAsync<ValidationException>(() => Validate(policy));
    }

    [Fact]
    public async Task Duplicate_approver_entries_are_rejected()
    {
        var policy = new ApprovalPolicy
        {
            Mode = ApprovalMode.Required,
            Approvers = new()
            {
                new ApproverSpec { AccountId = Known, Requirement = ApproverRequirement.Required },
                new ApproverSpec { AccountId = Known, Requirement = ApproverRequirement.Optional },
            },
        };
        await Assert.ThrowsAsync<ValidationException>(() => Validate(policy));
    }

    [Fact]
    public async Task ServiceOwner_can_be_the_only_required_approver()
    {
        // No fixed account to verify — the owner is bound per submission — so a service-owner
        // entry alone satisfies the "at least one required approver" rule.
        var policy = new ApprovalPolicy
        {
            Mode = ApprovalMode.Required,
            Approvers = new() { new ApproverSpec { Kind = ApproverKind.ServiceOwner, Requirement = ApproverRequirement.Required } },
        };
        await Validate(policy);
    }

    [Fact]
    public async Task ServiceOwner_alongside_a_known_account_is_valid()
    {
        var policy = new ApprovalPolicy
        {
            Mode = ApprovalMode.Required,
            Approvers = new()
            {
                new ApproverSpec { AccountId = Known, Requirement = ApproverRequirement.Required },
                new ApproverSpec { Kind = ApproverKind.ServiceOwner, Requirement = ApproverRequirement.Optional },
            },
        };
        await Validate(policy);
    }

    [Fact]
    public async Task Duplicate_service_owner_entries_are_rejected()
    {
        var policy = new ApprovalPolicy
        {
            Mode = ApprovalMode.Required,
            Approvers = new()
            {
                new ApproverSpec { Kind = ApproverKind.ServiceOwner, Requirement = ApproverRequirement.Required },
                new ApproverSpec { Kind = ApproverKind.ServiceOwner, Requirement = ApproverRequirement.Optional },
            },
        };
        await Assert.ThrowsAsync<ValidationException>(() => Validate(policy));
    }

    [Fact]
    public async Task UseGlobalDefault_is_rejected_when_not_allowed()
    {
        var policy = new ApprovalPolicy { Mode = ApprovalMode.UseGlobalDefault };
        await Assert.ThrowsAsync<ValidationException>(() => Validate(policy, allowUseGlobalDefault: false));
    }

    [Fact]
    public async Task UseGlobalDefault_is_allowed_for_schemas()
    {
        var policy = new ApprovalPolicy { Mode = ApprovalMode.UseGlobalDefault };
        await Validate(policy, allowUseGlobalDefault: true);
    }

    /// <summary>Minimal account repo: only the configured ids "exist"; everything else is null.</summary>
    private sealed class FakeAccounts(params Guid[] known) : IAccountRepository
    {
        private readonly HashSet<Guid> _known = known.ToHashSet();

        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_known.Contains(id) ? new Account { Id = id, Name = "approver", Kind = AccountKind.User, Role = AccountRole.Approver } : null);
        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => Task.FromResult<Account?>(null);
        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) => Task.FromResult<Account?>(null);
        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Account>(Array.Empty<Account>(), 0, request.Page, request.PageSize));
        public Task AddAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThan, CancellationToken ct = default) => Task.FromResult(0L);
    }
}
