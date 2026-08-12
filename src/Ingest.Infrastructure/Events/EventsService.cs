using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Events;

/// <summary>
/// Database-backed CRUD for the admin-recorded <see cref="Event"/> timeline. Each write validates
/// the required fields and the affected-services scope, then records an audit entry. Events are
/// soft-deleted to preserve history.
/// </summary>
public sealed class EventsService : IEventsService
{
    private readonly MongoContext _ctx;
    private readonly IAccountRepository _accounts;
    private readonly IAuditLogService _audit;
    private readonly IAuditContext _auditContext;

    /// <summary>Create a new <see cref="EventsService"/>.</summary>
    public EventsService(MongoContext ctx, IAccountRepository accounts, IAuditLogService audit, IAuditContext auditContext)
    {
        _ctx = ctx;
        _accounts = accounts;
        _audit = audit;
        _auditContext = auditContext;
    }

    /// <inheritdoc />
    public async Task<PagedResult<Event>> ListAsync(PageRequest request, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var filter = Builders<Event>.Filter.Eq(e => e.IsDeleted, false);
        // The lower bound (`from`) depends on each event's own duration, which Mongo can't filter
        // on directly (it varies per document) — so only the upper bound is pushed down to the
        // query; the rest of the overlap test runs in memory below. Events are a small,
        // admin-curated annotation set (not bulk telemetry), so loading the to-bounded candidates
        // and filtering/paging in memory is simple and fast enough at this scale.
        if (to is { } t)
            filter = Builders<Event>.Filter.And(filter, Builders<Event>.Filter.Lt(e => e.Timestamp, t));

        var candidates = await _ctx.Events
            .Find(filter)
            .SortByDescending(e => e.Timestamp)
            .ToListAsync(ct);

        var matching = from is { } f ? candidates.Where(e => Overlaps(e, f)).ToList() : candidates;

        var total = matching.Count;
        var items = matching.Skip(request.Skip).Take(request.Take).ToList();
        return new PagedResult<Event>(items, total, request.Page, request.Take);
    }

    /// <summary>Whether an event's span reaches on/after <paramref name="from"/> (the other half of the overlap test; the `to` side is already applied by the query).</summary>
    private static bool Overlaps(Event e, DateTime from) => e.Kind switch
    {
        EventKind.FromNowOn => true,
        EventKind.Interval => e.Timestamp + (e.Duration ?? TimeSpan.Zero) >= from,
        _ => e.Timestamp >= from,
    };

    /// <inheritdoc />
    public async Task<Event> CreateAsync(Event ev, CancellationToken ct = default)
    {
        await ValidateAsync(ev, ct);

        var now = _auditContext.UtcNow;
        ev.Id = ev.Id == Guid.Empty ? Guid.NewGuid() : ev.Id;
        ev.IsDeleted = false;
        ev.CreatedAt = now;
        ev.CreatedBy = _auditContext.UserName;
        ev.ModifiedAt = now;
        ev.ModifiedBy = _auditContext.UserName;

        await _ctx.Events.InsertOneAsync(ev, cancellationToken: ct);
        await _audit.RecordAsync(AuditTargetType.Event, AuditChangeType.Create, ev.Id, ev.Label, ct);
        return ev;
    }

    /// <inheritdoc />
    public async Task<Event> UpdateAsync(Guid id, Event ev, CancellationToken ct = default)
    {
        await ValidateAsync(ev, ct);

        var existing = await _ctx.Events
            .Find(Builders<Event>.Filter.And(
                Builders<Event>.Filter.Eq(e => e.Id, id),
                Builders<Event>.Filter.Eq(e => e.IsDeleted, false)))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Event");

        existing.Timestamp = ev.Timestamp;
        existing.Label = ev.Label;
        existing.Description = ev.Description;
        existing.Kind = ev.Kind;
        existing.Duration = ev.Duration;
        existing.ServiceIds = ev.ServiceIds;
        existing.ModifiedAt = _auditContext.UtcNow;
        existing.ModifiedBy = _auditContext.UserName;

        await _ctx.Events.ReplaceOneAsync(
            Builders<Event>.Filter.Eq(e => e.Id, existing.Id), existing, cancellationToken: ct);
        await _audit.RecordAsync(AuditTargetType.Event, AuditChangeType.Edit, existing.Id, existing.Label, ct);
        return existing;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _ctx.Events
            .Find(Builders<Event>.Filter.And(
                Builders<Event>.Filter.Eq(e => e.Id, id),
                Builders<Event>.Filter.Eq(e => e.IsDeleted, false)))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Event");

        existing.IsDeleted = true;
        existing.DeletedAt = _auditContext.UtcNow;
        existing.DeletedBy = _auditContext.UserName;
        existing.ModifiedAt = existing.DeletedAt.Value;
        existing.ModifiedBy = existing.DeletedBy;

        await _ctx.Events.ReplaceOneAsync(
            Builders<Event>.Filter.Eq(e => e.Id, existing.Id), existing, cancellationToken: ct);
        await _audit.RecordAsync(AuditTargetType.Event, AuditChangeType.Delete, existing.Id, existing.Label, ct);
    }

    /// <summary>
    /// Validate + normalise an event before it's saved: a non-blank label, a non-default UTC
    /// timestamp, a duration consistent with <see cref="Event.Kind"/>, and a de-duplicated
    /// <see cref="Event.ServiceIds"/> list where every id refers to an existing service account. An
    /// empty list is left as-is — it means "all services".
    /// </summary>
    private async Task ValidateAsync(Event ev, CancellationToken ct)
    {
        var errors = new List<Diagnostic>();

        ev.Label = ev.Label?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(ev.Label))
            errors.Add(new Diagnostic(DiagnosticCodes.Events.LabelRequired, "Label is required."));

        if (ev.Timestamp == default)
            errors.Add(new Diagnostic(DiagnosticCodes.Events.TimestampRequired, "Timestamp is required."));
        else
            ev.Timestamp = DateTime.SpecifyKind(ev.Timestamp, DateTimeKind.Utc);

        ev.Description = string.IsNullOrWhiteSpace(ev.Description) ? null : ev.Description.Trim();

        // Duration only makes sense for a bounded interval — required (and positive) there,
        // cleared everywhere else so a stray value can't linger after switching kinds.
        if (ev.Kind == EventKind.Interval)
        {
            if (ev.Duration is null || ev.Duration <= TimeSpan.Zero)
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Events.IntervalDurationRequired,
                    "Duration is required for interval events.",
                    ("eventKind", ev.Kind.ToString()),
                    ("duration", ev.Duration)));
        }
        else
        {
            ev.Duration = null;
        }

        var serviceIds = (ev.ServiceIds ?? new())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (serviceIds.Count > 0)
        {
            var unknown = new List<Guid>();
            foreach (var id in serviceIds)
            {
                var account = await _accounts.GetByIdAsync(id, ct: ct);
                if (account is null || account.Role != Core.Entities.AccountRole.Service)
                    unknown.Add(id);
            }
            if (unknown.Count > 0)
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Events.InvalidServiceIds,
                    $"Affected services must be existing service accounts. Unknown or non-service ids: {string.Join(", ", unknown)}.",
                    ("serviceIds", unknown)));
        }
        ev.ServiceIds = serviceIds;

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
