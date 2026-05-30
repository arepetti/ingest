namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// Binding target for the <c>Mongo</c> configuration section. The Aspire AppHost supplies these
/// values via a connection-string reference; for local development the defaults point at the
/// Aspire-managed container.
/// </summary>
public sealed class MongoOptions
{
    /// <summary>MongoDB connection string. Defaults to a local container on the standard port.</summary>
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    /// <summary>Database name to use inside the cluster.</summary>
    public string Database { get; set; } = "ingest";
}
