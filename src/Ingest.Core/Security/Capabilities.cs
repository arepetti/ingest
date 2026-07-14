using Ingest.Core.Entities;

namespace Ingest.Core.Security;

/// <summary>
/// The capability catalogue: named, fine-grained permissions that are the real unit of
/// authorization from Phase 2 onward. Roles (<see cref="AccountRole"/>) become decorative
/// templates that only seed a default bundle; the <b>effective</b> capability set on an account is
/// what actually governs what it may do and see.
/// </summary>
/// <remarks>
/// Naming convention: <c>"&lt;feature&gt;:&lt;action&gt;"</c> where action is <c>read</c> or
/// <c>manage</c> (plus the extra submission verbs <c>submit</c>/<c>delete</c>/<c>approve</c>).
/// Read-only features carry only <c>:read</c>. The strings are the stable wire/claim values and
/// must not change without a migration.
/// </remarks>
public static class Capabilities
{
    // --- Data & content -------------------------------------------------------------------
    /// <summary>View schema definitions (the admin schema listing/detail/history).</summary>
    public const string SchemasRead = "schemas:read";
    /// <summary>Create, edit, clone and delete schemas (and prune version history).</summary>
    public const string SchemasManage = "schemas:manage";
    /// <summary>Read submissions across services (the cross-service submissions list/detail/history).</summary>
    public const string SubmissionsRead = "submissions:read";
    /// <summary>Create/edit submissions on behalf of a service, including bulk import.</summary>
    public const string SubmissionsSubmit = "submissions:submit";
    /// <summary>Hard-delete a submission.</summary>
    public const string SubmissionsDelete = "submissions:delete";
    /// <summary>Approve or reject pending submissions (and see the pending queue).</summary>
    public const string SubmissionsApprove = "submissions:approve";
    /// <summary>Run ad-hoc queries: the OData feed (<c>/odata/samples</c>) and <c>POST /api/admin/query</c>.</summary>
    public const string QueryRead = "query:read";
    /// <summary>Use the in-app Explore analytics.</summary>
    public const string ExploreRead = "explore:read";
    /// <summary>View submission status / missing-submission analytics.</summary>
    public const string StatusRead = "status:read";
    /// <summary>View the report catalogue and render reports.</summary>
    public const string ReportsRead = "reports:read";
    /// <summary>Create and delete report definitions.</summary>
    public const string ReportsManage = "reports:manage";

    // --- Administration -------------------------------------------------------------------
    /// <summary>View accounts.</summary>
    public const string AccountsRead = "accounts:read";
    /// <summary>Create, edit and delete accounts (including their capability overrides).</summary>
    public const string AccountsManage = "accounts:manage";
    /// <summary>View API keys.</summary>
    public const string ApiKeysRead = "apikeys:read";
    /// <summary>Issue and revoke API keys.</summary>
    public const string ApiKeysManage = "apikeys:manage";
    /// <summary>Read the audit log (and export it).</summary>
    public const string AuditRead = "audit:read";
    /// <summary>View webhook endpoints and their deliveries.</summary>
    public const string WebhooksRead = "webhooks:read";
    /// <summary>Create/edit/delete webhook endpoints, rotate secrets, test, redeliver and drain.</summary>
    public const string WebhooksManage = "webhooks:manage";
    /// <summary>View notification + email configuration, templates and the email outbox.</summary>
    public const string NotificationsRead = "notifications:read";
    /// <summary>Edit notification/email configuration and templates; send test mail; run/drain queues.</summary>
    public const string NotificationsManage = "notifications:manage";
    /// <summary>View integrations (e.g. Microsoft Teams) and their connection settings.</summary>
    public const string IntegrationsRead = "integrations:read";
    /// <summary>Create/edit/delete integrations, edit the connection, run/test and drain deliveries.</summary>
    public const string IntegrationsManage = "integrations:manage";
    /// <summary>Export a subject's personal data (DSAR view/export).</summary>
    public const string PrivacyRead = "privacy:read";
    /// <summary>Erase personal data and run retention.</summary>
    public const string PrivacyManage = "privacy:manage";
    /// <summary>Export a backup.</summary>
    public const string BackupRead = "backup:read";
    /// <summary>Restore a backup.</summary>
    public const string BackupManage = "backup:manage";
    /// <summary>Read global settings (e.g. the default approval policy).</summary>
    public const string SettingsRead = "settings:read";
    /// <summary>Change global settings (e.g. the default approval policy).</summary>
    public const string SettingsManage = "settings:manage";
    /// <summary>View the events timeline.</summary>
    public const string EventsRead = "events:read";
    /// <summary>Create, edit and delete events.</summary>
    public const string EventsManage = "events:manage";

    /// <summary>Every capability in the catalogue, in display order (grouped data/content then administration).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        SchemasRead, SchemasManage,
        SubmissionsRead, SubmissionsSubmit, SubmissionsDelete, SubmissionsApprove,
        QueryRead, ExploreRead, StatusRead,
        ReportsRead, ReportsManage,
        AccountsRead, AccountsManage,
        ApiKeysRead, ApiKeysManage,
        AuditRead,
        WebhooksRead, WebhooksManage,
        NotificationsRead, NotificationsManage,
        IntegrationsRead, IntegrationsManage,
        PrivacyRead, PrivacyManage,
        BackupRead, BackupManage,
        SettingsRead, SettingsManage,
        EventsRead, EventsManage,
    };

    private static readonly HashSet<string> KnownSet = new(All, StringComparer.Ordinal);

    /// <summary>True when <paramref name="capability"/> is a member of the catalogue.</summary>
    public static bool IsKnown(string capability) => KnownSet.Contains(capability);
}

/// <summary>
/// Maps roles to their default capability bundles and computes the effective set for an account.
/// <see cref="AccountRole.Admin"/> is the one non-decorative role: it implicitly holds every
/// capability and cannot be reduced (the lockout-safe floor). The other roles are just templates.
/// </summary>
public static class RoleCapabilities
{
    // Operator: read-everything back-office reader, as in the pre-capability world.
    private static readonly string[] OperatorDefaults =
    {
        Capabilities.SchemasRead, Capabilities.SubmissionsRead, Capabilities.QueryRead,
        Capabilities.ExploreRead, Capabilities.StatusRead, Capabilities.ReportsRead,
    };

    // Approver: read submissions + approve/reject, nothing else.
    private static readonly string[] ApproverDefaults =
    {
        Capabilities.SubmissionsRead, Capabilities.SubmissionsApprove,
    };

    private static readonly HashSet<string> AllSet = new(Capabilities.All, StringComparer.Ordinal);

    /// <summary>
    /// The default capability bundle a role seeds at account creation. <see cref="AccountRole.Admin"/>
    /// returns the entire catalogue; <see cref="AccountRole.Service"/> returns none.
    /// </summary>
    public static IReadOnlyCollection<string> DefaultsFor(AccountRole role) => role switch
    {
        AccountRole.Admin => Capabilities.All,
        AccountRole.Operator => OperatorDefaults,
        AccountRole.Approver => ApproverDefaults,
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// Resolve the effective capability set for an account. <see cref="AccountRole.Admin"/> always
    /// gets the full catalogue (non-reducible). Otherwise the account's stored
    /// <paramref name="overrides"/> are authoritative when present; an empty/null override set
    /// falls back to the role's default bundle (so existing accounts behave exactly as before — no
    /// migration). Unknown capability strings are ignored defensively.
    /// </summary>
    /// <param name="role">The account's role.</param>
    /// <param name="overrides">The account's stored capability overrides, if any.</param>
    public static IReadOnlySet<string> Effective(AccountRole role, IReadOnlyCollection<string>? overrides)
    {
        if (role == AccountRole.Admin)
            return AllSet;

        if (overrides is { Count: > 0 })
            return overrides.Where(Capabilities.IsKnown).ToHashSet(StringComparer.Ordinal);

        return DefaultsFor(role).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Convenience overload that resolves the effective set straight from an <see cref="Account"/>.</summary>
    /// <param name="account">The account whose effective capabilities to compute.</param>
    public static IReadOnlySet<string> Effective(Account account) =>
        Effective(account.Role, account.Capabilities);
}
