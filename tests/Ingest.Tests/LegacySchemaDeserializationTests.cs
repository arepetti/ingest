using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Ingest.Tests;

public class LegacySchemaDeserializationTests
{
    static LegacySchemaDeserializationTests()
    {
        MongoSetup.RegisterClassMaps();
    }

    [Fact]
    public void Legacy_schema_document_deserializes_kind_and_expression_defaults()
    {
        var doc = new BsonDocument
        {
            { "_id", Guid.NewGuid().ToString() },
            { "name", "legacy_kpis" },
            { "values", new BsonArray
                {
                    new BsonDocument
                    {
                        { "name", "tonnes" },
                        { "type", "Number" },
                        { "cadence", "Weekly" }
                    }
                }
            }
        };

        var schema = BsonSerializer.Deserialize<Schema>(doc);
        var value = Assert.Single(schema.Values);
        Assert.Equal(SchemaValueKind.UserDefined, value.Kind);
        Assert.Null(value.Expression);
        Assert.False(value.IsCalculated);
    }

    [Fact]
    public void Legacy_sample_projection_deserializes_isDerived_default()
    {
        var doc = new BsonDocument
        {
            { "_id", Guid.NewGuid().ToString() },
            { "submissionId", Guid.NewGuid().ToString() },
            { "serviceAccountId", Guid.NewGuid().ToString() },
            { "serviceName", "svc" },
            { "schemaName", "demo" },
            { "valueName", "tonnes" },
            { "valueType", "Number" },
            { "numberValue", 1.0 },
            { "timestamp", DateTime.UtcNow },
            { "cadence", "Weekly" },
            { "periodStart", DateTime.UtcNow.Date },
            { "periodEnd", DateTime.UtcNow.Date.AddDays(7) }
        };

        var projection = BsonSerializer.Deserialize<SampleProjection>(doc);
        Assert.False(projection.IsDerived);
    }
}
