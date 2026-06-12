using Ingest.Core.Abstractions;

namespace Ingest.Infrastructure.Email;

/// <summary>
/// Renders an editable <see cref="Core.Entities.EmailTemplate"/> into a <see cref="RenderedEmail"/>
/// by reusing the same sandboxed Fluid renderer the reports feature uses. Producers (the
/// notification job today) call this; the queue/sender never see a template.
/// </summary>
public sealed class EmailContentBuilder : IEmailContentBuilder
{
    private readonly IEmailTemplateService _templates;
    private readonly IReportRenderer _renderer;

    /// <summary>Create a new <see cref="EmailContentBuilder"/>.</summary>
    public EmailContentBuilder(IEmailTemplateService templates, IReportRenderer renderer)
    {
        _templates = templates;
        _renderer = renderer;
    }

    /// <inheritdoc />
    public async Task<RenderedEmail> BuildAsync(string templateKey, object model, CancellationToken ct = default)
    {
        var template = await _templates.GetAsync(templateKey, ct);

        var subject = await _renderer.RenderAsync(template.Subject, model, ct);
        var text = await _renderer.RenderAsync(template.TextBody, model, ct);
        var html = string.IsNullOrWhiteSpace(template.HtmlBody)
            ? null
            : await _renderer.RenderAsync(template.HtmlBody!, model, ct);

        return new RenderedEmail(subject.Trim(), text, html);
    }
}
