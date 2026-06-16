using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Email;

/// <summary>Database-backed singleton notification configuration.</summary>
public sealed class NotificationSettingsService : INotificationSettingsService
{
    private readonly MongoContext _ctx;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="NotificationSettingsService"/>.</summary>
    public NotificationSettingsService(MongoContext ctx, IAuditContext audit)
    {
        _ctx = ctx;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<NotificationSettings> GetAsync(CancellationToken ct = default)
    {
        var existing = await _ctx.NotificationSettings
            .Find(FilterDefinition<NotificationSettings>.Empty).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        // Persist a defaults document so the admin UI always has something concrete to edit.
        var now = _audit.UtcNow;
        var settings = new NotificationSettings { CreatedAt = now, ModifiedAt = now };
        try { await _ctx.NotificationSettings.InsertOneAsync(settings, cancellationToken: ct); }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return await _ctx.NotificationSettings
                .Find(FilterDefinition<NotificationSettings>.Empty).FirstAsync(ct);
        }
        return settings;
    }

    /// <inheritdoc />
    public async Task<NotificationSettings> UpdateAsync(NotificationSettingsUpdate update, CancellationToken ct = default)
    {
        var settings = await GetAsync(ct);

        settings.Upcoming = Map(update.Upcoming);
        settings.Missed = Map(update.Missed);
        settings.Warnings = Map(update.Warnings);
        settings.PendingApproval = Map(update.PendingApproval);
        settings.Approved = Map(update.Approved);
        settings.Rejected = Map(update.Rejected);
        settings.UpcomingLeadHours = Math.Clamp(update.UpcomingLeadHours, 1, 24 * 30);
        settings.AdminRecipientAccountIds = update.AdminRecipientAccountIds?.Distinct().ToList() ?? new();
        settings.ModifiedAt = _audit.UtcNow;
        settings.ModifiedBy = _audit.UserName;

        await _ctx.NotificationSettings.ReplaceOneAsync(
            Builders<NotificationSettings>.Filter.Eq(s => s.Id, settings.Id), settings, cancellationToken: ct);
        return settings;
    }

    private static NotificationRule Map(NotificationRuleUpdate u) => new()
    {
        Enabled = u.Enabled,
        NotifyServiceAccount = u.NotifyServiceAccount,
        NotifyAdminList = u.NotifyAdminList,
    };
}
