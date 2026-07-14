using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// Server-wide singleton holding miscellaneous admin-configurable lists. Currently just the set of
/// "areas" an account can be tagged with (used to group and label services in the UI and exports).
/// At most one document exists; an absent one reads back as an empty configuration, which keeps
/// fresh and legacy deployments back-compatible.
/// </summary>
public sealed class AppConfiguration : AuditedEntity
{
    /// <summary>
    /// Ordered list of area names offered when editing an account. When empty the account editor
    /// falls back to a free-text area field.
    /// </summary>
    public List<string> Areas { get; set; } = new();
}
