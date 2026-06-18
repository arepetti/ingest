using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>Wire-level type of a <see cref="SchemaValue"/>. Drives shape validation and projection columns.</summary>
public enum SchemaValueType
{
    /// <summary>Free-form text. Use <see cref="SchemaValue.MinLength"/>/<see cref="SchemaValue.MaxLength"/>/<see cref="SchemaValue.RegexPattern"/> for shape rules.</summary>
    String = 0,

    /// <summary>Whole number. Use <see cref="SchemaValue.Min"/>/<see cref="SchemaValue.Max"/>.</summary>
    Integer = 1,

    /// <summary>Floating-point number. Use <see cref="SchemaValue.Min"/>/<see cref="SchemaValue.Max"/>.</summary>
    Number = 2,

    /// <summary>Date or date+time. Use <see cref="SchemaValue.MinDate"/>/<see cref="SchemaValue.MaxDate"/>.</summary>
    Date = 3,

    /// <summary>Boolean flag.</summary>
    Boolean = 4,
}

/// <summary>
/// Reporting cadence for a single <see cref="SchemaValue"/>. The validator uses the cadence to
/// bucket samples (only one sample per <c>(service, schema, value, bucket)</c> tuple), and the
/// status service uses it to compute the current "is this period satisfied?" snapshot.
/// </summary>
public enum Cadence
{
    /// <summary>One bucket per UTC day.</summary>
    Daily = 0,

    /// <summary>One bucket per ISO-week (Monday-anchored, UTC).</summary>
    Weekly = 1,

    /// <summary>One bucket per calendar month (UTC).</summary>
    Monthly = 2,

    /// <summary>One bucket per calendar year (UTC).</summary>
    Yearly = 3,

    /// <summary>One bucket per 14-day window, Monday-anchored. Aligned to a fixed reference Monday so consecutive fortnights never overlap regardless of when the schema was created.</summary>
    Fortnightly = 4,

    /// <summary>One bucket per calendar quarter (Q1: Jan–Mar, Q2: Apr–Jun, Q3: Jul–Sep, Q4: Oct–Dec, UTC).</summary>
    Quarterly = 5,

    /// <summary>One bucket per half-year (H1: Jan–Jun, H2: Jul–Dec, UTC).</summary>
    SemiAnnually = 6,
}

/// <summary>
/// A schema is a package of related KPI values that a service reports together — e.g. "monthly
/// waste KPIs" containing <c>tonnes_collected</c>, <c>incidents</c>, <c>downtime_hours</c>.
/// Package-level flags (<see cref="Enabled"/>, <see cref="Modifiable"/>) gate the whole package;
/// each <see cref="SchemaValue"/> also has its own flags that AND with the schema's.
/// </summary>
public sealed class Schema : AuditedEntity
{
    /// <summary>Machine-style identifier; unique across all schemas (including soft-deleted ones).</summary>
    public required string Name { get; set; }

    /// <summary>Friendly label for the UI.</summary>
    public string? Label { get; set; }

    /// <summary>Free-form description shown to admins and operators.</summary>
    public string? Description { get; set; }

    /// <summary>Free-form notes (rationale, change log, …). Hidden from the wire by default; admin UI shows it.</summary>
    public string? Notes { get; set; }

    /// <summary>When false, no submission against this schema can be replaced.</summary>
    public bool Modifiable { get; set; } = true;

    /// <summary>When false, every submission against this schema is rejected at validation time.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional approval policy. <c>null</c> (the default, and how legacy documents deserialize) is
    /// treated as <see cref="ApprovalMode.None"/> — no approval. Only consulted when the
    /// <c>Approval:Enabled</c> master switch is on.
    /// </summary>
    public ApprovalPolicy? Approval { get; set; }

    /// <summary>
    /// Validation rules evaluated against the assembled submission. Each rule runs once per
    /// schema present in the payload and can compare values to each other, and can compare the
    /// current submission against the service's last live values via <c>latest()</c> /
    /// <c>previous()</c>. See the admin guide for rule syntax.
    /// </summary>
    public List<string> SubmissionValidations { get; set; } = new();

    /// <summary>When true, every service can submit against this schema. When false, only the accounts in <see cref="ServiceIds"/> can.</summary>
    public bool IsGlobal { get; set; } = true;

    /// <summary>Restricted-audience list. Only meaningful when <see cref="IsGlobal"/> is false.</summary>
    public List<Guid> ServiceIds { get; set; } = new();

    /// <summary>Values defined by this schema. Each value has its own type, cadence, validation and flags.</summary>
    public List<SchemaValue> Values { get; set; } = new();

    /// <summary>
    /// Optional UI-only layout tree that arranges the <see cref="Values"/> into sections and
    /// nested subsections. The submission API ignores this completely — submissions stay a flat
    /// list of samples. When empty, the SPA falls back to rendering values in their declaration
    /// order. Each node either points at a value (<see cref="SchemaLayoutNode.Kind"/> = <c>value</c>)
    /// or describes a section that itself contains more nodes (<c>section</c>).
    /// </summary>
    public List<SchemaLayoutNode> Layout { get; set; } = new();

    /// <summary>
    /// Schema version number. Defaults to <c>1</c>. Bumped (manually, by an admin) whenever the
    /// shape changes in a way that should be flagged to users — typically when new values are
    /// added. Combined with <see cref="SchemaValue.SinceVersion"/> and
    /// <see cref="VersionModifiedAt"/> to drive the time-limited "New" tag in the UI.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Timestamp of the most recent change to <see cref="Version"/>. Server-managed: stamped on
    /// create (mirrors <c>CreatedAt</c>), updated to "now" when <see cref="Version"/> increases
    /// on update, and reset to "now" on clone. Never read from client requests. Anchors the
    /// time window in which the SPA renders the "New" tag (one cadence period from this point).
    /// </summary>
    public DateTime? VersionModifiedAt { get; set; }
}

/// <summary>
/// A single KPI definition inside a <see cref="Schema"/>. Carries the wire-type, the cadence,
/// shape constraints and an optional value-level validation rule.
/// </summary>
public sealed class SchemaValue
{
    /// <summary>Machine-style identifier; unique within the parent schema. Referenced by validation rules.</summary>
    public required string Name { get; set; }

    /// <summary>Friendly label for the UI.</summary>
    public string? Label { get; set; }

    /// <summary>Free-form description.</summary>
    public string? Description { get; set; }

    /// <summary>Free-form admin notes (hidden from service-facing endpoints by default).</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Optional UI-only heading rendered above this value's input in the submission editor and
    /// in the read-only submission view (think <c>&lt;h2&gt;</c>). Plays no role in validation
    /// or in the wire contract for API callers — it's purely a presentational hint that lets
    /// schema authors group related inputs under a banner.
    /// </summary>
    public string? Caption { get; set; }

    /// <summary>Expected wire-type of submitted samples.</summary>
    public SchemaValueType Type { get; set; }

    /// <summary>Unit of measure (e.g. <c>t</c>, <c>hours</c>, <c>%</c>). Displayed in the UI and PowerBI; not validated.</summary>
    public string? Unit { get; set; }

    /// <summary>Reporting cadence; drives the "one sample per bucket" rule.</summary>
    public Cadence Cadence { get; set; } = Cadence.Weekly;

    /// <summary>When true, the parent submission is rejected on create if no sample for this value is present.</summary>
    public bool Required { get; set; }

    /// <summary>When false, submissions against this value cannot be replaced (Service-role callers see a 400).</summary>
    public bool Modifiable { get; set; } = true;

    /// <summary>When false, submissions for this value are rejected at validation time.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Inclusive lower bound for <see cref="SchemaValueType.Integer"/> / <see cref="SchemaValueType.Number"/>.</summary>
    public double? Min { get; set; }

    /// <summary>Inclusive upper bound for <see cref="SchemaValueType.Integer"/> / <see cref="SchemaValueType.Number"/>.</summary>
    public double? Max { get; set; }

    /// <summary>Inclusive lower bound for <see cref="SchemaValueType.Date"/>.</summary>
    public DateTime? MinDate { get; set; }

    /// <summary>Inclusive upper bound for <see cref="SchemaValueType.Date"/>.</summary>
    public DateTime? MaxDate { get; set; }

    /// <summary>Minimum string length for <see cref="SchemaValueType.String"/>.</summary>
    public int? MinLength { get; set; }

    /// <summary>Maximum string length for <see cref="SchemaValueType.String"/>.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Optional .NET regex pattern matched against <see cref="SchemaValueType.String"/> samples (200ms timeout enforced).</summary>
    public string? RegexPattern { get; set; }

    /// <summary>
    /// Optional value-level validation rule. Runs against every submitted sample, with every value
    /// in the schema exposed by name (and <c>[name.minimum]</c> / <c>[name.maximum]</c> bounds for
    /// numerics). Can also compare against the service's last live values via <c>latest()</c> /
    /// <c>previous()</c>. See <c>docs/admin-user-guide/validation.md</c> for the syntax.
    /// </summary>
    public string? ValueValidation { get; set; }

    /// <summary>
    /// Optional rule deciding whether this value is "enabled" in the current submission context.
    /// Evaluated against the same parameter set as <see cref="VisibleIf"/>: every value declared
    /// by the parent schema is exposed by its name (set to the submitted value, or <c>null</c> if
    /// not present). A false-y result causes the submitted sample to be discarded with a warning;
    /// it is never persisted. The UI uses the same expression to disable the input.
    /// </summary>
    public string? EnabledIf { get; set; }

    /// <summary>
    /// Optional rule deciding whether this value is "visible" in the current submission context.
    /// Equivalent to <see cref="EnabledIf"/> server-side (false-y also discards the sample with a
    /// warning). The UI uses it to hide the input entirely rather than just disabling it.
    /// </summary>
    public string? VisibleIf { get; set; }

    /// <summary>
    /// Optional rule that, when truthy (or a non-empty string), surfaces a non-blocking warning
    /// on the response. Does not stop the submission. Same parameter set as
    /// <see cref="ValueValidation"/> (<c>value</c>, plus <c>minimum</c>/<c>maximum</c> for numerics).
    /// Strings are used verbatim as the warning text; booleans use a default phrasing.
    /// </summary>
    public string? Warning { get; set; }

    /// <summary>
    /// Optional <see cref="Schema.Version"/> in which this value was first introduced. When set,
    /// the UI uses it together with the parent schema's <c>Version</c> and <c>VersionModifiedAt</c>
    /// to render a time-limited "New" badge next to the value's label. <c>null</c> is treated as
    /// <c>1</c> ("always present") by both the server and the SPA. Must satisfy
    /// <c>0 &lt;= SinceVersion &lt;= Schema.Version</c>.
    /// </summary>
    public int? SinceVersion { get; set; }
}

/// <summary>
/// One node in a <see cref="Schema.Layout"/> tree. A single record with a <see cref="Kind"/>
/// discriminator keeps Mongo serialisation trivial (no class-map registrations needed). Layout
/// is purely a presentation hint: the server never inspects it when accepting submissions, and
/// the submission API surface stays a flat list of samples regardless of how deep the layout
/// nests sections.
/// </summary>
public sealed class SchemaLayoutNode
{
    /// <summary>Discriminator. Either <c>"value"</c> or <c>"section"</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>
    /// When <see cref="Kind"/> is <c>"value"</c>, the machine-style <see cref="SchemaValue.Name"/>
    /// this node points at. Must reference a value that exists in the parent
    /// <see cref="Schema.Values"/> list. Ignored when <see cref="Kind"/> is <c>"section"</c>.
    /// </summary>
    public string? ValueName { get; set; }

    /// <summary>
    /// When <see cref="Kind"/> is <c>"section"</c>, the section heading displayed by the SPA
    /// above this node's children. Required (and non-empty) for section nodes; ignored for
    /// value nodes.
    /// </summary>
    public string? Caption { get; set; }

    /// <summary>
    /// When <see cref="Kind"/> is <c>"section"</c>, an optional sub-heading rendered as a small
    /// paragraph under <see cref="Caption"/>. Ignored for value nodes.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Children of this node. For section nodes this is the ordered list of values/subsections
    /// that appear under this section's heading. For value nodes this is always empty.
    /// </summary>
    public List<SchemaLayoutNode> Items { get; set; } = new();
}

/// <summary>Well-known values for <see cref="SchemaLayoutNode.Kind"/>.</summary>
public static class SchemaLayoutNodeKind
{
    /// <summary>Node points at a <see cref="SchemaValue"/> via <see cref="SchemaLayoutNode.ValueName"/>.</summary>
    public const string Value = "value";

    /// <summary>Node is a section grouping more nodes under a caption.</summary>
    public const string Section = "section";
}
