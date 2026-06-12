using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Infrastructure.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Security.Claims;

namespace Ingest.Api.Controllers;

/// <summary>
/// Identity of the currently-authenticated caller. Use this to introspect which account a given
/// API key resolves to and which role/kind it carries — handy for the admin UI sign-in flow and
/// for clients that want to verify their credentials before doing work.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public sealed class MeController : ControllerBase
{
    // Resolved once from the entry assembly's informational version (set from Directory.Build.props).
    // The '+' build-metadata suffix (e.g. a git hash) is trimmed off for display.
    private static readonly string AppVersion =
        typeof(MeController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? typeof(MeController).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private readonly EmailOptions _email;

    /// <summary>Create a new <see cref="MeController"/>.</summary>
    /// <param name="email">Bound email options (only the master switch is read, to expose it to the SPA).</param>
    public MeController(IOptions<EmailOptions> email) => _email = email.Value;

    /// <summary>
    /// Returns the identity claims attached to the API key used for this request.
    /// </summary>
    /// <remarks>
    /// The response includes the account id, machine-style <c>name</c>, friendly <c>label</c>,
    /// <c>role</c> (Service/Operator/Admin), <c>kind</c> (User/Application) and the
    /// <c>emailEnabled</c> feature flag the SPA uses to show/hide the email + notification UI, and
    /// the server <c>version</c> (shown in the dashboard footer).
    /// Clients use the kind to decide whether an interactive UI session is allowed —
    /// Application-kind keys are API-only.
    /// </remarks>
    /// <response code="200">An object with id, name, label, role, kind and emailEnabled for the current account.</response>
    /// <response code="401">No API key was supplied or it does not resolve to an active account.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Get()
    {
        return Ok(new
        {
            id = User.CurrentAccountId(),
            name = User.CurrentAccountName(),
            label = User.FindFirst(AuthConstants.AccountLabelClaim)?.Value,
            role = User.FindFirst(ClaimTypes.Role)?.Value,
            kind = User.FindFirst(AuthConstants.KindClaim)?.Value,
            emailEnabled = _email.Enabled,
            version = AppVersion,
        });
    }
}
