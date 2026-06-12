using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Bootstrap;

/// <summary>
/// In-process outbox drainer. Registered only when <c>Email:Enabled</c> and
/// <c>Email:Worker:Enabled</c> are both on. It is a thin loop over
/// <see cref="IEmailDispatchService.DrainAsync"/> — the exact same work the
/// <c>POST /api/admin/email/drain</c> endpoint triggers — so the sender can later be extracted
/// into its own service that drives the endpoint instead, without touching the delivery logic.
/// </summary>
public sealed class EmailOutboxWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailOutboxWorker> _logger;

    /// <summary>Create a new <see cref="EmailOutboxWorker"/>.</summary>
    public EmailOutboxWorker(IServiceProvider services, IOptions<EmailOptions> options, ILogger<EmailOutboxWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.Worker.PollSeconds));
        _logger.LogInformation("Email outbox worker started (every {Seconds}s).", interval.TotalSeconds);

        // Small startup delay so indexes/seeding settle before the first pass.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var dispatch = scope.ServiceProvider.GetRequiredService<IEmailDispatchService>();
                var result = await dispatch.DrainAsync(_options.Worker.BatchSize, stoppingToken);
                if (result.Sent > 0 || result.Failed > 0)
                    _logger.LogInformation("Email drain: {Sent} sent, {Failed} failed.", result.Sent, result.Failed);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email outbox drain pass failed.");
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
