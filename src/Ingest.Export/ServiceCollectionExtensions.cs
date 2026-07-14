using Ingest.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ingest.Export;

/// <summary>
/// Registers the export services (PDF, and later XLSX) on a service collection. The caller is
/// responsible for binding <see cref="PdfExportOptions"/> (the composition root already has the
/// configuration packages), so this method only wires the services themselves.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Register the export services on <paramref name="services"/>.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddIngestExport(this IServiceCollection services)
    {
        // PDF export: an HTML template rendered by Fluid, converted to PDF by a Gotenberg sidecar
        // over a typed HttpClient. The client picks up the Aspire resilience handlers from
        // ServiceDefaults; its overall timeout is driven by Pdf:RequestTimeoutSeconds.
        services.AddHttpClient<IPdfConverter, GotenbergPdfConverter>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<PdfExportOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(opts.RequestTimeoutSeconds, 5, 600));
        });
        services.AddScoped<IPdfExportService, PdfExportService>();

        return services;
    }
}
