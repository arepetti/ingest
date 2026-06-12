var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongo")
                   .WithDataVolume("ingest-mongo-data")
                   .WithMongoExpress();

var ingestDb = mongo.AddDatabase("ingest");

var api = builder.AddProject<Projects.Ingest_Api>("api")
    .WithReference(ingestDb)
    .WaitFor(ingestDb)
    .WithExternalHttpEndpoints();

// SSO secret wiring (opt-in). Kept inert unless `Sso:EnableSso` is set in the AppHost's own
// configuration/user-secrets, so the default `aspire run` is unchanged. When enabled, the
// non-secret provider shape (Id/Authority/DisplayName/Scopes) comes from the API's
// appsettings.json; only the EnableSso flag and the client id/secret are projected here from
// AppHost parameters (set via `dotnet user-secrets set Parameters:MicrosoftClientSecret <v>`).
// Dev redirect URI to register with the IdP: http://localhost:5173/api/auth/callback/Microsoft
if (string.Equals(builder.Configuration["Sso:EnableSso"], "true", StringComparison.OrdinalIgnoreCase))
{
    var msClientId = builder.AddParameter("MicrosoftClientId");
    var msClientSecret = builder.AddParameter("MicrosoftClientSecret", secret: true);

    api.WithEnvironment("Sso__EnableSso", "true")
       .WithEnvironment("Sso__Providers__0__ClientId", msClientId)
       .WithEnvironment("Sso__Providers__0__ClientSecret", msClientSecret);

    // Google follows the same pattern at provider index 1 — uncomment and add the parameters:
    // var googleClientId = builder.AddParameter("GoogleClientId");
    // var googleClientSecret = builder.AddParameter("GoogleClientSecret", secret: true);
    // api.WithEnvironment("Sso__Providers__1__ClientId", googleClientId)
    //    .WithEnvironment("Sso__Providers__1__ClientSecret", googleClientSecret);
}

builder.AddNpmApp("admin", "../../web/admin", "dev")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
    .WithEnvironment("BROWSER", "none")
    .WithEnvironment("VITE_API_URL", api.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
