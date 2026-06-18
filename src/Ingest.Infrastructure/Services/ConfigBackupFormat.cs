using Ingest.Core.Common;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Pure (no I/O) reader/writer for the <em>configuration</em> backup file format — the Settings-page
/// data (approval policy + rules, email + notification settings and templates, webhooks,
/// integrations and the Teams connection) as opposed to the registry data covered by
/// <see cref="BackupFormat"/>. Kept separate so the format contract can be unit-tested without a
/// live database.
///
/// The on-disk shape is canonical extended JSON so every BSON type (GUIDs, 64-bit integers, dates)
/// round-trips exactly:
/// <code>
/// { "format": "ingest-config", "version": 1, "exportedAt": {…}, "collections": { "approvalRules": [ … ], … } }
/// </code>
///
/// Secret ciphertext (SMTP password, webhook signing secrets, the Teams bot secret) is exported as
/// stored. It is encrypted with a key derived from <c>ApiKey:Pepper</c>, so a restore only yields
/// usable secrets on a deployment configured with the same pepper.
/// </summary>
public static class ConfigBackupFormat
{
    /// <summary>Marker stamped into every configuration backup so we can reject unrelated files early.</summary>
    public const string Marker = "ingest-config";

    /// <summary>Current format version. Bumped only on a breaking change to the envelope.</summary>
    public const int Version = 1;

    /// <summary>The configuration collections included in a backup, in a stable order.</summary>
    public static readonly IReadOnlyList<string> Collections = new[]
    {
        "approvalSettings", "approvalRules", "emailSettings", "emailTemplates",
        "notificationSettings", "webhookEndpoints", "integrations", "teamsConnectionSettings",
    };

    /// <summary>Serialise the supplied per-collection documents into a configuration backup JSON string.</summary>
    /// <param name="data">Map of collection name → documents. Missing collections are written as empty arrays.</param>
    /// <param name="exportedAt">Timestamp stamped into the envelope (informational).</param>
    public static string Write(IReadOnlyDictionary<string, IReadOnlyList<BsonDocument>> data, DateTime exportedAt)
    {
        var collections = new BsonDocument();
        foreach (var name in Collections)
        {
            var docs = data.TryGetValue(name, out var list) ? list : Array.Empty<BsonDocument>();
            collections[name] = new BsonArray(docs);
        }

        var root = new BsonDocument
        {
            { "format", Marker },
            { "version", Version },
            { "exportedAt", BsonValue.Create(DateTime.SpecifyKind(exportedAt, DateTimeKind.Utc)) },
            { "collections", collections },
        };

        return root.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson });
    }

    /// <summary>
    /// Parse and validate a configuration backup file, returning the documents for each known
    /// collection that is present (unknown collections are ignored; absent ones are simply omitted
    /// from the result).
    /// </summary>
    /// <param name="json">The configuration backup JSON.</param>
    /// <exception cref="ValidationException">Empty/invalid JSON, missing/wrong marker, or unsupported version.</exception>
    public static IReadOnlyDictionary<string, List<BsonDocument>> Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ValidationException(new[] { "The configuration backup file is empty." });

        BsonDocument root;
        try
        {
            root = BsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            throw new ValidationException(new[] { $"The configuration backup file is not valid JSON: {ex.Message}" });
        }

        if (!root.TryGetValue("format", out var fmt) || !fmt.IsString || fmt.AsString != Marker)
            throw new ValidationException(new[] { "This file is not an Ingest configuration backup (missing or wrong format marker)." });

        var version = root.TryGetValue("version", out var v) && v.IsNumeric ? v.ToInt32() : 0;
        if (version <= 0 || version > Version)
            throw new ValidationException(new[] { $"Unsupported configuration backup version '{version}'. This server understands up to version {Version}." });

        if (!root.TryGetValue("collections", out var collsVal) || collsVal is not BsonDocument colls)
            throw new ValidationException(new[] { "The configuration backup file has no 'collections' section." });

        var result = new Dictionary<string, List<BsonDocument>>();
        foreach (var name in Collections)
        {
            if (colls.TryGetValue(name, out var arrVal) && arrVal is BsonArray arr)
                result[name] = arr.OfType<BsonDocument>().ToList();
        }
        return result;
    }
}
