using Ingest.Core.Entities;

namespace Ingest.Api.Models;

/// <summary>Wire representation of an account.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">Machine-style unique name.</param>
/// <param name="Label">Friendly UI label (may be null).</param>
/// <param name="Description">Free-form description.</param>
/// <param name="Kind">UI-capable (<see cref="AccountKind.User"/>) vs API-only (<see cref="AccountKind.Application"/>).</param>
/// <param name="Role">Authorisation tier.</param>
/// <param name="Enabled">Whether the account currently authenticates.</param>
/// <param name="CreatedAt">Creation timestamp (UTC).</param>
/// <param name="CreatedBy">Name of the creator.</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
/// <param name="IsDeleted">Soft-deletion flag.</param>
/// <param name="ExternalLogins">SSO identity links (provider + email). Only ever populated for <see cref="AccountKind.User"/> accounts; relevant only when SSO is enabled.</param>
public sealed record AccountDto(
    Guid Id,
    string Name,
    string? Label,
    string? Description,
    AccountKind Kind,
    AccountRole Role,
    bool Enabled,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime ModifiedAt,
    string? ModifiedBy,
    bool IsDeleted,
    List<ExternalLoginDto> ExternalLogins)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static AccountDto From(Account a) => new(
        a.Id, a.Name, a.Label, a.Description, a.Kind, a.Role, a.Enabled,
        a.CreatedAt, a.CreatedBy, a.ModifiedAt, a.ModifiedBy, a.IsDeleted,
        a.ExternalLogins.Select(ExternalLoginDto.From).ToList());
}

/// <summary>Wire representation of an SSO identity link on an account. The provider's subject is intentionally not exposed.</summary>
/// <param name="Provider">Provider id (e.g. <c>"Microsoft"</c>), matching an <c>Sso:Providers:*:Id</c>.</param>
/// <param name="Email">The verified email that signs this account in via the provider.</param>
public sealed record ExternalLoginDto(string Provider, string Email)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExternalLoginDto From(ExternalLogin e) => new(e.Provider, e.Email);
}

/// <summary>Body for <c>POST /api/admin/accounts</c>.</summary>
/// <param name="Name">Unique machine-style name.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Description">Free-form description.</param>
/// <param name="Kind">UI-capable vs API-only.</param>
/// <param name="Role">Authorisation tier.</param>
/// <param name="Enabled">Initial enabled state; defaults to <c>true</c>.</param>
/// <param name="ExternalLogins">Optional SSO identity links. Only valid for <see cref="AccountKind.User"/> accounts; each (provider, email) pair must be unique across accounts.</param>
public sealed record CreateAccountRequest(string Name, string? Label, string? Description, AccountKind Kind, AccountRole Role, bool Enabled = true, List<ExternalLoginDto>? ExternalLogins = null);

/// <summary>Body for <c>PUT /api/admin/accounts/{id}</c>. Only the mutable fields are accepted.</summary>
/// <param name="Label">New friendly label.</param>
/// <param name="Description">New description.</param>
/// <param name="Role">New authorisation tier.</param>
/// <param name="Enabled">New enabled state.</param>
/// <param name="ExternalLogins">Replacement set of SSO identity links. <c>null</c> leaves the existing links untouched; an empty list clears them.</param>
public sealed record UpdateAccountRequest(string? Label, string? Description, AccountRole Role, bool Enabled, List<ExternalLoginDto>? ExternalLogins = null);

/// <summary>Wire representation of an API key. <b>Never</b> carries the plaintext secret.</summary>
/// <param name="Id">Key id (primary key of the row).</param>
/// <param name="AccountId">Owning account.</param>
/// <param name="KeyId">Public, non-secret prefix of the plaintext.</param>
/// <param name="CreatedAt">Creation timestamp (UTC).</param>
/// <param name="ExpiresAt">Optional absolute expiry.</param>
/// <param name="RevokedAt">Set when the key has been revoked.</param>
public sealed record ApiKeyDto(
    Guid Id,
    Guid AccountId,
    string KeyId,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ApiKeyDto From(ApiKey k) => new(k.Id, k.AccountId, k.KeyId, k.CreatedAt, k.ExpiresAt, k.RevokedAt);
}

/// <summary>Response body when a new API key is minted: carries the plaintext exactly once.</summary>
/// <param name="Key">The stored metadata for the new key.</param>
/// <param name="Plaintext">The plaintext (<c>{keyId}.{secret}</c>). Surface to the user once; cannot be recovered later.</param>
public sealed record GeneratedApiKeyResponse(ApiKeyDto Key, string Plaintext);

/// <summary>Wire representation of a single KPI definition inside a schema.</summary>
/// <param name="Name">Machine-style name (unique within the schema).</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Description">Caller-facing description.</param>
/// <param name="Notes">Internal admin notes.</param>
/// <param name="Caption">Optional UI-only heading rendered above this value in the submission editor and view (think <c>&lt;h2&gt;</c>). Plays no role server-side — it's purely presentational.</param>
/// <param name="Type">Wire type.</param>
/// <param name="Unit">Unit of measure (displayed only).</param>
/// <param name="Cadence">Reporting cadence (one sample per bucket).</param>
/// <param name="Required">Whether absence rejects the submission on create.</param>
/// <param name="Modifiable">Whether existing samples for this value can be replaced.</param>
/// <param name="Enabled">When false, samples are rejected at validation time.</param>
/// <param name="Min">Inclusive numeric lower bound.</param>
/// <param name="Max">Inclusive numeric upper bound.</param>
/// <param name="MinDate">Inclusive date lower bound.</param>
/// <param name="MaxDate">Inclusive date upper bound.</param>
/// <param name="MinLength">String minimum length.</param>
/// <param name="MaxLength">String maximum length.</param>
/// <param name="RegexPattern">.NET regex to match string samples against.</param>
/// <param name="ValueValidation">Optional value-level expression rule (see <c>docs/admin-user-guide/validation.md</c>).</param>
/// <param name="EnabledIf">Optional rule; when false-y the sample is discarded with a warning instead of stored.</param>
/// <param name="VisibleIf">Optional rule; equivalent to <paramref name="EnabledIf"/> server-side (the UI hides the input).</param>
/// <param name="Warning">Optional rule; when truthy / non-empty produces a non-blocking warning on the response.</param>
/// <param name="SinceVersion">Optional schema version in which this value was first introduced. <c>null</c> is treated as <c>1</c>. Server enforces <c>0 &lt;= SinceVersion &lt;= Schema.Version</c>.</param>
public sealed record SchemaValueDto(
    string Name,
    string? Label,
    string? Description,
    string? Notes,
    string? Caption,
    SchemaValueType Type,
    string? Unit,
    Cadence Cadence,
    bool Required,
    bool Modifiable,
    bool Enabled,
    double? Min,
    double? Max,
    DateTime? MinDate,
    DateTime? MaxDate,
    int? MinLength,
    int? MaxLength,
    string? RegexPattern,
    string? ValueValidation,
    string? EnabledIf = null,
    string? VisibleIf = null,
    string? Warning = null,
    int? SinceVersion = null)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static SchemaValueDto From(SchemaValue v) => new(
        v.Name, v.Label, v.Description, v.Notes, v.Caption, v.Type, v.Unit, v.Cadence,
        v.Required, v.Modifiable, v.Enabled,
        v.Min, v.Max, v.MinDate, v.MaxDate, v.MinLength, v.MaxLength, v.RegexPattern, v.ValueValidation,
        v.EnabledIf, v.VisibleIf, v.Warning, v.SinceVersion);

    /// <summary>Convert the wire DTO back into a domain entity (used by upsert endpoints).</summary>
    public SchemaValue ToEntity() => new()
    {
        Name = Name,
        Label = Label,
        Description = Description,
        Notes = Notes,
        Caption = Caption,
        Type = Type,
        Unit = Unit,
        Cadence = Cadence,
        Required = Required,
        Modifiable = Modifiable,
        Enabled = Enabled,
        Min = Min,
        Max = Max,
        MinDate = MinDate,
        MaxDate = MaxDate,
        MinLength = MinLength,
        MaxLength = MaxLength,
        RegexPattern = RegexPattern,
        ValueValidation = ValueValidation,
        EnabledIf = EnabledIf,
        VisibleIf = VisibleIf,
        Warning = Warning,
        SinceVersion = SinceVersion,
    };
}

/// <summary>
/// One node in the UI-only layout tree carried on <see cref="SchemaDto.Layout"/>. Distinguished
/// by <see cref="Kind"/>: <c>"value"</c> nodes point at a value name; <c>"section"</c> nodes
/// carry a caption and recursively contain more nodes.
/// </summary>
/// <param name="Kind">Either <c>"value"</c> or <c>"section"</c>.</param>
/// <param name="ValueName">For value nodes: the value's machine-style name. Null otherwise.</param>
/// <param name="Caption">For section nodes: the section heading. Null otherwise.</param>
/// <param name="Description">For section nodes: optional sub-heading shown under the caption.</param>
/// <param name="Items">For section nodes: the ordered child nodes. Always empty for value nodes.</param>
public sealed record SchemaLayoutNodeDto(
    string Kind,
    string? ValueName = null,
    string? Caption = null,
    string? Description = null,
    List<SchemaLayoutNodeDto>? Items = null)
{
    /// <summary>Project the domain layout node onto the wire shape.</summary>
    public static SchemaLayoutNodeDto From(SchemaLayoutNode n) => new(
        n.Kind,
        n.ValueName,
        n.Caption,
        n.Description,
        n.Items?.Count > 0 ? n.Items.Select(From).ToList() : null);

    /// <summary>Convert the wire DTO back into a domain layout node.</summary>
    public SchemaLayoutNode ToEntity() => new()
    {
        Kind = Kind,
        ValueName = ValueName,
        Caption = Caption,
        Description = Description,
        Items = Items?.Select(i => i.ToEntity()).ToList() ?? new(),
    };
}

/// <summary>Wire representation of a schema (a package of <see cref="SchemaValueDto"/>).</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">Unique machine-style name.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Description">Caller-facing description.</param>
/// <param name="Notes">Internal admin notes.</param>
/// <param name="Modifiable">Whether any submission against this schema can be replaced.</param>
/// <param name="Enabled">Whether the schema accepts submissions.</param>
/// <param name="SubmissionValidations">Schema-level expression rules.</param>
/// <param name="IsGlobal">True when every service can submit against this schema.</param>
/// <param name="ServiceIds">Restricted audience list when <paramref name="IsGlobal"/> is false.</param>
/// <param name="Values">The KPI values defined by the schema.</param>
/// <param name="Layout">Optional UI-only layout tree grouping <paramref name="Values"/> into sections and subsections. Server never inspects it.</param>
/// <param name="Version">Schema version (defaults to 1). Bumped manually by admins; UI uses it for the "New" tag together with <paramref name="VersionModifiedAt"/>.</param>
/// <param name="VersionModifiedAt">Server-managed timestamp of the last <paramref name="Version"/> change. <c>null</c> on legacy documents that never had their version bumped.</param>
/// <param name="CreatedAt">Creation timestamp (UTC).</param>
/// <param name="CreatedBy">Name of the creator.</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
public sealed record SchemaDto(
    Guid Id,
    string Name,
    string? Label,
    string? Description,
    string? Notes,
    bool Modifiable,
    bool Enabled,
    List<string> SubmissionValidations,
    bool IsGlobal,
    List<Guid> ServiceIds,
    List<SchemaValueDto> Values,
    List<SchemaLayoutNodeDto> Layout,
    int Version,
    DateTime? VersionModifiedAt,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime ModifiedAt,
    string? ModifiedBy)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static SchemaDto From(Schema s) => new(
        s.Id, s.Name, s.Label, s.Description, s.Notes,
        s.Modifiable, s.Enabled, s.SubmissionValidations, s.IsGlobal, s.ServiceIds,
        s.Values.Select(SchemaValueDto.From).ToList(),
        s.Layout.Select(SchemaLayoutNodeDto.From).ToList(),
        s.Version, s.VersionModifiedAt,
        s.CreatedAt, s.CreatedBy, s.ModifiedAt, s.ModifiedBy);
}

/// <summary>
/// Body for <c>POST</c> and <c>PUT</c> on the admin schemas endpoint. Nullable list fields are
/// normalised to empty lists server-side; treat them as "no rules / no audience / no values".
/// </summary>
/// <param name="Name">Unique machine-style name.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Description">Caller-facing description.</param>
/// <param name="Notes">Internal admin notes.</param>
/// <param name="Modifiable">Whether submissions can be replaced.</param>
/// <param name="Enabled">Whether the schema accepts submissions.</param>
/// <param name="SubmissionValidations">Schema-level expression rules.</param>
/// <param name="IsGlobal">Global vs restricted audience.</param>
/// <param name="ServiceIds">Restricted audience list (ignored when <paramref name="IsGlobal"/> is true).</param>
/// <param name="Values">KPI values defined by the schema.</param>
/// <param name="Layout">Optional UI-only layout tree. The server validates referential integrity (every value-ref resolves, no duplicates, sections have a non-empty caption) but never inspects it for submissions.</param>
/// <param name="Version">Schema version. Defaults to 1 on create; must be greater than or equal to the existing version on update (monotonic). Any incoming <c>VersionModifiedAt</c> is ignored — the server stamps it itself.</param>
public sealed record UpsertSchemaRequest(
    string Name,
    string? Label,
    string? Description,
    string? Notes,
    bool Modifiable,
    bool Enabled,
    List<string>? SubmissionValidations,
    bool IsGlobal,
    List<Guid>? ServiceIds,
    List<SchemaValueDto>? Values,
    List<SchemaLayoutNodeDto>? Layout = null,
    int Version = 1);

/// <summary>Wire representation of one sample inside a submission.</summary>
/// <param name="SchemaName">Schema the sample belongs to.</param>
/// <param name="ValueName">Value inside the schema.</param>
/// <param name="Value">The submitted value, coerced to the value's declared type.</param>
/// <param name="Timestamp">When the measurement was taken (UTC).</param>
/// <param name="Note">Optional free-form note.</param>
public sealed record SampleDto(string SchemaName, string ValueName, object? Value, DateTime Timestamp, string? Note);

/// <summary>Wire representation of a submission (a batch of samples for one service).</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="ServiceAccountId">Owning service.</param>
/// <param name="ServiceName">Service name snapshot, copied at write time.</param>
/// <param name="Samples">The samples in this batch.</param>
/// <param name="SubmittedAt">First-accepted timestamp.</param>
/// <param name="ReplacedAt">Last-replaced timestamp, or null.</param>
/// <param name="CreatedAt">Creation timestamp.</param>
/// <param name="CreatedBy">Name of the creator.</param>
/// <param name="ModifiedAt">Last update timestamp.</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
/// <param name="IsDeleted">Soft-deletion flag.</param>
public sealed record SubmissionDto(
    Guid Id,
    Guid ServiceAccountId,
    string? ServiceName,
    List<SampleDto> Samples,
    DateTime SubmittedAt,
    DateTime? ReplacedAt,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime ModifiedAt,
    string? ModifiedBy,
    bool IsDeleted)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static SubmissionDto From(Submission s) => new(
        s.Id, s.ServiceAccountId, s.ServiceName,
        s.Samples.Select(x => new SampleDto(x.SchemaName, x.ValueName, x.Value, x.Timestamp, x.Note)).ToList(),
        s.SubmittedAt, s.ReplacedAt, s.CreatedAt, s.CreatedBy, s.ModifiedAt, s.ModifiedBy, s.IsDeleted);
}

/// <summary>
/// Response shape for submission create and replace operations. Carries the persisted
/// submission's id together with any non-blocking warnings the validator produced — typically
/// fired <c>Warning</c> rules and notices about samples discarded by <c>EnabledIf</c> /
/// <c>VisibleIf</c>.
/// </summary>
/// <param name="Id">Identifier of the newly-created or just-replaced submission.</param>
/// <param name="Warnings">
/// Human-readable warnings, one per triggered rule. Always non-null; empty when the validator
/// had nothing to report.
/// </param>
public sealed record SubmissionWriteResponse(Guid Id, IReadOnlyList<string> Warnings);

/// <summary>Request body for <c>POST /api/expressions/translate</c> and <c>POST /api/expressions/validate</c>.</summary>
/// <param name="Expression">The validation expression to translate or validate. The translate endpoint selects the target language through the <c>Accept</c> header; validate ignores it (response is always JSON).</param>
public sealed record TranslateExpressionRequest(string Expression);

/// <summary>
/// Response body for <c>POST /api/expressions/validate</c>. A failed syntax check is a normal
/// outcome (not an HTTP error), so the endpoint always returns 200 with this payload and the
/// SPA can render inline error indicators without HTTP exception handling.
/// </summary>
/// <param name="Ok">True when the expression parsed cleanly. When false, <paramref name="Error"/> is non-null.</param>
/// <param name="Error">Parser error message; <c>null</c> when <paramref name="Ok"/> is true.</param>
/// <param name="Position">Optional 0-based character offset where the parser stumbled; <c>null</c> when the underlying parser doesn't expose one.</param>
public sealed record ValidateExpressionResponse(bool Ok, string? Error = null, int? Position = null);

/// <summary>Generic paged response wrapper. Identical to <c>Ingest.Core.Common.PagedResult&lt;T&gt;</c> but lives in the API layer to keep Core wire-agnostic.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items in this page.</param>
/// <param name="Total">Total items across all pages.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Page size.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

/// <summary>Body for the admin sample-query endpoint. Each filter is optional and ANDed.</summary>
/// <param name="ServiceIds">Restrict to these services.</param>
/// <param name="SchemaNames">Restrict to these schema names.</param>
/// <param name="From">Inclusive lower-bound on sample timestamp.</param>
/// <param name="To">Exclusive upper-bound on sample timestamp.</param>
/// <param name="LatestOnly">Return only the most recent sample per (service, schema).</param>
/// <param name="IncludeDeleted">Include soft-deleted rows.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Page size.</param>
/// <param name="Sort">Optional sort hint.</param>
public sealed record QueryRequest(
    List<Guid>? ServiceIds,
    List<string>? SchemaNames,
    DateTime? From,
    DateTime? To,
    bool LatestOnly = false,
    bool IncludeDeleted = false,
    int Page = 1,
    int PageSize = 50,
    string? Sort = null);

/// <summary>Wire representation of one row of the denormalised sample read model.</summary>
/// <param name="Id">Row id.</param>
/// <param name="SubmissionId">Parent submission.</param>
/// <param name="ServiceAccountId">Owning service.</param>
/// <param name="ServiceName">Service name snapshot.</param>
/// <param name="SchemaName">Schema name snapshot.</param>
/// <param name="ValueName">Schema-value name snapshot.</param>
/// <param name="ValueType">Declared type of the value.</param>
/// <param name="StringValue">Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.String"/>.</param>
/// <param name="NumberValue">Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.Number"/>.</param>
/// <param name="IntegerValue">Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.Integer"/>.</param>
/// <param name="DateValue">Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.Date"/>.</param>
/// <param name="BooleanValue">Populated when <see cref="ValueType"/> is <see cref="SchemaValueType.Boolean"/>.</param>
/// <param name="Timestamp">Measurement timestamp.</param>
/// <param name="Note">Note carried from the original sample.</param>
/// <param name="Cadence">Cadence snapshot from the schema definition.</param>
/// <param name="PeriodStart">Cadence bucket start (inclusive).</param>
/// <param name="PeriodEnd">Cadence bucket end (exclusive).</param>
public sealed record SampleProjectionDto(
    Guid Id,
    Guid SubmissionId,
    Guid ServiceAccountId,
    string ServiceName,
    string SchemaName,
    string ValueName,
    SchemaValueType ValueType,
    string? StringValue,
    double? NumberValue,
    long? IntegerValue,
    DateTime? DateValue,
    bool? BooleanValue,
    DateTime Timestamp,
    string? Note,
    Cadence Cadence,
    DateTime PeriodStart,
    DateTime PeriodEnd)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static SampleProjectionDto From(SampleProjection s) => new(
        s.Id, s.SubmissionId, s.ServiceAccountId, s.ServiceName, s.SchemaName, s.ValueName, s.ValueType,
        s.StringValue, s.NumberValue, s.IntegerValue, s.DateValue, s.BooleanValue,
        s.Timestamp, s.Note, s.Cadence, s.PeriodStart, s.PeriodEnd);
}

/// <summary>Per-value status snapshot.</summary>
/// <param name="ValueName">Schema-value name.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Cadence">Reporting cadence.</param>
/// <param name="Required">Whether the value is required on submission create.</param>
/// <param name="Enabled">Whether the value is enabled (otherwise it's reported as "not expected").</param>
/// <param name="PeriodStart">Current cadence bucket start.</param>
/// <param name="PeriodEnd">Current cadence bucket end.</param>
/// <param name="LastSubmissionId">Most recent submission carrying this value, if any.</param>
/// <param name="LastTimestamp">Timestamp of that submission.</param>
/// <param name="Satisfied">True when there's a sample inside the current cadence bucket.</param>
public sealed record SchemaValueStatusDto(
    string ValueName,
    string? Label,
    Cadence Cadence,
    bool Required,
    bool Enabled,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    Guid? LastSubmissionId,
    DateTime? LastTimestamp,
    bool Satisfied);

/// <summary>Per-schema status snapshot, aggregating <see cref="SchemaValueStatusDto"/> rows.</summary>
/// <param name="SchemaName">Schema name.</param>
/// <param name="Label">Schema label.</param>
/// <param name="Enabled">Whether the schema is enabled overall.</param>
/// <param name="Values">Per-value status snapshots.</param>
public sealed record SchemaStatusDto(
    string SchemaName,
    string? Label,
    bool Enabled,
    List<SchemaValueStatusDto> Values);

/// <summary>Wire shape of <c>GET /api/services/{name}/status</c> and <c>GET /api/me/status</c>.</summary>
/// <param name="ServiceId">Resolved service account id.</param>
/// <param name="ServiceName">Service account name.</param>
/// <param name="Period">Period name echoed back from the request (<c>day</c>/<c>week</c>/…).</param>
/// <param name="Schemas">Status for each visible schema.</param>
public sealed record ServiceStatusDto(
    Guid ServiceId,
    string ServiceName,
    string Period,
    List<SchemaStatusDto> Schemas);

/// <summary>One row of the "missing submissions" report: a service that hasn't yet submitted every required value of a given cadence for one of its schemas inside the current cadence window.</summary>
/// <param name="ServiceId">Owning service account id.</param>
/// <param name="ServiceName">Service account name.</param>
/// <param name="ServiceLabel">Service account label.</param>
/// <param name="SchemaName">Schema name the missing values belong to.</param>
/// <param name="SchemaLabel">Schema label.</param>
/// <param name="MissingRequiredCount">Number of required-and-enabled values of this cadence that the service has not submitted in the current bucket.</param>
/// <param name="TotalRequiredCount">Denominator: total required-and-enabled values of this cadence on the schema.</param>
public sealed record MissingSubmissionEntryDto(
    Guid ServiceId,
    string ServiceName,
    string? ServiceLabel,
    string SchemaName,
    string? SchemaLabel,
    int MissingRequiredCount,
    int TotalRequiredCount);

/// <summary>One cadence-shaped bucket of the "missing submissions" report. Buckets are returned only when they have at least one entry.</summary>
/// <param name="Cadence">Cadence the bucket covers.</param>
/// <param name="PeriodStart">Inclusive start of the current cadence window.</param>
/// <param name="PeriodEnd">Exclusive end of the current cadence window.</param>
/// <param name="Entries">Rows sorted by service label then schema label.</param>
public sealed record MissingByCadenceDto(
    Cadence Cadence,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    List<MissingSubmissionEntryDto> Entries);

/// <summary>One time bucket of aggregated numeric samples for a schema value.</summary>
/// <param name="PeriodStart">Bucket start (inclusive).</param>
/// <param name="PeriodEnd">Bucket end (exclusive).</param>
/// <param name="Min">Minimum value in the bucket.</param>
/// <param name="Max">Maximum value in the bucket.</param>
/// <param name="Average">Arithmetic mean of values in the bucket.</param>
/// <param name="Count">Number of samples in the bucket.</param>
public sealed record HistoryBucketDto(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    double Min,
    double Max,
    double Average,
    int Count);

/// <summary>Per-value timeline of numeric samples grouped by the value's cadence.</summary>
/// <param name="ValueName">Schema-value name.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Type">Numeric type (<see cref="SchemaValueType.Number"/> or <see cref="SchemaValueType.Integer"/>).</param>
/// <param name="Cadence">Cadence of the buckets.</param>
/// <param name="Unit">Unit of measure (informational).</param>
/// <param name="Buckets">Buckets ordered chronologically.</param>
public sealed record SchemaValueHistoryDto(
    string ValueName,
    string? Label,
    SchemaValueType Type,
    Cadence Cadence,
    string? Unit,
    List<HistoryBucketDto> Buckets);

/// <summary>Historical view of a schema: one timeline per numeric value, grouped by cadence.</summary>
/// <param name="SchemaName">Schema name.</param>
/// <param name="Label">Schema label.</param>
/// <param name="Values">Per-value timelines (only numeric values appear).</param>
public sealed record SchemaHistoryDto(
    string SchemaName,
    string? Label,
    List<SchemaValueHistoryDto> Values);

/// <summary>Wire representation of a stored Liquid report.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">Machine-style unique name (URL segment).</param>
/// <param name="Label">Friendly label shown in the UI.</param>
/// <param name="Description">Free-form description shown next to the label and at the top of the viewer.</param>
/// <param name="Type">Data envelope the template expects (drives the viewer's filter UI).</param>
/// <param name="TargetSchemaNames">Schemas the report applies to. Empty list means global.</param>
/// <param name="CreatedAt">Creation timestamp (UTC).</param>
/// <param name="CreatedBy">Name of the creator.</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
public sealed record ReportDto(
    Guid Id,
    string Name,
    string? Label,
    string? Description,
    ReportType Type,
    List<string> TargetSchemaNames,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime ModifiedAt,
    string? ModifiedBy)
{
    /// <summary>Project the domain entity onto the wire shape (the template body is intentionally omitted from list responses).</summary>
    public static ReportDto From(Report r) => new(
        r.Id, r.Name, r.Label, r.Description, r.Type, r.TargetSchemaNames,
        r.CreatedAt, r.CreatedBy, r.ModifiedAt, r.ModifiedBy);
}

/// <summary>Wire shape returned by <c>POST /api/reports/{name}/render</c>: the produced HTML plus the resolved render context.</summary>
/// <param name="Html">Rendered HTML; ready to drop into a sandboxed iframe via <c>srcdoc</c>.</param>
/// <param name="ReportName">Report name.</param>
/// <param name="ReportLabel">Report label.</param>
/// <param name="Type">Report type (Single / Aggregate).</param>
/// <param name="SchemaName">Schema the renderer scoped to, when applicable.</param>
/// <param name="SubmissionId">Submission that was rendered (Single only).</param>
/// <param name="From">Resolved range start, after defaulting.</param>
/// <param name="To">Resolved range end, after defaulting.</param>
public sealed record ReportRenderResponse(
    string Html,
    string ReportName,
    string? ReportLabel,
    ReportType Type,
    string? SchemaName,
    Guid? SubmissionId,
    DateTime From,
    DateTime To);

/// <summary>Body for <c>POST /api/reports/{name}/render</c>.</summary>
/// <param name="SchemaName">Schema to scope the report to. Required for multi-target reports; ignored for single-target ones.</param>
/// <param name="SubmissionId">Submission to render. Required for Single-type reports; ignored otherwise.</param>
/// <param name="From">Inclusive lower bound of the report's time window. Defaults to "start of the current calendar month".</param>
/// <param name="To">Exclusive upper bound. Defaults to "now".</param>
public sealed record RenderReportRequest(
    string? SchemaName,
    Guid? SubmissionId,
    DateTime? From,
    DateTime? To);

/// <summary>Body for <c>POST /api/admin/reports</c> (the JSON path; the upload endpoint also accepts <c>text/html</c> raw bodies).</summary>
/// <param name="FileName">Original file name; used to derive a default <c>name</c> when the front matter doesn't carry one.</param>
/// <param name="Content">Full document text (front matter + Liquid template).</param>
public sealed record UploadReportRequest(string FileName, string Content);
