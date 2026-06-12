using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// The outbound events a webhook endpoint can subscribe to. These mirror the notification
/// triggers (<see cref="NotificationKind"/>) plus the real-time "accepted" event that has no
/// email equivalent because it fires synchronously on every successful submission write.
/// </summary>
public enum WebhookEventKind
{
    /// <summary>A submission was accepted by the API (created or replaced). Fires in real time on write.</summary>
    SubmissionAccepted = 0,

    /// <summary>A submission was accepted but carried non-blocking validation warnings.</summary>
    SubmissionWarnings = 1,

    /// <summary>A required value's cadence window is about to close and nothing has been submitted yet.</summary>
    WindowUpcoming = 2,

    /// <summary>A required value's previous cadence window closed unsatisfied (the deadline passed).</summary>
    WindowMissed = 3,
}

/// <summary>
/// An admin-registered HTTP endpoint that receives a JSON <c>POST</c> when one of its subscribed
/// <see cref="Events"/> fires. This is the webhook analogue of an email recipient: it says
/// <em>where</em> to push and <em>which</em> events to push. Defaults are conservative — an
/// endpoint does nothing until it is enabled and subscribed to at least one event.
/// </summary>
public sealed class WebhookEndpoint : AuditedEntity
{
    /// <summary>Friendly name shown in the admin UI (e.g. "Teams — submissions channel").</summary>
    public required string Name { get; set; }

    /// <summary>Absolute https/http URL the delivery is POSTed to (e.g. a Teams / Power Automate connector URL).</summary>
    public required string Url { get; set; }

    /// <summary>Master switch. When false the endpoint is skipped by the publisher.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The event kinds this endpoint wants. Empty means it receives nothing.</summary>
    public List<WebhookEventKind> Events { get; set; } = new();

    /// <summary>
    /// Optional service-account filter. When set, the endpoint only fires for events about that
    /// service; <c>null</c> means "all services".
    /// </summary>
    public Guid? ServiceAccountId { get; set; }

    /// <summary>
    /// Encrypted HMAC signing secret (opaque ciphertext, see <c>ISecretProtector</c>). When set, each
    /// delivery carries an <c>X-Ingest-Signature</c> header the consumer can verify. Null = unsigned.
    /// </summary>
    public string? SecretCipher { get; set; }

    /// <summary>Optional human description of what this endpoint is for.</summary>
    public string? Description { get; set; }
}

/// <summary>Lifecycle state of a queued webhook delivery as it moves through the outbox.</summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Enqueued and waiting for the dispatcher to pick it up (or to retry after a backoff).</summary>
    Pending = 0,

    /// <summary>Claimed by the dispatcher and being POSTed to the endpoint right now.</summary>
    Sending = 1,

    /// <summary>Delivered: the endpoint answered with a 2xx status.</summary>
    Sent = 2,

    /// <summary>Permanently failed (out of retries, or unrecoverable such as an invalid URL).</summary>
    Failed = 3,
}

/// <summary>
/// A single queued webhook delivery. The outbox is deliberately <em>content-agnostic</em>: the
/// publisher renders the JSON body once and stores it here; the dispatcher only knows how to POST
/// it and track delivery state. One delivery row is created per (event, endpoint) pair so each
/// endpoint retries independently.
/// </summary>
public sealed class WebhookDelivery : AuditedEntity
{
    /// <summary>The endpoint this delivery targets.</summary>
    public required Guid EndpointId { get; set; }

    /// <summary>Destination URL, snapshotted at enqueue time so edits to the endpoint don't rewrite history.</summary>
    public required string Url { get; set; }

    /// <summary>Which event produced this delivery.</summary>
    public WebhookEventKind Kind { get; set; }

    /// <summary>
    /// Deterministic event identifier (e.g. <c>accepted:{submissionId}:{writtenAt:o}</c>). Combined
    /// with <see cref="EndpointId"/> it dedupes enqueue (unique index) and is sent to the consumer as
    /// the <c>X-Ingest-Event-Id</c> idempotency key.
    /// </summary>
    public required string EventId { get; set; }

    /// <summary>The fully-rendered JSON payload that gets POSTed verbatim.</summary>
    public required string PayloadJson { get; set; }

    /// <summary>Current delivery state.</summary>
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;

    /// <summary>How many delivery attempts have been made so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Last delivery error, if any. Shown on the admin "Deliveries" panel.</summary>
    public string? LastError { get; set; }

    /// <summary>HTTP status code of the last attempt, if a response was received.</summary>
    public int? LastStatusCode { get; set; }

    /// <summary>UTC time the endpoint accepted the delivery (2xx), if it ever did.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>
    /// Earliest UTC time the next attempt may run. Set after a transient failure to space retries
    /// out with exponential backoff; null means "eligible immediately".
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>Optional service account this delivery relates to, for audit drill-down and the per-endpoint filter.</summary>
    public Guid? RelatedAccountId { get; set; }
}

/// <summary>Maps <see cref="WebhookEventKind"/> values to the stable dotted wire names used in payloads and the API.</summary>
public static class WebhookEventNames
{
    /// <summary>The dotted event name a consumer sees in the payload envelope (e.g. <c>submission.accepted</c>).</summary>
    public static string ToWire(this WebhookEventKind kind) => kind switch
    {
        WebhookEventKind.SubmissionAccepted => "submission.accepted",
        WebhookEventKind.SubmissionWarnings => "submission.warnings",
        WebhookEventKind.WindowUpcoming => "window.upcoming",
        WebhookEventKind.WindowMissed => "window.missed",
        _ => kind.ToString(),
    };
}
