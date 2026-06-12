using System.Security.Cryptography;
using System.Text.Json;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using Ingest.Infrastructure.Security;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Webhooks;

/// <summary>
/// CRUD over webhook endpoints plus signing-secret rotation and the "send test" action. Mirrors
/// the email settings/template services: validates input, encrypts the signing secret at rest,
/// and follows write-once semantics for the secret (returned in plaintext only on create/rotate).
/// </summary>
public sealed class WebhookEndpointService : IWebhookEndpointService
{
    private readonly MongoContext _ctx;
    private readonly ISecretProtector _protector;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="WebhookEndpointService"/>.</summary>
    public WebhookEndpointService(MongoContext ctx, ISecretProtector protector, IAuditContext audit)
    {
        _ctx = ctx;
        _protector = protector;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(CancellationToken ct = default) =>
        await _ctx.WebhookEndpoints
            .Find(Builders<WebhookEndpoint>.Filter.Eq(e => e.IsDeleted, false))
            .SortByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<WebhookEndpoint> GetAsync(Guid id, CancellationToken ct = default) =>
        await _ctx.WebhookEndpoints.Find(e => e.Id == id && !e.IsDeleted).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Webhook endpoint");

    /// <inheritdoc />
    public async Task<(WebhookEndpoint Endpoint, string? Secret)> CreateAsync(WebhookEndpointInput input, bool generateSecret, CancellationToken ct = default)
    {
        Validate(input);
        var now = _audit.UtcNow;
        string? secret = generateSecret ? NewSecret() : null;

        var endpoint = new WebhookEndpoint
        {
            Name = input.Name.Trim(),
            Url = input.Url.Trim(),
            Enabled = input.Enabled,
            Events = Normalize(input.Events),
            ServiceAccountId = input.ServiceAccountId,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            SecretCipher = secret is null ? null : _protector.Protect(secret),
            CreatedAt = now,
            ModifiedAt = now,
            CreatedBy = _audit.UserName,
            ModifiedBy = _audit.UserName,
        };

        await _ctx.WebhookEndpoints.InsertOneAsync(endpoint, cancellationToken: ct);
        return (endpoint, secret);
    }

    /// <inheritdoc />
    public async Task<WebhookEndpoint> UpdateAsync(Guid id, WebhookEndpointInput input, CancellationToken ct = default)
    {
        Validate(input);
        var endpoint = await GetAsync(id, ct);

        endpoint.Name = input.Name.Trim();
        endpoint.Url = input.Url.Trim();
        endpoint.Enabled = input.Enabled;
        endpoint.Events = Normalize(input.Events);
        endpoint.ServiceAccountId = input.ServiceAccountId;
        endpoint.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        endpoint.ModifiedAt = _audit.UtcNow;
        endpoint.ModifiedBy = _audit.UserName;

        await _ctx.WebhookEndpoints.ReplaceOneAsync(e => e.Id == endpoint.Id, endpoint, cancellationToken: ct);
        return endpoint;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var update = Builders<WebhookEndpoint>.Update
            .Set(e => e.IsDeleted, true)
            .Set(e => e.DeletedAt, _audit.UtcNow)
            .Set(e => e.DeletedBy, _audit.UserName)
            .Set(e => e.ModifiedAt, _audit.UtcNow)
            .Set(e => e.ModifiedBy, _audit.UserName);
        await _ctx.WebhookEndpoints.UpdateOneAsync(e => e.Id == id, update, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<(WebhookEndpoint Endpoint, string Secret)> RotateSecretAsync(Guid id, CancellationToken ct = default)
    {
        var endpoint = await GetAsync(id, ct);
        var secret = NewSecret();
        endpoint.SecretCipher = _protector.Protect(secret);
        endpoint.ModifiedAt = _audit.UtcNow;
        endpoint.ModifiedBy = _audit.UserName;
        await _ctx.WebhookEndpoints.ReplaceOneAsync(e => e.Id == endpoint.Id, endpoint, cancellationToken: ct);
        return (endpoint, secret);
    }

    /// <inheritdoc />
    public async Task<Guid> SendTestAsync(Guid id, CancellationToken ct = default)
    {
        var endpoint = await GetAsync(id, ct);
        var now = _audit.UtcNow;
        var eventId = "test:" + Guid.NewGuid().ToString("n");
        var envelope = new
        {
            @event = "webhook.test",
            eventId,
            occurredAt = now,
            data = new { endpointId = endpoint.Id, name = endpoint.Name, message = "This is a test delivery from Ingest." },
        };

        var delivery = new WebhookDelivery
        {
            EndpointId = endpoint.Id,
            Url = endpoint.Url,
            Kind = WebhookEventKind.SubmissionAccepted, // diagnostic only; surfaced as webhook.test via EventId prefix
            EventId = eventId,
            PayloadJson = JsonSerializer.Serialize(envelope, WebhookJson.Options),
            Status = WebhookDeliveryStatus.Pending,
            RelatedAccountId = endpoint.ServiceAccountId,
            CreatedAt = now,
            ModifiedAt = now,
            CreatedBy = _audit.UserName,
            ModifiedBy = _audit.UserName,
        };

        await _ctx.WebhookDeliveries.InsertOneAsync(delivery, cancellationToken: ct);
        return delivery.Id;
    }

    private static List<WebhookEventKind> Normalize(IReadOnlyList<WebhookEventKind> events) =>
        events?.Distinct().ToList() ?? new List<WebhookEventKind>();

    private static string NewSecret() =>
        "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static void Validate(WebhookEndpointInput input)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors.Add("Name is required.");
        if (string.IsNullOrWhiteSpace(input.Url))
            errors.Add("URL is required.");
        else if (!Uri.TryCreate(input.Url.Trim(), UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors.Add($"'{input.Url}' is not a valid absolute http(s) URL.");
        if (errors.Count > 0) throw new ValidationException(errors);
    }
}
