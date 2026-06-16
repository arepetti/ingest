using Ingest.Api.Auth;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Security;
using Ingest.Infrastructure.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>
/// Admin management of the server-wide default approval policy that schemas defer to via
/// <c>UseGlobalDefault</c>. Gated by the <c>Approval:Enabled</c> master switch — when approval is
/// disabled the whole subsystem is inert, so these endpoints return 404.
/// </summary>
[ApiController]
[Route("api/admin/approval/settings")]
[Authorize(Policy = Capabilities.SettingsRead)]
public sealed class AdminApprovalController(IApprovalSettingsService settings, IOptions<ApprovalOptions> options) : ControllerBase
{
    private bool Enabled => options.Value.Enabled;

    /// <summary>Fetch the global default approval policy.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The current global default policy.</response>
    /// <response code="404">The approval workflow is disabled.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApprovalPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var policy = await settings.GetDefaultAsync(ct);
        return Ok(ApprovalPolicyDto.From(policy));
    }

    /// <summary>Replace the global default approval policy.</summary>
    /// <param name="body">The new policy. <c>Mode</c> may be <c>None</c> or <c>Required</c> (not <c>UseGlobalDefault</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated policy.</response>
    /// <response code="400">The policy is invalid (e.g. <c>Required</c> with no required approver).</response>
    /// <response code="404">The approval workflow is disabled.</response>
    [HttpPut]
    [Authorize(Policy = Capabilities.SettingsManage)]
    [ProducesResponseType(typeof(ApprovalPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromBody] ApprovalPolicyDto body, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var updated = await settings.UpdateDefaultAsync(body.ToEntity(), ct);
        return Ok(ApprovalPolicyDto.From(updated));
    }
}
