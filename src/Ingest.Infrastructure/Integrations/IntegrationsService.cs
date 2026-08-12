using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using Ingest.Infrastructure.Security;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Integrations;

/// <summary>
/// CRUD over integrations plus read/write of the singleton Teams connection settings. Mirrors the
/// webhook-endpoint and email-settings services: validates input, soft-deletes, and stores the bot
/// secret encrypted at rest with write-only semantics (never returned in plaintext after save).
/// </summary>
public sealed class IntegrationsService : IIntegrationsService
{
    private readonly MongoContext _ctx;
    private readonly ISecretProtector _protector;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="IntegrationsService"/>.</summary>
    public IntegrationsService(MongoContext ctx, ISecretProtector protector, IAuditContext audit)
    {
        _ctx = ctx;
        _protector = protector;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Integration>> ListAsync(CancellationToken ct = default) =>
        await _ctx.Integrations
            .Find(Builders<Integration>.Filter.Eq(i => i.IsDeleted, false))
            .SortByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<Integration> GetAsync(Guid id, CancellationToken ct = default) =>
        await _ctx.Integrations.Find(i => i.Id == id && !i.IsDeleted).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Integration");

    /// <inheritdoc />
    public async Task<Integration> CreateAsync(Integration integration, CancellationToken ct = default)
    {
        Validate(integration);
        var now = _audit.UtcNow;
        integration.CreatedAt = now;
        integration.ModifiedAt = now;
        integration.CreatedBy = _audit.UserName;
        integration.ModifiedBy = _audit.UserName;
        integration.IsDeleted = false;
        // A freshly-created integration has no captured conversation yet.
        integration.Teams.ConversationReferenceJson = null;

        await _ctx.Integrations.InsertOneAsync(integration, cancellationToken: ct);
        return integration;
    }

    /// <inheritdoc />
    public async Task<Integration> UpdateAsync(Guid id, Integration integration, CancellationToken ct = default)
    {
        Validate(integration);
        var existing = await GetAsync(id, ct);

        existing.Label = string.IsNullOrWhiteSpace(integration.Label) ? null : integration.Label.Trim();
        existing.Enabled = integration.Enabled;
        existing.Kind = integration.Kind;
        existing.ServiceIds = integration.ServiceIds?.Distinct().ToList() ?? new();
        existing.SchemaIds = integration.SchemaIds?.Distinct().ToList() ?? new();
        existing.Schedule = integration.Schedule ?? new IntegrationSchedule();

        // Preserve the captured conversation reference across edits — the admin form never carries it.
        var capturedRef = existing.Teams.ConversationReferenceJson;
        existing.Teams = integration.Teams ?? new TeamsTarget();
        if (string.IsNullOrEmpty(existing.Teams.ConversationReferenceJson))
            existing.Teams.ConversationReferenceJson = capturedRef;

        existing.ModifiedAt = _audit.UtcNow;
        existing.ModifiedBy = _audit.UserName;

        await _ctx.Integrations.ReplaceOneAsync(i => i.Id == existing.Id, existing, cancellationToken: ct);
        return existing;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var update = Builders<Integration>.Update
            .Set(i => i.IsDeleted, true)
            .Set(i => i.DeletedAt, _audit.UtcNow)
            .Set(i => i.DeletedBy, _audit.UserName)
            .Set(i => i.ModifiedAt, _audit.UtcNow)
            .Set(i => i.ModifiedBy, _audit.UserName);
        await _ctx.Integrations.UpdateOneAsync(i => i.Id == id, update, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<TeamsConnectionSettings> GetConnectionAsync(CancellationToken ct = default)
    {
        var existing = await _ctx.TeamsConnectionSettings
            .Find(FilterDefinition<TeamsConnectionSettings>.Empty)
            .FirstOrDefaultAsync(ct);
        return existing ?? new TeamsConnectionSettings();
    }

    /// <inheritdoc />
    public async Task<TeamsConnectionSettings> UpdateConnectionAsync(TeamsConnectionUpdate update, CancellationToken ct = default)
    {
        var existing = await _ctx.TeamsConnectionSettings
            .Find(FilterDefinition<TeamsConnectionSettings>.Empty)
            .FirstOrDefaultAsync(ct);

        var now = _audit.UtcNow;
        var settings = existing ?? new TeamsConnectionSettings { CreatedAt = now, CreatedBy = _audit.UserName };

        settings.AppId = string.IsNullOrWhiteSpace(update.AppId) ? null : update.AppId.Trim();
        settings.TenantId = string.IsNullOrWhiteSpace(update.TenantId) ? null : update.TenantId.Trim();
        settings.SingleTenant = update.SingleTenant;

        // Write-only secret: leave the stored cipher untouched unless the caller opts to change it.
        if (update.UpdatePassword)
            settings.AppPasswordCipher = _protector.Protect(update.Password);

        settings.ModifiedAt = now;
        settings.ModifiedBy = _audit.UserName;

        await _ctx.TeamsConnectionSettings.ReplaceOneAsync(
            Builders<TeamsConnectionSettings>.Filter.Eq(s => s.Id, settings.Id),
            settings,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return settings;
    }

    private static void Validate(Integration integration)
    {
        var errors = new List<Diagnostic>();
        if (integration.Schedule is { } s)
        {
            if (s.HourUtc is < 0 or > 23)
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Integrations.ScheduleHourInvalid,
                    "Schedule hour must be between 0 and 23.",
                    ("hourUtc", s.HourUtc),
                    ("minimum", 0),
                    ("maximum", 23)));
            if (s.MinuteUtc is < 0 or > 59)
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Integrations.ScheduleMinuteInvalid,
                    "Schedule minute must be between 0 and 59.",
                    ("minuteUtc", s.MinuteUtc),
                    ("minimum", 0),
                    ("maximum", 59)));

            var usesDayOfMonth = s.Frequency is IntegrationFrequency.Monthly or IntegrationFrequency.Quarterly
                or IntegrationFrequency.SemiAnnually or IntegrationFrequency.Yearly;
            if (usesDayOfMonth && !s.LastDayOfMonth && s.DayOfMonth is < 1 or > 31)
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Integrations.ScheduleDayInvalid,
                    "Schedule day of month must be between 1 and 31.",
                    ("dayOfMonth", s.DayOfMonth),
                    ("minimum", 1),
                    ("maximum", 31),
                    ("frequency", s.Frequency.ToString())));

            var usesAnchorMonth = s.Frequency is IntegrationFrequency.Quarterly
                or IntegrationFrequency.SemiAnnually or IntegrationFrequency.Yearly;
            if (usesAnchorMonth && s.AnchorMonth is < 1 or > 12)
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Integrations.ScheduleAnchorMonthInvalid,
                    "Schedule anchor month must be between 1 and 12.",
                    ("anchorMonth", s.AnchorMonth),
                    ("minimum", 1),
                    ("maximum", 12),
                    ("frequency", s.Frequency.ToString())));
        }
        if (integration.Kind == IntegrationKind.MicrosoftTeams &&
            string.IsNullOrWhiteSpace(integration.Teams?.TargetId))
            errors.Add(Diagnostic.Create(
                DiagnosticCodes.Integrations.TeamsTargetRequired,
                "A Teams target (user or channel) is required.",
                ("integrationKind", integration.Kind.ToString()),
                ("targetKind", integration.Teams?.Kind.ToString())));
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
