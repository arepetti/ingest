using Ingest.Core.Entities;

namespace Ingest.Api.Common;

/// <summary>
/// Stable target identifiers (and display names) for the configuration areas recorded in the audit
/// log under <see cref="AuditTargetType.Settings"/> and <see cref="AuditTargetType.Backup"/>. Each
/// of these is a server-wide singleton, so a fixed, well-known id — rather than the underlying
/// document's id — keeps every edit of one area grouped together in the per-target audit history
/// and stable across deployments and rebuilds. (Email templates are the exception: they are keyed
/// records, so their own id and name are recorded directly.)
/// </summary>
public static class AuditTargets
{
    /// <summary>The server-wide default approval policy.</summary>
    public static readonly Guid ApprovalPolicy = new("a0d17e00-0000-0000-0000-000000000001");

    /// <summary>The SMTP / email server settings.</summary>
    public static readonly Guid EmailSettings = new("a0d17e00-0000-0000-0000-000000000002");

    /// <summary>The notification configuration.</summary>
    public static readonly Guid NotificationSettings = new("a0d17e00-0000-0000-0000-000000000003");

    /// <summary>The Microsoft Teams bot connection.</summary>
    public static readonly Guid TeamsConnection = new("a0d17e00-0000-0000-0000-000000000004");

    /// <summary>A restore of the data backup (the registry).</summary>
    public static readonly Guid DataBackup = new("b0d17e00-0000-0000-0000-000000000001");

    /// <summary>A restore of the configuration backup (the Settings-page configuration).</summary>
    public static readonly Guid ConfigBackup = new("b0d17e00-0000-0000-0000-000000000002");
}
