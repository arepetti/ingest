namespace Ingest.Infrastructure.Approvals;

/// <summary>
/// Binding target for the <c>Approval</c> configuration section. The whole submission-approval
/// feature is gated by <see cref="Enabled"/>, mirroring the email/webhooks master switches: when it
/// is <c>false</c> the approval endpoints return 404, no submission is ever held as Pending, and the
/// SPA hides every approval-related control — behaviour is identical to a build without approval.
/// </summary>
public sealed class ApprovalOptions
{
    /// <summary>
    /// Master switch. Defaults to <c>true</c> so the feature is available out of the box (it still
    /// does nothing until an admin configures a schema or the global default). A council that does
    /// not use approval can set this to <c>false</c> to remove the feature entirely.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
