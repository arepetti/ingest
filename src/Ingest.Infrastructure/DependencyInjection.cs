using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Approvals;
using Ingest.Infrastructure.Email;
using Ingest.Infrastructure.Integrations;
using Ingest.Infrastructure.Mongo;
using Ingest.Infrastructure.Reports;
using Ingest.Infrastructure.Security;
using Ingest.Infrastructure.Services;
using Ingest.Infrastructure.Validation;
using Ingest.Infrastructure.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Ingest.Infrastructure;

/// <summary>
/// Composition root for the infrastructure layer. Registers MongoDB, options binding,
/// repositories, the API-key hasher, validation services and the high-level application
/// services in a single call from the API host.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Register all infrastructure dependencies on the given service collection.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration; the <c>Mongo</c> and <c>ApiKey</c> sections are bound to <see cref="MongoOptions"/> and <see cref="Security.ApiKeyOptions"/> respectively.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// Expects an <see cref="IMongoClient"/> to already be in the container; the Aspire AppHost
    /// supplies it via <c>AddMongoDBClient</c>. The connection string named <c>ingest</c> (or
    /// fallback <c>mongo</c>) overrides <see cref="MongoOptions.Database"/> when it carries an
    /// explicit database segment.
    /// </remarks>
    public static IServiceCollection AddIngestInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        MongoSetup.RegisterClassMaps();

        services.Configure<MongoOptions>(configuration.GetSection("Mongo"));
        services.Configure<ApiKeyOptions>(configuration.GetSection("ApiKey"));
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        services.Configure<WebhookOptions>(configuration.GetSection("Webhooks"));
        services.Configure<IntegrationOptions>(configuration.GetSection("Integrations"));
        services.Configure<Retention.RetentionOptions>(configuration.GetSection("Retention"));
        services.Configure<ApprovalOptions>(configuration.GetSection("Approval"));

        services.AddSingleton<MongoContext>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoOptions>>().Value;

            // Prefer database from the connection string if it specifies one,
            // otherwise fall back to MongoOptions.Database.
            var settings = client.Settings;
            string dbName = opts.Database;
            try
            {
                var connStr = configuration.GetConnectionString("ingest") ?? configuration.GetConnectionString("mongo");
                if (!string.IsNullOrWhiteSpace(connStr))
                {
                    var url = new MongoUrl(connStr);
                    if (!string.IsNullOrWhiteSpace(url.DatabaseName)) dbName = url.DatabaseName;
                }
            }
            catch { /* fall back silently to options */ }

            return new MongoContext(client, dbName);
        });

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<ISchemaRepository, SchemaRepository>();
        services.AddScoped<ISchemaVersionHistoryRepository, SchemaVersionHistoryRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<ISampleRepository, SampleRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<INotificationLogRepository, NotificationLogRepository>();

        services.AddSingleton<IApiKeyHasher, ApiKeyHasher>();
        services.AddSingleton<IExpressionEvaluator, NCalcExpressionEvaluator>();
        services.AddSingleton<IExpressionTranslator, NCalcToJavaScriptTranslator>();
        services.AddSingleton<IReportRenderer, FluidReportRenderer>();
        services.AddScoped<ISubmissionValidator, SubmissionValidator>();
        services.AddScoped<IStatusService, StatusService>();

        // Application services — the thin orchestration layer the controllers delegate to.
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<ISchemaService, SchemaService>();
        services.AddScoped<ISubmissionService, SubmissionService>();
        services.AddScoped<IBulkImportService, BulkImportService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IExploreService, ExploreService>();
        services.AddScoped<IApprovalSettingsService, ApprovalSettingsService>();
        services.AddScoped<IApprovalRulesService, ApprovalRulesService>();

        // GDPR data-rights services (erasure, retention purge, DSAR export).
        services.AddScoped<IErasureService, ErasureService>();
        services.AddScoped<IRetentionService, RetentionService>();
        services.AddScoped<IPersonalDataService, PersonalDataService>();

        // Email + notifications. These are always registered (cheap, stateless); whether the
        // feature actually does anything is gated by Email:Enabled in the host (controllers guard,
        // workers are only registered when enabled). The SMTP password protector and the SMTP
        // sender are stateless singletons.
        services.AddSingleton<IEmailSecretProtector, EmailSecretProtector>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IEmailSettingsService, EmailSettingsService>();
        services.AddScoped<IEmailQueue, EmailQueue>();
        services.AddScoped<IEmailTemplateService, EmailTemplateService>();
        services.AddScoped<IEmailContentBuilder, EmailContentBuilder>();
        services.AddScoped<IEmailDispatchService, EmailDispatchService>();
        services.AddScoped<INotificationSettingsService, NotificationSettingsService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IApprovalNotificationService, ApprovalNotificationService>();
        services.AddScoped<IDraftNotificationService, DraftNotificationService>();

        // Outbound webhooks. Like email these are always registered (cheap); behaviour is gated by
        // Webhooks:Enabled (controllers 404, the dispatcher worker only registers when enabled, and
        // the publisher finds no endpoints when none are configured). The typed HttpClient the
        // dispatcher uses is registered by the host (Program.cs) so Aspire resilience applies.
        services.AddSingleton<ISecretProtector, WebhookSecretProtector>();
        services.AddScoped<IWebhookPublisher, WebhookPublisher>();
        services.AddScoped<IWebhookEndpointService, WebhookEndpointService>();
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();
        services.AddScoped<IWebhookDispatchService, WebhookDispatchService>();

        // Integrations (Microsoft Teams). Always registered (cheap); behaviour is gated by
        // Integrations:Enabled (controllers 404, the workers only register when enabled) and by the
        // TeamsConnectionSettings DB singleton (the feature stays inert until the bot is configured).
        // The Teams client and the (stateless) card builder are singletons; the typed HttpClient the
        // client uses is registered by the host (Program.cs) so Aspire resilience applies.
        services.AddSingleton<ITeamsClient, TeamsClient>();
        services.AddSingleton<TeamsCardBuilder>();
        services.AddScoped<IIntegrationsService, IntegrationsService>();
        services.AddScoped<IIntegrationRunService, IntegrationRunService>();
        services.AddScoped<IIntegrationDispatchService, IntegrationDispatchService>();

        return services;
    }
}
