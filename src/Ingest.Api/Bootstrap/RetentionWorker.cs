using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Retention;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Bootstrap;

/// <summary>
/// In-process retention sweeper. Registered only when <c>Retention:Enabled</c> is on. It runs the
/// same job the <c>POST /api/admin/retention/run</c> endpoint runs on a timer, so the scheduling
/// concern can later be replaced by an external scheduler hitting that endpoint without changing
/// the purge logic.
/// </summary>
public sealed class RetentionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly RetentionOptions _options;
    private readonly ILogger<RetentionWorker> _logger;

    /// <summary>Create a new <see cref="RetentionWorker"/>.</summary>
    public RetentionWorker(IServiceProvider services, IOptions<RetentionOptions> options, ILogger<RetentionWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.PollHours));
        _logger.LogInformation("Retention worker started (every {Hours}h).", interval.TotalHours);

        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var retention = scope.ServiceProvider.GetRequiredService<IRetentionService>();
                var result = await retention.PurgeAsync(stoppingToken);
                if (result.Total > 0)
                    _logger.LogInformation(
                        "Retention purge removed {Total} document(s): {Emails} emails, {SoftDeleted} soft-deleted rows, {Audit} audit entries, {Markers} notification markers.",
                        result.Total, result.EmailsPurged, result.SoftDeletedPurged, result.AuditEntriesPurged, result.NotificationMarkersPurged);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention purge failed.");
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
