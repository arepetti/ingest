using System.Text.Json.Serialization;
using Ingest.Api.Auth;
using Ingest.Api.Bootstrap;
using Ingest.Api.Odata;
using Ingest.Api.Options;
using Ingest.Core.Common;
using Ingest.Infrastructure;
using Ingest.Infrastructure.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OData;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Mongo via Aspire integration. Connection string is provided by the AppHost
// under the "ingest" connection name (mongo database resource).
builder.AddMongoDBClient("ingest");

builder.Services.Configure<IngestOptions>(builder.Configuration.GetSection("Ingest"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAuditContext, HttpAuditContext>();

builder.Services.AddIngestInfrastructure(builder.Configuration);

// SSO is an *optional second* authentication path that sits behind a single master switch.
// When off (the default) we register nothing here beyond the API-key scheme, so no cookie/OIDC
// code runs and the system behaves exactly as the API-key-only build.
builder.Services.Configure<SsoOptions>(builder.Configuration.GetSection("Sso"));
var ssoOptions = builder.Configuration.GetSection("Sso").Get<SsoOptions>() ?? new SsoOptions();
var ssoActive = ssoOptions.IsActive;

var authBuilder = builder.Services.AddAuthentication(AuthConstants.Scheme)
    .AddScheme<ApiKeyAuthSchemeOptions, ApiKeyAuthenticationHandler>(AuthConstants.Scheme, _ => { });

if (ssoActive)
{
    // Session cookie issued after a successful OIDC sign-in. API-style: never redirect to a login
    // page — emit 401/403 so the SPA's fetch layer handles it like any other auth failure.
    authBuilder.AddCookie(AuthConstants.SessionScheme, o =>
    {
        o.Cookie.Name = ssoOptions.CookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });

    // One OIDC handler per fully-configured provider. The generic handler covers Microsoft, Google
    // and any other standard OpenID Connect provider. The callback path is under /api so the Vite
    // dev proxy forwards it to the backend unchanged.
    foreach (var provider in ssoOptions.ActiveProviders)
    {
        var providerId = provider.Id;
        authBuilder.AddOpenIdConnect(AuthConstants.OidcScheme(providerId), o =>
        {
            o.SignInScheme = AuthConstants.SessionScheme;
            o.Authority = provider.Authority;
            o.ClientId = provider.ClientId;
            o.ClientSecret = provider.ClientSecret;
            o.ResponseType = "code";
            o.UsePkce = true;
            o.SaveTokens = false;
            o.GetClaimsFromUserInfoEndpoint = true;
            o.CallbackPath = $"/api/auth/callback/{providerId}";
            o.Scope.Clear();
            foreach (var scope in provider.Scopes) o.Scope.Add(scope);
            o.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = ctx => SsoSignIn.HandleTokenValidatedAsync(providerId, ctx),
                OnRemoteFailure = ctx =>
                {
                    ctx.Response.Redirect("/login?sso_error=remote");
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                },
            };
        });
    }
}

// Policies name both schemes. When SSO is off the IngestSession scheme is simply never
// registered/never authenticates, so the policies still behave exactly as today (API key only).
var authSchemes = ssoActive
    ? new[] { AuthConstants.Scheme, AuthConstants.SessionScheme }
    : new[] { AuthConstants.Scheme };

builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder(authSchemes).RequireAuthenticatedUser().Build())
    .AddPolicy(AuthConstants.ServicePolicy, p => { p.AddAuthenticationSchemes(authSchemes); p.RequireAuthenticatedUser(); })
    .AddPolicy(AuthConstants.OperatorPolicy, p => { p.AddAuthenticationSchemes(authSchemes); p.RequireRole("Operator", "Admin"); })
    .AddPolicy(AuthConstants.AdminPolicy, p => { p.AddAuthenticationSchemes(authSchemes); p.RequireRole("Admin"); });

builder.Services.AddCors(o => o.AddPolicy("dev", p =>
{
    var origins = builder.Configuration.GetSection("Ingest:CorsDevOrigins").Get<string[]>() ?? new[] { "http://localhost:5173" };
    // AllowCredentials is required for the session cookie to flow cross-origin in dev. It's only
    // valid alongside explicit origins (never with AllowAnyOrigin), which is exactly what we use.
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

// Serialize all enums as their string names (e.g. "Service" instead of 0) for both Minimal API and MVC/OData endpoints.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddOData(o => o.Select().Filter().OrderBy().Count().SetMaxTop(5000)
        .AddRouteComponents("odata", EdmModelBuilderExtensions.BuildSamplesEdmModel()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Ingest API",
        Version = "v1",
        Description = "KPI ingestion backend for local-council services. Authenticate by sending your API key " +
                      "in the X-Api-Key header. Bootstrap admin credentials are written to the server log on first start.",
    });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Description = "Paste your API key.",
    });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("ApiKey", document)] = new List<string>(),
    });

    // Pull the XML comments emitted by each project into the OpenAPI document so action summaries,
    // response descriptions, and DTO field docs show up in Swagger UI and any generated clients.
    foreach (var xml in new[] { "Ingest.Api.xml", "Ingest.Core.xml", "Ingest.Infrastructure.xml" })
    {
        var path = Path.Combine(AppContext.BaseDirectory, xml);
        if (File.Exists(path)) c.IncludeXmlComments(path, includeControllerXmlComments: true);
    }
});

builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHostedService<AdminBootstrapper>();

// Email + notifications. Gated by the Email:Enabled master switch (mirrors the SSO pattern):
// when off, nothing here runs — no seeding, no outbox drainer, no scheduler — and the admin UI
// hides the related settings. The two workers are additionally toggleable on their own so the
// sending / scheduling concerns can be driven by an external service hitting the internal
// trigger endpoints instead (POST /api/admin/email/drain, POST /api/admin/notifications/run).
var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
var notificationOptions = builder.Configuration.GetSection("Notifications").Get<NotificationOptions>() ?? new NotificationOptions();
if (emailOptions.Enabled)
{
    builder.Services.AddHostedService<EmailSeeder>();
    if (emailOptions.Worker.Enabled) builder.Services.AddHostedService<EmailOutboxWorker>();
    if (notificationOptions.Scheduler.Enabled) builder.Services.AddHostedService<NotificationSchedulerWorker>();
}

// Outbound webhooks. Gated by the Webhooks:Enabled master switch (mirrors email): when off, the
// admin endpoints 404, the publisher is never invoked, and the dispatcher worker doesn't run. The
// typed HttpClient picks up the Aspire standard resilience handler (retry/timeout/circuit-breaker)
// configured in ServiceDefaults. The worker is independently toggleable so delivery can be driven
// by an external scheduler hitting POST /api/admin/webhooks/drain instead.
var webhookOptions = builder.Configuration.GetSection("Webhooks").Get<Ingest.Infrastructure.Webhooks.WebhookOptions>() ?? new Ingest.Infrastructure.Webhooks.WebhookOptions();
builder.Services.AddHttpClient(Ingest.Infrastructure.Webhooks.WebhookDispatchService.HttpClientName);
if (webhookOptions.Enabled && webhookOptions.Worker.Enabled)
    builder.Services.AddHostedService<WebhookOutboxWorker>();

// Retention purge (GDPR storage limitation). Off by default; the in-process worker only runs when
// Retention:Enabled is on. The manual POST /api/admin/retention/run trigger works regardless, so
// the schedule can be driven externally instead.
var retentionOptions = builder.Configuration.GetSection("Retention").Get<Ingest.Infrastructure.Retention.RetentionOptions>() ?? new Ingest.Infrastructure.Retention.RetentionOptions();
if (retentionOptions.Enabled) builder.Services.AddHostedService<RetentionWorker>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

var ingest = app.Services.GetRequiredService<IOptions<IngestOptions>>().Value;
if (app.Environment.IsDevelopment() || ingest.EnableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Ingest API v1"));
}

if (app.Environment.IsDevelopment()) app.UseCors("dev");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Serve SPA from wwwroot in any environment if files are present.
var webRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(webRoot))
{
    var fileProvider = new PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

    app.MapFallback(async context =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/odata", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/alive", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(Path.Combine(webRoot, "index.html"));
    });
}

app.Run();
