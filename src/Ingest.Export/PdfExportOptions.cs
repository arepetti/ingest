namespace Ingest.Export;

/// <summary>
/// Configuration for the PDF export feature. Bound from the <c>Pdf</c> configuration section.
/// PDFs are produced by rendering an HTML document server-side and handing it to a Gotenberg
/// sidecar (a small headless-Chromium HTTP service) for conversion, so the only setting that
/// matters in most deployments is where that sidecar lives.
/// </summary>
public sealed class PdfExportOptions
{
    /// <summary>
    /// Base URL of the Gotenberg service. The converter POSTs to
    /// <c>{GotenbergUrl}/forms/chromium/convert/html</c>. Defaults to the local dev sidecar
    /// (<c>docker compose</c> exposes it in-network as <c>http://gotenberg:3000</c>).
    /// </summary>
    public string GotenbergUrl { get; set; } = "http://localhost:3000";

    /// <summary>Overall timeout, in seconds, for a single HTML-to-PDF conversion request.</summary>
    public int RequestTimeoutSeconds { get; set; } = 60;
}
