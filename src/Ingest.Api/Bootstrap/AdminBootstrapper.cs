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
/// exists) it creates the admin and gives it a single API key: if
/// <see cref="ApiKeyOptions.BootstrapAdminKey"/> is configured that exact key is used (so the
/// operator never has to read it from the logs), otherwise a random key is minted and logged
/// once at warning level. Subsequent boots only add a key when no active key remains. Also
/// responsible for the one-time Mongo index creation.
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
            // Prefer an operator-supplied key from configuration so nobody has to scrape it out of
            // the logs. Fall back to a random key (logged once) when none is configured or it's
            // malformed — that keeps a misconfigured deployment usable rather than locked out.
            var configuredKey = _apiKey.BootstrapAdminKey?.Trim();
            var fromConfig = !string.IsNullOrEmpty(configuredKey);

            GeneratedApiKey generated;
            if (fromConfig)
            {
                var imported = hasher.Import(configuredKey!);
                if (imported is null)
                {
                    _logger.LogError(
                        "ApiKey:BootstrapAdminKey is set but malformed (expected '<keyId>.<secret>'). " +
                        "Falling back to a generated key.");
                    generated = hasher.Generate();
                    fromConfig = false;
                }
                else
                {
                    generated = imported;
                }
            }
            else
            {
                generated = hasher.Generate();
            }

            var entity = new ApiKey
            {
                AccountId = admin.Id,
                KeyId = generated.KeyId,
                Hash = generated.Hash,
                Salt = generated.Salt,
            };
            await keys.AddAsync(entity, cancellationToken);

            if (fromConfig)
            {
                // Don't echo the configured secret back to the logs — the operator already has it.
                _logger.LogWarning(
                    "Bootstrapped admin account '{Name}' with the API key from ApiKey:BootstrapAdminKey. " +
                    "Present it in the {Header} header. Rotate it via POST /api/admin/accounts/{Id}/keys once you're in.",
                    admin.Name, _apiKey.HeaderName, admin.Id);
            }
            else
            {
                _logger.LogWarning(
                    "Bootstrapped admin API key (shown only this once): {Key}. " +
                    "Use it in the {Header} header. Set ApiKey:BootstrapAdminKey to avoid this next time, " +
                    "or rotate it via POST /api/admin/accounts/{Id}/keys then revoke this one.",
                    generated.Plaintext, _apiKey.HeaderName, admin.Id);
            }
        }
        else
        {
            _logger.LogWarning(
                "Admin account '{Name}' already has {Count} active API key(s); leaving it untouched. " +
                "Changing ApiKey:BootstrapAdminKey now has no effect — rotate via the API/SPA, or set " +
                "ApiKey:BootstrapAdminName to a new value to bootstrap another admin.",
                admin.Name, active.Count);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
