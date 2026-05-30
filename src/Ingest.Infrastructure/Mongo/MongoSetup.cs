using Ingest.Core.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// One-shot configuration helpers for the MongoDB driver. Both methods are idempotent and safe
/// to call on every host start.
/// </summary>
public static class MongoSetup
{
    private static int _registered;

    /// <summary>
    /// Register the global serialisation conventions used across the domain: camelCase element
    /// names, enums as strings, extra-element tolerance, and the standard <see cref="Guid"/>
    /// representation. Safe to call multiple times — only the first call has an effect.
    /// </summary>
    public static void RegisterClassMaps()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;

        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true),
        };
        ConventionRegistry.Register("ingest", pack, _ => true);

        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }

    /// <summary>
    /// Create the unique and lookup indexes used by the repositories. Mongo's
    /// <c>CreateIndexAsync</c> is idempotent, so calling this on every host start is cheap.
    /// </summary>
    /// <param name="ctx">Mongo context.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task EnsureIndexesAsync(MongoContext ctx, CancellationToken ct = default)
    {
        await ctx.Accounts.Indexes.CreateOneAsync(
            new CreateIndexModel<Account>(
                Builders<Account>.IndexKeys.Ascending(a => a.Name),
                new CreateIndexOptions { Unique = true, Name = "uniq_name" }),
            cancellationToken: ct);

        await ctx.ApiKeys.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<ApiKey>(
                Builders<ApiKey>.IndexKeys.Ascending(k => k.KeyId),
                new CreateIndexOptions { Unique = true, Name = "uniq_keyId" }),
            new CreateIndexModel<ApiKey>(
                Builders<ApiKey>.IndexKeys.Ascending(k => k.AccountId),
                new CreateIndexOptions { Name = "by_account" }),
        }, cancellationToken: ct);

        await ctx.Schemas.Indexes.CreateOneAsync(
            new CreateIndexModel<Schema>(
                Builders<Schema>.IndexKeys.Ascending(s => s.Name),
                new CreateIndexOptions { Unique = true, Name = "uniq_name" }),
            cancellationToken: ct);

        await ctx.Submissions.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<Submission>(
                Builders<Submission>.IndexKeys.Ascending(s => s.ServiceAccountId).Descending(s => s.SubmittedAt),
                new CreateIndexOptions { Name = "by_service_time" }),
        }, cancellationToken: ct);

        await ctx.Reports.Indexes.CreateOneAsync(
            new CreateIndexModel<Report>(
                Builders<Report>.IndexKeys.Ascending(r => r.Name),
                new CreateIndexOptions { Unique = true, Name = "uniq_name" }),
            cancellationToken: ct);

        await ctx.Samples.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<SampleProjection>(
                Builders<SampleProjection>.IndexKeys
                    .Ascending(s => s.ServiceAccountId)
                    .Ascending(s => s.SchemaName)
                    .Ascending(s => s.ValueName)
                    .Descending(s => s.Timestamp),
                new CreateIndexOptions { Name = "by_service_schema_value_time" }),
            new CreateIndexModel<SampleProjection>(
                Builders<SampleProjection>.IndexKeys
                    .Ascending(s => s.ServiceAccountId)
                    .Ascending(s => s.SchemaName)
                    .Ascending(s => s.ValueName)
                    .Ascending(s => s.PeriodStart),
                new CreateIndexOptions { Name = "by_service_schema_value_period" }),
            new CreateIndexModel<SampleProjection>(
                Builders<SampleProjection>.IndexKeys.Ascending(s => s.SubmissionId),
                new CreateIndexOptions { Name = "by_submission" }),
        }, cancellationToken: ct);
    }
}
