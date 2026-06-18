using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Ingest.Api.Auth;

/// <summary>
/// Validates the bearer token the Bot Framework connector sends on inbound activities to the Teams
/// messaging endpoint. The token is signed by Microsoft; we verify it against the Bot Framework
/// OpenID metadata and require the audience to equal the configured bot App ID. This is the
/// canonical "self-host the messaging endpoint" check (the same one the Bot Builder SDK performs)
/// implemented with the IdentityModel primitives already pulled in by the OpenID Connect package.
/// </summary>
public sealed class TeamsBotAuthenticator
{
    // Bot Framework (public Azure cloud) channel metadata + issuer.
    private const string MetadataUrl = "https://login.botframework.com/v1/.well-known/openidconfiguration";
    private const string Issuer = "https://api.botframework.com";

    private readonly ConfigurationManager<OpenIdConnectConfiguration> _config =
        new(MetadataUrl, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever());

    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>
    /// Validate the <c>Authorization: Bearer …</c> header against the Bot Framework signing keys and
    /// the expected audience. Returns <c>true</c> only when the token is well-formed, in date, signed
    /// by Microsoft, and issued for <paramref name="appId"/>.
    /// </summary>
    public async Task<bool> ValidateAsync(string? authorizationHeader, string appId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(appId))
            return false;

        var token = authorizationHeader["Bearer ".Length..].Trim();

        try
        {
            var config = await _config.GetConfigurationAsync(ct);
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = appId,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5),
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuerSigningKey = true,
            };

            var result = await _handler.ValidateTokenAsync(token, parameters);
            return result.IsValid;
        }
        catch
        {
            return false;
        }
    }
}
