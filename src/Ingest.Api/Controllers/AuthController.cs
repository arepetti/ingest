using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>
/// Anonymous endpoints that drive the server-side OIDC (BFF) sign-in flow. Every action
/// short-circuits — an empty provider list or a 404 — unless <c>Sso:EnableSso</c> is on and at
/// least one provider is fully configured, so with the flag off no OIDC code path executes and
/// the SPA shows exactly today's API-key-only login. The OIDC callback
/// (<c>/api/auth/callback/{provider}</c>) is owned by the OpenIdConnect middleware, not this
/// controller.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly SsoOptions _sso;

    /// <summary>Create a new <see cref="AuthController"/>.</summary>
    /// <param name="sso">Bound SSO options.</param>
    public AuthController(IOptions<SsoOptions> sso) => _sso = sso.Value;

    /// <summary>List the SSO providers the SPA should render "Continue with …" buttons for.</summary>
    /// <remarks>Returns an empty array when SSO is disabled or no provider is configured.</remarks>
    /// <response code="200">Zero or more enabled providers.</response>
    [HttpGet("providers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Providers()
    {
        var list = _sso.ActiveProviders.Select(p => new
        {
            id = p.Id,
            displayName = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Id : p.DisplayName,
            loginUrl = $"/api/auth/login/{p.Id}",
        });
        return Ok(list);
    }

    /// <summary>Begin the OIDC code flow for the named provider by issuing a challenge.</summary>
    /// <param name="provider">Provider id (matches an <c>Sso:Providers:*:Id</c>).</param>
    /// <param name="returnUrl">Local path to return to after sign-in. Defaults to <c>/</c>; non-local values are ignored.</param>
    /// <response code="404">SSO is disabled, or no provider with that id is configured.</response>
    [HttpGet("login/{provider}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Login(string provider, [FromQuery] string? returnUrl)
    {
        var match = _sso.ActiveProviders.FirstOrDefault(p =>
            string.Equals(p.Id, provider, StringComparison.OrdinalIgnoreCase));
        if (match is null) return NotFound(DiagnosticProblem.NotFound("SSO provider", provider));

        var props = new AuthenticationProperties { RedirectUri = SafeLocalPath(returnUrl) };
        return Challenge(props, AuthConstants.OidcScheme(match.Id));
    }

    /// <summary>Clear the SSO session cookie.</summary>
    /// <response code="204">Signed out (no-op when SSO is disabled or no cookie was present).</response>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        if (_sso.IsActive)
            await HttpContext.SignOutAsync(AuthConstants.SessionScheme);
        return NoContent();
    }

    /// <summary>
    /// Whitelist the post-login redirect to local, single-leading-slash paths to avoid open
    /// redirects. Anything else collapses to the app root.
    /// </summary>
    private static string SafeLocalPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "/";
        if (url.StartsWith('/') && !url.StartsWith("//") && Uri.IsWellFormedUriString(url, UriKind.Relative))
            return url;
        return "/";
    }
}
