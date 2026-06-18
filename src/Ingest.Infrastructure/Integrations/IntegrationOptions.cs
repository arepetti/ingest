namespace Ingest.Infrastructure.Integrations;

/// <summary>
/// Binding target for the <c>Integrations</c> configuration section. These are operational switches
/// only — the Teams bot credentials live in the <c>TeamsConnectionSettings</c> DB singleton and are
/// edited in the admin console, not here. The whole feature is gated by <see cref="Enabled"/>: when
/// it is <c>false</c> the scheduler/dispatcher never start, the admin endpoints return 404, and the
/// inbound bot endpoint rejects. Enabled by default.
/// </summary>
public sealed class IntegrationOptions
{
    /// <summary>Master switch. Defaults to <c>true</c>; set <c>false</c> to turn the feature off entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Per-request HTTP timeout (seconds) for a single Teams send / token acquisition.</summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>The scheduled-pass settings (the "daily check").</summary>
    public IntegrationSchedulerOptions Scheduler { get; set; } = new();

    /// <summary>The outbox-drainer settings.</summary>
    public IntegrationWorkerOptions Worker { get; set; } = new();
}

/// <summary>Controls the background job that runs each integration's scheduled pass.</summary>
public sealed class IntegrationSchedulerOptions
{
    /// <summary>When <c>true</c> (default) an in-process timer runs the pass. Set <c>false</c> to drive it from an external scheduler.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often (minutes) the scheduler wakes up to check whether any integration is due.</summary>
    public int PollMinutes { get; set; } = 15;
}

/// <summary>Controls the background service that drains the integration-delivery outbox.</summary>
public sealed class IntegrationWorkerOptions
{
    /// <summary>When <c>true</c> (default) an in-process background service drains the outbox on a timer.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often (seconds) the in-process dispatcher wakes up to look for due deliveries.</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>Max delivery attempts before a delivery is marked permanently <see cref="Core.Entities.IntegrationDeliveryStatus.Failed"/>.</summary>
    public int MaxAttempts { get; set; } = 6;

    /// <summary>Max deliveries drained per pass, to bound work per wake-up.</summary>
    public int BatchSize { get; set; } = 25;
}
