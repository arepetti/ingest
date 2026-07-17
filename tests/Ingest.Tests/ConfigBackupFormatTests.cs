using Ingest.Core.Common;
using Ingest.Infrastructure.Services;
using MongoDB.Bson;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="ConfigBackupFormat"/> — the pure read/write half of the configuration
/// backup feature. Covers the faithful BSON round-trip and the validation gates (marker, version,
/// JSON validity) without needing a live database. Secret-preservation on import is a database
/// behaviour and lives with <see cref="BackupService"/>, not here.
/// </summary>
public class ConfigBackupFormatTests
{
    [Fact]
    public void Write_then_Read_round_trips_bson_values_faithfully()
    {
        var id = Guid.NewGuid();
        var doc = new BsonDocument
        {
            { "_id", new BsonBinaryData(id, GuidRepresentation.Standard) },
            { "hourUtc", new BsonInt64(8L) },
            { "modifiedAt", new BsonDateTime(new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc)) },
            { "secretCipher", "ZW5jcnlwdGVk" },
        };
        var data = new Dictionary<string, IReadOnlyList<BsonDocument>>
        {
            ["webhookEndpoints"] = new[] { doc },
        };

        var json = ConfigBackupFormat.Write(data, DateTime.UtcNow);
        var read = ConfigBackupFormat.Read(json);

        var restored = Assert.Single(read["webhookEndpoints"]);
        Assert.Equal(8L, restored["hourUtc"].AsInt64);
        Assert.Equal("ZW5jcnlwdGVk", restored["secretCipher"].AsString);
        Assert.Equal(id, restored["_id"].AsGuid);
        Assert.Equal(new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc), restored["modifiedAt"].ToUniversalTime());
    }

    [Fact]
    public void Write_then_Read_round_trips_the_cadence_anchor_and_kill_switch_fields()
    {
        // AppConfiguration is exported/restored as a raw BsonDocument (no typed mapping), so any
        // field added to the entity round-trips automatically. This pins that for the fields added
        // alongside the configurable cadence anchors and ingestion kill switch.
        var doc = new BsonDocument
        {
            { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
            { "areas", new BsonArray(new[] { "North", "South" }) },
            { "fiscalYearStartMonth", new BsonInt32(4) },
            { "weekStartDay", new BsonInt32((int)DayOfWeek.Sunday) },
            { "monthStartDay", new BsonInt32(15) },
            { "fortnightAnchor", new BsonDateTime(new DateTime(2020, 6, 6, 0, 0, 0, DateTimeKind.Utc)) },
            { "submissionsClosed", BsonBoolean.True },
            { "submissionsClosedMessage", "Maintenance in progress" },
        };
        var data = new Dictionary<string, IReadOnlyList<BsonDocument>>
        {
            ["appConfiguration"] = new[] { doc },
        };

        var json = ConfigBackupFormat.Write(data, DateTime.UtcNow);
        var read = ConfigBackupFormat.Read(json);

        var restored = Assert.Single(read["appConfiguration"]);
        Assert.Equal(new[] { "North", "South" }, restored["areas"].AsBsonArray.Select(v => v.AsString));
        Assert.Equal(4, restored["fiscalYearStartMonth"].AsInt32);
        Assert.Equal((int)DayOfWeek.Sunday, restored["weekStartDay"].AsInt32);
        Assert.Equal(15, restored["monthStartDay"].AsInt32);
        Assert.Equal(new DateTime(2020, 6, 6, 0, 0, 0, DateTimeKind.Utc), restored["fortnightAnchor"].ToUniversalTime());
        Assert.True(restored["submissionsClosed"].AsBoolean);
        Assert.Equal("Maintenance in progress", restored["submissionsClosedMessage"].AsString);
    }

    [Fact]
    public void Write_includes_every_known_collection_even_when_empty()
    {
        var json = ConfigBackupFormat.Write(new Dictionary<string, IReadOnlyList<BsonDocument>>(), DateTime.UtcNow);
        var read = ConfigBackupFormat.Read(json);

        foreach (var name in ConfigBackupFormat.Collections)
            Assert.Empty(read[name]);
    }

    [Fact]
    public void Read_rejects_non_json()
    {
        var ex = Assert.Throws<ValidationException>(() => ConfigBackupFormat.Read("not json {"));
        Assert.Contains("not valid JSON", ex.Errors[0]);
    }

    [Fact]
    public void Read_rejects_empty()
    {
        var ex = Assert.Throws<ValidationException>(() => ConfigBackupFormat.Read("   "));
        Assert.Contains("empty", ex.Errors[0]);
    }

    [Fact]
    public void Read_rejects_a_file_without_the_marker()
    {
        var ex = Assert.Throws<ValidationException>(() => ConfigBackupFormat.Read("""{ "version": 1, "collections": {} }"""));
        Assert.Contains("not an Ingest configuration backup", ex.Errors[0]);
    }

    [Fact]
    public void Read_rejects_a_data_backup_file()
    {
        // A regular data backup must not be accepted by the configuration importer.
        var json = $$"""{ "format": "{{BackupFormat.Marker}}", "version": 1, "collections": {} }""";
        var ex = Assert.Throws<ValidationException>(() => ConfigBackupFormat.Read(json));
        Assert.Contains("not an Ingest configuration backup", ex.Errors[0]);
    }

    [Fact]
    public void Read_rejects_an_unsupported_version()
    {
        var json = $$"""{ "format": "{{ConfigBackupFormat.Marker}}", "version": 999, "collections": {} }""";
        var ex = Assert.Throws<ValidationException>(() => ConfigBackupFormat.Read(json));
        Assert.Contains("Unsupported configuration backup version", ex.Errors[0]);
    }

    [Fact]
    public void Read_ignores_unknown_collections_and_tolerates_missing_ones()
    {
        var json = $$"""
        { "format": "{{ConfigBackupFormat.Marker}}", "version": {{ConfigBackupFormat.Version}},
          "collections": { "approvalRules": [ { "name": "a" } ], "somethingElse": [ { "x": 1 } ] } }
        """;

        var read = ConfigBackupFormat.Read(json);

        Assert.Single(read["approvalRules"]);
        Assert.False(read.ContainsKey("somethingElse"));
        // A known-but-absent collection is simply not present (restore skips it).
        Assert.False(read.ContainsKey("integrations"));
    }
}
