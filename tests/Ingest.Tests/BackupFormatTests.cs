using Ingest.Core.Common;
using Ingest.Infrastructure.Services;
using MongoDB.Bson;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="BackupFormat"/> — the pure read/write half of the backup feature. Covers
/// the faithful BSON round-trip and the validation gates (marker, version, JSON validity) without
/// needing a live database.
/// </summary>
public class BackupFormatTests
{
    [Fact]
    public void Write_then_Read_round_trips_bson_values_faithfully()
    {
        var id = Guid.NewGuid();
        var doc = new BsonDocument
        {
            { "_id", new BsonBinaryData(id, GuidRepresentation.Standard) },
            { "count", new BsonInt64(9_000_000_000L) },
            { "when", new BsonDateTime(new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc)) },
            { "name", "roads" },
        };
        var data = new Dictionary<string, IReadOnlyList<BsonDocument>>
        {
            ["accounts"] = new[] { doc },
        };

        var json = BackupFormat.Write(data, DateTime.UtcNow);
        var read = BackupFormat.Read(json);

        var restored = Assert.Single(read["accounts"]);
        Assert.Equal(9_000_000_000L, restored["count"].AsInt64);
        Assert.Equal("roads", restored["name"].AsString);
        Assert.Equal(id, restored["_id"].AsGuid);
        Assert.Equal(new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc), restored["when"].ToUniversalTime());
    }

    [Fact]
    public void Write_includes_every_known_collection_even_when_empty()
    {
        var json = BackupFormat.Write(new Dictionary<string, IReadOnlyList<BsonDocument>>(), DateTime.UtcNow);
        var read = BackupFormat.Read(json);

        foreach (var name in BackupFormat.Collections)
            Assert.Empty(read[name]);
    }

    [Fact]
    public void Read_rejects_non_json()
    {
        var ex = Assert.Throws<ValidationException>(() => BackupFormat.Read("not json {"));
        Assert.Contains("not valid JSON", ex.Errors[0]);
    }

    [Fact]
    public void Read_rejects_empty()
    {
        var ex = Assert.Throws<ValidationException>(() => BackupFormat.Read("   "));
        Assert.Contains("empty", ex.Errors[0]);
    }

    [Fact]
    public void Read_rejects_a_file_without_the_marker()
    {
        var ex = Assert.Throws<ValidationException>(() => BackupFormat.Read("""{ "version": 1, "collections": {} }"""));
        Assert.Contains("not an Ingest backup", ex.Errors[0]);
    }

    [Fact]
    public void Read_rejects_an_unsupported_version()
    {
        var json = $$"""{ "format": "{{BackupFormat.Marker}}", "version": 999, "collections": {} }""";
        var ex = Assert.Throws<ValidationException>(() => BackupFormat.Read(json));
        Assert.Contains("Unsupported backup version", ex.Errors[0]);
    }

    [Fact]
    public void Read_ignores_unknown_collections_and_tolerates_missing_ones()
    {
        var json = $$"""
        { "format": "{{BackupFormat.Marker}}", "version": {{BackupFormat.Version}},
          "collections": { "accounts": [ { "name": "a" } ], "somethingElse": [ { "x": 1 } ] } }
        """;

        var read = BackupFormat.Read(json);

        Assert.Single(read["accounts"]);
        Assert.False(read.ContainsKey("somethingElse"));
        // A known-but-absent collection is simply not present (restore skips it).
        Assert.False(read.ContainsKey("reports"));
    }
}
