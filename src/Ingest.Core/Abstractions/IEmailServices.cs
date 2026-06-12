using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>Patch for the SMTP settings. The password follows write-only semantics.</summary>
/// <param name="Host">SMTP host.</param>
/// <param name="Port">SMTP port.</param>
/// <param name="UseStartTls">Whether to negotiate STARTTLS.</param>
/// <param name="Username">SMTP username (null/blank = anonymous).</param>
/// <param name="FromAddress">From address.</param>
/// <param name="FromName">From display name.</param>
/// <param name="UpdatePassword">When false the stored password is kept as-is. When true it is replaced with <paramref name="Password"/> (a null/blank value clears it).</param>
/// <param name="Password">New password; only honoured when <paramref name="UpdatePassword"/> is true.</param>
public sealed record EmailSettingsUpdate(
    string Host,
    int Port,
    bool UseStartTls,
    string? Username,
    string FromAddress,
    string? FromName,
    bool UpdatePassword,
    string? Password);

/// <summary>Reads and writes the singleton SMTP settings, seeding from configuration on first use.</summary>
public interface IEmailSettingsService
{
    /// <summary>Get the current SMTP settings, seeding from configuration if none exist yet. Never returns null once the feature is enabled.</summary>
    Task<EmailSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Apply an admin edit to the SMTP settings. Validates the from-address and encrypts the password.</summary>
    Task<EmailSettings> UpdateAsync(EmailSettingsUpdate update, CancellationToken ct = default);
}

/// <summary>Content-agnostic enqueue input handed to <see cref="IEmailQueue"/>.</summary>
/// <param name="ToAddress">Destination address.</param>
/// <param name="ToName">Optional recipient display name.</param>
/// <param name="Subject">Final, already-rendered subject.</param>
/// <param name="TextBody">Final, already-rendered plain-text body.</param>
/// <param name="HtmlBody">Optional already-rendered HTML body.</param>
/// <param name="Category">Audit category, e.g. <c>adhoc</c> or <c>notification:missed</c>.</param>
/// <param name="RelatedAccountId">Optional related account for audit drill-down.</param>
public sealed record EmailRequest(
    string ToAddress,
    string? ToName,
    string Subject,
    string TextBody,
    string? HtmlBody = null,
    string Category = "general",
    Guid? RelatedAccountId = null);

/// <summary>The durable outbox. Producers enqueue fully-rendered messages; the sender drains them.</summary>
public interface IEmailQueue
{
    /// <summary>Enqueue a fully-rendered message for delivery. Returns the new message id.</summary>
    Task<Guid> EnqueueAsync(EmailRequest request, CancellationToken ct = default);

    /// <summary>Page through the outbox newest-first, optionally filtered by status. Powers the audit "Sent emails" tab.</summary>
    Task<PagedResult<EmailMessage>> ListAsync(PageRequest request, EmailStatus? status = null, CancellationToken ct = default);

    /// <summary>
    /// Return every outbox message tied to a subject — by <see cref="EmailMessage.RelatedAccountId"/>
    /// or by recipient address. Used by the DSAR export (the registry backup omits the email
    /// collections).
    /// </summary>
    /// <param name="accountId">Related account id to match.</param>
    /// <param name="email">Recipient address to match (case-insensitive); ignored when null/blank.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching messages, newest first.</returns>
    Task<IReadOnlyList<EmailMessage>> ListForAccountAsync(Guid accountId, string? email, CancellationToken ct = default);

    /// <summary>
    /// Permanently remove every outbox message tied to a subject (by related account id or
    /// recipient address). Backs the GDPR erasure path — message bodies contain unbounded PII.
    /// </summary>
    /// <param name="accountId">Related account id to match.</param>
    /// <param name="email">Recipient address to match (case-insensitive); ignored when null/blank.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of messages removed.</returns>
    Task<long> HardDeleteForAccountAsync(Guid accountId, string? email, CancellationToken ct = default);

    /// <summary>
    /// Permanently remove processed (<see cref="EmailStatus.Sent"/> / <see cref="EmailStatus.Failed"/>)
    /// messages created before the cutoff. Backs the retention sweep — this is the highest-priority
    /// purge because outbox bodies are full-content PII with no other lifecycle bound.
    /// </summary>
    /// <param name="olderThanUtc">Messages created before this instant are purged.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of messages removed.</returns>
    Task<long> PurgeProcessedOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default);
}

/// <summary>Low-level SMTP delivery. Knows nothing about queueing or content production.</summary>
public interface IEmailSender
{
    /// <summary>Deliver one message using the supplied settings. Throws on any delivery failure.</summary>
    Task SendAsync(EmailMessage message, EmailSettings settings, CancellationToken ct = default);
}

/// <summary>A rendered email ready to enqueue.</summary>
/// <param name="Subject">Rendered subject.</param>
/// <param name="TextBody">Rendered text body.</param>
/// <param name="HtmlBody">Rendered HTML body, if the template defined one.</param>
public sealed record RenderedEmail(string Subject, string TextBody, string? HtmlBody);

/// <summary>Renders an <see cref="EmailTemplate"/> against a model into a <see cref="RenderedEmail"/>.</summary>
public interface IEmailContentBuilder
{
    /// <summary>Load the template by key and render its subject/text/html as Liquid against <paramref name="model"/>.</summary>
    /// <exception cref="NotFoundException">No template with that key exists.</exception>
    Task<RenderedEmail> BuildAsync(string templateKey, object model, CancellationToken ct = default);
}

/// <summary>Content edit for a built-in email template (the key is immutable).</summary>
/// <param name="Name">Friendly name.</param>
/// <param name="Description">Description of when the template is used.</param>
/// <param name="Subject">Liquid subject.</param>
/// <param name="HtmlBody">Optional Liquid HTML body.</param>
/// <param name="TextBody">Liquid text body.</param>
public sealed record EmailTemplateUpdate(
    string Name,
    string? Description,
    string Subject,
    string? HtmlBody,
    string TextBody);

/// <summary>CRUD-lite over the editable email templates (list, get, update; built-ins seeded on start).</summary>
public interface IEmailTemplateService
{
    /// <summary>Ensure the built-in templates exist (idempotent). Called on startup.</summary>
    Task SeedDefaultsAsync(CancellationToken ct = default);

    /// <summary>List every template, ordered by key.</summary>
    Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default);

    /// <summary>Get one template by key.</summary>
    /// <exception cref="NotFoundException">No template with that key exists.</exception>
    Task<EmailTemplate> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Apply an admin content edit to a template. Validates the Liquid parses.</summary>
    /// <exception cref="NotFoundException">No template with that key exists.</exception>
    Task<EmailTemplate> UpdateAsync(string key, EmailTemplateUpdate update, CancellationToken ct = default);
}

/// <summary>Outcome of one drain pass.</summary>
/// <param name="Sent">Messages successfully delivered.</param>
/// <param name="Failed">Messages that failed (transiently or permanently) this pass.</param>
public sealed record EmailDrainResult(int Sent, int Failed);

/// <summary>Drains pending messages from the outbox and delivers them via <see cref="IEmailSender"/>.</summary>
public interface IEmailDispatchService
{
    /// <summary>Drain up to <paramref name="max"/> pending messages, delivering each and recording its outcome.</summary>
    Task<EmailDrainResult> DrainAsync(int max, CancellationToken ct = default);
}
