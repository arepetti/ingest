namespace Ingest.Infrastructure.Security;

/// <summary>
/// Binding target for the <c>ApiKey</c> configuration section. Drives both the authentication
/// handler (header name) and the hasher (pepper). Loaded via
/// <see cref="DependencyInjection.AddIngestInfrastructure"/>.
/// </summary>
public sealed class ApiKeyOptions
{
    /// <summary>HTTP header callers send the key in. Case-insensitive on receipt.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Server-wide secret mixed into the key HMAC. MUST be supplied via environment variables,
    /// user-secrets or Key Vault in production — the default literal is a development-only
    /// placeholder.
    /// </summary>
    public string Pepper { get; set; } = "change-me-in-prod";

    /// <summary>
    /// Name of the bootstrap admin account created on first boot when no admin exists. Its API
    /// key is logged once at startup so an operator can capture it; subsequent boots leave the
    /// account alone.
    /// </summary>
    public string BootstrapAdminName { get; set; } = "admin";
}
