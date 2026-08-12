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
/// Administrative CRUD over the schema catalogue plus a read-only history endpoint used by the
/// admin dashboard's charts. Reads require <c>schemas:read</c>; mutations require <c>schemas:manage</c>.
/// </summary>
[ApiController]
[Route("api/admin/schemas")]
[Authorize(Policy = Capabilities.SchemasRead)]
public sealed class AdminSchemasController(ISchemaService service, IPdfExportService pdfExport) : ControllerBase
{
    /// <summary>List schemas in paged form.</summary>
    /// <param name="page">1-based page number; defaults to 1.</param>
    /// <param name="pageSize">Page size; defaults to 50.</param>
    /// <param name="sort">Sort hint; <c>createdAt</c> returns newest-first, otherwise label+name ascending.</param>
    /// <param name="includeDeleted">When true, soft-deleted schemas are included.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of schemas.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<SchemaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] bool? includeDeleted,
        CancellationToken ct)
    {
        var result = await service.ListAsync(RequestHelpers.ToPageRequest(page, pageSize, sort, includeDeleted), ct);
        return Ok(result.Map(SchemaDto.From));
    }

    /// <summary>Look up a schema by id.</summary>
    /// <param name="id">Schema id.</param>
    /// <param name="includeDeleted">When true, returns soft-deleted schemas; otherwise they appear as 404.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The schema.</response>
    /// <response code="404">No schema with that id (or it is soft-deleted and <paramref name="includeDeleted"/> is false).</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SchemaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool? includeDeleted, CancellationToken ct)
    {
        var s = await service.GetByIdAsync(id, includeDeleted ?? false, ct);
        return s is null ? NotFound(DiagnosticProblem.NotFound("Schema", id)) : Ok(SchemaDto.From(s));
    }

    /// <summary>Create a new schema.</summary>
    /// <remarks>
    /// The <c>name</c> must be globally unique (even across soft-deleted schemas). Each value
    /// inside the schema gets its own cadence and validators; the schema-level validators run
    /// once per submission and can compare values to each other (for example, a sanity check
    /// like <c>peak &gt;= average</c>).
    /// </remarks>
    /// <param name="req">Schema fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The created schema.</response>
    /// <response code="409">A schema with the same name already exists.</response>
    [HttpPost]
    [Authorize(Policy = Capabilities.SchemasManage)]
    [ProducesResponseType(typeof(SchemaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] UpsertSchemaRequest req, CancellationToken ct)
    {
        var created = await service.CreateAsync(BuildSchema(new Schema { Name = req.Name }, req), ct);
        return Created($"/api/admin/schemas/{created.Id}", SchemaDto.From(created));
    }

    /// <summary>Replace an existing schema.</summary>
    /// <remarks>
    /// The schema's <c>modifiable</c> flag (and the per-value <c>modifiable</c> flag) gate which
    /// updates are accepted — service requirements may have made some fields immutable. Removing
    /// a value from an existing schema is allowed; historical submissions retain their original
    /// shape.
    /// </remarks>
    /// <param name="id">Schema id.</param>
    /// <param name="req">New schema fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated schema.</response>
    /// <response code="404">No schema with that id.</response>
    /// <response code="409">The new name conflicts with another schema.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Capabilities.SchemasManage)]
    [ProducesResponseType(typeof(SchemaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertSchemaRequest req, CancellationToken ct)
    {
        var updated = await service.UpdateAsync(id, BuildSchema(new Schema { Name = req.Name }, req), ct);
        return updated is null ? NotFound(DiagnosticProblem.NotFound("Schema", id)) : Ok(SchemaDto.From(updated));
    }

    /// <summary>Soft-delete a schema.</summary>
    /// <remarks>
    /// The schema disappears from <c>/api/schemas</c> immediately. Historical submissions remain
    /// queryable via the OData/Query endpoints because they store a denormalised snapshot of the
    /// schema name and unit, not a foreign key.
    /// </remarks>
    /// <param name="id">Schema id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">Schema deleted (or already deleted — call is idempotent).</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Capabilities.SchemasManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Create an independent copy of an existing schema.</summary>
    /// <remarks>
    /// The clone keeps every overwritable field (values, layout, version, audience, validation
    /// rules, flags) and picks a unique name by appending <c>_copy</c>, then <c>_copy_2</c>,
    /// <c>_copy_3</c>, … until no collision is found. Audit fields are reset and
    /// <c>versionModifiedAt</c> is stamped with the current time so the clone behaves like a
    /// fresh schema for the SPA's "New" tag.
    /// </remarks>
    /// <param name="id">Source schema id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The created clone.</response>
    /// <response code="404">No schema with that id.</response>
    [HttpPost("{id:guid}/clone")]
    [Authorize(Policy = Capabilities.SchemasManage)]
    [ProducesResponseType(typeof(SchemaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Clone(Guid id, CancellationToken ct)
    {
        var clone = await service.CloneAsync(id, ct);
        return clone is null
            ? NotFound(DiagnosticProblem.NotFound("Schema", id))
            : Created($"/api/admin/schemas/{clone.Id}", SchemaDto.From(clone));
    }

    /// <summary>Returns an aggregated history of every submission for a schema, grouped by cadence.</summary>
    /// <remarks>
    /// For each numeric value in the schema the response includes a series of buckets aligned to
    /// that value's cadence; each bucket carries min/max/average and a sample count. The admin UI
    /// uses this to render time-series charts.
    /// </remarks>
    /// <param name="name">Schema name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Aggregated history; empty arrays where no data has been submitted.</response>
    /// <response code="404">No schema with that name.</response>
    [HttpGet("{name}/history")]
    [ProducesResponseType(typeof(SchemaHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(string name, CancellationToken ct)
    {
        var history = await service.GetHistoryAsync(name, ct);
        return history is null
            ? NotFound(DiagnosticProblem.NotFound("Schema", name))
            : Ok(SchemaHistoryMapper.ToDto(history));
    }

    /// <summary>Export a schema's full field specification as a PDF.</summary>
    /// <remarks>
    /// The document lists every field the schema defines — regardless of <c>visibleIf</c> /
    /// <c>enabledIf</c> gating — laid out in the same structure as the read-only submission view,
    /// but with no data. Validation/calculation rules are rendered in plain English.
    /// </remarks>
    /// <param name="name">Schema name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The rendered PDF.</response>
    /// <response code="404">No schema with that name.</response>
    [HttpGet("{name}/export.pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportPdf(string name, CancellationToken ct)
    {
        var doc = await pdfExport.ExportSchemaAsync(name, ct);
        return doc is null
            ? NotFound(DiagnosticProblem.NotFound("Schema", name))
            : File(doc.Content, "application/pdf", doc.FileName);
    }

    /// <summary>Page through a schema's saved version snapshots, newest change first.</summary>
    /// <remarks>
    /// One snapshot is written on every schema save (create or update). Each row records who saved
    /// it, when, the version before/after, whether the version was bumped, whether the schema was
    /// Published (Enabled) or Draft, and the submission count at that point. The schema body itself
    /// is omitted here — fetch a single entry to get the full snapshot.
    /// </remarks>
    /// <param name="name">Schema name.</param>
    /// <param name="page">1-based page number; defaults to 1.</param>
    /// <param name="pageSize">Page size; defaults to 50.</param>
    /// <param name="from">Lower bound on the change date (inclusive).</param>
    /// <param name="to">Upper bound on the change date (exclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of version-history entries.</response>
    [HttpGet("{name}/version-history")]
    [ProducesResponseType(typeof(PagedResponse<SchemaVersionHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersionHistory(
        string name,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await service.GetVersionHistoryAsync(
            name, RequestHelpers.ToPageRequest(page, pageSize, null, false), from, to, ct);
        return Ok(result.Map(SchemaVersionHistoryDto.From));
    }

    /// <summary>Fetch a single version-history entry, including the full schema snapshot.</summary>
    /// <param name="name">Schema name the entry belongs to.</param>
    /// <param name="entryId">Snapshot id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The snapshot and its metadata.</response>
    /// <response code="404">No entry with that id under this schema.</response>
    [HttpGet("{name}/version-history/{entryId:guid}")]
    [ProducesResponseType(typeof(SchemaVersionSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersionSnapshot(string name, Guid entryId, CancellationToken ct)
    {
        var entry = await service.GetVersionSnapshotAsync(entryId, ct);
        if (entry is null || !string.Equals(entry.SchemaName, name, StringComparison.Ordinal))
            return NotFound(DiagnosticProblem.NotFound("Schema version", entryId));
        return Ok(SchemaVersionSnapshotDto.From(entry));
    }

    /// <summary>Permanently delete one version-history entry.</summary>
    /// <remarks>
    /// Audited (recorded as a Delete against the schema). Never affects the live schema or its
    /// current version — this only cleans up the history log.
    /// </remarks>
    /// <param name="name">Schema name the entry belongs to.</param>
    /// <param name="entryId">Snapshot id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">Entry deleted.</response>
    /// <response code="404">No entry with that id under this schema.</response>
    [HttpDelete("{name}/version-history/{entryId:guid}")]
    [Authorize(Policy = Capabilities.SchemasManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVersionEntry(string name, Guid entryId, CancellationToken ct)
    {
        var ok = await service.DeleteVersionEntryAsync(name, entryId, ct);
        return ok ? NoContent() : NotFound(DiagnosticProblem.NotFound("Schema version", entryId));
    }

    /// <summary>Permanently delete the entire version history for a schema.</summary>
    /// <remarks>
    /// Audited (recorded as a Delete against the schema). Never affects the live schema or its
    /// current version — this only clears the history log.
    /// </remarks>
    /// <param name="name">Schema name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">History cleared (or already empty — call is idempotent).</response>
    [HttpDelete("{name}/version-history")]
    [Authorize(Policy = Capabilities.SchemasManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteVersionHistory(string name, CancellationToken ct)
    {
        await service.DeleteVersionHistoryAsync(name, ct);
        return NoContent();
    }

    /// <summary>Maps the wire-format <see cref="UpsertSchemaRequest"/> onto a fresh <see cref="Schema"/> entity.</summary>
    /// <remarks>
    /// No business rules here — that's the service's job. This method only copies fields and
    /// substitutes empty collections for nulls so downstream code can rely on non-null lists.
    /// </remarks>
    /// <param name="target">A pre-built entity (only <c>Name</c> matters; other fields are overwritten).</param>
    /// <param name="req">The request payload to copy from.</param>
    /// <returns>The same instance passed in, populated.</returns>
    private static Schema BuildSchema(Schema target, UpsertSchemaRequest req)
    {
        target.Label = req.Label;
        target.Description = req.Description;
        target.Notes = req.Notes;
        target.Modifiable = req.Modifiable;
        target.Enabled = req.Enabled;
        target.SubmissionValidations = req.SubmissionValidations ?? new();
        target.IsGlobal = req.IsGlobal;
        target.ServiceIds = req.ServiceIds ?? new();
        target.Values = (req.Values ?? new()).Select(v => v.ToEntity()).ToList();
        target.Layout = (req.Layout ?? new()).Select(n => n.ToEntity()).ToList();
        target.Version = req.Version;
        // A None policy with no approvers is equivalent to "no approval"; normalise to null so the
        // stored document stays clean and back-compatible.
        target.Approval = req.Approval is { } a && a.Mode != ApprovalMode.None ? a.ToEntity() : null;
        return target;
    }
}
