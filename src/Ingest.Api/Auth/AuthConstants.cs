namespace Ingest.Api.Auth;

/// <summary>
/// String constants shared by the authentication handler, the policy registration in
/// <c>Program.cs</c>, the controllers' <c>[Authorize]</c> attributes, and the audit-context
/// claim lookups. Kept in one place so changes don't drift.
/// </summary>
public static class AuthConstants
{
    /// <summary>Name of the API-key authentication scheme registered with ASP.NET Core.</summary>
    public const string Scheme = "ApiKey";

    /// <summary>
    /// Name of the cookie scheme issued after a successful SSO login. Only registered when
    /// <c>Sso:EnableSso</c> is on; the policies always name it, so when SSO is off it simply
    /// never authenticates anyone and behaviour is unchanged.
    /// </summary>
    public const string SessionScheme = "IngestSession";

    /// <summary>Builds the per-provider OpenIdConnect scheme name from a provider id (e.g. <c>oidc:Microsoft</c>).</summary>
    public static string OidcScheme(string providerId) => $"oidc:{providerId}";

    /// <summary>Policy that authorises any role at or above <see cref="Core.Entities.AccountRole.Service"/> (i.e. everyone).</summary>
    public const string ServicePolicy = "Service";

    /// <summary>Policy that authorises operators and admins for read-everything endpoints.</summary>
    public const string OperatorPolicy = "OperatorOrAdmin";

    /// <summary>Policy that restricts the endpoint to admin-only operations.</summary>
    public const string AdminPolicy = "Admin";

    /// <summary>Custom claim carrying the calling account's id as a string Guid.</summary>
    public const string AccountIdClaim = "ingest:accountId";

    /// <summary>Custom claim carrying the calling account's machine name.</summary>
    public const string AccountNameClaim = "ingest:accountName";

    /// <summary>Custom claim carrying the calling account's friendly label (only when one is set).</summary>
    public const string AccountLabelClaim = "ingest:accountLabel";

    /// <summary>Custom claim carrying the calling account's <see cref="Core.Entities.AccountKind"/> as a string.</summary>
    public const string KindClaim = "ingest:kind";
}
