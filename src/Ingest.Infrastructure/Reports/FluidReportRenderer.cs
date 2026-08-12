using Fluid;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;

namespace Ingest.Infrastructure.Reports;

/// <summary>
/// Default <see cref="IReportRenderer"/> backed by Fluid. The renderer is sandboxed by design —
/// Fluid does not expose .NET reflection, file I/O, or network access to the template; templates
/// can only read the data envelope we hand them. We register an
/// <see cref="UnsafeMemberAccessStrategy"/> on top of that because the data envelope is a deeply
/// nested anonymous-object graph (schema → values → buckets, …) and registering each shape by
/// hand would create a maintenance burden without adding any safety: the templates only see
/// data the report pipeline has already curated.
/// </summary>
public sealed class FluidReportRenderer : IReportRenderer
{
    private static readonly FluidParser _parser = new();
    private static readonly TemplateOptions _options = BuildOptions();

    /// <inheritdoc />
    public async Task<string> RenderAsync(string template, object model, CancellationToken ct = default)
    {
        if (!_parser.TryParse(template, out var parsed, out var parseError))
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Reports.TemplateParseFailed,
                    $"Liquid template failed to parse: {parseError}",
                    ("detail", parseError),
                    ("engine", "Liquid")),
            });

        ct.ThrowIfCancellationRequested();
        var context = new TemplateContext(model, _options);
        // Fluid's RenderAsync returns ValueTask<string>; wrap it in a Task so we can support
        // cancellation via WaitAsync. The template engine itself doesn't observe the token (no
        // I/O happens inside), so this only bounds the wait time observed by the caller.
        try
        {
            return await parsed.RenderAsync(context).AsTask().WaitAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Render-time failures (filter on a missing field, divide-by-zero, …) come back as
            // ordinary exceptions; surface them through ValidationException so the API layer
            // returns a 400 with a useful message rather than a 500.
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Reports.TemplateRenderFailed,
                    $"Liquid template render failed: {ex.Message}",
                    ("detail", ex.Message),
                    ("engine", "Liquid")),
            });
        }
    }

    private static TemplateOptions BuildOptions()
    {
        var options = new TemplateOptions
        {
            // Unsafe = "read any public member of any object in the envelope". Reports are
            // admin-uploaded against an envelope the server controls, so this is safe in our
            // setting and avoids forcing every entity through a Register<T> call.
            MemberAccessStrategy = new UnsafeMemberAccessStrategy(),
        };
        return options;
    }
}
