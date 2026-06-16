using System.Security.Claims;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Ingest.Api.Auth;

/// <summary>
/// Shared <c>OnTokenValidated</c> logic for every OIDC provider. Resolves the verified external
/// identity to a pre-linked, enabled <see cref="AccountKind.User"/> account and replaces the
/// principal with the <b>same canonical claim set</b> the API-key handler emits — so every
/// controller, policy and <c>HttpAuditContext</c> keeps working unchanged regardless of which
/// scheme authenticated the request. Unknown, disabled, or non-User identities are rejected.
/// </summary>
public static class SsoSignIn
{
    /// <summary>
    /// Validate the just-authenticated OIDC identity against the account registry and, on success,
    /// rebuild the context's <c>Principal</c> as a canonical Ingest principal.
    /// On any rejection the response is redirected to <c>/login?sso_error=…</c>.
    /// </summary>
    /// <param name="providerId">The provider id this handler serves (e.g. <c>"Microsoft"</c>).</param>
    /// <param name="ctx">The OIDC token-validated context.</param>
    public static async Task HandleTokenValidatedAsync(string providerId, TokenValidatedContext ctx)
    {
        var incoming = ctx.Principal;
        var email = incoming?.FindFirst(ClaimTypes.Email)?.Value
                    ?? incoming?.FindFirst("email")?.Value
                    ?? incoming?.FindFirst("preferred_username")?.Value;
        var subject = incoming?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? incoming?.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(email))
        {
            Reject(ctx, "no_email");
            return;
        }

        var accounts = ctx.HttpContext.RequestServices.GetRequiredService<IAccountRepository>();
        var account = await accounts.GetByExternalLoginAsync(providerId, email, ctx.HttpContext.RequestAborted);

        if (!IsEligibleAccount(account))
        {
            Reject(ctx, "not_linked");
            return;
        }

        // Bind the provider's subject on first successful login so subsequent audits/diagnostics
        // have a stable identifier even if the email later changes upstream.
        var link = account!.ExternalLogins.FirstOrDefault(l =>
            string.Equals(l.Provider, providerId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (link is not null && string.IsNullOrEmpty(link.Subject) && !string.IsNullOrWhiteSpace(subject))
        {
            link.Subject = subject;
            await accounts.UpdateAsync(account, ctx.HttpContext.RequestAborted);
        }

        ctx.Principal = BuildCanonicalPrincipal(account);
    }

    /// <summary>
    /// True when the resolved account may sign in via SSO: it must exist, be live, be enabled, and
    /// be a <see cref="AccountKind.User"/>. Encapsulates the match / unknown / disabled / Application
    /// rejection matrix so it can be exercised without OIDC plumbing.
    /// </summary>
    /// <param name="account">The account resolved from the external identity, or <c>null</c> when none matched.</param>
    public static bool IsEligibleAccount(Account? account) =>
        account is { IsDeleted: false, Enabled: true, Kind: AccountKind.User };

    /// <summary>
    /// Construct the canonical principal (identity, role, optional label and capability claims —
    /// the same set the API-key handler emits), tagged with the cookie scheme.
    /// </summary>
    private static ClaimsPrincipal BuildCanonicalPrincipal(Account account)
    {
        var claims = IngestClaims.Build(account);
        var identity = new ClaimsIdentity(claims, AuthConstants.SessionScheme, ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Fail the OIDC flow and bounce the browser back to the SPA login with an error code.</summary>
    private static void Reject(TokenValidatedContext ctx, string reason)
    {
        ctx.Fail($"SSO rejected: {reason}");
        ctx.Response.Redirect($"/login?sso_error={reason}");
        ctx.HandleResponse();
    }
}
