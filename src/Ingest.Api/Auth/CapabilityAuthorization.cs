using Microsoft.AspNetCore.Authorization;

namespace Ingest.Api.Auth;

/// <summary>
/// Authorization requirement satisfied when the calling principal holds a given capability
/// (Phase 2). Capabilities are emitted as <see cref="AuthConstants.CapabilityClaim"/> claims by the
/// authentication handlers, computed from the account's role-default bundle merged with its stored
/// overrides (Admin implicitly holds all).
/// </summary>
public sealed class CapabilityRequirement : IAuthorizationRequirement
{
    /// <summary>Create a requirement for a single capability (e.g. <c>schemas:manage</c>).</summary>
    /// <param name="capability">The capability that must be present on the principal.</param>
    public CapabilityRequirement(string capability) => Capability = capability;

    /// <summary>The required capability string.</summary>
    public string Capability { get; }
}

/// <summary>Grants a <see cref="CapabilityRequirement"/> when the principal carries the matching capability claim.</summary>
public sealed class CapabilityAuthorizationHandler : AuthorizationHandler<CapabilityRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, CapabilityRequirement requirement)
    {
        if (context.User.HasClaim(AuthConstants.CapabilityClaim, requirement.Capability))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
