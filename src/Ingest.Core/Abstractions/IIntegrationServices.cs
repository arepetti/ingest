using Ingest.Core.Entities;
using Ingest.Core.Common;

namespace Ingest.Core.Abstractions;

/// <summary>
/// CRUD access to the <see cref="Integration"/> set plus read/write of the singleton
/// <see cref="TeamsConnectionSettings"/>. Integrations are soft-deleted so audit history is
/// preserved. Mirrors <c>IApprovalRulesService</c> for the list, and the email-settings service
/// for the connection singleton.
/// </summary>
public interface IIntegrationsService
{
    /// <summary>List every integration (excluding soft-deleted ones), newest first.</summary>
    Task<IReadOnlyList<Integration>> ListAsync(CancellationToken ct = default);

    /// <summary>Get one integration by id.</summary>
    /// <exception cref="Common.NotFoundException">No integration with that id.</exception>
    Task<Integration> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Create a new integration. The target and schedule are validated.</summary>
    Task<Integration> CreateAsync(Integration integration, CancellationToken ct = default);

    /// <summary>Replace an existing integration by id. Throws when it doesn't exist; preserves the captured conversation reference.</summary>
    Task<Integration> UpdateAsync(Guid id, Integration integration, CancellationToken ct = default);

    /// <summary>Soft-delete an integration by id.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Get the Teams connection settings, returning an unpersisted blank when none exist yet.</summary>
    Task<TeamsConnectionSettings> GetConnectionAsync(CancellationToken ct = default);

    /// <summary>Apply an admin edit to the Teams connection settings. The bot password is write-only.</summary>
    Task<TeamsConnectionSettings> UpdateConnectionAsync(TeamsConnectionUpdate update, CancellationToken ct = default);
}

/// <summary>Admin edit for the Teams connection. The bot password follows write-once-style semantics.</summary>
/// <param name="AppId">Microsoft App (client) id.</param>
/// <param name="TenantId">Entra tenant id; null/empty for a multi-tenant bot.</param>
/// <param name="SingleTenant">Whether the bot app registration is single-tenant.</param>
/// <param name="UpdatePassword">When true, <paramref name="Password"/> replaces the stored secret (blank clears it).</param>
/// <param name="Password">New bot client secret; only consulted when <paramref name="UpdatePassword"/> is true.</param>
public sealed record TeamsConnectionUpdate(
    string? AppId,
    string? TenantId,
    bool SingleTenant,
    bool UpdatePassword,
    string? Password);

/// <summary>Outcome of one integration run pass.</summary>
/// <param name="Prompted">Number of prompt cards enqueued for delivery.</param>
/// <param name="Skipped">Number of matched (service, schema) pairs that had nothing outstanding (or were deduped).</param>
public sealed record IntegrationRunResult(int Prompted, int Skipped);

/// <summary>
/// Finds outstanding required values for the matched (service, schema) pairs of an integration and
/// enqueues Teams prompt cards. Mirrors the notification job's reuse of <c>IStatusService</c>, and
/// the webhook outbox for delivery. Self-gates on the master switch and connection settings.
/// </summary>
public interface IIntegrationRunService
{
    /// <summary>Run every enabled integration once and enqueue any resulting prompts.</summary>
    Task<IntegrationRunResult> RunAllAsync(CancellationToken ct = default);

    /// <summary>Run a single integration now (on-demand), regardless of its schedule.</summary>
    /// <exception cref="Common.NotFoundException">No integration with that id.</exception>
    Task<IntegrationRunResult> RunOneAsync(Guid id, CancellationToken ct = default);

    /// <summary>Enqueue a diagnostic test prompt to the integration's target so an admin can verify wiring.</summary>
    /// <exception cref="Common.NotFoundException">No integration with that id.</exception>
    Task SendTestAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Outcome of one integration-delivery drain pass.</summary>
/// <param name="Sent">Prompts delivered to Teams.</param>
/// <param name="Failed">Deliveries that failed (transiently or permanently) this pass.</param>
public sealed record IntegrationDrainResult(int Sent, int Failed);

/// <summary>Drains pending integration deliveries from the outbox and sends them to Teams.</summary>
public interface IIntegrationDispatchService
{
    /// <summary>Drain up to <paramref name="max"/> due deliveries, sending each and recording the outcome.</summary>
    Task<IntegrationDrainResult> DrainAsync(int max, CancellationToken ct = default);
}

/// <summary>Result of verifying the stored Teams bot credentials against Microsoft Entra.</summary>
/// <param name="Ok">True when a bot token was successfully obtained.</param>
/// <param name="Error">Failure reason when <paramref name="Ok"/> is false.</param>
public sealed record TeamsConnectionTestResult(bool Ok, string? Error = null)
{
    /// <summary>Structured counterpart to <see cref="Error"/>.</summary>
    public Diagnostic? ErrorDetail { get; init; } =
        Error is null
            ? null
            : Diagnostic.Create(
                DiagnosticCodes.Integrations.ConnectionFailed,
                Error,
                ("detail", Error));
}

/// <summary>Resolved (decrypted) Teams bot credentials handed to <see cref="ITeamsClient"/>.</summary>
/// <param name="AppId">Microsoft App (client) id.</param>
/// <param name="Password">Bot client secret (plaintext).</param>
/// <param name="TenantId">Entra tenant id; null/empty for a multi-tenant bot.</param>
/// <param name="SingleTenant">Whether the bot app registration is single-tenant.</param>
public sealed record TeamsCredentials(string AppId, string Password, string? TenantId, bool SingleTenant);

/// <summary>
/// Thin seam over the Microsoft Bot Framework connector: verifies credentials and sends a proactive
/// Adaptive Card to a stored conversation. Implemented in the infrastructure layer so the rest of
/// the app stays free of the Bot SDK. Credentials are passed explicitly (already decrypted) because
/// they live in a DB singleton rather than configuration.
/// </summary>
public interface ITeamsClient
{
    /// <summary>Verify the credentials by acquiring a bot token from Microsoft Entra.</summary>
    Task<TeamsConnectionTestResult> TestConnectionAsync(TeamsCredentials credentials, CancellationToken ct = default);

    /// <summary>
    /// Send an Adaptive Card to the conversation described by <paramref name="conversationReferenceJson"/>
    /// (captured when the bot was first contacted). <paramref name="adaptiveCard"/> is the card body as
    /// a serialisable object graph.
    /// </summary>
    Task SendAdaptiveCardAsync(TeamsCredentials credentials, string conversationReferenceJson, object adaptiveCard, CancellationToken ct = default);
}
