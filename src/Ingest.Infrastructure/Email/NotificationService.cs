using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Email;

/// <summary>
/// Evaluates the enabled notification triggers and turns them into queued emails. Everything is
/// deduplicated through the <c>notificationLogs</c> collection (unique key) so a given window or
/// submission is notified exactly once, no matter how often the job runs.
/// </summary>
/// <remarks>
/// The job is intentionally conservative about blast radius: "upcoming" only looks at windows
/// closing inside the configured lead time, "missed" only at the just-closed (previous) window,
/// and "warnings" only at submissions from the last few days. Enabling a trigger therefore never
/// floods recipients with a backlog of historical events.
/// </remarks>
public sealed class NotificationService : INotificationService
{
    private static readonly TimeSpan WarningLookback = TimeSpan.FromDays(7);

    private readonly MongoContext _ctx;
    private readonly IAccountRepository _accounts;
    private readonly IStatusService _status;
    private readonly IEmailContentBuilder _content;
    private readonly IEmailQueue _queue;
    private readonly INotificationSettingsService _settingsService;
    private readonly IAuditContext _audit;
    private readonly ILogger<NotificationService> _logger;

    /// <summary>Create a new <see cref="NotificationService"/>.</summary>
    public NotificationService(
        MongoContext ctx,
        IAccountRepository accounts,
        IStatusService status,
        IEmailContentBuilder content,
        IEmailQueue queue,
        INotificationSettingsService settingsService,
        IAuditContext audit,
        ILogger<NotificationService> logger)
    {
        _ctx = ctx;
        _accounts = accounts;
        _status = status;
        _content = content;
        _queue = queue;
        _settingsService = settingsService;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationRunResult> RunAsync(CancellationToken ct = default)
    {
        var settings = await _settingsService.GetAsync(ct);
        if (!settings.Upcoming.Enabled && !settings.Missed.Enabled && !settings.Warnings.Enabled)
            return new NotificationRunResult(0, 0, 0);

        // Service accounts keyed by id, plus the resolved admin/operator recipient addresses.
        var services = (await _accounts.ListAsync(new PageRequest(1, 500), role: AccountRole.Service, ct: ct)).Items;
        var serviceById = services.ToDictionary(a => a.Id);
        var adminRecipients = await ResolveAdminRecipientsAsync(settings, ct);

        var upcoming = settings.Upcoming.Enabled
            ? await RunUpcomingAsync(settings, services, adminRecipients, ct) : 0;
        var missed = settings.Missed.Enabled
            ? await RunMissedAsync(settings, serviceById, adminRecipients, ct) : 0;
        var warnings = settings.Warnings.Enabled
            ? await RunWarningsAsync(settings, serviceById, adminRecipients, ct) : 0;

        return new NotificationRunResult(upcoming, missed, warnings);
    }

    private async Task<int> RunUpcomingAsync(
        NotificationSettings settings,
        IReadOnlyList<Account> services,
        IReadOnlyList<Recipient> adminRecipients,
        CancellationToken ct)
    {
        var now = _audit.UtcNow;
        var deadline = now.AddHours(settings.UpcomingLeadHours);
        var queued = 0;

        foreach (var service in services)
        {
            if (!service.Enabled) continue;
            ct.ThrowIfCancellationRequested();

            ServiceStatus status;
            try { status = await _status.GetStatusAsync(service.Id, "week", ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Upcoming: status failed for {Service}.", service.Name); continue; }

            var items = new List<object>();
            foreach (var schema in status.Schemas.Where(s => s.Enabled))
            foreach (var value in schema.Values)
            {
                if (!value.Enabled || !value.Required || value.Satisfied) continue;
                if (value.PeriodEnd <= now || value.PeriodEnd > deadline) continue;

                var key = $"upcoming:{service.Id}:{schema.SchemaName}:{value.ValueName}:{value.PeriodStart:O}";
                if (!await ReserveAsync(key, NotificationKind.Upcoming, ct)) continue;

                items.Add(new
                {
                    schema = schema.SchemaName,
                    value = value.Label ?? value.ValueName,
                    cadence = value.Cadence.ToString(),
                    periodEnd = Fmt(value.PeriodEnd),
                });
            }

            if (items.Count == 0) continue;
            var model = new { service = ServiceModel(service), items };
            queued += await EnqueueAsync(DefaultEmailTemplates.Upcoming, model,
                RecipientsFor(service, settings.Upcoming, adminRecipients), "notification:upcoming", service.Id, ct);
        }

        return queued;
    }

    private async Task<int> RunMissedAsync(
        NotificationSettings settings,
        IReadOnlyDictionary<Guid, Account> serviceById,
        IReadOnlyList<Recipient> adminRecipients,
        CancellationToken ct)
    {
        IReadOnlyList<MissingByCadence> missing;
        try { missing = await _status.GetMissingAsync(ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Missed: missing report failed."); return 0; }

        // Accumulate per service across cadence buckets so a service gets one email listing everything.
        var perService = new Dictionary<Guid, List<object>>();
        foreach (var bucket in missing.Where(b => b.Period == MissingPeriodKind.Previous))
        foreach (var entry in bucket.Entries)
        {
            var key = $"missed:{entry.ServiceId}:{entry.SchemaName}:{bucket.PeriodStart:O}";
            if (!await ReserveAsync(key, NotificationKind.Missed, ct)) continue;

            if (!perService.TryGetValue(entry.ServiceId, out var list))
                perService[entry.ServiceId] = list = new List<object>();
            list.Add(new
            {
                schema = entry.SchemaLabel ?? entry.SchemaName,
                missingCount = entry.MissingRequiredCount,
                totalCount = entry.TotalRequiredCount,
                periodStart = Fmt(bucket.PeriodStart),
                periodEnd = Fmt(bucket.PeriodEnd),
            });
        }

        var queued = 0;
        foreach (var (serviceId, items) in perService)
        {
            serviceById.TryGetValue(serviceId, out var service);
            if (service is null) service = await _accounts.GetByIdAsync(serviceId, ct: ct);
            var model = new { service = ServiceModel(service, serviceId), items };
            queued += await EnqueueAsync(DefaultEmailTemplates.Missed, model,
                RecipientsFor(service, settings.Missed, adminRecipients), "notification:missed", serviceId, ct);
        }

        return queued;
    }

    private async Task<int> RunWarningsAsync(
        NotificationSettings settings,
        IReadOnlyDictionary<Guid, Account> serviceById,
        IReadOnlyList<Recipient> adminRecipients,
        CancellationToken ct)
    {
        var since = _audit.UtcNow - WarningLookback;
        var filter = Builders<Submission>.Filter.And(
            Builders<Submission>.Filter.Eq(s => s.IsDeleted, false),
            Builders<Submission>.Filter.Exists("warnings.0"),
            Builders<Submission>.Filter.Gte(s => s.SubmittedAt, since));

        var submissions = await _ctx.Submissions.Find(filter)
            .SortByDescending(s => s.SubmittedAt).Limit(500).ToListAsync(ct);

        var queued = 0;
        foreach (var sub in submissions)
        {
            if (sub.Warnings.Count == 0) continue;
            var key = $"warnings:{sub.Id}";
            if (!await ReserveAsync(key, NotificationKind.Warnings, ct)) continue;

            serviceById.TryGetValue(sub.ServiceAccountId, out var service);
            if (service is null) service = await _accounts.GetByIdAsync(sub.ServiceAccountId, ct: ct);

            var model = new
            {
                service = ServiceModel(service, sub.ServiceAccountId, sub.ServiceName),
                submissionId = sub.Id.ToString(),
                submittedAt = Fmt(sub.SubmittedAt),
                warnings = sub.Warnings,
            };
            queued += await EnqueueAsync(DefaultEmailTemplates.Warnings, model,
                RecipientsFor(service, settings.Warnings, adminRecipients), "notification:warnings", sub.ServiceAccountId, ct);
        }

        return queued;
    }

    /// <summary>Render a template once and enqueue an identical copy to each recipient. Returns the number queued.</summary>
    private async Task<int> EnqueueAsync(
        string templateKey, object model, IReadOnlyList<Recipient> recipients,
        string category, Guid? relatedAccountId, CancellationToken ct)
    {
        if (recipients.Count == 0) return 0;

        RenderedEmail rendered;
        try { rendered = await _content.BuildAsync(templateKey, model, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to render template {Key}.", templateKey); return 0; }

        var queued = 0;
        foreach (var r in recipients)
        {
            await _queue.EnqueueAsync(new EmailRequest(
                r.Email, r.Name, rendered.Subject, rendered.TextBody, rendered.HtmlBody, category, relatedAccountId), ct);
            queued++;
        }
        return queued;
    }

    /// <summary>Resolve the admin/operator recipient addresses configured on the settings (skipping any without an email).</summary>
    private async Task<IReadOnlyList<Recipient>> ResolveAdminRecipientsAsync(NotificationSettings settings, CancellationToken ct)
    {
        var list = new List<Recipient>();
        foreach (var id in settings.AdminRecipientAccountIds.Distinct())
        {
            var account = await _accounts.GetByIdAsync(id, ct: ct);
            if (account?.Email is { } email && !string.IsNullOrWhiteSpace(email))
                list.Add(new Recipient(email, account.Label ?? account.Name));
        }
        return list;
    }

    private static IReadOnlyList<Recipient> RecipientsFor(Account? service, NotificationRule rule, IReadOnlyList<Recipient> admins)
    {
        var list = new List<Recipient>();
        if (rule.NotifyServiceAccount && service?.Email is { } email && !string.IsNullOrWhiteSpace(email))
            list.Add(new Recipient(email, service.Label ?? service.Name));
        if (rule.NotifyAdminList) list.AddRange(admins);

        return list
            .GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Insert a dedupe marker; returns false if one already exists for this event.</summary>
    private async Task<bool> ReserveAsync(string key, NotificationKind kind, CancellationToken ct)
    {
        try
        {
            await _ctx.NotificationLogs.InsertOneAsync(
                new NotificationLog { Key = key, Kind = kind, CreatedAt = _audit.UtcNow }, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    private static object ServiceModel(Account? account, Guid? fallbackId = null, string? fallbackName = null) => new
    {
        name = account?.Name ?? fallbackName ?? fallbackId?.ToString() ?? "service",
        label = account?.Label ?? account?.Name ?? fallbackName ?? "the service",
    };

    private static string Fmt(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm 'UTC'");

    private readonly record struct Recipient(string Email, string? Name);
}
