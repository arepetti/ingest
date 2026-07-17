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

    [Fact]
    public void Legacy_submission_warnings_stored_as_strings_deserialize_with_null_value_name()
    {
        // Before warnings carried a value name they were persisted as a plain array of strings.
        // The custom serializer must read that shape back as value-less warnings — no migration,
        // no data loss.
        var doc = new BsonDocument
        {
            { "_id", Guid.NewGuid().ToString() },
            { "serviceAccountId", Guid.NewGuid().ToString() },
            { "warnings", new BsonArray { "Peak too high", "check data" } },
            { "samples", new BsonArray() },
        };

        var submission = BsonSerializer.Deserialize<Submission>(doc);

        Assert.Equal(2, submission.Warnings.Count);
        Assert.All(submission.Warnings, w => Assert.Null(w.ValueName));
        Assert.Equal("Peak too high", submission.Warnings[0].Message);
        Assert.Equal("check data", submission.Warnings[1].Message);
    }

    [Fact]
    public void Structured_submission_warnings_round_trip_through_bson()
    {
        var submission = new Submission
        {
            ServiceAccountId = Guid.NewGuid(),
            Warnings = new()
            {
                new SubmissionWarning("sick", "Sample 'Weekly / Sick leave': too high"),
                new SubmissionWarning(null, "submission-level note"),
            },
        };

        var back = BsonSerializer.Deserialize<Submission>(submission.ToBsonDocument());

        Assert.Equal(2, back.Warnings.Count);
        Assert.Equal("sick", back.Warnings[0].ValueName);
        Assert.Equal("Sample 'Weekly / Sick leave': too high", back.Warnings[0].Message);
        Assert.Null(back.Warnings[1].ValueName);
        Assert.Equal("submission-level note", back.Warnings[1].Message);
    }
}
