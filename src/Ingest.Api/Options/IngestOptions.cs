namespace Ingest.Api.Options;

/// <summary>
/// Binding target for the <c>Ingest</c> configuration section. Carries host-wide knobs that
/// aren't specific to a single subsystem.
/// </summary>
public sealed class IngestOptions
{
    /// <summary>When true, Swagger UI is mounted at <c>/swagger</c>.</summary>
    public bool EnableSwagger { get; set; } = true;

    /// <summary>Default period for <c>GET /api/me/status</c> when no <c>period</c> query-string parameter is supplied.</summary>
    public string DefaultStatusPeriod { get; set; } = "week";

    /// <summary>Default locale offered to clients that have not saved a supported preference.</summary>
    public string DefaultLocale { get; set; } = "en-US";

    /// <summary>Origins allowed by the dev CORS policy. Production deployments should disable the dev policy entirely.</summary>
    public string[] CorsDevOrigins { get; set; } = ["http://localhost:5173"];
}
