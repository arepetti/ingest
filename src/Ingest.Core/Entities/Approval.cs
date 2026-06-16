using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// Lifecycle state of a <see cref="Submission"/> with respect to the optional approval workflow.
/// Defaults to <see cref="NotRequired"/> so legacy documents (and every submission when approval
/// is off) behave exactly as before — they are live the moment they're accepted.
/// </summary>
public enum ApprovalStatus
{
    /// <summary>No approval is required; the submission is live as soon as it's accepted (the legacy behaviour).</summary>
    NotRequired = 0,

    /// <summary>Approval is required and not yet complete; the submission is held out of the live read model.</summary>
    Pending = 1,

    /// <summary>All required approvers have approved; the submission is live.</summary>
    Approved = 2,

    /// <summary>A reviewer rejected the submission; it is excluded from the live read model but remains visible in the UI.</summary>
    Rejected = 3,
}

/// <summary>
/// Where a submission originated. Lets an approval policy apply selectively (e.g. require approval
/// for manually typed entries but let automated API feeds through, or vice-versa).
/// </summary>
public enum SubmissionSource
{
    /// <summary>Submitted programmatically through the REST API (the default for direct callers).</summary>
    Api = 0,

    /// <summary>Entered by a human through the web console (the SPA marks its writes as manual).</summary>
    Manual = 1,
}

/// <summary>How a schema (or the global default) decides whether submissions need approval.</summary>
public enum ApprovalMode
{
    /// <summary>Approval is never required.</summary>
    None = 0,

    /// <summary>Defer to the server-wide global default policy. Only meaningful at the schema level.</summary>
    UseGlobalDefault = 1,

    /// <summary>Approval is required, governed by this policy's own approver list.</summary>
    Required = 2,
}

/// <summary>Which submission sources an approval policy applies to.</summary>
public enum ApprovalSourceScope
{
    /// <summary>Apply to both manual and API submissions (the default).</summary>
    Both = 0,

    /// <summary>Apply only to manual (web console) submissions.</summary>
    ManualOnly = 1,

    /// <summary>Apply only to API (programmatic) submissions.</summary>
    ApiOnly = 2,
}

/// <summary>Whether a designated approver must approve or is merely allowed to.</summary>
public enum ApproverRequirement
{
    /// <summary>This approver must approve before the submission can go live.</summary>
    Required = 0,

    /// <summary>This approver may approve but is not required for the submission to go live.</summary>
    Optional = 1,
}

/// <summary>The decision a reviewer recorded against a submission.</summary>
public enum ApprovalDecision
{
    /// <summary>The reviewer approved the submission.</summary>
    Approved = 0,

    /// <summary>The reviewer rejected the submission.</summary>
    Rejected = 1,
}

/// <summary>What kind of approver an <see cref="ApproverSpec"/> designates.</summary>
public enum ApproverKind
{
    /// <summary>A specific, named account (identified by <see cref="ApproverSpec.AccountId"/>).</summary>
    Account = 0,

    /// <summary>
    /// The owner of the submission's own service account — resolved per submission to the account
    /// that sent it (so the submitting service, or a user on it, can review its own data). The
    /// designated <see cref="ApproverSpec.AccountId"/> is ignored for this kind until resolution.
    /// </summary>
    ServiceOwner = 1,
}

/// <summary>A designated approver in an approval policy: which account, and whether it's required.</summary>
public sealed class ApproverSpec
{
    /// <summary>
    /// Account designated as an approver. Ignored when <see cref="Kind"/> is
    /// <see cref="ApproverKind.ServiceOwner"/> (then it's resolved per submission to the sender).
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>What kind of approver this is (a named account, or the dynamic service owner).</summary>
    public ApproverKind Kind { get; set; } = ApproverKind.Account;

    /// <summary>Whether this approver is required or optional.</summary>
    public ApproverRequirement Requirement { get; set; } = ApproverRequirement.Required;
}

/// <summary>
/// An approval policy: whether approval is required, which sources it applies to, and the list of
/// designated approvers. Used both per-schema (<see cref="Schema.Approval"/>, where
/// <see cref="ApprovalMode.UseGlobalDefault"/> is allowed) and as the server-wide global default
/// (<see cref="ApprovalSettings.Default"/>, where only <see cref="ApprovalMode.None"/> /
/// <see cref="ApprovalMode.Required"/> are meaningful).
/// </summary>
public sealed class ApprovalPolicy
{
    /// <summary>Whether (and how) approval is required.</summary>
    public ApprovalMode Mode { get; set; } = ApprovalMode.None;

    /// <summary>Which submission sources this policy applies to.</summary>
    public ApprovalSourceScope AppliesToSources { get; set; } = ApprovalSourceScope.Both;

    /// <summary>Designated approvers, each required or optional. At least one must be required when <see cref="Mode"/> is <see cref="ApprovalMode.Required"/>.</summary>
    public List<ApproverSpec> Approvers { get; set; } = new();
}

/// <summary>
/// One reviewer's recorded decision on a submission. Persisted on the parent
/// <see cref="Submission.Approvals"/> list so the full approval history (incl. reject reasons) is
/// queryable for the UI and the submitter.
/// </summary>
public sealed class SubmissionApproval
{
    /// <summary>Account that recorded the decision.</summary>
    public required Guid ApproverAccountId { get; set; }

    /// <summary>Machine-name snapshot of the approver, for read-friendly display.</summary>
    public string? ApproverName { get; set; }

    /// <summary>The decision.</summary>
    public ApprovalDecision Decision { get; set; }

    /// <summary>When the decision was recorded (UTC).</summary>
    public DateTime DecidedAt { get; set; }

    /// <summary>Optional free-form note; used for the reject reason (shown to the submitter and reviewers).</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Server-wide singleton holding the global default approval policy that schemas can defer to via
/// <see cref="ApprovalMode.UseGlobalDefault"/>. At most one document exists; an absent document is
/// treated as <see cref="ApprovalMode.None"/> (no default approval), which keeps fresh and legacy
/// deployments back-compatible.
/// </summary>
public sealed class ApprovalSettings : AuditedEntity
{
    /// <summary>The global default policy. <see cref="ApprovalMode.UseGlobalDefault"/> is not meaningful here.</summary>
    public ApprovalPolicy Default { get; set; } = new();
}
