using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Security;
using Ingest.Infrastructure.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>
/// Admin management of cross-cutting approval rules — per-service/per-schema approval requirements
/// that apply additively on top of the per-schema and global-default policies. Gated by the
/// <c>Approval:Enabled</c> master switch: when approval is disabled the whole subsystem is inert,
/// so these endpoints return 404.
/// </summary>
[ApiController]
[Route("api/admin/approval/rules")]
[Authorize(Policy = Capabilities.SettingsRead)]
public sealed class AdminApprovalRulesController(IApprovalRulesService rules, IOptions<ApprovalOptions> options) : ControllerBase
{
    private bool Enabled => options.Value.Enabled;

    /// <summary>List every approval rule.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The current rules, newest first.</response>
    /// <response code="404">The approval workflow is disabled.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ApprovalRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var items = await rules.ListAsync(ct);
        return Ok(items.Select(ApprovalRuleDto.From));
    }

    /// <summary>Create a new approval rule.</summary>
    /// <param name="body">The rule to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The created rule.</response>
    /// <response code="400">The rule is invalid (e.g. <c>Required</c> with no required approver).</response>
    /// <response code="404">The approval workflow is disabled.</response>
    [HttpPost]
    [Authorize(Policy = Capabilities.SettingsManage)]
    [ProducesResponseType(typeof(ApprovalRuleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] UpsertApprovalRuleRequest body, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var created = await rules.CreateAsync(body.ToEntity(), ct);
        return CreatedAtAction(nameof(List), new { }, ApprovalRuleDto.From(created));
    }

    /// <summary>Replace an existing approval rule.</summary>
    /// <param name="id">Id of the rule to update.</param>
    /// <param name="body">The new rule contents.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated rule.</response>
    /// <response code="400">The rule is invalid.</response>
    /// <response code="404">The approval workflow is disabled, or the rule doesn't exist.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Capabilities.SettingsManage)]
    [ProducesResponseType(typeof(ApprovalRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertApprovalRuleRequest body, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var updated = await rules.UpdateAsync(id, body.ToEntity(), ct);
        return Ok(ApprovalRuleDto.From(updated));
    }

    /// <summary>Delete an approval rule.</summary>
    /// <param name="id">Id of the rule to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The rule was deleted.</response>
    /// <response code="404">The approval workflow is disabled, or the rule doesn't exist.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Capabilities.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        await rules.DeleteAsync(id, ct);
        return NoContent();
    }
}
