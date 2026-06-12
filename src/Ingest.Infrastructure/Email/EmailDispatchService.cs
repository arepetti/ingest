using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Email;

/// <summary>
/// Drains the outbox: picks up pending messages, asks <see cref="IEmailSender"/> to deliver them,
/// and records the outcome. Missing SMTP settings is treated as a permanent failure with a clear
/// reason rather than an endless retry loop; transient send errors are retried up to
/// <see cref="EmailWorkerOptions.MaxAttempts"/> times before being marked failed.
/// </summary>
public sealed class EmailDispatchService : IEmailDispatchService
{
    private readonly MongoContext _ctx;
    private readonly IEmailSettingsService _settingsService;
    private readonly IEmailSender _sender;
    private readonly IAuditContext _audit;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailDispatchService> _logger;

    /// <summary>Create a new <see cref="EmailDispatchService"/>.</summary>
    public EmailDispatchService(
        MongoContext ctx,
        IEmailSettingsService settingsService,
        IEmailSender sender,
        IAuditContext audit,
        IOptions<EmailOptions> options,
        ILogger<EmailDispatchService> logger)
    {
        _ctx = ctx;
        _settingsService = settingsService;
        _sender = sender;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EmailDrainResult> DrainAsync(int max, CancellationToken ct = default)
    {
        var pending = await _ctx.EmailOutbox
            .Find(Builders<EmailMessage>.Filter.Eq(m => m.Status, EmailStatus.Pending))
            .SortBy(m => m.CreatedAt)
            .Limit(Math.Clamp(max, 1, 500))
            .ToListAsync(ct);

        if (pending.Count == 0) return new EmailDrainResult(0, 0);

        var settings = await _settingsService.GetAsync(ct);
        int sent = 0, failed = 0;

        foreach (var message in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (!settings.IsConfigured)
            {
                await MarkAsync(message, EmailStatus.Failed,
                    "SMTP is not configured. Set the email server under Settings → Email.", ct);
                failed++;
                continue;
            }

            try
            {
                await _sender.SendAsync(message, settings, ct);
                message.SentAt = _audit.UtcNow;
                await MarkAsync(message, EmailStatus.Sent, null, ct);
                sent++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                message.Attempts++;
                var permanent = message.Attempts >= _options.Worker.MaxAttempts;
                await MarkAsync(message, permanent ? EmailStatus.Failed : EmailStatus.Pending, ex.Message, ct);
                failed++;
                _logger.LogWarning(ex, "Email {Id} delivery failed (attempt {Attempt}/{Max}).",
                    message.Id, message.Attempts, _options.Worker.MaxAttempts);
            }
        }

        return new EmailDrainResult(sent, failed);
    }

    private Task MarkAsync(EmailMessage message, EmailStatus status, string? error, CancellationToken ct)
    {
        message.Status = status;
        message.LastError = error;
        message.ModifiedAt = _audit.UtcNow;
        return _ctx.EmailOutbox.ReplaceOneAsync(
            Builders<EmailMessage>.Filter.Eq(m => m.Id, message.Id), message, cancellationToken: ct);
    }
}
