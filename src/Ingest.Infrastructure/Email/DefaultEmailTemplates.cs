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

    /// <summary>Template key for the "submission pending approval" notice.</summary>
    public const string PendingApproval = "notification.pendingApproval";

    /// <summary>Template key for the "submission approved" notice.</summary>
    public const string Approved = "notification.approved";

    /// <summary>Template key for the "submission rejected" notice.</summary>
    public const string Rejected = "notification.rejected";

    /// <summary>Template key for the "draft saved" nudge.</summary>
    public const string DraftSaved = "notification.draftSaved";

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
        new EmailTemplate
        {
            Key = PendingApproval,
            Name = "Submission pending approval notice",
            Description = "Sent when a submission is accepted but held awaiting approval before it goes live. " +
                          "Model: service (name, label), submissionId, submittedAt, schemas[] (strings), sampleCount.",
            Subject = "Approval needed: submission from {{ service.label }}",
            TextBody =
                "Hello,\n\n" +
                "A submission from {{ service.label }} (id {{ submissionId }}, {{ submittedAt }}) is awaiting approval before it goes live.\n" +
                "Schemas: {% for s in schemas %}{{ s }}{% unless forloop.last %}, {% endunless %}{% endfor %} ({{ sampleCount }} value(s)).\n\n" +
                "Please review it in the admin console.\n\n" +
                "— Ingest",
            HtmlBody =
                "<p>Hello,</p>" +
                "<p>A submission from <strong>{{ service.label }}</strong> (id {{ submissionId }}, {{ submittedAt }}) is awaiting approval before it goes live.</p>" +
                "<p>Schemas: {% for s in schemas %}{{ s }}{% unless forloop.last %}, {% endunless %}{% endfor %} ({{ sampleCount }} value(s)).</p>" +
                "<p>Please review it in the admin console.</p><p>— Ingest</p>",
        },
        new EmailTemplate
        {
            Key = Approved,
            Name = "Submission approved notice",
            Description = "Sent when a pending submission is approved and becomes live. " +
                          "Model: service (name, label), submissionId, submittedAt, decidedBy, note, schemas[] (strings), sampleCount.",
            Subject = "Submission approved — {{ service.label }}",
            TextBody =
                "Hello,\n\n" +
                "The submission from {{ service.label }} (id {{ submissionId }}, {{ submittedAt }}) was approved{% if decidedBy %} by {{ decidedBy }}{% endif %} and is now live.\n" +
                "{% if note %}Note: {{ note }}\n{% endif %}\n" +
                "— Ingest",
            HtmlBody =
                "<p>Hello,</p>" +
                "<p>The submission from <strong>{{ service.label }}</strong> (id {{ submissionId }}, {{ submittedAt }}) was approved{% if decidedBy %} by {{ decidedBy }}{% endif %} and is now live.</p>" +
                "{% if note %}<p>Note: {{ note }}</p>{% endif %}" +
                "<p>— Ingest</p>",
        },
        new EmailTemplate
        {
            Key = Rejected,
            Name = "Submission rejected notice",
            Description = "Sent when a pending submission is rejected. " +
                          "Model: service (name, label), submissionId, submittedAt, decidedBy, reason, schemas[] (strings), sampleCount.",
            Subject = "Submission rejected — {{ service.label }}",
            TextBody =
                "Hello,\n\n" +
                "The submission from {{ service.label }} (id {{ submissionId }}, {{ submittedAt }}) was rejected{% if decidedBy %} by {{ decidedBy }}{% endif %} and will not go live.\n" +
                "{% if reason %}Reason: {{ reason }}\n{% endif %}\n" +
                "You can re-submit corrected data for the same period.\n\n" +
                "— Ingest",
            HtmlBody =
                "<p>Hello,</p>" +
                "<p>The submission from <strong>{{ service.label }}</strong> (id {{ submissionId }}, {{ submittedAt }}) was rejected{% if decidedBy %} by {{ decidedBy }}{% endif %} and will not go live.</p>" +
                "{% if reason %}<p>Reason: {{ reason }}</p>{% endif %}" +
                "<p>You can re-submit corrected data for the same period.</p><p>— Ingest</p>",
        },
        new EmailTemplate
        {
            Key = DraftSaved,
            Name = "Draft saved nudge",
            Description = "Sent every time a submission is saved as a draft, nudging collaborators to keep filling it in. " +
                          "Model: service (name, label), submissionId, submittedAt, schemas[] (strings), sampleCount, editPath. " +
                          "editPath is a relative path (e.g. /submissions/{id}/edit) shown as plain text — the server doesn't know its public URL, so paste it after your console's host.",
            Subject = "Draft saved — {{ service.label }}",
            TextBody =
                "Hello,\n\n" +
                "A draft submission for {{ service.label }} has been saved and is waiting to be completed.\n" +
                "Schemas: {% for s in schemas %}{{ s }}{% unless forloop.last %}, {% endunless %}{% endfor %} ({{ sampleCount }} value(s) so far).\n\n" +
                "Open it in the admin console at this path (add it after your console address):\n" +
                "{{ editPath }}\n\n" +
                "The draft stays out of reporting until it is published.\n\n" +
                "— Ingest",
            HtmlBody =
                "<p>Hello,</p>" +
                "<p>A draft submission for <strong>{{ service.label }}</strong> has been saved and is waiting to be completed.</p>" +
                "<p>Schemas: {% for s in schemas %}{{ s }}{% unless forloop.last %}, {% endunless %}{% endfor %} ({{ sampleCount }} value(s) so far).</p>" +
                "<p>Open it in the admin console at this path (add it after your console address):<br>" +
                "<code>{{ editPath }}</code></p>" +
                "<p>The draft stays out of reporting until it is published.</p><p>— Ingest</p>",
        },
    };
}
