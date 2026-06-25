using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Ingest.Api.Odata;

/// <summary>
/// OData feed over the flat <see cref="SampleProjection"/> store. Designed for PowerBI and other
/// generic OData clients: <c>$filter</c>, <c>$select</c>, <c>$orderby</c>, <c>$top</c>/<c>$skip</c>
/// and <c>$count</c> are all supported, with a server-side page size of 500 and a hard cap of
/// 5000 per request. Requires the <c>query:read</c> capability.
/// </summary>
[Authorize(Policy = Capabilities.QueryRead)]
public sealed class SamplesController : ODataController
{
    private readonly ISampleRepository _samples;

    /// <summary>Create a new <see cref="SamplesController"/>.</summary>
    /// <param name="samples">Repository exposing the queryable sample projection.</param>
    public SamplesController(ISampleRepository samples)
    {
        _samples = samples;
    }

    /// <summary>Return the queryable sample projection. OData translates the URL into the final filter.</summary>
    /// <remarks>
    /// A scoped caller (one carrying an assigned-service allowlist) has the feed pre-filtered to its
    /// services <i>before</i> OData composes the client's <c>$filter</c>, so a Power BI report can
    /// never read another service's rows regardless of the query it sends. Unrestricted callers see
    /// the whole store as before.
    /// </remarks>
    /// <returns>An <see cref="IQueryable{SampleProjection}"/> bound to the underlying store.</returns>
    [HttpGet("odata/samples")]
    [EnableQuery(PageSize = 500, MaxTop = 5000)]
    public IQueryable<SampleProjection> Get()
    {
        var query = _samples.AsQueryable();
        var scope = User.CurrentAssignedServiceIds();
        if (scope.Count > 0)
        {
            // A List (rather than HashSet) keeps the MongoDB LINQ provider on its $in translation.
            var allowed = scope.ToList();
            query = query.Where(s => allowed.Contains(s.ServiceAccountId));
        }
        return query;
    }
}
