using System.Net.Http.Headers;
using Testcontainers.MongoDb;

namespace Ingest.IntegrationTests.Fixtures;

/// <summary>
/// Shared per-collection fixture: spins up one throwaway MongoDB container and one in-process
/// instance of the Ingest API for the whole test run, then tears both down. Individual tests get
/// fresh data isolation through unique schema/account/service names rather than a new database.
/// </summary>
public sealed class IngestAppFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder()
        .WithImage("mongo:7")
        .Build();

    private IngestApiFactory? _factory;

    /// <summary>The booted API factory.</summary>
    public IngestApiFactory Factory => _factory ?? throw new InvalidOperationException("Fixture not initialised.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();

        // Aspire's AddMongoDBClient takes the database from the connection string when present; the
        // container's string has none, so we pass it separately via Mongo:Database.
        _factory = new IngestApiFactory(_mongo.GetConnectionString(), "ingest_itests");

        // Force the host (and its AdminBootstrapper hosted service) to start now so the admin
        // account + key and the Mongo indexes are in place before any test runs.
        using var _ = _factory.CreateClient();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    /// <summary>An <see cref="HttpClient"/> authenticated as the bootstrap admin (holds every capability).</summary>
    public HttpClient CreateAdminClient() => CreateClient(IngestApiFactory.AdminApiKey);

    /// <summary>An <see cref="HttpClient"/> presenting the given API key, or anonymous when null.</summary>
    public HttpClient CreateClient(string? apiKey)
    {
        var client = Factory.CreateClient();
        if (apiKey is not null) client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}

/// <summary>Binds every integration test to the single shared <see cref="IngestAppFixture"/>.</summary>
[CollectionDefinition(Name)]
public sealed class IngestCollection : ICollectionFixture<IngestAppFixture>
{
    /// <summary>The xUnit collection name shared by all integration tests.</summary>
    public const string Name = "ingest-integration";
}
