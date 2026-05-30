using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Read-only catalogue of the schemas the calling service account is allowed to submit against.
/// A schema is visible to a service when it is global, or when its <c>Services</c> list explicitly
/// names the calling account. Use this to discover what to submit before posting samples.
/// </summary>
[ApiController]
[Route("api/schemas")]
[Authorize(Policy = AuthConstants.ServicePolicy)]
public sealed class SchemasController(ISchemaService service) : ControllerBase
{
    /// <summary>List the schemas visible to the calling account.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The visible schemas, including their value definitions and cadences.</response>
    /// <response code="401">No API key was supplied.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<SchemaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListVisible(CancellationToken ct)
    {
        var list = await service.ListVisibleToAsync(User.CurrentAccountId(), ct);
        return Ok(list.Select(SchemaDto.From));
    }

    /// <summary>Fetch a single schema by name, if visible to the caller.</summary>
    /// <param name="name">Machine-style name of the schema.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The schema definition.</response>
    /// <response code="404">No schema with that name, or the caller is not allowed to see it.</response>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(SchemaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVisibleByName(string name, CancellationToken ct)
    {
        var schema = await service.GetVisibleAsync(User.CurrentAccountId(), name, ct);
        return schema is null ? NotFound() : Ok(SchemaDto.From(schema));
    }

    /// <summary>Build an example submission body for a schema, useful for bootstrapping integrations.</summary>
    /// <remarks>
    /// The returned payload follows the canonical submission shape (the same body
    /// <c>POST /api/submissions</c> accepts) with one sample per value defined in the schema.
    /// Defaults are picked per type: empty string for <c>String</c>, <c>0</c> (or the value's
    /// <c>Min</c> if set) for numerics, today (or the value's <c>MinDate</c>) for <c>Date</c>,
    /// and <c>false</c> for <c>Boolean</c>. <em>Validation rules are intentionally ignored</em> —
    /// the example is a starting template, not a guaranteed-valid submission. The same
    /// visibility rule used by <see cref="GetVisibleByName"/> applies.
    /// </remarks>
    /// <param name="name">Machine-style name of the schema.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The example payload.</response>
    /// <response code="404">No schema with that name, or the caller is not allowed to see it.</response>
    [HttpGet("{name}/example")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(SubmissionInput), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExample(string name, CancellationToken ct)
    {
        var example = await service.BuildExampleSubmissionAsync(User.CurrentAccountId(), name, ct);
        return example is null ? NotFound() : Ok(example);
    }
}
