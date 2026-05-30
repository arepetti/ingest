using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// Shape of the data envelope a <see cref="Report"/> receives at render time.
/// </summary>
public enum ReportType
{
    /// <summary>
    /// The report renders one specific <see cref="Submission"/>. The viewer picks the submission
    /// from a dropdown (filtered by the report's <see cref="Report.TargetSchemaNames"/> and the
    /// caller's period filter); the template gets the flat sample list, plus the owning service
    /// and the schema definition.
    /// </summary>
    Single = 0,

    /// <summary>
    /// The report renders aggregated history for a schema over a period (last week / month /
    /// custom range, …). The template gets the same per-value bucketed history that powers the
    /// historical-data charts.
    /// </summary>
    Aggregate = 1,
}

/// <summary>
/// A user-supplied HTML+Liquid template, stored verbatim, that the API renders to HTML on demand.
/// Reports are content (template + metadata), never executable code — the renderer runs them in a
/// sandboxed Liquid engine that exposes only the data the report's <see cref="Type"/> says it needs.
/// </summary>
/// <remarks>
/// The metadata lives in a YAML front-matter block at the top of the file (between two <c>---</c>
/// fences) and is also stored as denormalised columns so we don't have to re-parse the template
/// to list reports. The <see cref="Content"/> field always carries the original document (front
/// matter + template) so a "download original" can round-trip; <see cref="Template"/> is the
/// front-matter-stripped body the renderer compiles.
/// </remarks>
public sealed class Report : AuditedEntity
{
    /// <summary>Machine-style identifier; unique across all reports (including soft-deleted ones). Used in the route (<c>/api/reports/{name}</c>).</summary>
    public required string Name { get; set; }

    /// <summary>Friendly label shown in the UI. Falls back to <see cref="Name"/> when empty.</summary>
    public string? Label { get; set; }

    /// <summary>Free-form description shown next to the label in the report list and at the top of the viewer.</summary>
    public string? Description { get; set; }

    /// <summary>Data envelope the template expects. Drives the viewer's filter UI and the renderer's data preparation step.</summary>
    public ReportType Type { get; set; } = ReportType.Aggregate;

    /// <summary>
    /// Schemas this report applies to, identified by machine-style name. An empty list means the
    /// report is <i>global</i> (operators can target it at any schema). The viewer always passes
    /// a single chosen schema when rendering — this list is only used to constrain the picker.
    /// </summary>
    public List<string> TargetSchemaNames { get; set; } = new();

    /// <summary>The original, unmodified document the admin uploaded — front matter included. Round-trips through a "download original" link.</summary>
    public required string Content { get; set; }

    /// <summary>
    /// Liquid template body, with the YAML front matter stripped. This is what the renderer
    /// compiles. Equal to <see cref="Content"/> when the file had no front-matter block (then
    /// the metadata fields above were left at their defaults).
    /// </summary>
    public required string Template { get; set; }
}
