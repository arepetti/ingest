using System.Security.Claims;
using Ingest.Core.Entities;
using Ingest.Core.Security;

namespace Ingest.Api.Auth;

/// <summary>
/// Builds the canonical Ingest claim set for an authenticated account. Both the API-key handler
/// and the SSO sign-in path call this so every controller, policy and <c>HttpAuditContext</c> sees
/// an identical principal regardless of which scheme authenticated the request.
/// </summary>
public static class IngestClaims
{
    /// <summary>
    /// Produce the identity, role and capability claims for <paramref name="account"/>. One
    /// <see cref="AuthConstants.CapabilityClaim"/> is emitted per effective capability (Admin
    /// implicitly holds the entire catalogue, see <see cref="RoleCapabilities.Effective(Account)"/>).
    /// </summary>
    /// <param name="account">The authenticated account.</param>
    public static List<Claim> Build(Account account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.Name),
            new(AuthConstants.AccountIdClaim, account.Id.ToString()),
            new(AuthConstants.AccountNameClaim, account.Name),
            new(AuthConstants.KindClaim, account.Kind.ToString()),
            new(ClaimTypes.Role, account.Role.ToString()),
        };

        if (!string.IsNullOrEmpty(account.Label))
            claims.Add(new Claim(AuthConstants.AccountLabelClaim, account.Label));

        foreach (var capability in RoleCapabilities.Effective(account))
            claims.Add(new Claim(AuthConstants.CapabilityClaim, capability));

        // Per-service scope (empty = unrestricted). Admins always see everything, so they never
        // carry these even if an allowlist was somehow stored against the account.
        if (account.Role != AccountRole.Admin)
            foreach (var serviceId in account.AssignedServiceIds.Distinct())
                claims.Add(new Claim(AuthConstants.AssignedServiceClaim, serviceId.ToString()));

        return claims;
    }
}
