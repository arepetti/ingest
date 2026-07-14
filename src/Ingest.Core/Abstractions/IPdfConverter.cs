namespace Ingest.Core.Abstractions;

/// <summary>
/// Converts a self-contained HTML document into a PDF. The default implementation delegates to a
/// Gotenberg (headless-Chromium) sidecar over HTTP, but the abstraction keeps the export services
/// testable without a live browser.
/// </summary>
public interface IPdfConverter
{
    /// <summary>Render <paramref name="html"/> to a PDF and return the raw bytes.</summary>
    /// <param name="html">
    /// A complete, self-contained HTML document. It must inline its own CSS and reference no
    /// external assets — the converter is not given network access to this application.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered PDF as a byte array.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="html"/> is null/empty/whitespace.</exception>
    Task<byte[]> HtmlToPdfAsync(string html, CancellationToken ct = default);
}
