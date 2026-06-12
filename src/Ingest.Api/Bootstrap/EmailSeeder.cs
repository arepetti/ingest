using Ingest.Core.Abstractions;

namespace Ingest.Api.Bootstrap;

/// <summary>
/// One-shot startup task (only registered when email is enabled) that makes the feature usable on
/// first boot: it seeds the built-in email templates and materialises the SMTP / notification
/// settings documents so the admin Settings page always has something concrete to edit.
/// </summary>
public sealed class EmailSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<EmailSeeder> _logger;

    /// <summary>Create a new <see cref="EmailSeeder"/>.</summary>
    public EmailSeeder(IServiceProvider services, ILogger<EmailSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<IEmailTemplateService>().SeedDefaultsAsync(cancellationToken);
            await sp.GetRequiredService<IEmailSettingsService>().GetAsync(cancellationToken);
            await sp.GetRequiredService<INotificationSettingsService>().GetAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Seeding is best-effort; the services lazily create what they need on first use too.
            _logger.LogError(ex, "Email seeding failed. Continuing — settings will be created on first use.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
