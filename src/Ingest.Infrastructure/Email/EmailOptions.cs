namespace Ingest.Infrastructure.Email;

/// <summary>
/// Binding target for the <c>Email</c> configuration section. The whole email + notifications
/// feature is gated by <see cref="Enabled"/>, mirroring the SSO master switch: when it is
/// <c>false</c> the workers never start, the admin Settings tabs are hidden, the ad-hoc send
/// action disappears, and the notification scheduler stays inert.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>Master switch. Defaults to <c>true</c>; set <c>false</c> to disable email entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>In-process outbox sender settings.</summary>
    public EmailWorkerOptions Worker { get; set; } = new();

    /// <summary>
    /// One-time seed for the SMTP settings stored in the database. Used only when no
    /// <see cref="Core.Entities.EmailSettings"/> document exists yet — after that the database is
    /// the source of truth and these values are ignored.
    /// </summary>
    public EmailSmtpSeed Smtp { get; set; } = new();
}

/// <summary>Controls the background service that drains the outbox.</summary>
public sealed class EmailWorkerOptions
{
    /// <summary>
    /// When <c>true</c> (default) an in-process background service drains the outbox on a timer.
    /// Set <c>false</c> to drive sending purely from an external scheduler hitting
    /// <c>POST /api/admin/email/drain</c>, so the sender can later be split into its own service.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often (seconds) the in-process drainer wakes up to look for pending mail.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>Max delivery attempts before a message is marked permanently <see cref="Core.Entities.EmailStatus.Failed"/>.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Max messages drained per pass, to bound work per wake-up.</summary>
    public int BatchSize { get; set; } = 25;
}

/// <summary>Initial SMTP values seeded into the database if no settings exist yet.</summary>
public sealed class EmailSmtpSeed
{
    /// <summary>SMTP host. Leaving it blank means "don't seed" — admins configure it in the UI.</summary>
    public string? Host { get; set; }

    /// <summary>SMTP port.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Negotiate STARTTLS.</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>SMTP username (optional).</summary>
    public string? Username { get; set; }

    /// <summary>SMTP password (optional). Encrypted before it's written to the database.</summary>
    public string? Password { get; set; }

    /// <summary>Default From address.</summary>
    public string? FromAddress { get; set; }

    /// <summary>Default From display name.</summary>
    public string? FromName { get; set; }

    /// <summary>True when there's at least a host to seed from.</summary>
    public bool HasSeed => !string.IsNullOrWhiteSpace(Host);
}
