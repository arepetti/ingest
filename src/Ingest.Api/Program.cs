using System.Text.Json.Serialization;
using Ingest.Api.Auth;
using Ingest.Api.Bootstrap;
using Ingest.Api.Odata;
using Ingest.Api.Options;
using Ingest.Core.Common;
using Ingest.Infrastructure;
using Microsoft.AspNetCore.Authentication;
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

builder.Services.AddAuthentication(AuthConstants.Scheme)
    .AddScheme<ApiKeyAuthSchemeOptions, ApiKeyAuthenticationHandler>(AuthConstants.Scheme, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthConstants.ServicePolicy, p => p.RequireAuthenticatedUser())
    .AddPolicy(AuthConstants.OperatorPolicy, p => p.RequireRole("Operator", "Admin"))
    .AddPolicy(AuthConstants.AdminPolicy, p => p.RequireRole("Admin"));

builder.Services.AddCors(o => o.AddPolicy("dev", p =>
{
    var origins = builder.Configuration.GetSection("Ingest:CorsDevOrigins").Get<string[]>() ?? new[] { "http://localhost:5173" };
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
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
