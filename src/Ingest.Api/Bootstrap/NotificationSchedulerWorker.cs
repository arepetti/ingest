using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Bootstrap;

/// <summary>
/// In-process notification scheduler. Registered only when <c>Email:Enabled</c> and
/// <c>Notifications:Scheduler:Enabled</c> are both on. It periodically runs the same job the
/// <c>POST /api/admin/notifications/run</c> endpoint runs, so the scheduling concern can later be
/// replaced by an external scheduler (cron, a separate service, …) hitting that endpoint without
/// changing the evaluation logic.
/// </summary>
public sealed class NotificationSchedulerWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationSchedulerWorker> _logger;

    /// <summary>Create a new <see cref="NotificationSchedulerWorker"/>.</summary>
    public NotificationSchedulerWorker(IServiceProvider services, IOptions<NotificationOptions> options, ILogger<NotificationSchedulerWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.Scheduler.PollMinutes));
        _logger.LogInformation("Notification scheduler started (every {Minutes}m).", interval.TotalMinutes);

        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var result = await notifications.RunAsync(stoppingToken);
                if (result.TotalQueued > 0)
                    _logger.LogInformation(
                        "Notification run queued {Total} email(s): {Upcoming} upcoming, {Missed} missed, {Warnings} warnings.",
                        result.TotalQueued, result.UpcomingQueued, result.MissedQueued, result.WarningsQueued);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification run failed.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
