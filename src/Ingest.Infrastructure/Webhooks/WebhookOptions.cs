namespace Ingest.Infrastructure.Webhooks;

/// <summary>
/// Binding target for the <c>Webhooks</c> configuration section. The whole outbound-webhook
/// feature is gated by <see cref="Enabled"/>, mirroring the email master switch: when it is
/// <c>false</c> the dispatcher never starts, the admin endpoints return 404, and the publisher is
/// never invoked. Disabled by default — a fresh deployment opts in explicitly.
/// </summary>
public sealed class WebhookOptions
{
    /// <summary>Master switch. Defaults to <c>false</c>; set <c>true</c> to enable outbound webhooks.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>In-process dispatcher (outbox drainer) settings.</summary>
    public WebhookWorkerOptions Worker { get; set; } = new();

    /// <summary>Per-request HTTP timeout (seconds) for a single delivery attempt.</summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Optional SSRF allow-list. When non-empty, a delivery URL's host must end with one of these
    /// suffixes (case-insensitive, e.g. <c>.example.org</c> or <c>office.com</c>) or the delivery is
    /// failed permanently. Empty (the default) allows any host — endpoints are admin-configured.
    /// </summary>
    public string[] AllowedHostSuffixes { get; set; } = Array.Empty<string>();
}

/// <summary>Controls the background service that drains the webhook delivery outbox.</summary>
public sealed class WebhookWorkerOptions
{
    /// <summary>
    /// When <c>true</c> (default) an in-process background service drains the outbox on a timer.
    /// Set <c>false</c> to drive delivery purely from an external scheduler hitting
    /// <c>POST /api/admin/webhooks/drain</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often (seconds) the in-process dispatcher wakes up to look for due deliveries.</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>Max delivery attempts before a delivery is marked permanently <see cref="Core.Entities.WebhookDeliveryStatus.Failed"/>.</summary>
    public int MaxAttempts { get; set; } = 6;

    /// <summary>Max deliveries drained per pass, to bound work per wake-up.</summary>
    public int BatchSize { get; set; } = 25;
}
