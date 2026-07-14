using System.Net.Http.Headers;
using System.Text;
using Ingest.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace Ingest.Infrastructure.Export;

/// <summary>
/// <see cref="IPdfConverter"/> backed by a Gotenberg sidecar. Posts the HTML to Gotenberg's
/// Chromium route (<c>/forms/chromium/convert/html</c>) as multipart form data and streams the
/// resulting PDF back. Gotenberg requires the primary document to be named <c>index.html</c>.
/// </summary>
public sealed class GotenbergPdfConverter : IPdfConverter
{
    private readonly HttpClient _http;
    private readonly PdfExportOptions _options;

    /// <summary>Create a new <see cref="GotenbergPdfConverter"/>.</summary>
    /// <param name="http">Typed HTTP client (resilience handlers are applied by the host).</param>
    /// <param name="options">PDF export options, primarily the Gotenberg base URL.</param>
    public GotenbergPdfConverter(HttpClient http, IOptions<PdfExportOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<byte[]> HtmlToPdfAsync(string html, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new ArgumentException("HTML must not be empty.", nameof(html));

        var baseUrl = (_options.GotenbergUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("PDF export is not configured (Pdf:GotenbergUrl is empty).");

        var url = $"{baseUrl}/forms/chromium/convert/html";

        using var form = new MultipartFormDataContent();
        var doc = new ByteArrayContent(Encoding.UTF8.GetBytes(html));
        doc.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        // The form field name is "files"; the file name must be index.html for Gotenberg to treat
        // it as the document to render.
        form.Add(doc, "files", "index.html");
        // Keep the shaded section headers / badges we style with background colours.
        form.Add(new StringContent("true"), "printBackground");
        form.Add(new StringContent("0.4"), "marginTop");
        form.Add(new StringContent("0.4"), "marginBottom");
        form.Add(new StringContent("0.5"), "marginLeft");
        form.Add(new StringContent("0.5"), "marginRight");

        using var response = await _http.PostAsync(url, form, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadAsync(response, ct);
            throw new InvalidOperationException(
                $"PDF conversion failed ({(int)response.StatusCode} {response.ReasonPhrase}). {body}".Trim());
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            return text.Length <= 500 ? text : text[..500];
        }
        catch
        {
            return string.Empty;
        }
    }
}
