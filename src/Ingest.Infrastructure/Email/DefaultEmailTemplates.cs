using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Email;

/// <summary>
/// The built-in email templates seeded on first start. Admins edit these from the Settings page;
/// the keys are stable and referenced by the notification job. Each template is plain Liquid and
/// is rendered against a small, documented model (see <c>docs/admin-user-guide/notifications.md</c>).
/// </summary>
public static class DefaultEmailTemplates
{
    /// <summary>Template key for the "upcoming submission" reminder.</summary>
    public const string Upcoming = "notification.upcoming";

    /// <summary>Template key for the "missed submission" alert.</summary>
    public const string Missed = "notification.missed";

    /// <summary>Template key for the "submission with warnings" notice.</summary>
    public const string Warnings = "notification.warnings";

    /// <summary>Every built-in template, in display order.</summary>
    public static IReadOnlyList<EmailTemplate> All { get; } = new[]
    {
        new EmailTemplate
        {
            Key = Upcoming,
            Name = "Upcoming submission reminder",
            Description = "Sent when a required value's cadence window is about to close and nothing has been submitted yet. " +
                          "Model: service (name, label), items[] (schema, value, cadence, periodEnd).",
            Subject = "Reminder: {{ items.size }} submission(s) due soon for {{ service.label }}",
            TextBody =
                "Hello,\n\n" +
                "The following submissions for {{ service.label }} are due soon:\n" +
                "{% for item in items %}- {{ item.value }} ({{ item.schema }}, {{ item.cadence }}) by {{ item.periodEnd }}\n{% endfor %}\n" +
                "Please submit before the window closes.\n\n" +
                "— Ingest",
            HtmlBody =
                "<p>Hello,</p>" +
                "<p>The following submissions for <strong>{{ service.label }}</strong> are due soon:</p>" +
                "<ul>{% for item in items %}<li>{{ item.value }} ({{ item.schema }}, {{ item.cadence }}) — by {{ item.periodEnd }}</li>{% endfor %}</ul>" +
                "<p>Please submit before the window closes.</p><p>— Ingest</p>",
        },
        new EmailTemplate
        {
            Key = Missed,
            Name = "Missed submission alert",
            Description = "Sent when a required value's previous cadence window closed without a submission. " +
                          "Model: service (name, label), items[] (schema, missingCount, totalCount, periodStart, periodEnd).",
            Subject = "Missed submission(s) for {{ service.label }}",
            TextBody =
                "Hello,\n\n" +
                "The following required submissions for {{ service.label }} were not received before their deadline:\n" +
                "{% for item in items %}- {{ item.schema }}: {{ item.missingCount }} of {{ item.totalCount }} missing (window {{ item.periodStart }} – {{ item.periodEnd }})\n{% endfor %}\n" +
                "Please follow up as soon as possible.\n\n" +
                "— Ingest",
            HtmlBody =
                "<p>Hello,</p>" +
                "<p>The following required submissions for <strong>{{ service.label }}</strong> were not received before their deadline:</p>" +
                "<ul>{% for item in items %}<li>{{ item.schema }}: {{ item.missingCount }} of {{ item.totalCount }} missing (window {{ item.periodStart }} – {{ item.periodEnd }})</li>{% endfor %}</ul>" +
                "<p>Please follow up as soon as possible.</p><p>— Ingest</p>",
        },
        new EmailTemplate
        {
            Key = Warnings,
            Name = "Submission with warnings notice",
            Description = "Sent when a submission was accepted but carried validation warnings. " +
                          "Model: service (name, label), submissionId, submittedAt, warnings[] (strings).",
            Subject = "Submission accepted with {{ warnings.size }} warning(s) — {{ service.label }}",
            TextBody =
                "Hello,\n\n" +
                "A submission from {{ service.label }} (id {{ submissionId }}, {{ submittedAt }}) was accepted with warnings:\n" +
                "{% for w in warnings %}- {{ w }}\n{% endfor %}\n" +
                "Review the submission to confirm the data is correct.\n\n" +
                "— Ingest",
            HtmlBody =
                "<p>Hello,</p>" +
                "<p>A submission from <strong>{{ service.label }}</strong> (id {{ submissionId }}, {{ submittedAt }}) was accepted with warnings:</p>" +
                "<ul>{% for w in warnings %}<li>{{ w }}</li>{% endfor %}</ul>" +
                "<p>Review the submission to confirm the data is correct.</p><p>— Ingest</p>",
        },
    };
}
