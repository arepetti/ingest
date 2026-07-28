using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Ingest.Core.Validation;

namespace Ingest.Api.Models;

/// <summary>Wire representation of an account.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">Machine-style unique name.</param>
/// <param name="Label">Friendly UI label (may be null).</param>
/// <param name="Description">Free-form description.</param>
/// <param name="Email">Contact email used by the email/notification features. May be null for legacy accounts.</param>
/// <param name="Area">Informative-only area tag (may be null).</param>
/// <param name="Kind">UI-capable (<see cref="AccountKind.User"/>) vs API-only (<see cref="AccountKind.Application"/>).</param>
/// <param name="Role">Authorisation tier.</param>
/// <param name="Enabled">Whether the account currently authenticates.</param>
/// <param name="CreatedAt">Creation timestamp (UTC).</param>
/// <param name="CreatedBy">Name of the creator.</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
/// <param name="IsDeleted">Soft-deletion flag.</param>
/// <param name="ExternalLogins">SSO identity links (provider + email). Only ever populated for <see cref="AccountKind.User"/> accounts; relevant only when SSO is enabled.</param>
/// <param name="Capabilities">The account's stored capability overrides. Empty means "follow the role default bundle"; the admin UI pre-fills the picker from <paramref name="EffectiveCapabilities"/> in that case.</param>
/// <param name="EffectiveCapabilities">The resolved capability set actually in force (role defaults when there are no overrides; the full catalogue for Admins). Read-only — set <paramref name="Capabilities"/> to change it.</param>
/// <param name="AssignedServiceIds">Assigned-service allowlist. Empty means "unrestricted" (the account sees every service); a non-empty list confines every cross-service read to those services. Ignored for Admins.</param>
public sealed record AccountDto(
    Guid Id,
    string Name,
    string? Label,
    string? Description,
    string? Email,
    string? Area,
    AccountKind Kind,
    AccountRole Role,
    bool Enabled,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime ModifiedAt,
    string? ModifiedBy,
    bool IsDeleted,
    List<ExternalLoginDto> ExternalLogins,
    List<string> Capabilities,
    IReadOnlyCollection<string> EffectiveCapabilities,
    List<Guid> AssignedServiceIds)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static AccountDto From(Account a) => new(
        a.Id, a.Name, a.Label, a.Description, a.Email, a.Area, a.Kind, a.Role, a.Enabled,
        a.CreatedAt, a.CreatedBy, a.ModifiedAt, a.ModifiedBy, a.IsDeleted,
        a.ExternalLogins.Select(ExternalLoginDto.From).ToList(),
        a.Capabilities.ToList(),
        RoleCapabilities.Effective(a).ToList(),
        a.AssignedServiceIds.ToList());
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
/// <param name="Email">Contact email for the email/notification features. Optional server-side (blank accepted); the admin UI asks for it.</param>
/// <param name="Area">Optional, informative-only area tag. Blank accepted; only normalised (trimmed), never validated against the configured list.</param>
/// <param name="Kind">UI-capable vs API-only.</param>
/// <param name="Role">Authorisation tier.</param>
/// <param name="Enabled">Initial enabled state; defaults to <c>true</c>.</param>
/// <param name="ExternalLogins">Optional SSO identity links. Only valid for <see cref="AccountKind.User"/> accounts; each (provider, email) pair must be unique across accounts.</param>
/// <param name="Capabilities">Optional capability overrides. <c>null</c>/empty seeds the account with the chosen role's default bundle (so it behaves exactly as before); a non-empty list is stored verbatim as the effective set. Ignored for Admins (who implicitly hold every capability).</param>
/// <param name="AssignedServiceIds">Optional assigned-service allowlist. <c>null</c>/empty leaves the account unrestricted (sees every service); a non-empty list confines every cross-service read to those services. Ignored for Admins.</param>
public sealed record CreateAccountRequest(string Name, string? Label, string? Description, string? Email, string? Area, AccountKind Kind, AccountRole Role, bool Enabled = true, List<ExternalLoginDto>? ExternalLogins = null, List<string>? Capabilities = null, List<Guid>? AssignedServiceIds = null);

/// <summary>Body for <c>PUT /api/admin/accounts/{id}</c>. Only the mutable fields are accepted.</summary>
/// <param name="Label">New friendly label.</param>
/// <param name="Description">New description.</param>
/// <param name="Email">New contact email (blank accepted to clear it).</param>
/// <param name="Area">New informative-only area tag (blank accepted to clear it). Only normalised (trimmed), never validated against the configured list.</param>
/// <param name="Role">New authorisation tier.</param>
/// <param name="Enabled">New enabled state.</param>
/// <param name="ExternalLogins">Replacement set of SSO identity links. <c>null</c> leaves the existing links untouched; an empty list clears them.</param>
/// <param name="Capabilities">Replacement capability override set. <c>null</c> leaves the stored overrides untouched; an empty list clears them (reverting the account to its role default bundle); a non-empty list replaces them. Ignored for Admins.</param>
/// <param name="AssignedServiceIds">Replacement assigned-service allowlist. <c>null</c> leaves it untouched; an empty list clears it (unrestricted, sees every service); a non-empty list confines every cross-service read to those services. Ignored for Admins.</param>
public sealed record UpdateAccountRequest(string? Label, string? Description, string? Email, string? Area, AccountRole Role, bool Enabled, List<ExternalLoginDto>? ExternalLogins = null, List<string>? Capabilities = null, List<Guid>? AssignedServiceIds = null);

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
    DateTime? RevokedAt,
    string? Description)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ApiKeyDto From(ApiKey k) => new(k.Id, k.AccountId, k.KeyId, k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.Description);
}

/// <summary>Optional request body when minting a new API key.</summary>
/// <param name="ExpiresAt">Absolute expiry for the new key. <c>null</c> (or an omitted body) means the key never expires; when supplied it must be in the future and no more than two years out.</param>
/// <param name="Description">Optional free-form note recording who/what the key is for (e.g. holiday cover). Trimmed; blank is stored as none; capped at 200 characters.</param>
public sealed record GenerateApiKeyRequest(DateTime? ExpiresAt = null, string? Description = null);

/// <summary>Request body when updating a key's free-form description (its only mutable field).</summary>
/// <param name="Description">New description; trimmed, blank clears it, capped at 200 characters.</param>
public sealed record UpdateApiKeyRequest(string? Description);

/// <summary>One account inside an accounts export/import file. Secret-free: no id, audit stamps or API keys.</summary>
/// <param name="Name">Unique machine-style name; the match key on import.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Description">Free-form description.</param>
/// <param name="Email">Contact email (may be null).</param>
/// <param name="Area">Informative-only area tag (may be null).</param>
/// <param name="Kind">UI-capable (User) vs API-only (Application).</param>
/// <param name="Role">Authorisation tier.</param>
/// <param name="Enabled">Whether the account is enabled.</param>
/// <param name="Capabilities">Capability overrides (empty = follow the role default bundle).</param>
/// <param name="ExternalLogins">SSO identity links (provider + email); only meaningful for User-kind accounts.</param>
/// <param name="AssignedServiceIds">Assigned-service allowlist (empty = unrestricted). Ignored for Admins.</param>
public sealed record AccountBackupEntryDto(
    string Name,
    string? Label,
    string? Description,
    string? Email,
    string? Area,
    AccountKind Kind,
    AccountRole Role,
    bool Enabled,
    List<string> Capabilities,
    List<ExternalLoginDto> ExternalLogins,
    List<Guid>? AssignedServiceIds = null)
{
    /// <summary>Project a domain backup entry onto the wire shape.</summary>
    public static AccountBackupEntryDto From(AccountBackupEntry e) => new(
        e.Name, e.Label, e.Description, e.Email, e.Area, e.Kind, e.Role, e.Enabled,
        e.Capabilities.ToList(),
        e.ExternalLogins.Select(l => new ExternalLoginDto(l.Provider, l.Email)).ToList(),
        e.AssignedServiceIds.ToList());

    /// <summary>Map back to the domain backup entry for import.</summary>
    public AccountBackupEntry ToEntry() => new(
        Name, Label, Description, Email, Area, Kind, Role, Enabled,
        Capabilities ?? new(),
        (ExternalLogins ?? new()).Select(l => new AccountBackupLogin(l.Provider, l.Email)).ToList(),
        AssignedServiceIds ?? new());
}

/// <summary>Wrapper for an accounts export file: a marker, version and the account list. API keys are never included.</summary>
/// <param name="Format">Format marker; always <c>ingest-accounts</c>.</param>
/// <param name="Version">Format version.</param>
/// <param name="ExportedAt">When the file was produced (UTC).</param>
/// <param name="Accounts">The exported accounts.</param>
public sealed record AccountsBackupFileDto(
    string Format,
    int Version,
    DateTime ExportedAt,
    List<AccountBackupEntryDto> Accounts);

/// <summary>Result of an accounts import.</summary>
/// <param name="Created">Number of accounts created.</param>
/// <param name="Updated">Number of existing accounts updated (matched by name).</param>
/// <param name="Errors">Per-account errors for entries that were skipped.</param>
public sealed record AccountsImportResultDto(int Created, int Updated, List<string> Errors)
{
    /// <summary>Project the domain result onto the wire shape.</summary>
    public static AccountsImportResultDto From(AccountsImportResult r) =>
        new(r.Created, r.Updated, r.Errors.ToList());
}

/// <summary>Body for <c>POST /api/admin/accounts/{id}/erase</c> — a GDPR right-to-erasure request.</summary>
/// <param name="Mode">
/// <c>Anonymise</c> keeps the statistical KPI values but strips identity (pseudonymises the
/// account, redacts free-text, drops keys/emails, rewrites the audit trail). <c>Delete</c> removes
/// everything tied to the subject. Defaults to <c>Anonymise</c>.
/// </param>
public sealed record EraseAccountRequest(ErasureMode Mode = ErasureMode.Anonymise);

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
/// <param name="GreenMin">Optional lower edge of the ideal (green) range in the RAG target band. Non-enforced; charts only.</param>
/// <param name="GreenMax">Optional upper edge of the ideal (green) range. Non-enforced; charts only.</param>
/// <param name="AmberMin">Optional lower edge of the acceptable (amber) range; below it is "red". Non-enforced; charts only.</param>
/// <param name="AmberMax">Optional upper edge of the acceptable (amber) range; above it is "red". Non-enforced; charts only.</param>
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
/// <param name="Kind">User-defined (submitted) or calculated (derived from sibling values).</param>
/// <param name="Expression">NCalc formula for calculated values; ignored when <paramref name="Kind"/> is user-defined.</param>
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
    double? GreenMin,
    double? GreenMax,
    double? AmberMin,
    double? AmberMax,
    DateTime? MinDate,
    DateTime? MaxDate,
    int? MinLength,
    int? MaxLength,
    string? RegexPattern,
    string? ValueValidation,
    string? EnabledIf = null,
    string? VisibleIf = null,
    string? Warning = null,
    int? SinceVersion = null,
    SchemaValueKind Kind = SchemaValueKind.UserDefined,
    string? Expression = null)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static SchemaValueDto From(SchemaValue v) => new(
        v.Name, v.Label, v.Description, v.Notes, v.Caption, v.Type, v.Unit, v.Cadence,
        v.Required, v.Modifiable, v.Enabled,
        v.Min, v.Max, v.GreenMin, v.GreenMax, v.AmberMin, v.AmberMax, v.MinDate, v.MaxDate, v.MinLength, v.MaxLength, v.RegexPattern, v.ValueValidation,
        v.EnabledIf, v.VisibleIf, v.Warning, v.SinceVersion, v.Kind, v.Expression);

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
        GreenMin = GreenMin,
        GreenMax = GreenMax,
        AmberMin = AmberMin,
        AmberMax = AmberMax,
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
        Kind = Kind,
        Expression = Expression,
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
    string? ModifiedBy,
    ApprovalPolicyDto? Approval)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static SchemaDto From(Schema s) => new(
        s.Id, s.Name, s.Label, s.Description, s.Notes,
        s.Modifiable, s.Enabled, s.SubmissionValidations, s.IsGlobal, s.ServiceIds,
        s.Values.Select(SchemaValueDto.From).ToList(),
        s.Layout.Select(SchemaLayoutNodeDto.From).ToList(),
        s.Version, s.VersionModifiedAt,
        s.CreatedAt, s.CreatedBy, s.ModifiedAt, s.ModifiedBy,
        s.Approval is null ? null : ApprovalPolicyDto.From(s.Approval));
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
    int Version = 1,
    ApprovalPolicyDto? Approval = null);

/// <summary>One row in a schema's version history: metadata about a single save, without the snapshot.</summary>
/// <param name="Id">Snapshot id.</param>
/// <param name="SchemaId">Id of the live schema this snapshot belongs to.</param>
/// <param name="SchemaName">Schema name at the time of the save.</param>
/// <param name="ChangeDate">When the save happened (UTC).</param>
/// <param name="AuthorId">Id of the account that performed the save, or <c>null</c>.</param>
/// <param name="AuthorName">Name of the account that performed the save, or <c>null</c>.</param>
/// <param name="OldVersion">Version before this save; <c>null</c> for the initial create.</param>
/// <param name="NewVersion">Version after this save.</param>
/// <param name="VersionBumped">Whether the version number changed in this save.</param>
/// <param name="Enabled">Whether the schema was Published (Enabled) at this point; <c>false</c> means Draft.</param>
/// <param name="SubmissionCount">Number of submissions for this schema at the time of the save.</param>
public sealed record SchemaVersionHistoryDto(
    Guid Id,
    Guid SchemaId,
    string SchemaName,
    DateTime ChangeDate,
    Guid? AuthorId,
    string? AuthorName,
    int? OldVersion,
    int NewVersion,
    bool VersionBumped,
    bool Enabled,
    long SubmissionCount)
{
    /// <summary>Project the snapshot entity onto the metadata wire shape (no schema body).</summary>
    public static SchemaVersionHistoryDto From(SchemaVersionHistory h) => new(
        h.Id, h.SchemaId, h.SchemaName, h.ChangeDate, h.AuthorId, h.AuthorName,
        h.OldVersion, h.NewVersion, h.VersionBumped, h.Enabled, h.SubmissionCount);
}

/// <summary>A version-history entry plus the full schema snapshot, used by the read-only "view this version" page.</summary>
/// <param name="Id">Snapshot id.</param>
/// <param name="SchemaId">Id of the live schema this snapshot belongs to.</param>
/// <param name="SchemaName">Schema name at the time of the save.</param>
/// <param name="ChangeDate">When the save happened (UTC).</param>
/// <param name="AuthorId">Id of the account that performed the save, or <c>null</c>.</param>
/// <param name="AuthorName">Name of the account that performed the save, or <c>null</c>.</param>
/// <param name="OldVersion">Version before this save; <c>null</c> for the initial create.</param>
/// <param name="NewVersion">Version after this save.</param>
/// <param name="VersionBumped">Whether the version number changed in this save.</param>
/// <param name="Enabled">Whether the schema was Published (Enabled) at this point; <c>false</c> means Draft.</param>
/// <param name="SubmissionCount">Number of submissions for this schema at the time of the save.</param>
/// <param name="Schema">Full snapshot of the schema as it was at this point in time.</param>
public sealed record SchemaVersionSnapshotDto(
    Guid Id,
    Guid SchemaId,
    string SchemaName,
    DateTime ChangeDate,
    Guid? AuthorId,
    string? AuthorName,
    int? OldVersion,
    int NewVersion,
    bool VersionBumped,
    bool Enabled,
    long SubmissionCount,
    SchemaDto Schema)
{
    /// <summary>Project the snapshot entity, including the schema body, onto the wire shape.</summary>
    public static SchemaVersionSnapshotDto From(SchemaVersionHistory h) => new(
        h.Id, h.SchemaId, h.SchemaName, h.ChangeDate, h.AuthorId, h.AuthorName,
        h.OldVersion, h.NewVersion, h.VersionBumped, h.Enabled, h.SubmissionCount,
        SchemaDto.From(h.Snapshot));
}

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
/// <param name="Warnings">Non-blocking warnings recorded at the last write (fired <c>Warning</c> rules, <c>EnabledIf</c> / <c>VisibleIf</c> discards). Empty for legacy submissions that predate warning persistence.</param>
/// <param name="SubmittedAt">First-accepted timestamp.</param>
/// <param name="ReplacedAt">Last-replaced timestamp, or null.</param>
/// <param name="CreatedAt">Creation timestamp.</param>
/// <param name="CreatedBy">Name of the creator.</param>
/// <param name="ModifiedAt">Last update timestamp.</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
/// <param name="IsDeleted">Soft-deletion flag.</param>
/// <param name="Source">Where the submission originated (<c>Api</c> or <c>Manual</c>).</param>
/// <param name="ApprovalStatus">Approval lifecycle state (<c>NotRequired</c> / <c>Pending</c> / <c>Approved</c> / <c>Rejected</c>).</param>
/// <param name="RequiredApprovers">Snapshot of designated approvers governing this submission (empty when approval isn't required).</param>
/// <param name="Approvals">Recorded approval/rejection decisions for the current cycle (carries reject reasons).</param>
public sealed record SubmissionDto(
    Guid Id,
    Guid ServiceAccountId,
    string? ServiceName,
    List<SampleDto> Samples,
    List<string> Warnings,
    DateTime SubmittedAt,
    DateTime? ReplacedAt,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime ModifiedAt,
    string? ModifiedBy,
    bool IsDeleted,
    SubmissionSource Source,
    ApprovalStatus ApprovalStatus,
    List<ApproverSpecDto> RequiredApprovers,
    List<SubmissionApprovalDto> Approvals,
    bool IsDraft)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static SubmissionDto From(Submission s) => new(
        s.Id, s.ServiceAccountId, s.ServiceName,
        s.Samples.Select(x => new SampleDto(x.SchemaName, x.ValueName, x.Value, x.Timestamp, x.Note)).ToList(),
        (s.Warnings ?? new()).Select(w => w.Message).ToList(),
        s.SubmittedAt, s.ReplacedAt, s.CreatedAt, s.CreatedBy, s.ModifiedAt, s.ModifiedBy, s.IsDeleted,
        s.Source, s.ApprovalStatus,
        (s.RequiredApprovers ?? new()).Select(ApproverSpecDto.From).ToList(),
        (s.Approvals ?? new()).Select(SubmissionApprovalDto.From).ToList(),
        s.IsDraft);
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

/// <summary>Wire shape of one (schema, value) pair that a dry-run would discard before persistence.</summary>
/// <param name="SchemaName">Machine-style schema name.</param>
/// <param name="ValueName">Machine-style value name inside the schema.</param>
public sealed record SampleRefDto(string SchemaName, string ValueName)
{
    /// <summary>Project the domain reference onto the wire shape.</summary>
    public static SampleRefDto From(SampleRef r) => new(r.SchemaName, r.ValueName);
}

/// <summary>
/// Result of a validate-only (dry-run) submission. Reports exactly what a real submission would do
/// — validity, blocking errors, non-blocking warnings, conditionally-discarded samples, and the
/// approval state it would land in — without persisting anything. The HTTP status is always 200,
/// even when <paramref name="Valid"/> is false: inspect <paramref name="Valid"/> / <paramref name="Errors"/>
/// for the verdict (a non-200 means the request itself couldn't be processed).
/// </summary>
/// <param name="Valid">True when a real submission of this payload would be accepted.</param>
/// <param name="Errors">Blocking validation errors, one per rejected rule. Empty when <paramref name="Valid"/> is true.</param>
/// <param name="Warnings">Non-blocking diagnostics (fired <c>Warning</c> rules, <c>EnabledIf</c>/<c>VisibleIf</c> discard notices).</param>
/// <param name="DiscardedSamples">Samples that would be dropped before persistence because their <c>EnabledIf</c>/<c>VisibleIf</c> rule is false.</param>
/// <param name="ApprovalStatus">The approval state the submission would land in (<c>NotRequired</c> = live immediately; <c>Pending</c> = held for approval).</param>
/// <param name="RequiredApprovers">Approvers that would govern the submission when it would be held for approval; empty otherwise.</param>
public sealed record SubmissionValidationResponse(
    bool Valid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SampleRefDto> DiscardedSamples,
    ApprovalStatus ApprovalStatus,
    IReadOnlyList<ApproverSpecDto> RequiredApprovers)
{
    /// <summary>Project the service-layer outcome onto the wire shape.</summary>
    public static SubmissionValidationResponse From(SubmissionValidationOutcome o) => new(
        o.Valid,
        o.Errors,
        o.Warnings,
        o.DiscardedSamples.Select(SampleRefDto.From).ToList(),
        o.ApprovalStatus,
        o.RequiredApprovers.Select(ApproverSpecDto.From).ToList());
}

/// <summary>Wire shape of a single designated approver in an approval policy.</summary>
/// <param name="AccountId">Account designated as an approver (ignored for the <c>ServiceOwner</c> kind).</param>
/// <param name="Requirement">Whether this approver is <c>Required</c> or <c>Optional</c>.</param>
/// <param name="Kind">Approver kind: a named <c>Account</c>, or the dynamic <c>ServiceOwner</c> (the submitting service).</param>
public sealed record ApproverSpecDto(Guid AccountId, ApproverRequirement Requirement, ApproverKind Kind = ApproverKind.Account)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ApproverSpecDto From(ApproverSpec a) => new(a.AccountId, a.Requirement, a.Kind);

    /// <summary>Convert the wire DTO back into a domain entity.</summary>
    public ApproverSpec ToEntity() => new() { AccountId = AccountId, Requirement = Requirement, Kind = Kind };
}

/// <summary>Wire shape for the configurable list of selectable areas.</summary>
/// <param name="Areas">Area names in display order. Empty means the account editor uses a free-text area field.</param>
public sealed record AreasConfigurationDto(List<string> Areas);

/// <summary>Wire shape for the configurable cadence bucket alignment points.</summary>
/// <param name="FiscalYearStartMonth">Month (1-12) the fiscal year begins on; also anchors Quarterly/SemiAnnually.</param>
/// <param name="WeekStartDay">Day of week a Weekly bucket begins on.</param>
/// <param name="MonthStartDay">Day of month (1-28) a Monthly bucket begins on.</param>
/// <param name="FortnightAnchor">A UTC date a Fortnightly bucket boundary is aligned to.</param>
public sealed record SubmissionWindowDto(int FiscalYearStartMonth, DayOfWeek WeekStartDay, int MonthStartDay, DateTime FortnightAnchor)
{
    /// <summary>Build the wire DTO from the resolved domain anchors.</summary>
    public static SubmissionWindowDto From(CadenceAnchors a) =>
        new(a.FiscalYearStartMonth, a.WeekStartDay, a.MonthStartDay, a.FortnightAnchor);

    /// <summary>Map back to the domain record for the update call.</summary>
    public CadenceAnchors ToAnchors() =>
        new(FiscalYearStartMonth, WeekStartDay, MonthStartDay, FortnightAnchor);
}

/// <summary>Wire shape for the global "close all submissions" ingestion kill switch.</summary>
/// <param name="Closed">When true, service-facing ingestion (create/replace, bulk import, Teams inbound) is blocked.</param>
/// <param name="Message">Optional operator-facing message shown in the site banner and the 503 body.</param>
public sealed record IngestionStatusDto(bool Closed, string? Message);

/// <summary>Wire shape for one cadence's submission-window offsets.</summary>
/// <param name="OpenOffsetHours">Hours after the bucket's start before the window opens.</param>
/// <param name="GraceHours">Hours after the bucket's end during which the window stays open.</param>
public sealed record CadenceWindowDto(double OpenOffsetHours, double GraceHours)
{
    /// <summary>Build the wire DTO from the resolved domain window.</summary>
    public static CadenceWindowDto From(CadenceWindow w) => new(w.OpenOffsetHours, w.GraceHours);

    /// <summary>Map back to the domain record.</summary>
    public CadenceWindow ToDomain() => new(OpenOffsetHours, GraceHours);
}

/// <summary>Wire shape for the per-cadence submission-window configuration (all 7 cadences).</summary>
public sealed record CadenceWindowsDto(
    CadenceWindowDto Daily,
    CadenceWindowDto Weekly,
    CadenceWindowDto Fortnightly,
    CadenceWindowDto Monthly,
    CadenceWindowDto Quarterly,
    CadenceWindowDto SemiAnnually,
    CadenceWindowDto Yearly)
{
    /// <summary>Build the wire DTO from the resolved domain windows.</summary>
    public static CadenceWindowsDto From(CadenceWindows w) => new(
        CadenceWindowDto.From(w.Daily), CadenceWindowDto.From(w.Weekly), CadenceWindowDto.From(w.Fortnightly),
        CadenceWindowDto.From(w.Monthly), CadenceWindowDto.From(w.Quarterly), CadenceWindowDto.From(w.SemiAnnually),
        CadenceWindowDto.From(w.Yearly));

    /// <summary>Map back to the domain record for the update call.</summary>
    public CadenceWindows ToDomain() => new(
        Daily.ToDomain(), Weekly.ToDomain(), Fortnightly.ToDomain(),
        Monthly.ToDomain(), Quarterly.ToDomain(), SemiAnnually.ToDomain(), Yearly.ToDomain());
}

/// <summary>
/// A single cadence's live-computed period (the bucket containing "now") and submission window
/// (the bucket extended by the resolved open offset/grace), as returned by the cadence-preview
/// endpoint. Purely informational — computed on demand from the current anchors/windows, never stored.
/// </summary>
/// <param name="Cadence">Cadence this entry describes.</param>
/// <param name="PeriodStart">Inclusive start of the current bucket.</param>
/// <param name="PeriodEnd">Exclusive end of the current bucket.</param>
/// <param name="WindowStart">Inclusive start of the submission window (bucket start + open offset).</param>
/// <param name="WindowEnd">Exclusive end of the submission window (bucket end + grace).</param>
public sealed record CadencePreviewEntryDto(Cadence Cadence, DateTime PeriodStart, DateTime PeriodEnd, DateTime WindowStart, DateTime WindowEnd);

/// <summary>Wire shape of an approval policy (per-schema or the global default).</summary>
/// <param name="Mode">Whether (and how) approval is required (<c>None</c> / <c>UseGlobalDefault</c> / <c>Required</c>).</param>
/// <param name="AppliesToSources">Which submission sources the policy applies to (<c>Both</c> / <c>ManualOnly</c> / <c>ApiOnly</c>).</param>
/// <param name="Approvers">Designated approvers, each required or optional.</param>
public sealed record ApprovalPolicyDto(
    ApprovalMode Mode,
    ApprovalSourceScope AppliesToSources,
    List<ApproverSpecDto> Approvers)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ApprovalPolicyDto From(ApprovalPolicy p) => new(
        p.Mode, p.AppliesToSources, (p.Approvers ?? new()).Select(ApproverSpecDto.From).ToList());

    /// <summary>Convert the wire DTO back into a domain entity.</summary>
    public ApprovalPolicy ToEntity() => new()
    {
        Mode = Mode,
        AppliesToSources = AppliesToSources,
        Approvers = Approvers?.Select(a => a.ToEntity()).ToList() ?? new(),
    };
}

/// <summary>Wire shape of a cross-cutting approval rule (per-service/per-schema approval requirement).</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Label">Optional friendly label.</param>
/// <param name="Enabled">Whether the rule is active.</param>
/// <param name="ServiceIds">Services the rule applies to; empty means all services.</param>
/// <param name="SchemaIds">Schemas the rule applies to; empty means all schemas.</param>
/// <param name="Policy">The approval policy imposed when the rule matches.</param>
/// <param name="CreatedAt">Creation timestamp (UTC).</param>
/// <param name="CreatedBy">Name of the creator.</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
public sealed record ApprovalRuleDto(
    Guid Id,
    string? Label,
    bool Enabled,
    List<Guid> ServiceIds,
    List<Guid> SchemaIds,
    ApprovalPolicyDto Policy,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime ModifiedAt,
    string? ModifiedBy)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ApprovalRuleDto From(ApprovalRule r) => new(
        r.Id, r.Label, r.Enabled, r.ServiceIds, r.SchemaIds,
        ApprovalPolicyDto.From(r.Policy),
        r.CreatedAt, r.CreatedBy, r.ModifiedAt, r.ModifiedBy);
}

/// <summary>Body for <c>POST</c> and <c>PUT</c> on the admin approval-rules endpoint. Null list fields are normalised to "all".</summary>
/// <param name="Label">Optional friendly label.</param>
/// <param name="Enabled">Whether the rule is active.</param>
/// <param name="ServiceIds">Services the rule applies to; null/empty means all services.</param>
/// <param name="SchemaIds">Schemas the rule applies to; null/empty means all schemas.</param>
/// <param name="Policy">The approval policy imposed when the rule matches.</param>
public sealed record UpsertApprovalRuleRequest(
    string? Label,
    bool Enabled,
    List<Guid>? ServiceIds,
    List<Guid>? SchemaIds,
    ApprovalPolicyDto Policy)
{
    /// <summary>Convert the wire DTO into a domain entity.</summary>
    public ApprovalRule ToEntity() => new()
    {
        Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(),
        Enabled = Enabled,
        ServiceIds = ServiceIds ?? new(),
        SchemaIds = SchemaIds ?? new(),
        Policy = Policy.ToEntity(),
    };
}

/// <summary>Wire shape of an admin-recorded timeline event.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Timestamp">UTC instant the event occurred (or is scheduled for).</param>
/// <param name="Label">Required short title.</param>
/// <param name="Description">Optional longer free-text description.</param>
/// <param name="Kind">How the event relates to time: a single instant, a bounded interval, or an open-ended span.</param>
/// <param name="DurationMinutes">Duration in whole minutes; only set (and only meaningful) when <paramref name="Kind"/> is <c>Interval</c>.</param>
/// <param name="ServiceIds">Services the event affects; empty means all services.</param>
/// <param name="CreatedAt">Creation timestamp (UTC).</param>
/// <param name="CreatedBy">Name of the creator.</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
public sealed record EventDto(
    Guid Id,
    DateTime Timestamp,
    string Label,
    string? Description,
    EventKind Kind,
    int? DurationMinutes,
    List<Guid> ServiceIds,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime ModifiedAt,
    string? ModifiedBy)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static EventDto From(Event e) => new(
        e.Id, e.Timestamp, e.Label, e.Description, e.Kind, ToMinutes(e.Duration), e.ServiceIds,
        e.CreatedAt, e.CreatedBy, e.ModifiedAt, e.ModifiedBy);

    private static int? ToMinutes(TimeSpan? d) => d is null ? null : (int)Math.Round(d.Value.TotalMinutes);
}

/// <summary>Body for <c>POST</c> and <c>PUT</c> on the admin events endpoint.</summary>
/// <param name="Timestamp">UTC instant the event occurred (or is scheduled for).</param>
/// <param name="Label">Required short title.</param>
/// <param name="Description">Optional longer free-text description.</param>
/// <param name="Kind">How the event relates to time: a single instant, a bounded interval, or an open-ended span.</param>
/// <param name="DurationMinutes">Duration in whole minutes; required when <paramref name="Kind"/> is <c>Interval</c>, ignored otherwise.</param>
/// <param name="ServiceIds">Services the event affects; null/empty means all services.</param>
public sealed record UpsertEventRequest(
    DateTime Timestamp,
    string Label,
    string? Description,
    EventKind Kind,
    int? DurationMinutes,
    List<Guid>? ServiceIds)
{
    /// <summary>Convert the wire DTO into a domain entity.</summary>
    public Event ToEntity() => new()
    {
        Timestamp = Timestamp,
        Label = Label,
        Description = Description,
        Kind = Kind,
        Duration = DurationMinutes is null ? null : TimeSpan.FromMinutes(DurationMinutes.Value),
        ServiceIds = ServiceIds ?? new(),
    };
}

/// <summary>Wire shape of one recorded approval/rejection decision on a submission.</summary>
/// <param name="ApproverAccountId">Account that recorded the decision.</param>
/// <param name="ApproverName">Machine-name snapshot of the approver.</param>
/// <param name="Decision">The decision (<c>Approved</c> / <c>Rejected</c>).</param>
/// <param name="DecidedAt">When the decision was recorded (UTC).</param>
/// <param name="Note">Optional note; carries the reject reason.</param>
public sealed record SubmissionApprovalDto(
    Guid ApproverAccountId,
    string? ApproverName,
    ApprovalDecision Decision,
    DateTime DecidedAt,
    string? Note)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static SubmissionApprovalDto From(SubmissionApproval a) => new(
        a.ApproverAccountId, a.ApproverName, a.Decision, a.DecidedAt, a.Note);
}

/// <summary>Body for <c>POST /api/admin/submissions/{id}/approve</c> and <c>.../reject</c>. The note is optional (used as the reject reason).</summary>
/// <param name="Note">Optional free-form note recorded against the decision.</param>
public sealed record ApprovalDecisionRequest(string? Note = null);

/// <summary>Body for <c>POST /api/admin/submissions/import</c>: bulk import historical submissions for one service.</summary>
/// <param name="ServiceAccountId">The service account every imported submission is attributed to.</param>
/// <param name="Format">Whether <paramref name="Content"/> is <c>Json</c> or <c>Csv</c>.</param>
/// <param name="Content">The raw file text (the admin SPA reads the chosen file and posts its contents here).</param>
public sealed record BulkImportRequest(Guid ServiceAccountId, BulkImportFormat Format, string Content);

/// <summary>Wire shape of the SMTP settings. The password is write-only: it is never returned, only a flag saying whether one is set.</summary>
/// <param name="Host">SMTP host.</param>
/// <param name="Port">SMTP port.</param>
/// <param name="UseStartTls">Whether STARTTLS is negotiated.</param>
/// <param name="Username">SMTP username (null = anonymous).</param>
/// <param name="FromAddress">From address.</param>
/// <param name="FromName">From display name.</param>
/// <param name="HasPassword">True when a password is stored (the value itself is never exposed).</param>
/// <param name="Configured">True when enough is set to attempt a send (host + from address).</param>
public sealed record EmailSettingsDto(
    string Host,
    int Port,
    bool UseStartTls,
    string? Username,
    string FromAddress,
    string? FromName,
    bool HasPassword,
    bool Configured)
{
    /// <summary>Project the domain entity onto the wire shape, omitting the password.</summary>
    public static EmailSettingsDto From(EmailSettings s) => new(
        s.Host, s.Port, s.UseStartTls, s.Username, s.FromAddress, s.FromName,
        !string.IsNullOrEmpty(s.PasswordCipher), s.IsConfigured);
}

/// <summary>Body for <c>PUT /api/admin/email/settings</c>.</summary>
/// <param name="Host">SMTP host.</param>
/// <param name="Port">SMTP port.</param>
/// <param name="UseStartTls">Whether to negotiate STARTTLS.</param>
/// <param name="Username">SMTP username (null/blank = anonymous).</param>
/// <param name="FromAddress">From address (validated).</param>
/// <param name="FromName">From display name.</param>
/// <param name="UpdatePassword">When false the stored password is kept. When true it is replaced with <paramref name="Password"/> (blank clears it).</param>
/// <param name="Password">New password; honoured only when <paramref name="UpdatePassword"/> is true.</param>
public sealed record UpdateEmailSettingsRequest(
    string Host,
    int Port,
    bool UseStartTls,
    string? Username,
    string FromAddress,
    string? FromName,
    bool UpdatePassword = false,
    string? Password = null);

/// <summary>Wire shape of an editable email template.</summary>
/// <param name="Key">Stable lookup key (immutable).</param>
/// <param name="Name">Friendly name.</param>
/// <param name="Description">When the template is used.</param>
/// <param name="Subject">Liquid subject.</param>
/// <param name="HtmlBody">Optional Liquid HTML body.</param>
/// <param name="TextBody">Liquid text body.</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
public sealed record EmailTemplateDto(
    string Key,
    string Name,
    string? Description,
    string Subject,
    string? HtmlBody,
    string TextBody,
    DateTime ModifiedAt,
    string? ModifiedBy)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static EmailTemplateDto From(EmailTemplate t) => new(
        t.Key, t.Name, t.Description, t.Subject, t.HtmlBody, t.TextBody, t.ModifiedAt, t.ModifiedBy);
}

/// <summary>Body for <c>PUT /api/admin/email/templates/{key}</c>. The key is immutable.</summary>
/// <param name="Name">Friendly name.</param>
/// <param name="Description">When the template is used.</param>
/// <param name="Subject">Liquid subject (validated).</param>
/// <param name="HtmlBody">Optional Liquid HTML body (validated when present).</param>
/// <param name="TextBody">Liquid text body (validated).</param>
public sealed record UpdateEmailTemplateRequest(
    string Name,
    string? Description,
    string Subject,
    string? HtmlBody,
    string TextBody);

/// <summary>Wire shape of one outbox message for the audit "Sent emails" tab. Bodies are omitted to keep the list light.</summary>
/// <param name="Id">Message id.</param>
/// <param name="ToAddress">Recipient address.</param>
/// <param name="ToName">Recipient display name.</param>
/// <param name="Subject">Rendered subject.</param>
/// <param name="Status">Delivery status.</param>
/// <param name="Attempts">Number of delivery attempts.</param>
/// <param name="LastError">Last delivery error, if any.</param>
/// <param name="CreatedAt">Enqueue timestamp.</param>
/// <param name="SentAt">Delivery timestamp, if delivered.</param>
/// <param name="Category">Audit category, e.g. <c>adhoc</c> or <c>notification:missed</c>.</param>
/// <param name="RelatedAccountId">Related account, if any.</param>
public sealed record EmailMessageDto(
    Guid Id,
    string ToAddress,
    string? ToName,
    string Subject,
    EmailStatus Status,
    int Attempts,
    string? LastError,
    DateTime CreatedAt,
    DateTime? SentAt,
    string Category,
    Guid? RelatedAccountId)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static EmailMessageDto From(EmailMessage m) => new(
        m.Id, m.ToAddress, m.ToName, m.Subject, m.Status, m.Attempts, m.LastError,
        m.CreatedAt, m.SentAt, m.Category, m.RelatedAccountId);
}

/// <summary>Body for <c>POST /api/admin/email/send</c>: an ad-hoc plain-text email to one account.</summary>
/// <param name="AccountId">Recipient account; must have a contact email set.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="Body">Plain-text body.</param>
public sealed record SendAdhocEmailRequest(Guid AccountId, string Subject, string Body);

/// <summary>Wire shape of a single notification trigger.</summary>
/// <param name="Enabled">Master switch.</param>
/// <param name="NotifyServiceAccount">Copy the service account's contact email.</param>
/// <param name="NotifyAdminList">Copy the admin/operator recipient list.</param>
public sealed record NotificationRuleDto(bool Enabled, bool NotifyServiceAccount, bool NotifyAdminList)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static NotificationRuleDto From(NotificationRule r) => new(r.Enabled, r.NotifyServiceAccount, r.NotifyAdminList);

    /// <summary>Convert to the service-layer patch.</summary>
    public NotificationRuleUpdate ToUpdate() => new(Enabled, NotifyServiceAccount, NotifyAdminList);
}

/// <summary>Wire shape of the notification configuration.</summary>
/// <param name="Upcoming">Upcoming-reminder rule.</param>
/// <param name="Missed">Missed-alert rule.</param>
/// <param name="Warnings">Warnings-notice rule.</param>
/// <param name="PendingApproval">Pending-approval notice rule.</param>
/// <param name="Approved">Approved-notice rule.</param>
/// <param name="Rejected">Rejected-notice rule.</param>
/// <param name="DraftSaved">Draft-saved nudge rule.</param>
/// <param name="UpcomingLeadHours">Lead time (hours) before a window closes that an upcoming reminder fires.</param>
/// <param name="AdminRecipientAccountIds">Accounts that receive the admin-list copy.</param>
public sealed record NotificationSettingsDto(
    NotificationRuleDto Upcoming,
    NotificationRuleDto Missed,
    NotificationRuleDto Warnings,
    NotificationRuleDto PendingApproval,
    NotificationRuleDto Approved,
    NotificationRuleDto Rejected,
    NotificationRuleDto DraftSaved,
    int UpcomingLeadHours,
    List<Guid> AdminRecipientAccountIds)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static NotificationSettingsDto From(NotificationSettings s) => new(
        NotificationRuleDto.From(s.Upcoming),
        NotificationRuleDto.From(s.Missed),
        NotificationRuleDto.From(s.Warnings),
        NotificationRuleDto.From(s.PendingApproval),
        NotificationRuleDto.From(s.Approved),
        NotificationRuleDto.From(s.Rejected),
        NotificationRuleDto.From(s.DraftSaved),
        s.UpcomingLeadHours,
        s.AdminRecipientAccountIds);
}

/// <summary>Body for <c>PUT /api/admin/notifications/settings</c>.</summary>
/// <param name="Upcoming">Upcoming-reminder rule.</param>
/// <param name="Missed">Missed-alert rule.</param>
/// <param name="Warnings">Warnings-notice rule.</param>
/// <param name="PendingApproval">Pending-approval notice rule.</param>
/// <param name="Approved">Approved-notice rule.</param>
/// <param name="Rejected">Rejected-notice rule.</param>
/// <param name="DraftSaved">Draft-saved nudge rule.</param>
/// <param name="UpcomingLeadHours">Lead time (hours, clamped 1..720).</param>
/// <param name="AdminRecipientAccountIds">Accounts that receive the admin-list copy.</param>
public sealed record UpdateNotificationSettingsRequest(
    NotificationRuleDto Upcoming,
    NotificationRuleDto Missed,
    NotificationRuleDto Warnings,
    NotificationRuleDto PendingApproval,
    NotificationRuleDto Approved,
    NotificationRuleDto Rejected,
    NotificationRuleDto DraftSaved,
    int UpcomingLeadHours,
    List<Guid>? AdminRecipientAccountIds = null);

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

/// <summary>
/// Request body for <c>POST /api/expressions/dependencies</c> — a batch of expressions to parse
/// for identifier references, in the order the caller wants results back in. Powers the schema
/// editor's dependency diagram: the SPA sends every rule on the (possibly unsaved) schema in one
/// round trip rather than one request per rule.
/// </summary>
/// <param name="Expressions">Expression sources, one per rule. Order is preserved in the response.</param>
public sealed record ExpressionDependencyBatchRequest(IReadOnlyList<string> Expressions);

/// <summary>One expression's parse outcome within an <see cref="ExpressionDependencyBatchRequest"/>.</summary>
/// <param name="Identifiers">
/// Every identifier the expression references, verbatim as written (case preserved, so
/// <c>[name.minimum]</c>/<c>[name.maximum]</c> bound keys keep their suffix — callers matching
/// against schema value names should do so case-insensitively and strip a trailing
/// <c>.minimum</c>/<c>.maximum</c> themselves). Empty when the expression is blank or failed to parse.
/// </param>
/// <param name="Error">Parser error message when the expression failed to parse; <c>null</c> on success (including a blank input).</param>
public sealed record ExpressionDependencyResult(IReadOnlyList<string> Identifiers, string? Error);

/// <summary>Response body for <c>POST /api/expressions/dependencies</c>. <see cref="Results"/> has exactly one entry per input expression, in the same order.</summary>
public sealed record ExpressionDependencyBatchResponse(IReadOnlyList<ExpressionDependencyResult> Results);

/// <summary>Generic paged response wrapper. Identical to <c>Ingest.Core.Common.PagedResult&lt;T&gt;</c> but lives in the API layer to keep Core wire-agnostic.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items in this page.</param>
/// <param name="Total">Total items across all pages.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Page size.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

/// <summary>Wire representation of a single audit-log entry.</summary>
/// <param name="Id">Stable identifier of the log entry.</param>
/// <param name="Timestamp">UTC time the change occurred.</param>
/// <param name="TargetType">Type of object that changed (lets the UI resolve the id without scanning every collection).</param>
/// <param name="TargetId">Id of the changed object.</param>
/// <param name="TargetName">Name of the changed object when it has one; otherwise <c>null</c>.</param>
/// <param name="Change">The kind of change (Create, Edit or Delete).</param>
/// <param name="ActorId">Id of the account that made the change, or <c>null</c>.</param>
/// <param name="ActorName">Machine name of the account that made the change, or <c>null</c>.</param>
/// <param name="Note">Optional free-form context (e.g. a submission rejection reason); <c>null</c> when none.</param>
public sealed record AuditLogDto(
    Guid Id,
    DateTime Timestamp,
    AuditTargetType TargetType,
    Guid TargetId,
    string? TargetName,
    AuditChangeType Change,
    Guid? ActorId,
    string? ActorName,
    string? Note)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static AuditLogDto From(AuditLog a) => new(
        a.Id, a.Timestamp, a.TargetType, a.TargetId, a.TargetName, a.Change, a.ActorId, a.ActorName, a.Note);
}

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
/// <param name="SubmittedAt">When the parent submission was first accepted by the API.</param>
/// <param name="Note">Note carried from the original sample.</param>
/// <param name="Cadence">Cadence snapshot from the schema definition.</param>
/// <param name="PeriodStart">Cadence bucket start (inclusive).</param>
/// <param name="PeriodEnd">Cadence bucket end (exclusive).</param>
/// <param name="IsDerived">True when the row was computed from a calculated schema value.</param>
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
    DateTime SubmittedAt,
    string? Note,
    Cadence Cadence,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    bool IsDerived = false)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static SampleProjectionDto From(SampleProjection s) => new(
        s.Id, s.SubmissionId, s.ServiceAccountId, s.ServiceName, s.SchemaName, s.ValueName, s.ValueType,
        s.StringValue, s.NumberValue, s.IntegerValue, s.DateValue, s.BooleanValue,
        s.Timestamp, s.SubmittedAt, s.Note, s.Cadence, s.PeriodStart, s.PeriodEnd, s.IsDerived);
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
/// <param name="PeriodStart">Inclusive start of the cadence window.</param>
/// <param name="PeriodEnd">Exclusive end of the cadence window.</param>
/// <param name="Period">Whether the window is the current (still-open) one or the previous (overdue) one.</param>
/// <param name="Entries">Rows sorted by service label then schema label.</param>
public sealed record MissingByCadenceDto(
    Cadence Cadence,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    MissingPeriodKind Period,
    List<MissingSubmissionEntryDto> Entries);

/// <summary>Detailed missing-submissions report for a single cadence and a single (possibly historical) window addressed by <paramref name="Offset"/>.</summary>
/// <param name="Cadence">Cadence the window belongs to.</param>
/// <param name="Offset">Signed bucket offset from "now" (0 = current, negative = past).</param>
/// <param name="PeriodStart">Inclusive start of the window.</param>
/// <param name="PeriodEnd">Exclusive end of the window.</param>
/// <param name="Entries">Rows sorted by service label then schema label.</param>
public sealed record MissingPeriodReportDto(
    Cadence Cadence,
    int Offset,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    List<MissingSubmissionEntryDto> Entries);

/// <summary>One point on the "missing submissions over time" trend for a single cadence.</summary>
/// <param name="Offset">Signed bucket offset from "now" (0 = current, negative = past).</param>
/// <param name="PeriodStart">Inclusive start of the window.</param>
/// <param name="PeriodEnd">Exclusive end of the window.</param>
/// <param name="TotalMissing">Total number of missing required values across every service and schema in the window.</param>
public sealed record MissingHistoryPointDto(
    int Offset,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TotalMissing);

/// <summary>The "missing submissions over time" trend for a single cadence, oldest period first.</summary>
/// <param name="Cadence">Cadence the trend covers.</param>
/// <param name="Points">One point per period, ordered oldest → current.</param>
public sealed record MissingHistoryDto(
    Cadence Cadence,
    List<MissingHistoryPointDto> Points);

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
/// <param name="GreenMin">Optional lower edge of the ideal (green) range, overlaid on the chart.</param>
/// <param name="GreenMax">Optional upper edge of the ideal (green) range, overlaid on the chart.</param>
/// <param name="AmberMin">Optional lower edge of the acceptable (amber) range, overlaid on the chart.</param>
/// <param name="AmberMax">Optional upper edge of the acceptable (amber) range, overlaid on the chart.</param>
/// <param name="Buckets">Buckets ordered chronologically.</param>
public sealed record SchemaValueHistoryDto(
    string ValueName,
    string? Label,
    SchemaValueType Type,
    Cadence Cadence,
    string? Unit,
    double? GreenMin,
    double? GreenMax,
    double? AmberMin,
    double? AmberMax,
    List<HistoryBucketDto> Buckets);

/// <summary>Historical view of a schema: one timeline per numeric value, grouped by cadence.</summary>
/// <param name="SchemaName">Schema name.</param>
/// <param name="Label">Schema label.</param>
/// <param name="Values">Per-value timelines (only numeric values appear).</param>
public sealed record SchemaHistoryDto(
    string SchemaName,
    string? Label,
    List<SchemaValueHistoryDto> Values);

/// <summary>One service's reduced value inside an Explore bucket.</summary>
/// <param name="ServiceId">Service account id (join key back to <see cref="ExploreSeriesResponse.Services"/>).</param>
/// <param name="Value">The bucket reduced by the requested aggregation, for this service only.</param>
/// <param name="Count">Number of samples this service contributed to the bucket.</param>
/// <param name="Z">Anomaly score against this service's preceding history; <c>null</c> unless anomaly scoring was requested.</param>
/// <param name="IsAnomaly">Whether <paramref name="Z"/> crossed the requested threshold.</param>
public sealed record ExploreServicePointDto(Guid ServiceId, double Value, int Count, double? Z, bool IsAnomaly)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreServicePointDto From(ExploreServicePoint p) => new(p.ServiceId, p.Value, p.Count, p.Z, p.IsAnomaly);
}

/// <summary>One cadence bucket of an Explore value series, with the overall and per-service reductions.</summary>
/// <param name="PeriodStart">Inclusive bucket start.</param>
/// <param name="PeriodEnd">Exclusive bucket end.</param>
/// <param name="Value">The bucket reduced across every in-scope service.</param>
/// <param name="Count">Total samples folded into the bucket.</param>
/// <param name="Services">Per-service reductions.</param>
/// <param name="Z">Anomaly score of the overall (combined) value against preceding buckets; <c>null</c> unless anomaly scoring was requested.</param>
/// <param name="IsAnomaly">Whether the overall <paramref name="Z"/> crossed the requested threshold.</param>
public sealed record ExploreBucketDto(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    double Value,
    int Count,
    List<ExploreServicePointDto> Services,
    double? Z,
    bool IsAnomaly)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreBucketDto From(ExploreBucket b) => new(
        b.PeriodStart, b.PeriodEnd, b.Value, b.Count,
        b.Services.Select(ExploreServicePointDto.From).ToList(), b.Z, b.IsAnomaly);
}

/// <summary>A single value's bucketed Explore timeline.</summary>
/// <param name="ValueName">Machine-style value name.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Type">Value type (always numeric).</param>
/// <param name="Cadence">Cadence the buckets follow.</param>
/// <param name="Unit">Unit of measure.</param>
/// <param name="Buckets">Buckets ordered chronologically.</param>
public sealed record ExploreValueSeriesDto(
    string ValueName,
    string? Label,
    SchemaValueType Type,
    Cadence Cadence,
    string? Unit,
    List<ExploreBucketDto> Buckets)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreValueSeriesDto From(ExploreValueSeries v) => new(
        v.ValueName, v.Label, v.Type, v.Cadence, v.Unit,
        v.Buckets.Select(ExploreBucketDto.From).ToList());
}

/// <summary>A service appearing in an Explore result, with its label resolved.</summary>
/// <param name="ServiceId">Service account id.</param>
/// <param name="ServiceName">Machine-style service name.</param>
/// <param name="ServiceLabel">Friendly label, or <c>null</c>.</param>
public sealed record ExploreServiceRefDto(Guid ServiceId, string ServiceName, string? ServiceLabel)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreServiceRefDto From(ExploreServiceRef s) => new(s.ServiceId, s.ServiceName, s.ServiceLabel);
}

/// <summary>Wire shape of <c>GET /api/admin/explore/series</c>: a per-value, per-cadence, per-service breakdown.</summary>
/// <param name="SchemaName">Schema that was explored.</param>
/// <param name="SchemaLabel">Friendly schema label.</param>
/// <param name="Aggregation">The aggregation applied to every bucket.</param>
/// <param name="From">Resolved lower bound echoed back from the request.</param>
/// <param name="To">Resolved upper bound echoed back from the request.</param>
/// <param name="Services">Every service appearing in the result.</param>
/// <param name="Values">One timeline per in-scope numeric value.</param>
public sealed record ExploreSeriesResponse(
    string SchemaName,
    string? SchemaLabel,
    ExploreAggregation Aggregation,
    DateTime? From,
    DateTime? To,
    List<ExploreServiceRefDto> Services,
    List<ExploreValueSeriesDto> Values)
{
    /// <summary>Project the domain result onto the wire shape. (Named <c>FromResult</c> rather than the usual <c>From</c> because the record already carries a <c>From</c> date property.)</summary>
    public static ExploreSeriesResponse FromResult(ExploreSeriesResult r) => new(
        r.SchemaName, r.SchemaLabel, r.Aggregation, r.From, r.To,
        r.Services.Select(ExploreServiceRefDto.From).ToList(),
        r.Values.Select(ExploreValueSeriesDto.From).ToList());
}

/// <summary>One service's RAG-classified sample for a banded value on the scorecard.</summary>
/// <param name="ServiceId">Service account id (join key back to <see cref="ExploreScorecardResponse.Services"/>).</param>
/// <param name="SubmissionId">Submission the sample came from, so the UI can deep-link to it; <c>null</c> when missing.</param>
/// <param name="Value">The numeric value the service reported; <c>null</c> when missing.</param>
/// <param name="Status">Where <paramref name="Value"/> falls in the value's target band; <c>null</c> when missing.</param>
/// <param name="PeriodStart">Inclusive start of the period the sample belongs to (or was expected for).</param>
/// <param name="PeriodEnd">Exclusive end of that period.</param>
public sealed record ExploreScorecardCellDto(
    Guid ServiceId,
    Guid? SubmissionId,
    double? Value,
    RagStatus? Status,
    DateTime PeriodStart,
    DateTime PeriodEnd)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreScorecardCellDto From(ExploreScorecardCell c) => new(
        c.ServiceId, c.SubmissionId, c.Value, c.Status, c.PeriodStart, c.PeriodEnd);
}

/// <summary>A banded value and the latest RAG status of every service that reported it.</summary>
/// <param name="ValueName">Machine-style value name.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Unit">Unit of measure.</param>
/// <param name="Cells">One cell per reporting service.</param>
public sealed record ExploreScorecardValueDto(
    string ValueName,
    string? Label,
    string? Unit,
    List<ExploreScorecardCellDto> Cells)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreScorecardValueDto From(ExploreScorecardValue v) => new(
        v.ValueName, v.Label, v.Unit, v.Cells.Select(ExploreScorecardCellDto.From).ToList());
}

/// <summary>One enabled schema's banded values, grouped under the schema for the scorecard.</summary>
/// <param name="SchemaName">Machine-style schema name.</param>
/// <param name="SchemaLabel">Friendly schema label.</param>
/// <param name="Values">Banded numeric values with at least one reporting service.</param>
public sealed record ExploreScorecardSchemaDto(
    string SchemaName,
    string? SchemaLabel,
    List<ExploreScorecardValueDto> Values)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreScorecardSchemaDto From(ExploreScorecardSchema s) => new(
        s.SchemaName, s.SchemaLabel, s.Values.Select(ExploreScorecardValueDto.From).ToList());
}

/// <summary>Wire shape of <c>GET /api/admin/explore/scorecard</c>: a cross-schema RAG status board.</summary>
/// <param name="Services">Every service appearing in the result, with labels resolved.</param>
/// <param name="Schemas">Enabled schemas that have at least one banded value with data.</param>
public sealed record ExploreScorecardResponse(
    List<ExploreServiceRefDto> Services,
    List<ExploreScorecardSchemaDto> Schemas)
{
    /// <summary>Project the domain result onto the wire shape.</summary>
    public static ExploreScorecardResponse FromResult(ExploreScorecardResult r) => new(
        r.Services.Select(ExploreServiceRefDto.From).ToList(),
        r.Schemas.Select(ExploreScorecardSchemaDto.From).ToList());
}

/// <summary>One service's anomaly result for a numeric value in the target period.</summary>
/// <param name="ServiceId">Service account id (join key back to <see cref="ExploreAnomalyResponse.Services"/>).</param>
/// <param name="SubmissionId">Submission the tested sample came from, so the UI can deep-link; <c>null</c> when missing.</param>
/// <param name="Value">The value tested; <c>null</c> when missing.</param>
/// <param name="Z">The standardised score; <c>null</c> when missing or with too little history.</param>
/// <param name="State">Anomaly classification (<c>Normal</c>/<c>Anomaly</c>); <c>null</c> when missing.</param>
/// <param name="PeriodStart">Inclusive start of the period tested (or expected).</param>
/// <param name="PeriodEnd">Exclusive end of that period.</param>
public sealed record ExploreAnomalyCellDto(
    Guid ServiceId,
    Guid? SubmissionId,
    double? Value,
    double? Z,
    AnomalyState? State,
    DateTime PeriodStart,
    DateTime PeriodEnd)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreAnomalyCellDto From(ExploreAnomalyCell c) => new(
        c.ServiceId, c.SubmissionId, c.Value, c.Z, c.State, c.PeriodStart, c.PeriodEnd);
}

/// <summary>A numeric value and every applicable service's anomaly result for the target period.</summary>
/// <param name="ValueName">Machine-style value name.</param>
/// <param name="Label">Friendly label.</param>
/// <param name="Unit">Unit of measure.</param>
/// <param name="Cells">One cell per applicable service.</param>
public sealed record ExploreAnomalyValueDto(
    string ValueName,
    string? Label,
    string? Unit,
    List<ExploreAnomalyCellDto> Cells)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreAnomalyValueDto From(ExploreAnomalyValue v) => new(
        v.ValueName, v.Label, v.Unit, v.Cells.Select(ExploreAnomalyCellDto.From).ToList());
}

/// <summary>One scanned schema's numeric values for the anomaly board, grouped under the schema.</summary>
/// <param name="SchemaName">Machine-style schema name.</param>
/// <param name="SchemaLabel">Friendly schema label.</param>
/// <param name="Values">Numeric values that apply to at least one service.</param>
public sealed record ExploreAnomalySchemaDto(
    string SchemaName,
    string? SchemaLabel,
    List<ExploreAnomalyValueDto> Values)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static ExploreAnomalySchemaDto From(ExploreAnomalySchema s) => new(
        s.SchemaName, s.SchemaLabel, s.Values.Select(ExploreAnomalyValueDto.From).ToList());
}

/// <summary>Wire shape of <c>GET /api/admin/explore/anomalies</c>: a per-period anomaly status board.</summary>
/// <param name="Services">Every service appearing in the result, with labels resolved.</param>
/// <param name="Schemas">Scanned schemas with at least one numeric value applying to a service.</param>
public sealed record ExploreAnomalyResponse(
    List<ExploreServiceRefDto> Services,
    List<ExploreAnomalySchemaDto> Schemas)
{
    /// <summary>Project the domain result onto the wire shape.</summary>
    public static ExploreAnomalyResponse FromResult(ExploreAnomalyResult r) => new(
        r.Services.Select(ExploreServiceRefDto.From).ToList(),
        r.Schemas.Select(ExploreAnomalySchemaDto.From).ToList());
}

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

/// <summary>Wire shape of a webhook endpoint. The signing secret is write-once: only <see cref="HasSecret"/> is ever returned.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">Friendly name.</param>
/// <param name="Url">Destination URL.</param>
/// <param name="Enabled">Whether the endpoint currently receives deliveries.</param>
/// <param name="Events">Subscribed event kinds.</param>
/// <param name="ServiceAccountId">Optional service filter; null = all services.</param>
/// <param name="Description">Optional human description.</param>
/// <param name="HasSecret">True when a signing secret is set (the value itself is never exposed).</param>
/// <param name="CreatedAt">Creation timestamp (UTC).</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
public sealed record WebhookEndpointDto(
    Guid Id,
    string Name,
    string Url,
    bool Enabled,
    List<WebhookEventKind> Events,
    Guid? ServiceAccountId,
    string? Description,
    bool HasSecret,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string? ModifiedBy)
{
    /// <summary>Project the domain entity onto the wire shape, omitting the secret.</summary>
    public static WebhookEndpointDto From(WebhookEndpoint e) => new(
        e.Id, e.Name, e.Url, e.Enabled, e.Events, e.ServiceAccountId, e.Description,
        !string.IsNullOrEmpty(e.SecretCipher), e.CreatedAt, e.ModifiedAt, e.ModifiedBy);
}

/// <summary>Body for <c>POST /api/admin/webhooks</c>.</summary>
/// <param name="Name">Friendly name.</param>
/// <param name="Url">Absolute http(s) destination URL.</param>
/// <param name="Enabled">Whether the endpoint is active; defaults to true.</param>
/// <param name="Events">Subscribed event kinds.</param>
/// <param name="ServiceAccountId">Optional service filter; null = all services.</param>
/// <param name="Description">Optional human description.</param>
/// <param name="GenerateSecret">When true, mint a signing secret and return it once in the response.</param>
public sealed record CreateWebhookEndpointRequest(
    string Name,
    string Url,
    bool Enabled = true,
    List<WebhookEventKind>? Events = null,
    Guid? ServiceAccountId = null,
    string? Description = null,
    bool GenerateSecret = false);

/// <summary>Body for <c>PUT /api/admin/webhooks/{id}</c>. The signing secret is managed via rotate, not here.</summary>
/// <param name="Name">Friendly name.</param>
/// <param name="Url">Absolute http(s) destination URL.</param>
/// <param name="Enabled">Whether the endpoint is active.</param>
/// <param name="Events">Subscribed event kinds.</param>
/// <param name="ServiceAccountId">Optional service filter; null = all services.</param>
/// <param name="Description">Optional human description.</param>
public sealed record UpdateWebhookEndpointRequest(
    string Name,
    string Url,
    bool Enabled,
    List<WebhookEventKind>? Events = null,
    Guid? ServiceAccountId = null,
    string? Description = null);

/// <summary>Response when an endpoint is created. <paramref name="Secret"/> is non-null only when a secret was generated, and is shown exactly once.</summary>
/// <param name="Endpoint">The created endpoint.</param>
/// <param name="Secret">The plaintext signing secret, or null when none was generated.</param>
public sealed record WebhookEndpointCreatedResponse(WebhookEndpointDto Endpoint, string? Secret);

/// <summary>Response when a signing secret is rotated: carries the plaintext exactly once.</summary>
/// <param name="Endpoint">The updated endpoint.</param>
/// <param name="Secret">The new plaintext signing secret.</param>
public sealed record WebhookSecretResponse(WebhookEndpointDto Endpoint, string Secret);

/// <summary>Wire shape of one webhook delivery for the admin "Deliveries" panel.</summary>
/// <param name="Id">Delivery id (also sent to the consumer as the delivery header).</param>
/// <param name="EndpointId">Target endpoint.</param>
/// <param name="Url">Destination URL (snapshot).</param>
/// <param name="Event">Dotted event name as the consumer sees it (e.g. <c>submission.accepted</c>, <c>webhook.test</c>).</param>
/// <param name="EventId">Deterministic event id / idempotency key.</param>
/// <param name="Status">Delivery status.</param>
/// <param name="Attempts">Number of delivery attempts.</param>
/// <param name="LastError">Last delivery error, if any.</param>
/// <param name="LastStatusCode">HTTP status of the last attempt, if a response was received.</param>
/// <param name="CreatedAt">Enqueue timestamp.</param>
/// <param name="DeliveredAt">Delivery timestamp, if delivered.</param>
/// <param name="NextAttemptAt">Earliest time the next retry runs, if pending after a failure.</param>
/// <param name="RelatedAccountId">Related service account, if any.</param>
public sealed record WebhookDeliveryDto(
    Guid Id,
    Guid EndpointId,
    string Url,
    string Event,
    string EventId,
    WebhookDeliveryStatus Status,
    int Attempts,
    string? LastError,
    int? LastStatusCode,
    DateTime CreatedAt,
    DateTime? DeliveredAt,
    DateTime? NextAttemptAt,
    Guid? RelatedAccountId)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static WebhookDeliveryDto From(WebhookDelivery d) => new(
        d.Id, d.EndpointId, d.Url,
        d.EventId.StartsWith("test:", StringComparison.Ordinal) ? "webhook.test" : d.Kind.ToWire(),
        d.EventId, d.Status, d.Attempts, d.LastError, d.LastStatusCode,
        d.CreatedAt, d.DeliveredAt, d.NextAttemptAt, d.RelatedAccountId);
}

// ── Integrations (Microsoft Teams) ──────────────────────────────────────────────────────────

/// <summary>Wire shape of an integration schedule.</summary>
/// <param name="Frequency">How often the pass runs (Daily / Weekly / Monthly / Quarterly / SemiAnnually / Yearly).</param>
/// <param name="Days">Weekdays the pass runs on (Weekly only); empty = every day.</param>
/// <param name="DayOfMonth">Day of the month (1-31) for the Monthly-and-longer frequencies; clamped to month length.</param>
/// <param name="LastDayOfMonth">When true, run on the last day of the month instead of <paramref name="DayOfMonth"/>.</param>
/// <param name="AnchorMonth">Anchor month (1-12) for Quarterly / SemiAnnually / Yearly.</param>
/// <param name="HourUtc">Hour of day (UTC, 0-23).</param>
/// <param name="MinuteUtc">Minute of the hour (UTC, 0-59).</param>
public sealed record IntegrationScheduleDto(
    IntegrationFrequency Frequency,
    List<DayOfWeek> Days,
    int DayOfMonth,
    bool LastDayOfMonth,
    int AnchorMonth,
    int HourUtc,
    int MinuteUtc)
{
    /// <summary>Project the domain object onto the wire shape.</summary>
    public static IntegrationScheduleDto From(IntegrationSchedule s) =>
        new(s.Frequency, s.Days, s.DayOfMonth, s.LastDayOfMonth, s.AnchorMonth, s.HourUtc, s.MinuteUtc);

    /// <summary>Build the domain object from the wire shape.</summary>
    public IntegrationSchedule ToEntity() => new()
    {
        Frequency = Frequency,
        Days = Days ?? new(),
        DayOfMonth = DayOfMonth,
        LastDayOfMonth = LastDayOfMonth,
        AnchorMonth = AnchorMonth,
        HourUtc = HourUtc,
        MinuteUtc = MinuteUtc,
    };
}

/// <summary>Wire shape of a Teams target. The captured conversation reference is never exposed.</summary>
/// <param name="Kind">User or channel.</param>
/// <param name="TargetId">Stable id of the user (Entra object id / UPN / email) or channel.</param>
/// <param name="DisplayName">Optional friendly label for the target.</param>
/// <param name="HasConversation">True once the bot has been contacted and a conversation reference is stored.</param>
public sealed record TeamsTargetDto(TeamsTargetKind Kind, string TargetId, string? DisplayName, bool HasConversation)
{
    /// <summary>Project the domain object onto the wire shape, omitting the conversation reference.</summary>
    public static TeamsTargetDto From(TeamsTarget t) => new(
        t.Kind, t.TargetId, t.DisplayName, !string.IsNullOrEmpty(t.ConversationReferenceJson));
}

/// <summary>Target fields a client may set when creating/updating an integration.</summary>
/// <param name="Kind">User or channel.</param>
/// <param name="TargetId">Stable id of the user or channel.</param>
/// <param name="DisplayName">Optional friendly label.</param>
public sealed record TeamsTargetInput(TeamsTargetKind Kind, string TargetId, string? DisplayName = null)
{
    /// <summary>Build the domain object (conversation reference is preserved by the service layer).</summary>
    public TeamsTarget ToEntity() => new() { Kind = Kind, TargetId = TargetId?.Trim() ?? "", DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName!.Trim() };
}

/// <summary>Wire shape of an integration.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Label">Optional friendly label.</param>
/// <param name="Enabled">Whether the integration is active.</param>
/// <param name="Kind">The provider (Microsoft Teams today).</param>
/// <param name="ServiceIds">Scoped services; empty = all.</param>
/// <param name="SchemaIds">Scoped schemas; empty = all.</param>
/// <param name="Schedule">When the scheduled pass runs.</param>
/// <param name="Teams">Teams target.</param>
/// <param name="CreatedAt">Creation timestamp (UTC).</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
public sealed record IntegrationDto(
    Guid Id,
    string? Label,
    bool Enabled,
    IntegrationKind Kind,
    List<Guid> ServiceIds,
    List<Guid> SchemaIds,
    IntegrationScheduleDto Schedule,
    TeamsTargetDto Teams,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string? ModifiedBy)
{
    /// <summary>Project the domain entity onto the wire shape.</summary>
    public static IntegrationDto From(Integration i) => new(
        i.Id, i.Label, i.Enabled, i.Kind, i.ServiceIds, i.SchemaIds,
        IntegrationScheduleDto.From(i.Schedule), TeamsTargetDto.From(i.Teams),
        i.CreatedAt, i.ModifiedAt, i.ModifiedBy);
}

/// <summary>Body for <c>POST /api/admin/integrations</c> and <c>PUT /api/admin/integrations/{id}</c>.</summary>
/// <param name="Label">Optional friendly label.</param>
/// <param name="Enabled">Whether the integration is active; defaults to true.</param>
/// <param name="Kind">The provider; defaults to Microsoft Teams.</param>
/// <param name="ServiceIds">Scoped services; empty/null = all.</param>
/// <param name="SchemaIds">Scoped schemas; empty/null = all.</param>
/// <param name="Schedule">When the scheduled pass runs; defaults to 08:00 UTC daily.</param>
/// <param name="Teams">Teams target (required for the Teams provider).</param>
public sealed record IntegrationRequest(
    string? Label = null,
    bool Enabled = true,
    IntegrationKind Kind = IntegrationKind.MicrosoftTeams,
    List<Guid>? ServiceIds = null,
    List<Guid>? SchemaIds = null,
    IntegrationScheduleDto? Schedule = null,
    TeamsTargetInput? Teams = null)
{
    /// <summary>Build the domain entity from the request (conversation reference handled by the service).</summary>
    public Integration ToEntity() => new()
    {
        Label = string.IsNullOrWhiteSpace(Label) ? null : Label!.Trim(),
        Enabled = Enabled,
        Kind = Kind,
        ServiceIds = ServiceIds ?? new(),
        SchemaIds = SchemaIds ?? new(),
        Schedule = Schedule?.ToEntity() ?? new IntegrationSchedule(),
        Teams = Teams?.ToEntity() ?? new TeamsTarget(),
    };
}

/// <summary>Wire shape of the Teams connection settings. The bot secret is write-once and never returned.</summary>
/// <param name="AppId">Microsoft App (client) id.</param>
/// <param name="TenantId">Entra tenant id (null/empty for multi-tenant).</param>
/// <param name="SingleTenant">Whether the bot app registration is single-tenant.</param>
/// <param name="HasPassword">True when a bot secret is stored.</param>
/// <param name="IsConfigured">True when both an app id and a secret are present.</param>
/// <param name="ModifiedAt">Last update timestamp (UTC).</param>
/// <param name="ModifiedBy">Name of the last modifier.</param>
public sealed record TeamsConnectionDto(
    string? AppId,
    string? TenantId,
    bool SingleTenant,
    bool HasPassword,
    bool IsConfigured,
    DateTime ModifiedAt,
    string? ModifiedBy)
{
    /// <summary>Project the domain entity onto the wire shape, omitting the secret.</summary>
    public static TeamsConnectionDto From(TeamsConnectionSettings s) => new(
        s.AppId, s.TenantId, s.SingleTenant,
        !string.IsNullOrEmpty(s.AppPasswordCipher), s.IsConfigured, s.ModifiedAt, s.ModifiedBy);
}

/// <summary>Body for <c>PUT /api/admin/integrations/connection</c>.</summary>
/// <param name="AppId">Microsoft App (client) id.</param>
/// <param name="TenantId">Entra tenant id (null/empty for multi-tenant).</param>
/// <param name="SingleTenant">Whether the bot app registration is single-tenant.</param>
/// <param name="UpdatePassword">When true, <paramref name="Password"/> replaces the stored secret (blank clears it).</param>
/// <param name="Password">New bot client secret; only consulted when <paramref name="UpdatePassword"/> is true.</param>
public sealed record UpdateTeamsConnectionRequest(
    string? AppId,
    string? TenantId,
    bool SingleTenant = false,
    bool UpdatePassword = false,
    string? Password = null);
