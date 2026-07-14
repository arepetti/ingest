using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// Distinguishes interactive accounts from automated callers. Orthogonal to <see cref="AccountRole"/>:
/// any kind can hold any role. The admin SPA refuses to sign in an <see cref="Application"/>-kind
/// account; everything else is enforced server-side by the role-based policies.
/// </summary>
public enum AccountKind
{
    /// <summary>API-only credential intended for automation/services. Cannot log in to the UI.</summary>
    Application = 0,

    /// <summary>Interactive human account. Can log in to the UI and call the APIs.</summary>
    User = 1,
}

/// <summary>
/// Authorisation tier carried as a <c>ClaimTypes.Role</c> claim. The three roles form a strict
/// hierarchy for the read endpoints (<c>Service ⊂ Operator ⊂ Admin</c>); for the write endpoints
/// each role gets specific operations rather than inheriting them.
/// </summary>
public enum AccountRole
{
    /// <summary>Submitter on behalf of a single service. Reads/writes its own data only.</summary>
    Service = 0,

    /// <summary>Back-office reader (e.g. data analyst). May read everything but cannot mutate accounts, keys, schemas, or submissions.</summary>
    Operator = 1,

    /// <summary>Full control: account/key/schema/submission CRUD, on-behalf-of submissions, hard delete.</summary>
    Admin = 2,

    /// <summary>
    /// Reviewer in the submission-approval workflow. Like <see cref="Operator"/> for reads, but
    /// additionally allowed to approve/reject submissions they are designated approvers for.
    /// (Phase 1 baseline role; finer-grained capabilities arrive in Phase 2.)
    /// </summary>
    Approver = 3,
}

/// <summary>
/// Top-level registry entity. Holds the credentials of every actor — both interactive users
/// (admins, operators) and automated submitters (services). Soft-deleted via the inherited
/// <see cref="AuditedEntity.IsDeleted"/> flag so audit history is preserved.
/// </summary>
public sealed class Account : AuditedEntity
{
    /// <summary>Stable machine-style identifier; unique across all accounts (including soft-deleted ones).</summary>
    public required string Name { get; set; }

    /// <summary>Friendly label displayed in the UI. Falls back to <see cref="Name"/> when empty.</summary>
    public string? Label { get; set; }

    /// <summary>Free-form description.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Contact email used by the notifications/email features. Optional at the data layer (legacy
    /// accounts and the bootstrap admin may have none) even though the admin UI now asks for it on
    /// create/edit. Stored lower-cased/trimmed; <c>null</c> when unset.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Optional, informative-only grouping tag (e.g. a geographic or organisational area). When a
    /// configured list of areas exists the admin UI offers it as a dropdown, otherwise as free text.
    /// Always optional and never validated against the configured list server-side, so a later change
    /// to that list cannot invalidate existing accounts. Trimmed; <c>null</c> when unset.
    /// </summary>
    public string? Area { get; set; }

    /// <summary>Distinguishes UI-capable accounts from API-only ones.</summary>
    public AccountKind Kind { get; set; } = AccountKind.Application;

    /// <summary>Authorisation tier. From Phase 2 the role is a decorative template that seeds the
    /// default capability bundle; the effective authorization is governed by <see cref="Capabilities"/>.</summary>
    public AccountRole Role { get; set; } = AccountRole.Service;

    /// <summary>
    /// Per-account capability overrides (Phase 2). When non-empty this set is the authoritative
    /// effective capability set for the account; when empty the account falls back to its
    /// <see cref="Role"/>'s default bundle (so legacy accounts behave exactly as before — no data
    /// migration). <see cref="AccountRole.Admin"/> ignores this and implicitly holds every
    /// capability. Capability strings come from <c>Ingest.Core.Security.Capabilities</c>.
    /// </summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>
    /// Optional per-service scope for back-office readers. When non-empty, every capability this
    /// account holds is confined to data belonging to the listed service accounts: cross-service
    /// reads (submissions, status, Explore, the OData/Power BI feed) are filtered to these ids and
    /// any other service is invisible. When <b>empty</b> (the default) the account is unrestricted
    /// and sees every service exactly as before — so existing accounts need no migration.
    /// <see cref="AccountRole.Admin"/> ignores this and always sees every service. The ids are
    /// expected to be <see cref="AccountRole.Service"/> accounts.
    /// </summary>
    public List<Guid> AssignedServiceIds { get; set; } = new();

    /// <summary>When false, every API key attached to this account is invalid for new requests.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// External identity-provider links that let this account sign in via SSO. Only
    /// <see cref="AccountKind.User"/> accounts may hold links (enforced by the account service).
    /// Empty for API-only accounts and whenever SSO isn't used.
    /// </summary>
    public List<ExternalLogin> ExternalLogins { get; set; } = new();
}

/// <summary>
/// A link between an <see cref="Account"/> and an external identity-provider identity. An admin
/// pre-registers the <see cref="Provider"/> + <see cref="Email"/> pair; the OIDC callback matches
/// on it (case-insensitively) and binds <see cref="Subject"/> on the first successful login.
/// </summary>
public sealed class ExternalLogin
{
    /// <summary>Provider id this link belongs to (matches an <c>Sso:Providers:*:Id</c>, e.g. <c>"Microsoft"</c> or <c>"Google"</c>).</summary>
    public required string Provider { get; set; }

    /// <summary>Verified email used to match the incoming identity. Stored lower-cased for case-insensitive lookups.</summary>
    public required string Email { get; set; }

    /// <summary>The provider's stable subject (<c>sub</c>) claim, bound on first successful login. Null until then.</summary>
    public string? Subject { get; set; }
}
