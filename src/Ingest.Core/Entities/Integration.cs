using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// The external system an <see cref="Integration"/> talks to. Modelled as an enum so the feature
/// can grow more providers later; only <see cref="MicrosoftTeams"/> ships today.
/// </summary>
public enum IntegrationKind
{
    /// <summary>Microsoft Teams: prompt a user or channel with an interactive Adaptive Card.</summary>
    MicrosoftTeams = 0,
}

/// <summary>Whether a Teams integration targets a single user (1:1 chat) or a channel.</summary>
public enum TeamsTargetKind
{
    /// <summary>A single Teams user, reached in a 1:1 chat with the bot.</summary>
    User = 0,

    /// <summary>A channel inside a team.</summary>
    Channel = 1,
}

/// <summary>
/// How often an integration's scheduled pass runs. The pass only decides <em>when to look</em> for
/// outstanding values; the outbox dedupes per cadence period, so the longer frequencies can be a
/// forgiving "on or after day N" window without double-prompting. Mirrors the schema cadences
/// (minus Fortnightly).
/// </summary>
public enum IntegrationFrequency
{
    /// <summary>Every day, at the configured time.</summary>
    Daily = 0,

    /// <summary>On the selected weekdays (empty = every day), at the configured time.</summary>
    Weekly = 1,

    /// <summary>Once a month, on (or after) the configured day-of-month.</summary>
    Monthly = 2,

    /// <summary>Every three months from the anchor month, on (or after) the configured day-of-month.</summary>
    Quarterly = 3,

    /// <summary>Every six months from the anchor month, on (or after) the configured day-of-month.</summary>
    SemiAnnually = 4,

    /// <summary>Once a year, in the anchor month, on (or after) the configured day-of-month.</summary>
    Yearly = 5,
}

/// <summary>
/// When an integration's scheduled pass runs. The pass becomes eligible at or after
/// <see cref="HourUtc"/>:<see cref="MinuteUtc"/> (UTC) on the days implied by <see cref="Frequency"/>.
/// Per-period dedupe in the outbox means a late or repeated scheduler tick never double-prompts, so
/// the monthly-and-longer frequencies use a forgiving "on or after day N" rule rather than an exact day.
/// </summary>
public sealed class IntegrationSchedule
{
    /// <summary>How often the pass runs. Defaults to <see cref="IntegrationFrequency.Daily"/>.</summary>
    public IntegrationFrequency Frequency { get; set; } = IntegrationFrequency.Daily;

    /// <summary>Weekdays the pass runs on (only used when <see cref="Frequency"/> is Weekly). Empty means "every day".</summary>
    public List<DayOfWeek> Days { get; set; } = new();

    /// <summary>
    /// Day of the month (1-31) the pass runs on for the Monthly-and-longer frequencies. Clamped to the
    /// actual month length, so 31 fires on the last day of a short month. Ignored when <see cref="LastDayOfMonth"/> is set.
    /// </summary>
    public int DayOfMonth { get; set; } = 1;

    /// <summary>When set, the pass runs on the last day of the month instead of <see cref="DayOfMonth"/>.</summary>
    public bool LastDayOfMonth { get; set; }

    /// <summary>
    /// Anchor month (1-12) for the Quarterly/Semi-annually/Yearly frequencies: the period repeats
    /// every N months from this month (e.g. quarterly with anchor February = Feb/May/Aug/Nov).
    /// </summary>
    public int AnchorMonth { get; set; } = 1;

    /// <summary>Hour of day (UTC, 0-23) at or after which the pass becomes eligible.</summary>
    public int HourUtc { get; set; } = 8;

    /// <summary>Minute of the hour (UTC, 0-59).</summary>
    public int MinuteUtc { get; set; }
}

/// <summary>
/// Where a Teams integration sends its prompt, plus the conversation reference captured the first
/// time the bot is contacted (needed for proactive sends). The reference is stored as opaque JSON
/// produced by the Bot Framework SDK.
/// </summary>
public sealed class TeamsTarget
{
    /// <summary>Whether the prompt goes to a user or a channel.</summary>
    public TeamsTargetKind Kind { get; set; } = TeamsTargetKind.User;

    /// <summary>
    /// Stable identifier of the target: for a <see cref="TeamsTargetKind.User"/> this is the user's
    /// Microsoft Entra object id, UPN, or email; for a <see cref="TeamsTargetKind.Channel"/> the
    /// channel id. Used to match an inbound conversation and to label the row in the UI.
    /// </summary>
    public string TargetId { get; set; } = "";

    /// <summary>Optional friendly label for the target (display only).</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Bot Framework conversation reference (serialised JSON) captured when the bot is first added
    /// to the chat/channel. <c>null</c> until first contact; proactive sends are skipped until it's set.
    /// </summary>
    public string? ConversationReferenceJson { get; set; }
}

/// <summary>
/// A cross-cutting integration: when its scheduled pass (or an on-demand run) finds outstanding
/// required values for a matching <c>(service, schema)</c> pair, it prompts the configured Teams
/// target to fill them in. Either scoping axis may be empty to mean "all": an empty
/// <see cref="ServiceIds"/> matches every service, an empty <see cref="SchemaIds"/> matches every
/// schema. Mirrors the <see cref="ApprovalRule"/> shape so the admin UI and matcher are familiar.
/// </summary>
public sealed class Integration : AuditedEntity
{
    /// <summary>Optional friendly label shown in the admin console.</summary>
    public string? Label { get; set; }

    /// <summary>Whether the integration is active. Disabled integrations are skipped by the scheduler.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The provider this integration targets. Only <see cref="IntegrationKind.MicrosoftTeams"/> today.</summary>
    public IntegrationKind Kind { get; set; } = IntegrationKind.MicrosoftTeams;

    /// <summary>Services this integration applies to. Empty means "all services".</summary>
    public List<Guid> ServiceIds { get; set; } = new();

    /// <summary>Schemas this integration applies to. Empty means "all schemas".</summary>
    public List<Guid> SchemaIds { get; set; } = new();

    /// <summary>When the scheduled pass runs.</summary>
    public IntegrationSchedule Schedule { get; set; } = new();

    /// <summary>Teams-specific target. Only meaningful when <see cref="Kind"/> is <see cref="IntegrationKind.MicrosoftTeams"/>.</summary>
    public TeamsTarget Teams { get; set; } = new();
}

/// <summary>
/// Server-wide singleton holding the Microsoft Teams bot credentials, edited from the admin console
/// (Settings &gt; Integrations &gt; Connection) rather than baked into configuration. At most one
/// document exists; an absent document means "Teams is not configured yet" and the feature stays
/// inert. The bot password is encrypted at rest via <c>ISecretProtector</c> and never returned in
/// plaintext after save (write-only, mirroring the SMTP password).
/// </summary>
public sealed class TeamsConnectionSettings : AuditedEntity
{
    /// <summary>Microsoft Entra application (client) id of the bot, a.k.a. the Microsoft App ID.</summary>
    public string? AppId { get; set; }

    /// <summary>Encrypted bot client secret (opaque ciphertext, see <c>ISecretProtector</c>). Null = not set.</summary>
    public string? AppPasswordCipher { get; set; }

    /// <summary>Microsoft Entra tenant id the bot is registered in. Empty/null for a multi-tenant bot.</summary>
    public string? TenantId { get; set; }

    /// <summary>True when the bot app type is single-tenant (affects the token authority).</summary>
    public bool SingleTenant { get; set; }

    /// <summary>True when both an app id and a stored password are present (i.e. the bot can authenticate).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppPasswordCipher);
}

/// <summary>Lifecycle state of a queued integration delivery as it moves through the outbox.</summary>
public enum IntegrationDeliveryStatus
{
    /// <summary>Enqueued and waiting for the dispatcher (or to retry after a backoff).</summary>
    Pending = 0,

    /// <summary>Claimed by the dispatcher and being sent right now.</summary>
    Sending = 1,

    /// <summary>Delivered: Teams accepted the proactive message.</summary>
    Sent = 2,

    /// <summary>Permanently failed (out of retries, or unrecoverable such as a missing conversation reference).</summary>
    Failed = 3,
}

/// <summary>
/// A single queued Teams prompt. The dispatcher builds the Adaptive Card from the live schema at
/// send time (so conditional fields reflect the latest definition); this row carries just enough
/// to address the message: which integration, which service/schema, and the outstanding required
/// values discovered when the pass ran. Mirrors <c>WebhookDelivery</c>'s durable-outbox shape.
/// </summary>
public sealed class IntegrationDelivery : AuditedEntity
{
    /// <summary>The integration this delivery belongs to (resolves the target + connection at send time).</summary>
    public required Guid IntegrationId { get; set; }

    /// <summary>The service the prompt is about.</summary>
    public required Guid ServiceAccountId { get; set; }

    /// <summary>Machine-name snapshot of the service, for display.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Schema whose outstanding required values are being prompted for.</summary>
    public required string SchemaName { get; set; }

    /// <summary>Outstanding required value names discovered when the pass ran (the seed of the prompt).</summary>
    public List<string> ValueNames { get; set; } = new();

    /// <summary>
    /// Deterministic id used to dedupe enqueue (unique with <see cref="IntegrationId"/>). Typically
    /// <c>teams:{integrationId}:{serviceId}:{schemaName}:{periodStart:o}</c>; test prompts use a
    /// random <c>test:</c> id so they never collide with a real pass.
    /// </summary>
    public required string EventId { get; set; }

    /// <summary>True for a diagnostic "send test" prompt (rendered with sample content, not a real submission flow).</summary>
    public bool IsTest { get; set; }

    /// <summary>Current delivery state.</summary>
    public IntegrationDeliveryStatus Status { get; set; } = IntegrationDeliveryStatus.Pending;

    /// <summary>How many delivery attempts have been made so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Last delivery error, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>UTC time Teams accepted the delivery, if it ever did.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>Earliest UTC time the next attempt may run (exponential backoff); null = eligible immediately.</summary>
    public DateTime? NextAttemptAt { get; set; }
}
