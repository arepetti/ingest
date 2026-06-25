using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Approvals;
using Ingest.Infrastructure.Email;
using Ingest.Infrastructure.Integrations;
using Ingest.Infrastructure.Webhooks;
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
    private readonly WebhookOptions _webhooks;
    private readonly IntegrationOptions _integrations;
    private readonly ApprovalOptions _approval;
    private readonly IApprovalSettingsService _approvalSettings;

    /// <summary>Create a new <see cref="MeController"/>.</summary>
    /// <param name="email">Bound email options (only the master switch is read, to expose it to the SPA).</param>
    /// <param name="webhooks">Bound webhook options (only the master switch is read, to expose it to the SPA).</param>
    /// <param name="integrations">Bound integration options (only the master switch is read, to expose it to the SPA).</param>
    /// <param name="approval">Bound approval options (only the master switch is read, to expose it to the SPA).</param>
    /// <param name="approvalSettings">Global default approval policy provider; used to expose whether the default gates submissions.</param>
    public MeController(IOptions<EmailOptions> email, IOptions<WebhookOptions> webhooks, IOptions<IntegrationOptions> integrations, IOptions<ApprovalOptions> approval, IApprovalSettingsService approvalSettings)
    {
        _email = email.Value;
        _webhooks = webhooks.Value;
        _integrations = integrations.Value;
        _approval = approval.Value;
        _approvalSettings = approvalSettings;
    }

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
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        // Surface whether the *global default* policy gates submissions, so the SPA can flag schemas
        // set to "use the global default" without needing the (admin-only) full policy. Only read it
        // when the workflow is on; otherwise it's trivially false.
        var approvalDefaultRequired = _approval.Enabled
            && (await _approvalSettings.GetDefaultAsync(ct)).Mode == ApprovalMode.Required;

        // The effective capability set drives every UI gate in the SPA. It's already materialised on
        // the principal as one claim per capability by the auth handlers (role default bundle merged
        // with per-account overrides; Admin implicitly holds all), so we just project the claims.
        var capabilities = User.FindAll(AuthConstants.CapabilityClaim).Select(c => c.Value).ToArray();

        // Per-service scope (empty = unrestricted). Lets the SPA badge the active scope and hint to
        // the operator that they only see a subset of services. Admins never carry these.
        var assignedServiceIds = User.CurrentAssignedServiceIds().Select(id => id.ToString()).ToArray();

        return Ok(new
        {
            id = User.CurrentAccountId(),
            name = User.CurrentAccountName(),
            label = User.FindFirst(AuthConstants.AccountLabelClaim)?.Value,
            role = User.FindFirst(ClaimTypes.Role)?.Value,
            kind = User.FindFirst(AuthConstants.KindClaim)?.Value,
            capabilities,
            assignedServiceIds,
            emailEnabled = _email.Enabled,
            webhooksEnabled = _webhooks.Enabled,
            integrationsEnabled = _integrations.Enabled,
            approvalEnabled = _approval.Enabled,
            approvalDefaultRequired,
            version = AppVersion,
        });
    }
}
