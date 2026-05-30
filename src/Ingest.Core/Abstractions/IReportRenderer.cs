namespace Ingest.Core.Abstractions;

/// <summary>
/// Compiles a Liquid template body and renders it against an arbitrary data envelope, returning
/// HTML. Implementations are responsible for sandboxing (no file/network access, no arbitrary
/// .NET reflection) and for surfacing template syntax errors as <see cref="Common.ValidationException"/>
/// so they're translated to a 400.
/// </summary>
public interface IReportRenderer
{
    /// <summary>Render <paramref name="template"/> against <paramref name="model"/>.</summary>
    /// <param name="template">Liquid template body (already stripped of YAML front matter).</param>
    /// <param name="model">Arbitrary object graph exposed to the template under top-level Liquid variables.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered HTML.</returns>
    /// <exception cref="Common.ValidationException">The template failed to parse or threw at render time.</exception>
    Task<string> RenderAsync(string template, object model, CancellationToken ct = default);
}
