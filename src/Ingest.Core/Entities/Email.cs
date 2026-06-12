using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>Lifecycle state of a queued email as it moves through the outbox.</summary>
public enum EmailStatus
{
    /// <summary>Enqueued and waiting for the sender to pick it up.</summary>
    Pending = 0,

    /// <summary>Claimed by the sender and being delivered to the SMTP server right now.</summary>
    Sending = 1,

    /// <summary>Accepted by the SMTP server.</summary>
    Sent = 2,

    /// <summary>Permanently failed (out of retries, or unrecoverable such as "SMTP not configured").</summary>
    Failed = 3,
}

/// <summary>
/// A single queued email. The outbox is deliberately <em>content-agnostic</em>: whoever enqueues
/// the message (notifications, the ad-hoc send action, anything future) is responsible for
/// rendering the final subject/body. The sender service only knows how to talk SMTP and how to
/// track delivery state, never how the content was produced.
/// </summary>
public sealed class EmailMessage : AuditedEntity
{
    /// <summary>Destination email address.</summary>
    public required string ToAddress { get; set; }

    /// <summary>Optional friendly recipient name used in the To header.</summary>
    public string? ToName { get; set; }

    /// <summary>Rendered subject line.</summary>
    public required string Subject { get; set; }

    /// <summary>Optional rendered HTML body. When present it's sent as a multipart alternative alongside <see cref="TextBody"/>.</summary>
    public string? HtmlBody { get; set; }

    /// <summary>Rendered plain-text body. Always required so every message has a text fallback.</summary>
    public required string TextBody { get; set; }

    /// <summary>Current delivery state.</summary>
    public EmailStatus Status { get; set; } = EmailStatus.Pending;

    /// <summary>How many delivery attempts have been made so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Last delivery error, if any. Shown on the audit "Sent emails" tab.</summary>
    public string? LastError { get; set; }

    /// <summary>UTC time the message was accepted by the SMTP server, if it was.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Free-form category for filtering/audit, e.g. <c>adhoc</c> or <c>notification:upcoming</c>.</summary>
    public string Category { get; set; } = "general";

    /// <summary>Optional account this message relates to (the recipient service/admin), for audit drill-down.</summary>
    public Guid? RelatedAccountId { get; set; }
}

/// <summary>
/// SMTP connection settings, stored in the database so admins can change them at runtime without
/// a redeploy. Exactly one document lives in the collection. The password is encrypted at rest
/// (see <c>IEmailSecretProtector</c>) and never leaves the server in a DTO — the API only ever
/// reports whether a password is set.
/// </summary>
public sealed class EmailSettings : AuditedEntity
{
    /// <summary>SMTP server host name.</summary>
    public string Host { get; set; } = "";

    /// <summary>SMTP server port. 587 (STARTTLS) is the usual default.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether to negotiate TLS (STARTTLS) on the connection.</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>SMTP username. Null/blank means anonymous relay.</summary>
    public string? Username { get; set; }

    /// <summary>Encrypted SMTP password (opaque ciphertext). Null when no password is set.</summary>
    public string? PasswordCipher { get; set; }

    /// <summary>From address stamped on every outgoing message.</summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Optional friendly From display name.</summary>
    public string? FromName { get; set; }

    /// <summary>True once enough is configured to attempt a send (host + from address present).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>
/// A reusable email body template. The notification job (and any future content producer) looks
/// templates up by <see cref="Key"/> and renders <see cref="Subject"/>, <see cref="HtmlBody"/> and
/// <see cref="TextBody"/> as Liquid against a per-message model. A small set of built-in templates
/// is seeded on first start; admins edit them from the Settings page.
/// </summary>
public sealed class EmailTemplate : AuditedEntity
{
    /// <summary>Stable lookup key, e.g. <c>notification.upcoming</c>. Unique across templates.</summary>
    public required string Key { get; set; }

    /// <summary>Friendly name shown in the editor list.</summary>
    public string Name { get; set; } = "";

    /// <summary>Human description of when this template is used.</summary>
    public string? Description { get; set; }

    /// <summary>Liquid template for the subject line.</summary>
    public string Subject { get; set; } = "";

    /// <summary>Optional Liquid template for the HTML body. Blank → text-only email.</summary>
    public string? HtmlBody { get; set; }

    /// <summary>Liquid template for the plain-text body. Always required as the text fallback.</summary>
    public string TextBody { get; set; } = "";
}
