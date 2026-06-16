namespace Ingest.Core.Entities;

/// <summary>
/// An immutable record of a single schema save (create or update). One row is written every time a
/// schema is persisted, capturing a full <see cref="Snapshot"/> of the schema as it was at that
/// moment plus enough metadata for the admin "version history" page (who, when, the version
/// before/after the save, whether the version was bumped, whether the schema was Published
/// (Enabled) or Draft, and how many submissions existed at that point).
/// <para>
/// Stored in its own <c>schemaVersionHistories</c> collection. Unlike the audit log this is not a
/// soft-deleted aggregate — admins may permanently delete individual entries or the whole history
/// to reclaim space (those deletions are themselves recorded in the audit log). Deleting history
/// never affects the live schema.
/// </para>
/// </summary>
public sealed class SchemaVersionHistory
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Id of the live schema this snapshot belongs to.</summary>
    public Guid SchemaId { get; set; }

    /// <summary>Machine-style schema name at the time of the save (snapshot, so it survives renames/deletes).</summary>
    public required string SchemaName { get; set; }

    /// <summary>UTC timestamp at which the save happened.</summary>
    public DateTime ChangeDate { get; set; }

    /// <summary>Id of the account that performed the save, or <c>null</c> when acted outside an authenticated context.</summary>
    public Guid? AuthorId { get; set; }

    /// <summary>Machine name of the account that performed the save, or <c>null</c> as above.</summary>
    public string? AuthorName { get; set; }

    /// <summary>Schema version before this save. <c>null</c> for the initial create (no prior version).</summary>
    public int? OldVersion { get; set; }

    /// <summary>Schema version after this save.</summary>
    public int NewVersion { get; set; }

    /// <summary>True when this save changed the version number (<see cref="NewVersion"/> differs from <see cref="OldVersion"/>).</summary>
    public bool VersionBumped { get; set; }

    /// <summary>Whether the schema was Enabled (Published) at this point. <c>false</c> means Draft.</summary>
    public bool Enabled { get; set; }

    /// <summary>Number of submissions referencing this schema at the time of the save.</summary>
    public long SubmissionCount { get; set; }

    /// <summary>Full snapshot of the schema as persisted by this save. Used to reconstruct the read-only "view this version" page.</summary>
    public required Schema Snapshot { get; set; }
}
