using System.Security.Claims;
using Ingest.Api.Auth;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
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

    /// <summary>
    /// The set of service-account ids the caller is scoped to (from the <see cref="AuthConstants.AssignedServiceClaim"/>
    /// claims). An <b>empty</b> result means the caller is <i>unrestricted</i> — it sees every service.
    /// A non-empty result confines every cross-service read to those ids.
    /// </summary>
    /// <param name="user">The current principal.</param>
    public static IReadOnlyList<Guid> CurrentAssignedServiceIds(this ClaimsPrincipal user)
    {
        var ids = new List<Guid>();
        foreach (var claim in user.FindAll(AuthConstants.AssignedServiceClaim))
            if (Guid.TryParse(claim.Value, out var g))
                ids.Add(g);
        return ids;
    }

    /// <summary>
    /// True when the caller may see data belonging to <paramref name="serviceId"/>: either it is
    /// unrestricted (no assigned-service scope) or the id is within its allowlist.
    /// </summary>
    /// <param name="user">The current principal.</param>
    /// <param name="serviceId">The owning service account to test.</param>
    public static bool CanAccessService(this ClaimsPrincipal user, Guid serviceId)
    {
        var scope = user.CurrentAssignedServiceIds();
        return scope.Count == 0 || scope.Contains(serviceId);
    }

    /// <summary>
    /// Combine a caller-supplied service filter with the caller's assigned-service scope into the
    /// effective list of service ids a cross-service query should be confined to.
    /// <list type="bullet">
    /// <item>Returns <c>null</c> when there is no restriction at all (unrestricted caller, no explicit filter) — query every service.</item>
    /// <item>Returns the requested ids when the caller is unrestricted but asked for a subset.</item>
    /// <item>Returns the scope (intersected with any request) when the caller is scoped.</item>
    /// </list>
    /// Sets <paramref name="empty"/> to <c>true</c> when a scoped caller asked only for services
    /// outside its scope: the intersection is empty and the caller must return an empty result
    /// rather than querying (an empty id list must never be treated as "all").
    /// </summary>
    /// <param name="user">The current principal.</param>
    /// <param name="requested">The service ids the caller explicitly asked to filter by, if any.</param>
    /// <param name="empty">Set true when the resolved filter is empty because the request fell entirely outside the caller's scope.</param>
    public static IReadOnlyList<Guid>? ResolveServiceFilter(this ClaimsPrincipal user, IReadOnlyList<Guid>? requested, out bool empty)
    {
        empty = false;
        var scope = user.CurrentAssignedServiceIds();
        var hasRequest = requested is { Count: > 0 };

        if (scope.Count == 0)
            return hasRequest ? requested : null;

        if (!hasRequest)
            return scope;

        var intersection = requested!.Where(scope.Contains).Distinct().ToList();
        if (intersection.Count == 0) empty = true;
        return intersection;
    }

    /// <summary>
    /// Parse the <c>omit</c> query parameter on the validate endpoints into a set of pipeline toggles.
    /// Accepts a comma-separated, case-insensitive list of check names to skip; today only
    /// <c>cadence</c> is recognised (the parser is intentionally extensible — add a token here and a
    /// matching flag on <see cref="SubmissionValidationOptions"/> per future check). A blank/absent
    /// value runs the full pipeline.
    /// </summary>
    /// <param name="omit">The raw <c>omit</c> query value, e.g. <c>cadence</c>.</param>
    /// <returns>The resolved options.</returns>
    /// <exception cref="ValidationException">An unrecognised token was supplied.</exception>
    public static SubmissionValidationOptions ParseValidationOptions(string? omit)
    {
        if (string.IsNullOrWhiteSpace(omit)) return SubmissionValidationOptions.Full;

        var skipCadence = false;
        foreach (var raw in omit.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(raw, "cadence", StringComparison.OrdinalIgnoreCase))
                skipCadence = true;
            else
                throw new ValidationException(new[] { $"Unknown 'omit' value '{raw}'. Supported values: cadence." });
        }

        return new SubmissionValidationOptions(SkipCadence: skipCadence);
    }

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
