using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Bootstrap;

/// <summary>
/// In-process integration outbox drainer. Registered only when <c>Integrations:Enabled</c> and
/// <c>Integrations:Worker:Enabled</c> are both on. A thin loop over
/// <see cref="IIntegrationDispatchService.DrainAsync"/> — the same work the
/// <c>POST /api/admin/integrations/drain</c> endpoint triggers — so delivery can later be driven by
/// an external scheduler hitting that endpoint instead. Mirrors <see cref="WebhookOutboxWorker"/>.
/// </summary>
public sealed class IntegrationOutboxWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IntegrationOptions _options;
    private readonly ILogger<IntegrationOutboxWorker> _logger;

    /// <summary>Create a new <see cref="IntegrationOutboxWorker"/>.</summary>
    public IntegrationOutboxWorker(IServiceProvider services, IOptions<IntegrationOptions> options, ILogger<IntegrationOutboxWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.Worker.PollSeconds));
        _logger.LogInformation("Integration outbox worker started (every {Seconds}s).", interval.TotalSeconds);

        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var dispatch = scope.ServiceProvider.GetRequiredService<IIntegrationDispatchService>();
                var result = await dispatch.DrainAsync(_options.Worker.BatchSize, stoppingToken);
                if (result.Sent > 0 || result.Failed > 0)
                    _logger.LogInformation("Integration drain: {Sent} sent, {Failed} failed.", result.Sent, result.Failed);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Integration outbox drain pass failed.");
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
