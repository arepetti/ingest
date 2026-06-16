using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingest.Infrastructure.Email;

/// <summary>
/// Sends the three approval-lifecycle emails (pending / approved / rejected) the instant a
/// submission changes approval state. Mirrors the <see cref="NotificationService"/> recipient and
/// rendering conventions, but is event-driven rather than scheduler-driven so reviewers and
/// submitters hear about decisions without waiting for the next poll.
/// </summary>
/// <remarks>
/// Every method is best-effort: it returns silently when email is disabled or the matching rule is
/// off, and swallows (logs) any failure so a notification hiccup never turns an accepted write or a
/// completed approval decision into an error.
/// </remarks>
public sealed class ApprovalNotificationService : IApprovalNotificationService
{
    private readonly INotificationSettingsService _settingsService;
    private readonly IEmailContentBuilder _content;
    private readonly IEmailQueue _queue;
    private readonly IAccountRepository _accounts;
    private readonly bool _enabled;
    private readonly ILogger<ApprovalNotificationService> _logger;

    /// <summary>Create a new <see cref="ApprovalNotificationService"/>.</summary>
    public ApprovalNotificationService(
        INotificationSettingsService settingsService,
        IEmailContentBuilder content,
        IEmailQueue queue,
        IAccountRepository accounts,
        IOptions<EmailOptions> emailOptions,
        ILogger<ApprovalNotificationService> logger)
    {
        _settingsService = settingsService;
        _content = content;
        _queue = queue;
        _accounts = accounts;
        _enabled = emailOptions.Value.Enabled;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyPendingAsync(Submission submission, CancellationToken ct = default)
    {
        if (!_enabled) return;
        try
        {
            var settings = await _settingsService.GetAsync(ct);
            if (!settings.PendingApproval.Enabled) return;

            // The whole point of a "pending" notice is to alert the people who must act, so the
            // designated approvers are always included; the rule's two switches add submitter/admin copies.
            var recipients = new List<Recipient>();
            foreach (var spec in submission.RequiredApprovers)
            {
                var account = await _accounts.GetByIdAsync(spec.AccountId, ct: ct);
                if (account?.Email is { } email && !string.IsNullOrWhiteSpace(email))
                    recipients.Add(new Recipient(email, account.Label ?? account.Name));
            }
            recipients.AddRange(await ConfiguredRecipientsAsync(submission, settings.PendingApproval, ct));

            await EnqueueAsync(DefaultEmailTemplates.PendingApproval, BaseModel(submission, null, null), recipients,
                "notification:pending_approval", submission.ServiceAccountId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pending-approval notification failed for submission {Id}.", submission.Id);
        }
    }

    /// <inheritdoc />
    public async Task NotifyApprovedAsync(Submission submission, CancellationToken ct = default)
    {
        if (!_enabled) return;
        try
        {
            var settings = await _settingsService.GetAsync(ct);
            if (!settings.Approved.Enabled) return;

            var decision = LastDecision(submission, ApprovalDecision.Approved);
            var model = BaseModel(submission, decision?.ApproverName, decision?.Note);
            var recipients = await ConfiguredRecipientsAsync(submission, settings.Approved, ct);

            await EnqueueAsync(DefaultEmailTemplates.Approved, model, recipients,
                "notification:approved", submission.ServiceAccountId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Approved notification failed for submission {Id}.", submission.Id);
        }
    }

    /// <inheritdoc />
    public async Task NotifyRejectedAsync(Submission submission, string? reason, CancellationToken ct = default)
    {
        if (!_enabled) return;
        try
        {
            var settings = await _settingsService.GetAsync(ct);
            if (!settings.Rejected.Enabled) return;

            var decision = LastDecision(submission, ApprovalDecision.Rejected);
            var text = string.IsNullOrWhiteSpace(reason) ? decision?.Note : reason;
            var model = BaseModel(submission, decision?.ApproverName, text);
            var recipients = await ConfiguredRecipientsAsync(submission, settings.Rejected, ct);

            await EnqueueAsync(DefaultEmailTemplates.Rejected, model, recipients,
                "notification:rejected", submission.ServiceAccountId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rejected notification failed for submission {Id}.", submission.Id);
        }
    }

    // The decision text is exposed under both `note` (approved template) and `reason` (rejected
    // template) so each template can use the field name that reads naturally; the pending template
    // ignores both.
    private static object BaseModel(Submission submission, string? decidedBy, string? text) => new
    {
        service = ServiceModel(submission.ServiceName, submission.ServiceAccountId),
        submissionId = submission.Id.ToString(),
        submittedAt = Fmt(submission.SubmittedAt),
        schemas = submission.Samples.Select(s => s.SchemaName).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        sampleCount = submission.Samples.Count,
        decidedBy,
        note = text,
        reason = text,
    };

    /// <summary>Resolve the submitter (when the rule wants it) plus the shared admin recipient list (when the rule wants it).</summary>
    private async Task<IReadOnlyList<Recipient>> ConfiguredRecipientsAsync(Submission submission, NotificationRule rule, CancellationToken ct)
    {
        var list = new List<Recipient>();

        if (rule.NotifyServiceAccount)
        {
            var service = await _accounts.GetByIdAsync(submission.ServiceAccountId, ct: ct);
            if (service?.Email is { } email && !string.IsNullOrWhiteSpace(email))
                list.Add(new Recipient(email, service.Label ?? service.Name));
        }

        if (rule.NotifyAdminList)
        {
            var settings = await _settingsService.GetAsync(ct);
            foreach (var id in settings.AdminRecipientAccountIds.Distinct())
            {
                var account = await _accounts.GetByIdAsync(id, ct: ct);
                if (account?.Email is { } email && !string.IsNullOrWhiteSpace(email))
                    list.Add(new Recipient(email, account.Label ?? account.Name));
            }
        }

        return list;
    }

    /// <summary>Render the template once and enqueue an identical copy per (deduplicated) recipient.</summary>
    private async Task EnqueueAsync(string templateKey, object model, IEnumerable<Recipient> recipients, string category, Guid relatedAccountId, CancellationToken ct)
    {
        var deduped = recipients
            .GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (deduped.Count == 0) return;

        var rendered = await _content.BuildAsync(templateKey, model, ct);
        foreach (var r in deduped)
            await _queue.EnqueueAsync(new EmailRequest(
                r.Email, r.Name, rendered.Subject, rendered.TextBody, rendered.HtmlBody, category, relatedAccountId), ct);
    }

    private static SubmissionApproval? LastDecision(Submission submission, ApprovalDecision decision) =>
        submission.Approvals
            .Where(a => a.Decision == decision)
            .OrderByDescending(a => a.DecidedAt)
            .FirstOrDefault();

    private static object ServiceModel(string? name, Guid id) => new
    {
        name = name ?? id.ToString(),
        label = name ?? "the service",
    };

    private static string Fmt(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm 'UTC'");

    private readonly record struct Recipient(string Email, string? Name);
}
