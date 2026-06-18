using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using Ingest.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Integrations;

/// <summary>
/// Drains the integration outbox: picks up due pending deliveries, builds the Adaptive Card from
/// the live schema, and sends it to the integration's Teams target via <see cref="ITeamsClient"/>.
/// Transient send errors are retried with exponential backoff up to the configured cap; an
/// unrecoverable condition (missing conversation reference, missing credentials, deleted schema) is
/// failed permanently with a clear reason. Mirrors <c>WebhookDispatchService</c>.
/// </summary>
public sealed class IntegrationDispatchService : IIntegrationDispatchService
{
    private static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackoffCap = TimeSpan.FromHours(1);

    private readonly MongoContext _ctx;
    private readonly IIntegrationsService _integrations;
    private readonly ISchemaRepository _schemas;
    private readonly ITeamsClient _teams;
    private readonly ISecretProtector _protector;
    private readonly TeamsCardBuilder _cards;
    private readonly IAuditContext _audit;
    private readonly IntegrationOptions _options;
    private readonly ILogger<IntegrationDispatchService> _logger;

    /// <summary>Create a new <see cref="IntegrationDispatchService"/>.</summary>
    public IntegrationDispatchService(
        MongoContext ctx,
        IIntegrationsService integrations,
        ISchemaRepository schemas,
        ITeamsClient teams,
        ISecretProtector protector,
        TeamsCardBuilder cards,
        IAuditContext audit,
        IOptions<IntegrationOptions> options,
        ILogger<IntegrationDispatchService> logger)
    {
        _ctx = ctx;
        _integrations = integrations;
        _schemas = schemas;
        _teams = teams;
        _protector = protector;
        _cards = cards;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IntegrationDrainResult> DrainAsync(int max, CancellationToken ct = default)
    {
        var now = _audit.UtcNow;
        var fb = Builders<IntegrationDelivery>.Filter;
        var due = fb.And(
            fb.Eq(d => d.Status, IntegrationDeliveryStatus.Pending),
            fb.Or(fb.Eq(d => d.NextAttemptAt, null), fb.Lte(d => d.NextAttemptAt, now)));

        var pending = await _ctx.IntegrationDeliveries
            .Find(due)
            .SortBy(d => d.CreatedAt)
            .Limit(Math.Clamp(max, 1, 500))
            .ToListAsync(ct);

        if (pending.Count == 0) return new IntegrationDrainResult(0, 0);

        // Resolve credentials once per pass (the connection is a singleton).
        var connection = await _integrations.GetConnectionAsync(ct);
        var password = _protector.Unprotect(connection.AppPasswordCipher);
        TeamsCredentials? creds = connection.IsConfigured && !string.IsNullOrEmpty(password)
            ? new TeamsCredentials(connection.AppId!, password!, connection.TenantId, connection.SingleTenant)
            : null;

        var integrationCache = new Dictionary<Guid, Integration?>();
        int sent = 0, failed = 0;

        foreach (var delivery in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (creds is null)
            {
                await MarkAsync(delivery, IntegrationDeliveryStatus.Failed, "Teams connection is not configured.", ct);
                failed++;
                continue;
            }

            if (!integrationCache.TryGetValue(delivery.IntegrationId, out var integration))
            {
                integration = await _ctx.Integrations.Find(i => i.Id == delivery.IntegrationId && !i.IsDeleted).FirstOrDefaultAsync(ct);
                integrationCache[delivery.IntegrationId] = integration;
            }

            if (integration is null)
            {
                await MarkAsync(delivery, IntegrationDeliveryStatus.Failed, "Integration no longer exists.", ct);
                failed++;
                continue;
            }

            var conversationRef = integration.Teams.ConversationReferenceJson;
            if (string.IsNullOrEmpty(conversationRef))
            {
                await MarkAsync(delivery, IntegrationDeliveryStatus.Failed,
                    "No conversation reference captured yet — add the bot to the target chat/channel first.", ct);
                failed++;
                continue;
            }

            object? card;
            if (delivery.IsTest)
            {
                card = _cards.BuildResultCard("Ingest connection test",
                    new[] { "If you can see this card, the Microsoft Teams integration is wired up correctly." }, false);
            }
            else
            {
                var schema = await _schemas.GetByNameAsync(delivery.SchemaName, false, ct);
                if (schema is null)
                {
                    await MarkAsync(delivery, IntegrationDeliveryStatus.Failed, $"Schema '{delivery.SchemaName}' no longer exists.", ct);
                    failed++;
                    continue;
                }

                var emptyAnswers = new Dictionary<string, object?>();
                var ask = _cards.ActiveValues(schema, emptyAnswers)
                    .Where(v => v.Required && delivery.ValueNames.Contains(v.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (ask.Count == 0)
                {
                    // Everything either filled in elsewhere or now hidden — nothing left to ask.
                    await MarkAsync(delivery, IntegrationDeliveryStatus.Sent, null, ct);
                    sent++;
                    continue;
                }

                card = _cards.BuildPromptCard(integration, schema, delivery.ServiceAccountId,
                    delivery.ServiceName ?? "Service", "Current reporting period", emptyAnswers, ask);
            }

            try
            {
                await _teams.SendAdaptiveCardAsync(creds, conversationRef, card!, ct);
                delivery.DeliveredAt = _audit.UtcNow;
                await MarkAsync(delivery, IntegrationDeliveryStatus.Sent, null, ct);
                sent++;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await RecordFailureAsync(delivery, "Request timed out.", ct);
                failed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                await RecordFailureAsync(delivery, ex.Message, ct);
                failed++;
                _logger.LogWarning(ex, "Integration delivery {Id} failed (attempt {Attempt}/{Max}).",
                    delivery.Id, delivery.Attempts + 1, _options.Worker.MaxAttempts);
            }
        }

        return new IntegrationDrainResult(sent, failed);
    }

    private Task RecordFailureAsync(IntegrationDelivery delivery, string error, CancellationToken ct)
    {
        delivery.Attempts++;
        if (delivery.Attempts >= _options.Worker.MaxAttempts)
            return MarkAsync(delivery, IntegrationDeliveryStatus.Failed, error, ct);

        var delaySeconds = Math.Min(BackoffCap.TotalSeconds, BackoffBase.TotalSeconds * Math.Pow(2, delivery.Attempts - 1));
        delivery.NextAttemptAt = _audit.UtcNow.AddSeconds(delaySeconds);
        return MarkAsync(delivery, IntegrationDeliveryStatus.Pending, error, ct);
    }

    private Task MarkAsync(IntegrationDelivery delivery, IntegrationDeliveryStatus status, string? error, CancellationToken ct)
    {
        delivery.Status = status;
        delivery.LastError = error;
        delivery.ModifiedAt = _audit.UtcNow;
        delivery.ModifiedBy = _audit.UserName;
        return _ctx.IntegrationDeliveries.ReplaceOneAsync(
            Builders<IntegrationDelivery>.Filter.Eq(d => d.Id, delivery.Id), delivery, cancellationToken: ct);
    }
}
