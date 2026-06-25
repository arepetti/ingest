using Ingest.Core.Entities;

namespace Ingest.Api.Odata;

/// <summary>
/// Simplified, read-only projection of a <see cref="Schema"/> for the <c>/odata/schemas</c> feed.
/// It is a deliberately small catalogue of the fields a BI tool needs to label and bucket the
/// <see cref="SampleProjection"/> rows it pulls from <c>/odata/samples</c> — name, label, the
/// per-value type/unit/cadence and the (charting-only) numeric bounds and RAG band edges. The
/// noisy operational surface of a schema — versioning, notes, layout, approval policy, validation
/// expressions, the restricted-audience list — is intentionally <b>excluded</b>.
/// </summary>
public sealed class SchemaSummary
{
    /// <summary>Machine-style schema name. Doubles as the OData key (filter with <c>name eq '…'</c>).</summary>
    public required string Name { get; set; }

    /// <summary>Friendly schema label, or <c>null</c>.</summary>
    public string? Label { get; set; }

    /// <summary>Free-form description, or <c>null</c>.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the schema is enabled (disabled schemas reject submissions).</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether every service may submit against it (<c>false</c> means audience-restricted).</summary>
    public bool IsGlobal { get; set; }

    /// <summary>The value definitions carried by the schema.</summary>
    public List<SchemaValueSummary> Values { get; set; } = new();

    /// <summary>Project a domain <see cref="Schema"/> onto the simplified summary shape.</summary>
    /// <param name="s">The schema to summarise.</param>
    /// <returns>The summary.</returns>
    public static SchemaSummary From(Schema s) => new()
    {
        Name = s.Name,
        Label = s.Label,
        Description = s.Description,
        Enabled = s.Enabled,
        IsGlobal = s.IsGlobal,
        Values = s.Values.Select(SchemaValueSummary.From).ToList(),
    };
}

/// <summary>
/// Simplified projection of a single <see cref="SchemaValue"/> for the <c>/odata/schemas</c> feed.
/// Exposed as a nested complex type inside <see cref="SchemaSummary.Values"/>. Carries the labelling
/// and bucketing metadata plus the numeric bounds (<see cref="Min"/>/<see cref="Max"/>) and the
/// charting-only RAG band edges; type-specific constraints (string length/regex, date bounds) and
/// the validation/visibility expressions are omitted.
/// </summary>
public sealed class SchemaValueSummary
{
    /// <summary>Machine-style value name; unique within the parent schema.</summary>
    public required string Name { get; set; }

    /// <summary>Friendly value label, or <c>null</c>.</summary>
    public string? Label { get; set; }

    /// <summary>Free-form value description, or <c>null</c>.</summary>
    public string? Description { get; set; }

    /// <summary>Declared wire-type of the value.</summary>
    public SchemaValueType Type { get; set; }

    /// <summary>Unit of measure (e.g. <c>t</c>, <c>hours</c>, <c>%</c>), or <c>null</c>.</summary>
    public string? Unit { get; set; }

    /// <summary>Reporting cadence the value is collected on.</summary>
    public Cadence Cadence { get; set; }

    /// <summary>Whether a sample for this value is required when its submission is created.</summary>
    public bool Required { get; set; }

    /// <summary>
    /// Whether the value is enabled. A disabled value still appears in this catalogue but is
    /// rejected at submission time and never surfaces in the scorecard/recent samples — exposing
    /// the flag lets a report explain the gap.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Inclusive lower numeric bound (Integer/Number), or <c>null</c>.</summary>
    public double? Min { get; set; }

    /// <summary>Inclusive upper numeric bound (Integer/Number), or <c>null</c>.</summary>
    public double? Max { get; set; }

    /// <summary>Lower edge of the acceptable (amber) RAG range, or <c>null</c>.</summary>
    public double? AmberMin { get; set; }

    /// <summary>Lower edge of the ideal (green) RAG range, or <c>null</c>.</summary>
    public double? GreenMin { get; set; }

    /// <summary>Upper edge of the ideal (green) RAG range, or <c>null</c>.</summary>
    public double? GreenMax { get; set; }

    /// <summary>Upper edge of the acceptable (amber) RAG range, or <c>null</c>.</summary>
    public double? AmberMax { get; set; }

    /// <summary>User-defined (submitted) or calculated (derived from sibling values).</summary>
    public SchemaValueKind Kind { get; set; } = SchemaValueKind.UserDefined;

    /// <summary>NCalc formula for calculated values; <c>null</c> for user-defined values.</summary>
    public string? Expression { get; set; }

    /// <summary>Project a domain <see cref="SchemaValue"/> onto the simplified summary shape.</summary>
    /// <param name="v">The value to summarise.</param>
    /// <returns>The summary.</returns>
    public static SchemaValueSummary From(SchemaValue v) => new()
    {
        Name = v.Name,
        Label = v.Label,
        Description = v.Description,
        Type = v.Type,
        Unit = v.Unit,
        Cadence = v.Cadence,
        Required = v.Required,
        Enabled = v.Enabled,
        Min = v.Min,
        Max = v.Max,
        AmberMin = v.AmberMin,
        GreenMin = v.GreenMin,
        GreenMax = v.GreenMax,
        AmberMax = v.AmberMax,
        Kind = v.Kind,
        Expression = v.Expression,
    };
}
