using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingest.Infrastructure.Email;

/// <summary>
/// Sends the "draft saved" nudge every time a submission is saved as a draft (create or re-save).
/// Mirrors <see cref="ApprovalNotificationService"/>'s recipient and rendering conventions, but is
/// fired by the submission write path rather than the scheduler so collaborators are prompted the
/// moment a draft changes. Unlike the approval notices there is no <c>NotificationLog</c> dedupe —
/// every save re-notifies, which is the intended collaborative nudge.
/// </summary>
/// <remarks>
/// Best-effort: it returns silently when email is disabled or the draft-saved rule is off, and
/// swallows (logs) any failure so a notification hiccup never turns a saved draft into an error.
/// </remarks>
public sealed class DraftNotificationService : IDraftNotificationService
{
    private readonly INotificationSettingsService _settingsService;
    private readonly IEmailContentBuilder _content;
    private readonly IEmailQueue _queue;
    private readonly IAccountRepository _accounts;
    private readonly bool _enabled;
    private readonly ILogger<DraftNotificationService> _logger;

    /// <summary>Create a new <see cref="DraftNotificationService"/>.</summary>
    public DraftNotificationService(
        INotificationSettingsService settingsService,
        IEmailContentBuilder content,
        IEmailQueue queue,
        IAccountRepository accounts,
        IOptions<EmailOptions> emailOptions,
        ILogger<DraftNotificationService> logger)
    {
        _settingsService = settingsService;
        _content = content;
        _queue = queue;
        _accounts = accounts;
        _enabled = emailOptions.Value.Enabled;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyDraftSavedAsync(Submission submission, CancellationToken ct = default)
    {
        if (!_enabled) return;
        try
        {
            var settings = await _settingsService.GetAsync(ct);
            if (!settings.DraftSaved.Enabled) return;

            var recipients = await ConfiguredRecipientsAsync(submission, settings.DraftSaved, settings, ct);

            await EnqueueAsync(DefaultEmailTemplates.DraftSaved, BuildModel(submission), recipients,
                "notification:draft_saved", submission.ServiceAccountId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Draft-saved notification failed for submission {Id}.", submission.Id);
        }
    }

    // The edit path is a RELATIVE path on purpose: the server has no knowledge of its public base
    // URL, so the email shows the path as plain text (rendered without an <a>/href in the template)
    // for the operator to paste after their console's host.
    private static object BuildModel(Submission submission) => new
    {
        service = ServiceModel(submission.ServiceName, submission.ServiceAccountId),
        submissionId = submission.Id.ToString(),
        submittedAt = Fmt(submission.SubmittedAt),
        schemas = submission.Samples.Select(s => s.SchemaName).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        sampleCount = submission.Samples.Count,
        editPath = $"/submissions/{submission.Id}/edit",
    };

    /// <summary>Resolve the submitter (when the rule wants it) plus the shared admin recipient list (when the rule wants it).</summary>
    private async Task<IReadOnlyList<Recipient>> ConfiguredRecipientsAsync(Submission submission, NotificationRule rule, NotificationSettings settings, CancellationToken ct)
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

    private static object ServiceModel(string? name, Guid id) => new
    {
        name = name ?? id.ToString(),
        label = name ?? "the service",
    };

    private static string Fmt(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm 'UTC'");

    private readonly record struct Recipient(string Email, string? Name);
}
