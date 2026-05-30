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

    /// <summary>Distinguishes UI-capable accounts from API-only ones.</summary>
    public AccountKind Kind { get; set; } = AccountKind.Application;

    /// <summary>Authorisation tier; drives every role-based policy.</summary>
    public AccountRole Role { get; set; } = AccountRole.Service;

    /// <summary>When false, every API key attached to this account is invalid for new requests.</summary>
    public bool Enabled { get; set; } = true;
}
