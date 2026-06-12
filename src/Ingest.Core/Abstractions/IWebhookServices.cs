using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Fans a domain event out to every enabled webhook endpoint subscribed to it: builds the JSON
/// envelope once and enqueues one <see cref="WebhookDelivery"/> per matching endpoint. Producers
/// (the submission service, the notification job) call this; the dispatcher delivers later.
/// </summary>
public interface IWebhookPublisher
{
    /// <summary>
    /// Enqueue a delivery for each enabled endpoint subscribed to <paramref name="kind"/> whose
    /// service filter matches (or is unset). Deduplicated on <paramref name="eventId"/> per endpoint,
    /// so calling it again for the same event is a no-op.
    /// </summary>
    /// <param name="kind">The event kind that fired.</param>
    /// <param name="eventId">Deterministic id for the event; dedupes enqueue and becomes the idempotency key.</param>
    /// <param name="data">The event-specific payload object (serialised as the envelope's <c>data</c>).</param>
    /// <param name="serviceAccountId">Service the event is about, used by the per-endpoint filter. Null = not service-scoped.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of deliveries enqueued.</returns>
    Task<int> PublishAsync(WebhookEventKind kind, string eventId, object data, Guid? serviceAccountId, CancellationToken ct = default);
}

/// <summary>Outcome of one webhook drain pass.</summary>
/// <param name="Sent">Deliveries that succeeded (endpoint answered 2xx).</param>
/// <param name="Failed">Deliveries that failed (transiently or permanently) this pass.</param>
public sealed record WebhookDrainResult(int Sent, int Failed);

/// <summary>Drains pending webhook deliveries from the outbox and POSTs them to their endpoints.</summary>
public interface IWebhookDispatchService
{
    /// <summary>Drain up to <paramref name="max"/> due deliveries, POSTing each and recording the outcome.</summary>
    Task<WebhookDrainResult> DrainAsync(int max, CancellationToken ct = default);
}

/// <summary>Admin edit for a webhook endpoint. The signing secret is managed separately (create/rotate), not here.</summary>
/// <param name="Name">Friendly name.</param>
/// <param name="Url">Absolute destination URL (validated).</param>
/// <param name="Enabled">Whether the endpoint currently receives deliveries.</param>
/// <param name="Events">Subscribed event kinds.</param>
/// <param name="ServiceAccountId">Optional service filter; null = all services.</param>
/// <param name="Description">Optional human description.</param>
public sealed record WebhookEndpointInput(
    string Name,
    string Url,
    bool Enabled,
    IReadOnlyList<WebhookEventKind> Events,
    Guid? ServiceAccountId,
    string? Description);

/// <summary>
/// CRUD over webhook endpoints plus secret rotation and a "send test" action. The signing secret
/// is returned in plaintext exactly once (on create-with-secret and on rotate), mirroring the API
/// key pattern; afterwards only its presence is ever exposed.
/// </summary>
public interface IWebhookEndpointService
{
    /// <summary>List every endpoint, newest first.</summary>
    Task<IReadOnlyList<WebhookEndpoint>> ListAsync(CancellationToken ct = default);

    /// <summary>Get one endpoint by id.</summary>
    /// <exception cref="NotFoundException">No endpoint with that id.</exception>
    Task<WebhookEndpoint> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Create an endpoint. When <paramref name="generateSecret"/> is true a signing secret is
    /// minted and returned once via the tuple's <c>Secret</c> (null otherwise).
    /// </summary>
    Task<(WebhookEndpoint Endpoint, string? Secret)> CreateAsync(WebhookEndpointInput input, bool generateSecret, CancellationToken ct = default);

    /// <summary>Apply an admin edit. Leaves the signing secret untouched.</summary>
    /// <exception cref="NotFoundException">No endpoint with that id.</exception>
    Task<WebhookEndpoint> UpdateAsync(Guid id, WebhookEndpointInput input, CancellationToken ct = default);

    /// <summary>Permanently remove an endpoint (its past deliveries are retained for audit).</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Mint a fresh signing secret for the endpoint and return it once.</summary>
    /// <exception cref="NotFoundException">No endpoint with that id.</exception>
    Task<(WebhookEndpoint Endpoint, string Secret)> RotateSecretAsync(Guid id, CancellationToken ct = default);

    /// <summary>Enqueue a <c>webhook.test</c> delivery to the endpoint so an admin can verify wiring. Returns the delivery id.</summary>
    /// <exception cref="NotFoundException">No endpoint with that id.</exception>
    Task<Guid> SendTestAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Read/maintenance access to the webhook delivery outbox (admin log, redelivery, GDPR/retention purge).</summary>
public interface IWebhookDeliveryRepository
{
    /// <summary>Page through the delivery log newest-first, optionally filtered by status and a created-at window (from inclusive, to exclusive).</summary>
    Task<PagedResult<WebhookDelivery>> ListAsync(PageRequest request, WebhookDeliveryStatus? status = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

    /// <summary>Requeue a delivery (reset to <see cref="WebhookDeliveryStatus.Pending"/>, clear backoff). Returns false if not found.</summary>
    Task<bool> RequeueAsync(Guid deliveryId, CancellationToken ct = default);

    /// <summary>Permanently remove deliveries tied to a service account. Backs the GDPR erasure path.</summary>
    Task<long> HardDeleteForServiceAsync(Guid serviceId, CancellationToken ct = default);

    /// <summary>Permanently remove processed (Sent/Failed) deliveries created before the cutoff. Backs the retention sweep.</summary>
    Task<long> PurgeProcessedOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default);
}
