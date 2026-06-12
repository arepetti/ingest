using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Bootstrap;

/// <summary>
/// In-process webhook outbox drainer. Registered only when <c>Webhooks:Enabled</c> and
/// <c>Webhooks:Worker:Enabled</c> are both on. It is a thin loop over
/// <see cref="IWebhookDispatchService.DrainAsync"/> — the same work the
/// <c>POST /api/admin/webhooks/drain</c> endpoint triggers — so delivery can later be driven by an
/// external scheduler hitting that endpoint instead, without touching the delivery logic.
/// </summary>
public sealed class WebhookOutboxWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookOutboxWorker> _logger;

    /// <summary>Create a new <see cref="WebhookOutboxWorker"/>.</summary>
    public WebhookOutboxWorker(IServiceProvider services, IOptions<WebhookOptions> options, ILogger<WebhookOutboxWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.Worker.PollSeconds));
        _logger.LogInformation("Webhook outbox worker started (every {Seconds}s).", interval.TotalSeconds);

        // Small startup delay so indexes/seeding settle before the first pass.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var dispatch = scope.ServiceProvider.GetRequiredService<IWebhookDispatchService>();
                var result = await dispatch.DrainAsync(_options.Worker.BatchSize, stoppingToken);
                if (result.Sent > 0 || result.Failed > 0)
                    _logger.LogInformation("Webhook drain: {Sent} sent, {Failed} failed.", result.Sent, result.Failed);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook outbox drain pass failed.");
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
