using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// One reading of one <see cref="SchemaValue"/> at one point in time. Lives inside a parent
/// <see cref="Submission"/> as part of a batch; the denormalised
/// <see cref="SampleProjection"/> is rebuilt from these samples on every save.
/// </summary>
public sealed class Sample
{
    /// <summary>Name of the schema this sample belongs to.</summary>
    public required string SchemaName { get; set; }

    /// <summary>Name of the value (inside the schema) this sample is for.</summary>
    public required string ValueName { get; set; }

    /// <summary>The submitted value, already coerced to the value's declared CLR type (string/long/double/DateTime/bool/null).</summary>
    public object? Value { get; set; }

    /// <summary>When the measurement was taken (UTC).</summary>
    public required DateTime Timestamp { get; set; }

    /// <summary>Optional free-form note attached to this single sample.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// A batch of <see cref="Sample"/> rows submitted together by one service. Submissions are the
/// unit of writes; replacements happen at this level (you can't replace a single sample inside
/// a submission, you replace the whole submission).
/// </summary>
public sealed class Submission : AuditedEntity
{
    /// <summary>Owning service account.</summary>
    public required Guid ServiceAccountId { get; set; }

    /// <summary>Denormalised service name snapshot, copied at write time for read-friendliness.</summary>
    public string? ServiceName { get; set; }

    /// <summary>The samples carried by this submission. All samples typically refer to the same schema.</summary>
    public List<Sample> Samples { get; set; } = new();

    /// <summary>When the submission was first accepted by the API.</summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>Set when a replacement has overwritten this submission. Useful for audit timelines.</summary>
    public DateTime? ReplacedAt { get; set; }
}

/// <summary>
/// Denormalised one-document-per-sample read model. Rebuilt on every <see cref="Submission"/>
/// save and exposed through the OData feed (<c>/odata/samples</c>) and the admin query endpoint.
/// Soft-deletion on the parent submission cascades into <see cref="AuditedEntity.IsDeleted"/>
/// here so reporting tools see consistent state.
/// </summary>
public sealed class SampleProjection : AuditedEntity
{
    /// <summary>Parent submission.</summary>
    public required Guid SubmissionId { get; set; }

    /// <summary>Owning service account.</summary>
    public required Guid ServiceAccountId { get; set; }

    /// <summary>Service name snapshot.</summary>
    public required string ServiceName { get; set; }

    /// <summary>Schema name snapshot.</summary>
    public required string SchemaName { get; set; }

    /// <summary>Schema value name snapshot.</summary>
    public required string ValueName { get; set; }

    /// <summary>Declared type of the value. Only one of the typed columns below is populated for any row.</summary>
    public SchemaValueType ValueType { get; set; }

    /// <summary>Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.String"/>.</summary>
    public string? StringValue { get; set; }

    /// <summary>Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.Number"/>.</summary>
    public double? NumberValue { get; set; }

    /// <summary>Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.Integer"/>.</summary>
    public long? IntegerValue { get; set; }

    /// <summary>Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.Date"/>.</summary>
    public DateTime? DateValue { get; set; }

    /// <summary>Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.Boolean"/>.</summary>
    public bool? BooleanValue { get; set; }

    /// <summary>When the sample was measured.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Free-form note from the parent <see cref="Sample"/>.</summary>
    public string? Note { get; set; }

    /// <summary>Cadence snapshot from the schema definition at write-time.</summary>
    public Cadence Cadence { get; set; }

    /// <summary>Inclusive start of the cadence bucket the sample's timestamp falls into.</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>Exclusive end of the cadence bucket.</summary>
    public DateTime PeriodEnd { get; set; }
}
