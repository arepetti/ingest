using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// Server-wide singleton holding miscellaneous admin-configurable settings: the "areas" account
/// tag list, the cadence period anchors, and the ingestion kill switch. At most one document
/// exists; an absent one reads back as an empty/default configuration, which keeps fresh and
/// legacy deployments back-compatible.
/// </summary>
public sealed class AppConfiguration : AuditedEntity
{
    /// <summary>
    /// Ordered list of area names offered when editing an account. When empty the account editor
    /// falls back to a free-text area field.
    /// </summary>
    public List<string> Areas { get; set; } = new();

    /// <summary>
    /// Month (1-12) the fiscal year begins on; also anchors Quarterly/SemiAnnually as fiscal
    /// sub-periods. <c>null</c> = January (calendar year), matching the pre-configuration behaviour.
    /// </summary>
    public int? FiscalYearStartMonth { get; set; }

    /// <summary>Day of week a Weekly cadence bucket begins on. <c>null</c> = Monday.</summary>
    public DayOfWeek? WeekStartDay { get; set; }

    /// <summary>Day of month (1-28) a Monthly cadence bucket begins on. <c>null</c> = the 1st.</summary>
    public int? MonthStartDay { get; set; }

    /// <summary>
    /// A UTC instant a Fortnightly cadence bucket boundary is aligned to (only its date matters).
    /// <c>null</c> = 2001-01-01 (a Monday), the original hard-coded anchor.
    /// </summary>
    public DateTime? FortnightAnchor { get; set; }

    /// <summary>
    /// Global "close all submissions" kill switch. When true, service-facing ingestion (service
    /// account create/replace, bulk import, Teams inbound) is rejected with a 503; every other
    /// operation (reads, OData, admin create/replace for remediation, schemas, settings) is
    /// unaffected.
    /// </summary>
    public bool SubmissionsClosed { get; set; }

    /// <summary>
    /// Optional operator-facing message shown in the site-wide banner and returned in the 503 body
    /// while <see cref="SubmissionsClosed"/> is true.
    /// </summary>
    public string? SubmissionsClosedMessage { get; set; }

    /// <summary>
    /// Per-cadence overrides for the submission window (how long before/after the bucket itself a
    /// service may create/edit a sample for it). <c>null</c>, or any individual cadence/field left
    /// <c>null</c>, resolves to no offset/grace — i.e. the window is exactly the bucket, the
    /// historical behaviour. See <see cref="Ingest.Core.Validation.CadenceWindows"/>.
    /// </summary>
    public CadenceWindowSettings? CadenceWindows { get; set; }
}

/// <summary>Per-cadence <see cref="CadenceWindowOverride"/> overrides. Every field is optional.</summary>
public sealed class CadenceWindowSettings
{
    /// <summary>Override for the Daily cadence.</summary>
    public CadenceWindowOverride? Daily { get; set; }

    /// <summary>Override for the Weekly cadence.</summary>
    public CadenceWindowOverride? Weekly { get; set; }

    /// <summary>Override for the Fortnightly cadence.</summary>
    public CadenceWindowOverride? Fortnightly { get; set; }

    /// <summary>Override for the Monthly cadence.</summary>
    public CadenceWindowOverride? Monthly { get; set; }

    /// <summary>Override for the Quarterly cadence.</summary>
    public CadenceWindowOverride? Quarterly { get; set; }

    /// <summary>Override for the SemiAnnually cadence.</summary>
    public CadenceWindowOverride? SemiAnnually { get; set; }

    /// <summary>Override for the Yearly cadence.</summary>
    public CadenceWindowOverride? Yearly { get; set; }
}

/// <summary>
/// One cadence's window offsets. <c>null</c> on either field resolves to <c>0</c> (no offset/grace).
/// </summary>
public sealed class CadenceWindowOverride
{
    /// <summary>Hours after the bucket's start before the window opens. <c>null</c> = 0 (opens with the bucket).</summary>
    public double? OpenOffsetHours { get; set; }

    /// <summary>Hours after the bucket's end during which the window stays open. <c>null</c> = 0 (closes with the bucket).</summary>
    public double? GraceHours { get; set; }
}
