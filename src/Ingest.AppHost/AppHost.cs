var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongo")
                   .WithDataVolume("ingest-mongo-data")
                   .WithMongoExpress();

var ingestDb = mongo.AddDatabase("ingest");

var api = builder.AddProject<Projects.Ingest_Api>("api")
    .WithReference(ingestDb)
    .WaitFor(ingestDb)
    .WithExternalHttpEndpoints();

builder.AddNpmApp("admin", "../../web/admin", "dev")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
    .WithEnvironment("BROWSER", "none")
    .WithEnvironment("VITE_API_URL", api.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
