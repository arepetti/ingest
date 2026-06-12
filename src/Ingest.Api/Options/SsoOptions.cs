namespace Ingest.Api.Options;

/// <summary>
/// Binding target for the <c>Sso</c> configuration section. The whole single-sign-on feature is
/// gated by <see cref="EnableSso"/>: when it is <c>false</c> (the default) no cookie/OIDC schemes
/// are registered, no auth endpoints execute, and <c>GET /api/auth/providers</c> returns an empty
/// list — the running system behaves exactly as the API-key-only build.
/// </summary>
public sealed class SsoOptions
{
    /// <summary>Master switch. When false (default) the entire SSO feature is inert.</summary>
    public bool EnableSso { get; set; } = false;

    /// <summary>Name of the session cookie issued after a successful SSO login.</summary>
    public string CookieName { get; set; } = "ingest.session";

    /// <summary>Configured identity providers. Only entries that are fully configured activate (see <see cref="SsoProviderOptions.IsConfigured"/>).</summary>
    public List<SsoProviderOptions> Providers { get; set; } = new();

    /// <summary>
    /// True when the feature is switched on AND at least one provider is fully configured.
    /// Everything in <c>Program.cs</c> and <c>AuthController</c> keys off this so a half-filled
    /// config can't accidentally light up the feature.
    /// </summary>
    public bool IsActive => EnableSso && Providers.Any(p => p.IsConfigured);

    /// <summary>The configured providers, in declaration order. Empty when the feature is off.</summary>
    public IEnumerable<SsoProviderOptions> ActiveProviders =>
        EnableSso ? Providers.Where(p => p.IsConfigured) : Enumerable.Empty<SsoProviderOptions>();
}

/// <summary>One OIDC provider entry under <c>Sso:Providers</c>.</summary>
public sealed class SsoProviderOptions
{
    /// <summary>Stable id; drives the route (<c>/api/auth/login/{Id}</c>) and the per-provider scheme name. E.g. <c>"Microsoft"</c> or <c>"Google"</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Friendly name rendered on the SPA's "Continue with …" button. Falls back to <see cref="Id"/> when blank.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>OIDC authority (issuer). E.g. <c>https://login.microsoftonline.com/&lt;tenant&gt;/v2.0</c> or <c>https://accounts.google.com</c>.</summary>
    public string Authority { get; set; } = "";

    /// <summary>OAuth client id. Blank in <c>appsettings.json</c> by design — supplied from a secret source per environment.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>OAuth client secret. Blank in <c>appsettings.json</c> by design — supplied from a secret source per environment.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Scopes requested at the authorize endpoint. Defaults to the minimal OIDC set needed to resolve a verified email.</summary>
    public List<string> Scopes { get; set; } = new() { "openid", "profile", "email" };

    /// <summary>True only when id, authority, client id and secret are all present — i.e. the provider can actually complete a code flow.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Id) &&
        !string.IsNullOrWhiteSpace(Authority) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
