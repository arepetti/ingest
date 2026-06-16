using System.Security.Claims;
using Ingest.Api.Auth;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;

namespace Ingest.Tests;

/// <summary>
/// Phase 2 capability model: the effective-capability resolver (<see cref="RoleCapabilities"/>),
/// the claim emission (<see cref="IngestClaims"/>) and the gate that turns a capability claim into
/// an authorization decision (<see cref="CapabilityAuthorizationHandler"/>). These are the load-
/// bearing pieces behind every <c>[Authorize(Policy = "&lt;cap&gt;")]</c> attribute.
/// </summary>
public class CapabilityResolutionTests
{
    private static Account NewAccount(AccountRole role, params string[] overrides) => new()
    {
        Name = "acct",
        Kind = AccountKind.User,
        Role = role,
        Enabled = true,
        Capabilities = overrides.ToList(),
    };

    // ── Role-default bundles ────────────────────────────────────────────────────────────────

    [Fact]
    public void Service_role_seeds_no_capabilities()
    {
        Assert.Empty(RoleCapabilities.DefaultsFor(AccountRole.Service));
    }

    [Fact]
    public void Operator_role_seeds_the_read_only_back_office_bundle()
    {
        var defaults = RoleCapabilities.DefaultsFor(AccountRole.Operator);

        Assert.Contains(Capabilities.SchemasRead, defaults);
        Assert.Contains(Capabilities.SubmissionsRead, defaults);
        Assert.Contains(Capabilities.ReportsRead, defaults);
        // Operators are readers — they get no manage/approve verbs by default.
        Assert.DoesNotContain(Capabilities.SchemasManage, defaults);
        Assert.DoesNotContain(Capabilities.SubmissionsApprove, defaults);
    }

    [Fact]
    public void Approver_role_seeds_only_read_and_approve_submissions()
    {
        var defaults = RoleCapabilities.DefaultsFor(AccountRole.Approver);

        Assert.Equal(
            new[] { Capabilities.SubmissionsRead, Capabilities.SubmissionsApprove }.OrderBy(x => x),
            defaults.OrderBy(x => x));
    }

    [Fact]
    public void Admin_role_seeds_the_entire_catalogue()
    {
        Assert.Equal(Capabilities.All.OrderBy(x => x), RoleCapabilities.DefaultsFor(AccountRole.Admin).OrderBy(x => x));
    }

    // ── Effective resolution: defaults vs overrides ─────────────────────────────────────────

    [Fact]
    public void Effective_falls_back_to_role_defaults_when_no_overrides()
    {
        var effective = RoleCapabilities.Effective(NewAccount(AccountRole.Operator));

        Assert.Equal(RoleCapabilities.DefaultsFor(AccountRole.Operator).OrderBy(x => x), effective.OrderBy(x => x));
    }

    [Fact]
    public void Effective_overrides_replace_the_role_default_bundle_entirely()
    {
        // A trusted operator granted schemas:manage — the override set is authoritative, so the
        // role's read bundle is NOT additively merged.
        var account = NewAccount(AccountRole.Operator, Capabilities.SchemasManage);

        var effective = RoleCapabilities.Effective(account);

        Assert.Contains(Capabilities.SchemasManage, effective);
        Assert.DoesNotContain(Capabilities.SubmissionsRead, effective);
        Assert.Single(effective);
    }

    [Fact]
    public void Effective_ignores_unknown_capability_strings_defensively()
    {
        var account = NewAccount(AccountRole.Operator, Capabilities.StatusRead, "totally:bogus");

        var effective = RoleCapabilities.Effective(account);

        Assert.Contains(Capabilities.StatusRead, effective);
        Assert.DoesNotContain("totally:bogus", effective);
    }

    [Fact]
    public void Effective_for_admin_is_the_full_catalogue_regardless_of_overrides()
    {
        // Even a (nonsensical) attempt to reduce an Admin's powers is ignored — Admin is the
        // non-reducible lockout-safe floor.
        var account = NewAccount(AccountRole.Admin, Capabilities.StatusRead);

        var effective = RoleCapabilities.Effective(account);

        Assert.Equal(Capabilities.All.OrderBy(x => x), effective.OrderBy(x => x));
    }

    [Fact]
    public void IsKnown_recognises_catalogue_members_and_rejects_strangers()
    {
        Assert.True(Capabilities.IsKnown(Capabilities.SubmissionsApprove));
        Assert.False(Capabilities.IsKnown("submissions:teleport"));
    }

    // ── Claim emission ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_emits_one_capability_claim_per_effective_capability()
    {
        var claims = IngestClaims.Build(NewAccount(AccountRole.Approver));

        var caps = claims.Where(c => c.Type == AuthConstants.CapabilityClaim).Select(c => c.Value).ToHashSet();
        Assert.Equal(
            new[] { Capabilities.SubmissionsRead, Capabilities.SubmissionsApprove }.OrderBy(x => x),
            caps.OrderBy(x => x));

        // Identity scaffolding is still present.
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role && c.Value == nameof(AccountRole.Approver));
        Assert.Contains(claims, c => c.Type == AuthConstants.AccountNameClaim);
    }

    [Fact]
    public void Build_for_admin_emits_the_whole_catalogue_as_capability_claims()
    {
        var claims = IngestClaims.Build(NewAccount(AccountRole.Admin));

        var caps = claims.Where(c => c.Type == AuthConstants.CapabilityClaim).Select(c => c.Value).ToHashSet();
        Assert.Equal(Capabilities.All.OrderBy(x => x), caps.OrderBy(x => x));
    }

    // ── The authorization gate (capability-gated approve/reject) ────────────────────────────

    private static async Task<bool> EvaluateAsync(Account account, string requiredCapability)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(IngestClaims.Build(account), "test"));
        var requirement = new CapabilityRequirement(requiredCapability);
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);

        await new CapabilityAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Approver_passes_the_submissions_approve_gate()
    {
        Assert.True(await EvaluateAsync(NewAccount(AccountRole.Approver), Capabilities.SubmissionsApprove));
    }

    [Fact]
    public async Task Operator_is_denied_the_submissions_approve_gate_by_default()
    {
        Assert.False(await EvaluateAsync(NewAccount(AccountRole.Operator), Capabilities.SubmissionsApprove));
    }

    [Fact]
    public async Task Operator_granted_the_override_passes_the_approve_gate()
    {
        var account = NewAccount(AccountRole.Operator, Capabilities.SubmissionsRead, Capabilities.SubmissionsApprove);
        Assert.True(await EvaluateAsync(account, Capabilities.SubmissionsApprove));
    }

    [Fact]
    public async Task Admin_passes_every_gate_implicitly()
    {
        var admin = NewAccount(AccountRole.Admin);
        Assert.True(await EvaluateAsync(admin, Capabilities.SubmissionsApprove));
        Assert.True(await EvaluateAsync(admin, Capabilities.SchemasManage));
        Assert.True(await EvaluateAsync(admin, Capabilities.BackupManage));
    }

    [Fact]
    public async Task Service_account_is_denied_back_office_gates()
    {
        var svc = NewAccount(AccountRole.Service);
        Assert.False(await EvaluateAsync(svc, Capabilities.SubmissionsRead));
        Assert.False(await EvaluateAsync(svc, Capabilities.SchemasManage));
    }
}
