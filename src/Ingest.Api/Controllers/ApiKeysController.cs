using Ingest.Api.Auth;
using Ingest.Api.Common;
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
    /// <param name="request">Optional creation options. Supply <c>expiresAt</c> to set an absolute expiry (future-dated, at most two years out) and/or a <c>description</c> note; omit the body for a key that never expires.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">Returns the key metadata and the one-time plaintext.</response>
    /// <response code="400">The supplied expiry is in the past or more than two years in the future, or the description is too long.</response>
    /// <response code="404">No account with that id exists.</response>
    [HttpPost]
    [Authorize(Policy = Capabilities.ApiKeysManage)]
    [ProducesResponseType(typeof(GeneratedApiKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(Guid accountId, [FromBody] GenerateApiKeyRequest? request, CancellationToken ct)
    {
        var generated = await service.RotateAsync(accountId, request?.ExpiresAt, request?.Description, ct);
        return Created(
            $"/api/admin/accounts/{accountId}/keys/{generated.Entity.Id}",
            new GeneratedApiKeyResponse(ApiKeyDto.From(generated.Entity), generated.Plaintext));
    }

    /// <summary>Update an existing key's free-form description.</summary>
    /// <remarks>The description is purely informational and the only mutable field on a key; everything else (expiry, hash) is fixed at creation.</remarks>
    /// <param name="accountId">Account that owns the key.</param>
    /// <param name="keyId">Identifier of the key to annotate.</param>
    /// <param name="request">The new description (trimmed; blank clears it; capped at 200 characters).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated key metadata.</response>
    /// <response code="400">The supplied description is too long.</response>
    /// <response code="404">No such key (or it belongs to a different account).</response>
    [HttpPut("{keyId:guid}")]
    [Authorize(Policy = Capabilities.ApiKeysManage)]
    [ProducesResponseType(typeof(ApiKeyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid accountId, Guid keyId, [FromBody] UpdateApiKeyRequest request, CancellationToken ct)
    {
        var updated = await service.UpdateDescriptionAsync(accountId, keyId, request.Description, ct);
        return updated is null ? NotFound(DiagnosticProblem.NotFound("API key", keyId)) : Ok(ApiKeyDto.From(updated));
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
        return revoked is null ? NotFound(DiagnosticProblem.NotFound("API key", keyId)) : Ok(ApiKeyDto.From(revoked));
    }

    /// <summary>Permanently delete an API key.</summary>
    /// <remarks>
    /// Unlike <see cref="Revoke"/>, this removes the key record entirely. It works whether the key
    /// is still active or already revoked, so callers can tidy up old credentials. Deleting an
    /// active key invalidates it immediately, just like a revoke.
    /// </remarks>
    /// <param name="accountId">Account that owns the key.</param>
    /// <param name="keyId">Identifier of the key to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The key was deleted.</response>
    /// <response code="404">No such key (or it belongs to a different account).</response>
    [HttpDelete("{keyId:guid}")]
    [Authorize(Policy = Capabilities.ApiKeysManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid accountId, Guid keyId, CancellationToken ct)
    {
        var deleted = await service.DeleteAsync(accountId, keyId, ct);
        return deleted ? NoContent() : NotFound(DiagnosticProblem.NotFound("API key", keyId));
    }
}
