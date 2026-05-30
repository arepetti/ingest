using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using Ingest.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Bootstrap;

/// <summary>
/// Startup hosted service that gives a fresh deployment a usable admin account and key.
/// On first boot (when no account with the configured <see cref="ApiKeyOptions.BootstrapAdminName"/>
/// exists) it creates the admin and mints a single API key, logging the plaintext exactly once
/// as a warning so an operator can capture it from the console/log sink. Subsequent boots only
/// re-create the key when no active key remains. Also responsible for the one-time Mongo index
/// creation.
/// </summary>
public sealed class AdminBootstrapper : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AdminBootstrapper> _logger;
    private readonly ApiKeyOptions _apiKey;
    private readonly TimeProvider _time;

    /// <summary>Create a new <see cref="AdminBootstrapper"/>.</summary>
    /// <param name="services">Root service provider; an async scope is created for the run.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="apiKey">Bound API-key options.</param>
    /// <param name="time">Clock (currently unused but kept for forward-compatibility).</param>
    public AdminBootstrapper(
        IServiceProvider services,
        ILogger<AdminBootstrapper> logger,
        IOptions<ApiKeyOptions> apiKey,
        TimeProvider time)
    {
        _services = services;
        _logger = logger;
        _apiKey = apiKey.Value;
        _time = time;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var ctx = sp.GetRequiredService<MongoContext>();

        try
        {
            await MongoSetup.EnsureIndexesAsync(ctx, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Mongo indexes. Continuing anyway.");
        }

        var accounts = sp.GetRequiredService<IAccountRepository>();
        var keys = sp.GetRequiredService<IApiKeyRepository>();
        var hasher = sp.GetRequiredService<IApiKeyHasher>();

        var admin = await accounts.GetByNameAsync(_apiKey.BootstrapAdminName, ct: cancellationToken);
        if (admin is null)
        {
            admin = new Account
            {
                Name = _apiKey.BootstrapAdminName,
                Label = "Bootstrap administrator",
                Kind = AccountKind.User,
                Role = AccountRole.Admin,
                Enabled = true,
            };
            await accounts.AddAsync(admin, cancellationToken);
            _logger.LogWarning("Created bootstrap admin account '{Name}'.", admin.Name);
        }

        var active = await keys.GetActiveByAccountAsync(admin.Id, cancellationToken);
        if (active.Count == 0)
        {
            var generated = hasher.Generate();
            var entity = new ApiKey
            {
                AccountId = admin.Id,
                KeyId = generated.KeyId,
                Hash = generated.Hash,
                Salt = generated.Salt,
            };
            await keys.AddAsync(entity, cancellationToken);

            _logger.LogWarning(
                "Bootstrapped admin API key (shown only this once): {Key}. " +
                "Use it in the {Header} header. " +
                "Rotate it via POST /api/admin/accounts/{Id}/keys, then revoke this one.",
                generated.Plaintext, _apiKey.HeaderName, admin.Id);
        }
        else
        {
            _logger.LogWarning(
                "Admin account '{Name}' already has {Count} active API key(s). " +
                "If you've lost it, create a new admin account on the database directly or set ApiKey:BootstrapAdminName to a new value to bootstrap another one.",
                admin.Name, active.Count);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
