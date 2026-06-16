using Ingest.Api.Auth;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Manages the API keys attached to a single account. The plaintext key is generated server-side
/// and returned to the caller exactly once — only its hash and a short prefix are persisted, so
/// lost keys cannot be recovered and must be rotated.
/// </summary>
[ApiController]
[Route("api/admin/accounts/{accountId:guid}/keys")]
[Authorize(Policy = Capabilities.ApiKeysRead)]
public sealed class ApiKeysController(IApiKeyService service) : ControllerBase
{
    /// <summary>List the keys (metadata only) attached to an account.</summary>
    /// <param name="accountId">Account that owns the keys.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Key metadata. The plaintext is never returned.</response>
    /// <response code="404">No account with that id exists.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ApiKeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid accountId, CancellationToken ct)
    {
        var list = await service.ListAsync(accountId, ct);
        return Ok(list.Select(ApiKeyDto.From));
    }

    /// <summary>Generate a brand-new API key for an account.</summary>
    /// <remarks>
    /// The response includes the plaintext key under the <c>plaintext</c> field — this is the
    /// only time it will ever be returned. Copy it to a safe place immediately. The existing keys
    /// for the account are left untouched; call <see cref="Revoke"/> separately if you want to
    /// invalidate them.
    /// </remarks>
    /// <param name="accountId">Account to attach the new key to.</param>
    /// <param name="request">Optional creation options. Supply <c>expiresAt</c> to set an absolute expiry (future-dated, at most two years out); omit the body for a key that never expires.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">Returns the key metadata and the one-time plaintext.</response>
    /// <response code="400">The supplied expiry is in the past or more than two years in the future.</response>
    /// <response code="404">No account with that id exists.</response>
    [HttpPost]
    [Authorize(Policy = Capabilities.ApiKeysManage)]
    [ProducesResponseType(typeof(GeneratedApiKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(Guid accountId, [FromBody] GenerateApiKeyRequest? request, CancellationToken ct)
    {
        var generated = await service.RotateAsync(accountId, request?.ExpiresAt, ct);
        return Created(
            $"/api/admin/accounts/{accountId}/keys/{generated.Entity.Id}",
            new GeneratedApiKeyResponse(ApiKeyDto.From(generated.Entity), generated.Plaintext));
    }

    /// <summary>Revoke an existing API key.</summary>
    /// <remarks>The operation is idempotent — re-revoking an already-revoked key returns the same metadata.</remarks>
    /// <param name="accountId">Account that owns the key.</param>
    /// <param name="keyId">Identifier of the key to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The revoked key's metadata.</response>
    /// <response code="404">No such key (or it belongs to a different account).</response>
    [HttpPost("{keyId:guid}/revoke")]
    [Authorize(Policy = Capabilities.ApiKeysManage)]
    [ProducesResponseType(typeof(ApiKeyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid accountId, Guid keyId, CancellationToken ct)
    {
        var revoked = await service.RevokeAsync(accountId, keyId, ct);
        return revoked is null ? NotFound() : Ok(ApiKeyDto.From(revoked));
    }
}
