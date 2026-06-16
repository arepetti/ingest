using System.Security.Claims;
using Ingest.Api.Auth;
using Ingest.Api.Models;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Microsoft.AspNetCore.Http;

namespace Ingest.Api.Common;

/// <summary>
/// Cross-cutting helpers shared by controllers: pulls common claims off the principal, builds
/// PageRequests from query-string params, and translates paged repository results into the API DTO.
/// </summary>
public static class RequestHelpers
{
    /// <summary>Read the calling account's id from its claims.</summary>
    /// <param name="user">The current principal.</param>
    /// <returns>The account id.</returns>
    /// <exception cref="UnauthorizedAccessException">No valid <see cref="AuthConstants.AccountIdClaim"/> is present.</exception>
    public static Guid CurrentAccountId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(AuthConstants.AccountIdClaim);
        return Guid.TryParse(raw, out var g) ? g : throw new UnauthorizedAccessException();
    }

    /// <summary>Header the SPA sets to mark its writes as manual (web console) submissions.</summary>
    public const string SourceHeader = "X-Ingest-Source";

    /// <summary>
    /// Resolve the submission source from the request. The admin SPA sends
    /// <c>X-Ingest-Source: manual</c> on its writes; direct API callers omit it and default to
    /// <see cref="SubmissionSource.Api"/>. Drives the source-aware approval policy.
    /// </summary>
    /// <param name="request">The current request.</param>
    public static SubmissionSource ResolveSource(this HttpRequest request) =>
        string.Equals(request.Headers[SourceHeader].ToString(), "manual", StringComparison.OrdinalIgnoreCase)
            ? SubmissionSource.Manual
            : SubmissionSource.Api;

    /// <summary>Read the calling account's machine name from its claims, if present.</summary>
    /// <param name="user">The current principal.</param>
    /// <returns>The account name, or null when the claim is absent (e.g. anonymous request).</returns>
    public static string? CurrentAccountName(this ClaimsPrincipal user) =>
        user.FindFirstValue(AuthConstants.AccountNameClaim);

    /// <summary>Compose a <see cref="PageRequest"/> from the conventional query-string parameters.</summary>
    /// <param name="page">1-based page number; defaults to 1 when null.</param>
    /// <param name="pageSize">Page size; defaults to 50 when null.</param>
    /// <param name="sort">Optional sort hint passed through unchanged.</param>
    /// <param name="includeDeleted">Whether to include soft-deleted rows; defaults to false.</param>
    public static PageRequest ToPageRequest(int? page, int? pageSize, string? sort, bool? includeDeleted) =>
        new(page ?? 1, pageSize ?? 50, sort, includeDeleted ?? false);

    /// <summary>Project a paged repository result into its wire-format counterpart.</summary>
    /// <typeparam name="TIn">Domain item type.</typeparam>
    /// <typeparam name="TOut">DTO item type.</typeparam>
    /// <param name="r">Source result.</param>
    /// <param name="map">Per-item projection (typically <c>FooDto.From</c>).</param>
    public static PagedResponse<TOut> Map<TIn, TOut>(this PagedResult<TIn> r, Func<TIn, TOut> map)
        => new(r.Items.Select(map).ToList(), r.Total, r.Page, r.PageSize);
}
