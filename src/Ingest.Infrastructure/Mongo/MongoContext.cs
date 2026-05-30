using Ingest.Core.Entities;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// Lightweight wrapper around a single <see cref="IMongoDatabase"/> that exposes the strongly-
/// typed collections used by repositories. Registered as a singleton; the collection getters are
/// cheap and stateless so they don't need caching.
/// </summary>
public sealed class MongoContext
{
    /// <summary>Underlying database handle.</summary>
    public IMongoDatabase Database { get; }

    /// <summary><c>accounts</c> collection (users + service identities).</summary>
    public IMongoCollection<Account> Accounts => Database.GetCollection<Account>("accounts");

    /// <summary><c>apiKeys</c> collection (hashed API keys).</summary>
    public IMongoCollection<ApiKey> ApiKeys => Database.GetCollection<ApiKey>("apiKeys");

    /// <summary><c>schemas</c> collection (KPI definitions).</summary>
    public IMongoCollection<Schema> Schemas => Database.GetCollection<Schema>("schemas");

    /// <summary><c>submissions</c> collection (raw batches as received).</summary>
    public IMongoCollection<Submission> Submissions => Database.GetCollection<Submission>("submissions");

    /// <summary><c>samples</c> collection — the denormalised, one-document-per-sample read model.</summary>
    public IMongoCollection<SampleProjection> Samples => Database.GetCollection<SampleProjection>("samples");

    /// <summary><c>reports</c> collection (Liquid templates with their parsed metadata).</summary>
    public IMongoCollection<Report> Reports => Database.GetCollection<Report>("reports");

    /// <summary>Create a new <see cref="MongoContext"/>.</summary>
    /// <param name="client">Mongo client supplied by the container.</param>
    /// <param name="databaseName">Name of the database to use.</param>
    public MongoContext(IMongoClient client, string databaseName)
    {
        Database = client.GetDatabase(databaseName);
    }
}
