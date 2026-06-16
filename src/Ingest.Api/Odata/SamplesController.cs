using Ingest.Api.Auth;
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
    /// <returns>An <see cref="IQueryable{SampleProjection}"/> bound to the underlying store.</returns>
    [HttpGet("odata/samples")]
    [EnableQuery(PageSize = 500, MaxTop = 5000)]
    public IQueryable<SampleProjection> Get() => _samples.AsQueryable();
}
