using System.Net.Mail;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Email;

/// <summary>MongoDB-backed durable outbox.</summary>
public sealed class EmailQueue : IEmailQueue
{
    private readonly MongoContext _ctx;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="EmailQueue"/>.</summary>
    public EmailQueue(MongoContext ctx, IAuditContext audit)
    {
        _ctx = ctx;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<Guid> EnqueueAsync(EmailRequest request, CancellationToken ct = default)
    {
        var to = request.ToAddress?.Trim();
        if (string.IsNullOrWhiteSpace(to) || !MailAddress.TryCreate(to, out _))
            throw new ValidationException(new[] { $"'{request.ToAddress}' is not a valid email address." });
        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new ValidationException(new[] { "Email subject is required." });

        var now = _audit.UtcNow;
        var message = new EmailMessage
        {
            ToAddress = to,
            ToName = string.IsNullOrWhiteSpace(request.ToName) ? null : request.ToName.Trim(),
            Subject = request.Subject,
            TextBody = request.TextBody ?? "",
            HtmlBody = string.IsNullOrWhiteSpace(request.HtmlBody) ? null : request.HtmlBody,
            Status = EmailStatus.Pending,
            Category = string.IsNullOrWhiteSpace(request.Category) ? "general" : request.Category,
            RelatedAccountId = request.RelatedAccountId,
            CreatedAt = now,
            ModifiedAt = now,
            CreatedBy = _audit.UserName,
            ModifiedBy = _audit.UserName,
        };

        await _ctx.EmailOutbox.InsertOneAsync(message, cancellationToken: ct);
        return message.Id;
    }

    /// <inheritdoc />
    public async Task<PagedResult<EmailMessage>> ListAsync(PageRequest request, EmailStatus? status = null, CancellationToken ct = default)
    {
        var filter = status is null
            ? FilterDefinition<EmailMessage>.Empty
            : Builders<EmailMessage>.Filter.Eq(m => m.Status, status.Value);

        var total = await _ctx.EmailOutbox.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _ctx.EmailOutbox
            .Find(filter)
            .SortByDescending(m => m.CreatedAt)
            .Skip(request.Skip)
            .Limit(request.Take)
            .ToListAsync(ct);

        return new PagedResult<EmailMessage>(items, total, request.Page, request.Take);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailMessage>> ListForAccountAsync(Guid accountId, string? email, CancellationToken ct = default)
    {
        return await _ctx.EmailOutbox.Find(ForAccountFilter(accountId, email))
            .SortByDescending(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<long> HardDeleteForAccountAsync(Guid accountId, string? email, CancellationToken ct = default)
    {
        var result = await _ctx.EmailOutbox.DeleteManyAsync(ForAccountFilter(accountId, email), ct);
        return result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<long> PurgeProcessedOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default)
    {
        var fb = Builders<EmailMessage>.Filter;
        var filter = fb.And(
            fb.In(m => m.Status, new[] { EmailStatus.Sent, EmailStatus.Failed }),
            fb.Lt(m => m.CreatedAt, olderThanUtc));
        var result = await _ctx.EmailOutbox.DeleteManyAsync(filter, ct);
        return result.DeletedCount;
    }

    /// <summary>Match outbox messages tied to a subject: by related account id, or by recipient address (case-insensitive).</summary>
    private static FilterDefinition<EmailMessage> ForAccountFilter(Guid accountId, string? email)
    {
        var fb = Builders<EmailMessage>.Filter;
        var filter = fb.Eq(m => m.RelatedAccountId, accountId);
        if (!string.IsNullOrWhiteSpace(email))
        {
            var rx = new MongoDB.Bson.BsonRegularExpression(
                $"^{System.Text.RegularExpressions.Regex.Escape(email.Trim())}$", "i");
            filter = fb.Or(filter, fb.Regex(m => m.ToAddress, rx));
        }
        return filter;
    }
}
