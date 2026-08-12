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
/// A single non-blocking diagnostic recorded against a submission at write time. Carries the
/// machine name of the schema value it relates to when the warning is value-scoped (a fired
/// <c>Warning</c> rule or an <c>EnabledIf</c>/<c>VisibleIf</c> discard); <see cref="ValueName"/>
/// is <c>null</c> for submission-level diagnostics that don't map to a single value.
/// </summary>
/// <remarks>
/// Persisted with a bespoke BSON serializer (see <c>SubmissionWarningBsonSerializer</c>) so
/// legacy submissions — which stored warnings as a plain array of strings — still deserialize:
/// a stored string becomes a warning with a <c>null</c> <see cref="ValueName"/>.
/// </remarks>
/// <param name="ValueName">Machine name of the associated schema value, or <c>null</c> for submission-level warnings.</param>
/// <param name="Message">Human-readable warning text (unchanged from the pre-structured format).</param>
/// <param name="Code">Stable diagnostic code; null only for warnings read from legacy storage.</param>
/// <param name="Params">Named diagnostic parameters; null only for legacy storage.</param>
public sealed record SubmissionWarning(
    string? ValueName,
    string Message,
    string? Code = null,
    IReadOnlyDictionary<string, object?>? Params = null)
{
    /// <summary>Project this persisted warning onto the shared diagnostic contract.</summary>
    public Diagnostic ToDiagnostic() =>
        new(
            Code ?? DiagnosticCodes.Submissions.LegacyWarning,
            Message,
            Params ?? Diagnostic.EmptyParams);
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

    /// <summary>
    /// Non-blocking diagnostics produced by the validator at the last write (fired <c>Warning</c>
    /// rules and notices about samples discarded by <c>EnabledIf</c> / <c>VisibleIf</c>). Persisted
    /// so operators/admins can review them later. Legacy documents that predate this field
    /// deserialize to an empty list; legacy documents that stored warnings as plain strings
    /// deserialize with a <c>null</c> <see cref="SubmissionWarning.ValueName"/> (see the entity's
    /// custom serializer), so no data migration is required.
    /// </summary>
    public List<SubmissionWarning> Warnings { get; set; } = new();

    /// <summary>When the submission was first accepted by the API.</summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>Set when a replacement has overwritten this submission. Useful for audit timelines.</summary>
    public DateTime? ReplacedAt { get; set; }

    /// <summary>
    /// Where this submission came from. Drives the source-aware approval policy. Legacy documents
    /// deserialize to <see cref="SubmissionSource.Api"/>.
    /// </summary>
    public SubmissionSource Source { get; set; } = SubmissionSource.Api;

    /// <summary>
    /// Approval lifecycle state. Legacy documents (and every submission when approval is off or not
    /// required for the schema/source) deserialize to <see cref="ApprovalStatus.NotRequired"/>,
    /// which means "live as soon as accepted" — identical to the pre-approval behaviour.
    /// </summary>
    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.NotRequired;

    /// <summary>
    /// Snapshot of the approvers that govern this submission, captured when approval was first
    /// required for it. Snapshotting means later edits to the schema/global policy don't retroactively
    /// change what an in-flight submission needs. Empty when <see cref="ApprovalStatus"/> is
    /// <see cref="ApprovalStatus.NotRequired"/>.
    /// </summary>
    public List<ApproverSpec> RequiredApprovers { get; set; } = new();

    /// <summary>
    /// Recorded approval/rejection decisions for the current approval cycle. Cleared whenever the
    /// submission is replaced (a re-send resets approval). Empty for submissions that never needed
    /// approval. Legacy documents deserialize to an empty list.
    /// </summary>
    public List<SubmissionApproval> Approvals { get; set; } = new();

    /// <summary>
    /// True while this submission is a work-in-progress draft: it is excluded from the live read
    /// model (OData / Explore / status rows) and from the accepted/pending webhooks exactly like a
    /// Pending submission, and the approval workflow does not run for it until it is published.
    /// A dedicated flag (rather than an <see cref="ApprovalStatus"/> value) so drafts work even when
    /// the approval feature is off. Legacy documents deserialize to <c>false</c> (never a draft).
    /// Once a submission is published it cannot return to draft.
    /// </summary>
    public bool IsDraft { get; set; }
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

    /// <summary>
    /// When the parent <see cref="Submission"/> was first accepted by the API (snapshot of
    /// <see cref="Submission.SubmittedAt"/>). Lets reporting tools tell "when it happened" (the
    /// measurement <see cref="Timestamp"/>) apart from "when it was reported". Legacy projection
    /// documents that predate this field deserialize to <c>default</c> until the submission is
    /// next saved and the projection is rebuilt.
    /// </summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>Free-form note from the parent <see cref="Sample"/>.</summary>
    public string? Note { get; set; }

    /// <summary>Cadence snapshot from the schema definition at write-time.</summary>
    public Cadence Cadence { get; set; }

    /// <summary>Inclusive start of the cadence bucket the sample's timestamp falls into.</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>Exclusive end of the cadence bucket.</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>
    /// True when this row was computed from a calculated schema value rather than submitted.
    /// Legacy projection documents deserialize to <c>false</c>.
    /// </summary>
    public bool IsDerived { get; set; }
}
