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
    /// Name of the bootstrap admin account created on first boot when no admin exists.
    /// Subsequent boots leave the account alone.
    /// </summary>
    public string BootstrapAdminName { get; set; } = "admin";

    /// <summary>
    /// Optional plaintext API key (<c>{keyId}.{secret}</c>) used for the bootstrap admin account
    /// on first boot. When set, an operator knows the admin key up-front and never has to read it
    /// from the startup logs. When empty (the production default), the bootstrapper falls back to
    /// generating a random key and logging it once. Set this to a long, unique value — anyone who
    /// knows it has full admin access until you rotate it.
    /// </summary>
    public string BootstrapAdminKey { get; set; } = "";
}
