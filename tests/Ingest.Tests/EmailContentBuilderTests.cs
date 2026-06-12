using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Email;
using Ingest.Infrastructure.Reports;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="EmailContentBuilder"/> — that it renders a template's subject/text/html
/// through the (real) Fluid renderer against a model, and omits HTML when the template has none.
/// </summary>
public class EmailContentBuilderTests
{
    private static EmailContentBuilder New(EmailTemplate template) =>
        new(new FakeTemplateService(template), new FluidReportRenderer());

    [Fact]
    public async Task Renders_subject_text_and_html()
    {
        var template = new EmailTemplate
        {
            Key = "notification.upcoming",
            Subject = "{{ items.size }} due for {{ service.label }}",
            TextBody = "Service {{ service.name }}:\n{% for i in items %}- {{ i.value }}\n{% endfor %}",
            HtmlBody = "<ul>{% for i in items %}<li>{{ i.value }}</li>{% endfor %}</ul>",
        };
        var model = new
        {
            service = new { name = "roads", label = "Roads" },
            items = new[] { new { value = "Tonnes" }, new { value = "Incidents" } },
        };

        var rendered = await New(template).BuildAsync("notification.upcoming", model);

        Assert.Equal("2 due for Roads", rendered.Subject);
        Assert.Contains("- Tonnes", rendered.TextBody);
        Assert.Contains("- Incidents", rendered.TextBody);
        Assert.NotNull(rendered.HtmlBody);
        Assert.Contains("<li>Tonnes</li>", rendered.HtmlBody);
    }

    [Fact]
    public async Task Omits_html_when_template_has_none()
    {
        var template = new EmailTemplate
        {
            Key = "adhoc",
            Subject = "Hi {{ name }}",
            TextBody = "Hello {{ name }}",
            HtmlBody = null,
        };

        var rendered = await New(template).BuildAsync("adhoc", new { name = "Sam" });

        Assert.Equal("Hi Sam", rendered.Subject);
        Assert.Equal("Hello Sam", rendered.TextBody);
        Assert.Null(rendered.HtmlBody);
    }

    private sealed class FakeTemplateService : IEmailTemplateService
    {
        private readonly EmailTemplate _template;
        public FakeTemplateService(EmailTemplate template) => _template = template;

        public Task<EmailTemplate> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_template);
        public Task SeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmailTemplate>>(new[] { _template });
        public Task<EmailTemplate> UpdateAsync(string key, EmailTemplateUpdate update, CancellationToken ct = default) =>
            Task.FromResult(_template);
    }
}
