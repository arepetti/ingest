using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Manages registry accounts — both interactive users (admins/operators) and the service
/// credentials used by automated submitters. Admin-only.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
[Authorize(Policy = Capabilities.AccountsRead)]
public sealed class AccountsController(IAccountService service) : ControllerBase
{
    /// <summary>List accounts in a paged form, optionally filtered by kind and/or role.</summary>
    /// <param name="page">1-based page number; defaults to 1 when omitted.</param>
    /// <param name="pageSize">Page size; defaults to 50.</param>
    /// <param name="sort">Sort hint; <c>createdAt</c> returns newest-first, otherwise label+name ascending.</param>
    /// <param name="includeDeleted">When true, soft-deleted accounts are included in the result.</param>
    /// <param name="kind">Restrict the listing to accounts of the given kind (User or Application).</param>
    /// <param name="role">Restrict the listing to accounts holding the given role (Service, Operator or Admin).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of accounts.</response>
    /// <response code="401">No API key supplied.</response>
    /// <response code="403">Caller is not an Admin.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<AccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] bool? includeDeleted,
        [FromQuery] AccountKind? kind,
        [FromQuery] AccountRole? role,
        CancellationToken ct)
    {
        var result = await service.ListAsync(RequestHelpers.ToPageRequest(page, pageSize, sort, includeDeleted), kind, role, ct);
        return Ok(result.Map(AccountDto.From));
    }

    /// <summary>Look up a single account by id.</summary>
    /// <param name="id">Account id.</param>
    /// <param name="includeDeleted">When true, returns soft-deleted accounts; otherwise they appear as 404.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The account.</response>
    /// <response code="404">No account with that id (or it is soft-deleted and <paramref name="includeDeleted"/> is false).</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool? includeDeleted, CancellationToken ct)
    {
        var a = await service.GetAsync(id, includeDeleted ?? false, ct);
        return a is null ? NotFound() : Ok(AccountDto.From(a));
    }

    /// <summary>Create a new account.</summary>
    /// <remarks>
    /// The name must be unique across all accounts, including soft-deleted ones — pick a stable
    /// machine-style identifier and use <c>label</c> for the human-readable name. The account
    /// starts without any API key; rotate one through <c>/api/admin/accounts/{id}/keys</c>.
    /// </remarks>
    /// <param name="req">Account fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The account was created and is returned in the body.</response>
    /// <response code="409">Another account (including a soft-deleted one) already uses the same name.</response>
    [HttpPost]
    [Authorize(Policy = Capabilities.AccountsManage)]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest req, CancellationToken ct)
    {
        var created = await service.CreateAsync(new Account
        {
            Name = req.Name,
            Label = req.Label,
            Description = req.Description,
            Email = req.Email,
            Kind = req.Kind,
            Role = req.Role,
            Enabled = req.Enabled,
            ExternalLogins = ToExternalLogins(req.ExternalLogins) ?? new(),
            Capabilities = req.Capabilities ?? new(),
            AssignedServiceIds = req.AssignedServiceIds ?? new(),
        }, ct);
        return Created($"/api/admin/accounts/{created.Id}", AccountDto.From(created));
    }

    /// <summary>Update the mutable fields on an existing account.</summary>
    /// <remarks>
    /// Name and Kind are immutable once an account exists — only label, description, role and the
    /// enabled flag can be changed. Disabling an account immediately invalidates all of its API
    /// keys for new requests.
    /// </remarks>
    /// <param name="id">Account id.</param>
    /// <param name="req">New values for the mutable fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated account.</response>
    /// <response code="404">No account with that id.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Capabilities.AccountsManage)]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAccountRequest req, CancellationToken ct)
    {
        var updated = await service.UpdateAsync(id, new AccountUpdate(req.Label, req.Description, req.Email, req.Role, req.Enabled, ToExternalLogins(req.ExternalLogins), req.Capabilities, req.AssignedServiceIds), ct);
        return updated is null ? NotFound() : Ok(AccountDto.From(updated));
    }

    /// <summary>Map the wire SSO links onto domain entities. Returns <c>null</c> when none were supplied so the service can tell "leave untouched" from "clear".</summary>
    private static List<ExternalLogin>? ToExternalLogins(List<ExternalLoginDto>? dtos) =>
        dtos?.Select(d => new ExternalLogin { Provider = d.Provider, Email = d.Email }).ToList();

    /// <summary>Soft-delete an account.</summary>
    /// <remarks>
    /// The record is retained for audit but the account no longer authenticates and its API keys
    /// stop working. A subsequent <see cref="Create"/> with the same name will conflict (409),
    /// preventing accidental re-use of the identifier.
    /// </remarks>
    /// <param name="id">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">Account soft-deleted (or already deleted — the call is idempotent).</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Capabilities.AccountsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
