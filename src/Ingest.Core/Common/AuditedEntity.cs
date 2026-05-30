namespace Ingest.Core.Common;

/// <summary>
/// Base class for every persisted aggregate. Carries the common audit (created/modified/deleted
/// by/at) plus the soft-deletion flag. Repositories stamp the audit fields automatically using
/// <see cref="IAuditContext"/>.
/// </summary>
public abstract class AuditedEntity
{
    /// <summary>Primary key. Pre-populated with a random Guid so callers can construct entities without going through a generator first.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp at which the entity was first persisted.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Name (machine-style account name) of the user/service who created the entity. <c>null</c> when seeded outside an HTTP context.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>UTC timestamp at which the entity was last updated. Matches <see cref="CreatedAt"/> for never-updated entities.</summary>
    public DateTime ModifiedAt { get; set; }

    /// <summary>Name of the last modifier. <c>null</c> when modified outside an HTTP context.</summary>
    public string? ModifiedBy { get; set; }

    /// <summary>Soft-deletion flag. When true, default repository queries exclude this row.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp at which the entity was soft-deleted, if it ever was.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Name of the user/service who soft-deleted the entity.</summary>
    public string? DeletedBy { get; set; }
}
