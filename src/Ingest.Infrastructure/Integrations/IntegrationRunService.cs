using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Integrations;
using Ingest.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Integrations;

/// <summary>
/// Finds outstanding required values for an integration's matched (service, schema) pairs and
/// enqueues Teams prompt cards into the integration outbox. Reuses <see cref="IStatusService"/>
/// exactly like the notification job, and dedupes per cadence period via the unique outbox index so
/// a repeated or late scheduler tick never double-prompts.
/// </summary>
public sealed class IntegrationRunService : IIntegrationRunService
{
    private readonly MongoContext _ctx;
    private readonly IIntegrationsService _integrations;
    private readonly IAccountRepository _accounts;
    private readonly ISchemaRepository _schemas;
    private readonly IStatusService _status;
    private readonly IAuditContext _audit;
    private readonly IntegrationOptions _options;
    private readonly ILogger<IntegrationRunService> _logger;

    /// <summary>Create a new <see cref="IntegrationRunService"/>.</summary>
    public IntegrationRunService(
        MongoContext ctx,
        IIntegrationsService integrations,
        IAccountRepository accounts,
        ISchemaRepository schemas,
        IStatusService status,
        IAuditContext audit,
        IOptions<IntegrationOptions> options,
        ILogger<IntegrationRunService> logger)
    {
        _ctx = ctx;
        _integrations = integrations;
        _accounts = accounts;
        _schemas = schemas;
        _status = status;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IntegrationRunResult> RunAllAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return new IntegrationRunResult(0, 0);

        var connection = await _integrations.GetConnectionAsync(ct);
        if (!connection.IsConfigured)
        {
            _logger.LogDebug("Integration run skipped: Teams connection is not configured.");
            return new IntegrationRunResult(0, 0);
        }

        var now = _audit.UtcNow;
        var due = (await _integrations.ListAsync(ct))
            .Where(i => i.Enabled && IntegrationScheduleEvaluator.IsDue(i.Schedule, now))
            .ToList();
        if (due.Count == 0) return new IntegrationRunResult(0, 0);

        int prompted = 0, skipped = 0;
        foreach (var integration in due)
        {
            ct.ThrowIfCancellationRequested();
            var (p, s) = await RunIntegrationAsync(integration, isTest: false, ct);
            prompted += p;
            skipped += s;
        }
        return new IntegrationRunResult(prompted, skipped);
    }

    /// <inheritdoc />
    public async Task<IntegrationRunResult> RunOneAsync(Guid id, CancellationToken ct = default)
    {
        var integration = await _integrations.GetAsync(id, ct);
        var (prompted, skipped) = await RunIntegrationAsync(integration, isTest: false, ct);
        return new IntegrationRunResult(prompted, skipped);
    }

    /// <inheritdoc />
    public async Task SendTestAsync(Guid id, CancellationToken ct = default)
    {
        var integration = await _integrations.GetAsync(id, ct);
        var now = _audit.UtcNow;
        var delivery = new IntegrationDelivery
        {
            IntegrationId = integration.Id,
            ServiceAccountId = Guid.Empty,
            ServiceName = integration.Teams.DisplayName ?? integration.Teams.TargetId,
            SchemaName = "(test)",
            EventId = "test:" + Guid.NewGuid().ToString("n"),
            IsTest = true,
            Status = IntegrationDeliveryStatus.Pending,
            CreatedAt = now,
            ModifiedAt = now,
            CreatedBy = _audit.UserName,
            ModifiedBy = _audit.UserName,
        };
        await _ctx.IntegrationDeliveries.InsertOneAsync(delivery, cancellationToken: ct);
    }

    private async Task<(int Prompted, int Skipped)> RunIntegrationAsync(Integration integration, bool isTest, CancellationToken ct)
    {
        var serviceIds = await ResolveServiceIdsAsync(integration, ct);
        var schemaIdByName = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        var now = _audit.UtcNow;

        int prompted = 0, skipped = 0;
        foreach (var serviceId in serviceIds)
        {
            ct.ThrowIfCancellationRequested();
            ServiceStatus status;
            try { status = await _status.GetStatusAsync(serviceId, "current", ct); }
            catch (NotFoundException) { continue; }

            foreach (var schema in status.Schemas)
            {
                if (!schema.Enabled) continue;

                if (!schemaIdByName.TryGetValue(schema.SchemaName, out var schemaId))
                {
                    var entity = await _schemas.GetByNameAsync(schema.SchemaName, false, ct);
                    schemaId = entity?.Id;
                    schemaIdByName[schema.SchemaName] = schemaId;
                }

                if (!IntegrationMatcher.Matches(integration, serviceId, schemaId)) continue;

                var outstanding = schema.Values
                    .Where(v => v.Required && v.Enabled && !v.Satisfied)
                    .ToList();
                if (outstanding.Count == 0) { skipped++; continue; }

                var periodStart = outstanding.Min(v => v.PeriodStart);
                var eventId = $"teams:{integration.Id}:{serviceId}:{schema.SchemaName}:{periodStart:o}";

                var delivery = new IntegrationDelivery
                {
                    IntegrationId = integration.Id,
                    ServiceAccountId = serviceId,
                    ServiceName = status.ServiceName,
                    SchemaName = schema.SchemaName,
                    ValueNames = outstanding.Select(v => v.ValueName).ToList(),
                    EventId = eventId,
                    IsTest = isTest,
                    Status = IntegrationDeliveryStatus.Pending,
                    CreatedAt = now,
                    ModifiedAt = now,
                    CreatedBy = _audit.UserName,
                    ModifiedBy = _audit.UserName,
                };

                try
                {
                    await _ctx.IntegrationDeliveries.InsertOneAsync(delivery, cancellationToken: ct);
                    prompted++;
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    skipped++; // already prompted for this (integration, service, schema, period)
                }
            }
        }
        return (prompted, skipped);
    }

    private async Task<IReadOnlyList<Guid>> ResolveServiceIdsAsync(Integration integration, CancellationToken ct)
    {
        if (integration.ServiceIds.Count > 0) return integration.ServiceIds;

        var page = await _accounts.ListAsync(new PageRequest(1, 500), null, AccountRole.Service, ct);
        return page.Items.Select(a => a.Id).ToList();
    }
}
