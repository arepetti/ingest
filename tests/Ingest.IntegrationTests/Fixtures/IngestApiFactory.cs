using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ingest.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real Ingest API in-process against a throwaway MongoDB. SSO and every background
/// worker are turned off so tests are deterministic, and the two outbound transports (SMTP and the
/// webhook HTTP client) are replaced with recording fakes so nothing leaves the process.
/// </summary>
public sealed class IngestApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _databaseName;

    /// <summary>The known bootstrap admin key the app is seeded with (format <c>keyId.secret</c>).</summary>
    public const string AdminApiKey = "itests.integration-admin-key";

    /// <summary>Captures emails the dispatcher tries to send.</summary>
    public RecordingEmailSender EmailSender { get; } = new();

    /// <summary>Captures webhook POSTs the dispatcher tries to deliver.</summary>
    public RecordingHttpHandler WebhookHandler { get; } = new();

    /// <summary>Create a factory bound to the given Mongo instance.</summary>
    /// <param name="connectionString">Connection string for the throwaway Mongo container.</param>
    /// <param name="databaseName">Database name to isolate this run.</param>
    public IngestApiFactory(string connectionString, string databaseName)
    {
        _connectionString = connectionString;
        _databaseName = databaseName;

        // Program.cs reads the Mongo connection string and every feature-flag section at *build*
        // time (e.g. AddMongoDBClient, GetSection("Email").Get<...>()), which happens before
        // WebApplicationFactory can layer in ConfigureAppConfiguration. Environment variables are
        // part of the configuration from CreateBuilder onward, so they're the reliable override.
        foreach (var (key, value) in Overrides)
            Environment.SetEnvironmentVariable(key, value);
    }

    private Dictionary<string, string?> Overrides => new()
    {
        ["ConnectionStrings__ingest"] = _connectionString,
        ["Mongo__Database"] = _databaseName,

        // Deterministic, known admin credential so tests don't have to scrape logs.
        ["ApiKey__BootstrapAdminName"] = "Admin",
        ["ApiKey__BootstrapAdminKey"] = AdminApiKey,
        ["ApiKey__Pepper"] = "integration-test-pepper",

        // Keep the third-party auth path inert.
        ["Sso__EnableSso"] = "false",
        ["Ingest__EnableSwagger"] = "false",
        ["Ingest__DefaultLocale"] = " en-us ",

        // Features stay enabled (so their endpoints are reachable) but every background worker is
        // off — tests trigger drains/runs explicitly for determinism.
        ["Approval__Enabled"] = "true",
        ["Email__Enabled"] = "true",
        ["Email__Worker__Enabled"] = "false",
        ["Notifications__Scheduler__Enabled"] = "false",
        ["Webhooks__Enabled"] = "true",
        ["Webhooks__Worker__Enabled"] = "false",
        ["Integrations__Enabled"] = "false",
        ["Retention__Enabled"] = "false",
    };

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Swap the SMTP transport for a recorder.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

            // Route the webhook dispatcher's typed HttpClient through the recording handler so no
            // real HTTP leaves the process; the rest of the dispatch pipeline stays real.
            services.AddHttpClient(WebhookDispatchService.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => WebhookHandler);
        });
    }
}
