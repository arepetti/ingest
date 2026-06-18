using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Bootstrap;

/// <summary>
/// In-process scheduler for integration prompts. Registered only when <c>Integrations:Enabled</c>
/// and <c>Integrations:Scheduler:Enabled</c> are both on. Wakes on a timer and asks
/// <see cref="IIntegrationRunService.RunAllAsync"/> to enqueue prompts for any integration whose
/// schedule is due — the same work <c>POST /api/admin/integrations/run</c> triggers — so the cadence
/// can instead be driven by an external scheduler. Per-period dedupe in the outbox makes the coarse
/// poll harmless. Mirrors <c>NotificationSchedulerWorker</c>.
/// </summary>
public sealed class IntegrationSchedulerWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IntegrationOptions _options;
    private readonly ILogger<IntegrationSchedulerWorker> _logger;

    /// <summary>Create a new <see cref="IntegrationSchedulerWorker"/>.</summary>
    public IntegrationSchedulerWorker(IServiceProvider services, IOptions<IntegrationOptions> options, ILogger<IntegrationSchedulerWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.Scheduler.PollMinutes));
        _logger.LogInformation("Integration scheduler started (every {Minutes}m).", interval.TotalMinutes);

        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var run = scope.ServiceProvider.GetRequiredService<IIntegrationRunService>();
                var result = await run.RunAllAsync(stoppingToken);
                if (result.Prompted > 0)
                    _logger.LogInformation("Integration run: {Prompted} prompt(s) enqueued, {Skipped} skipped.", result.Prompted, result.Skipped);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Integration scheduled run failed.");
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
