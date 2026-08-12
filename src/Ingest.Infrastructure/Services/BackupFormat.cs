using Ingest.Core.Common;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Pure (no I/O) reader/writer for the backup file format. Kept separate from
/// <see cref="BackupService"/> so the format contract — the marker, the version gate, and the
/// faithful BSON round-trip — can be unit-tested without a live database.
///
/// The on-disk shape is canonical extended JSON so every BSON type (GUIDs, 64-bit integers,
/// dates) round-trips exactly:
/// <code>
/// { "format": "ingest-backup", "version": 1, "exportedAt": {…}, "collections": { "accounts": [ … ], … } }
/// </code>
/// </summary>
public static class BackupFormat
{
    /// <summary>Marker stamped into every backup so we can reject unrelated files early.</summary>
    public const string Marker = "ingest-backup";

    /// <summary>Current format version. Bumped only on a breaking change to the envelope.</summary>
    public const int Version = 1;

    /// <summary>
    /// Collections included in a backup, in a stable order. <c>samples</c> is the derived read
    /// model; it's included so a restore is a straight insert with no projection rebuild.
    /// </summary>
    public static readonly IReadOnlyList<string> Collections = new[]
    {
        "accounts", "apiKeys", "schemas", "submissions", "samples", "reports", "auditLogs",
    };

    /// <summary>Serialise the supplied per-collection documents into a backup JSON string.</summary>
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
    /// Parse and validate a backup file, returning the documents for each known collection that is
    /// present (unknown collections are ignored; absent ones are simply omitted from the result).
    /// </summary>
    /// <param name="json">The backup JSON.</param>
    /// <exception cref="ValidationException">Empty/invalid JSON, missing/wrong marker, or unsupported version.</exception>
    public static IReadOnlyDictionary<string, List<BsonDocument>> Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ValidationException(new[]
            {
                new Diagnostic(DiagnosticCodes.Configuration.BackupEmpty, "The backup file is empty."),
            });

        BsonDocument root;
        try
        {
            root = BsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Configuration.BackupInvalidJson,
                    $"The backup file is not valid JSON: {ex.Message}",
                    ("detail", ex.Message)),
            });
        }

        if (!root.TryGetValue("format", out var fmt) || !fmt.IsString || fmt.AsString != Marker)
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Configuration.BackupInvalidMarker,
                    "This file is not an Ingest backup (missing or wrong format marker).",
                    ("expectedFormat", Marker),
                    ("actualFormat", fmt?.IsString == true ? fmt.AsString : null)),
            });

        var version = root.TryGetValue("version", out var v) && v.IsNumeric ? v.ToInt32() : 0;
        if (version <= 0 || version > Version)
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Configuration.BackupUnsupportedVersion,
                    $"Unsupported backup version '{version}'. This server understands up to version {Version}.",
                    ("version", version),
                    ("maximumVersion", Version)),
            });

        if (!root.TryGetValue("collections", out var collsVal) || collsVal is not BsonDocument colls)
            throw new ValidationException(new[]
            {
                new Diagnostic(
                    DiagnosticCodes.Configuration.BackupMissingCollections,
                    "The backup file has no 'collections' section."),
            });

        var result = new Dictionary<string, List<BsonDocument>>();
        foreach (var name in Collections)
        {
            if (colls.TryGetValue(name, out var arrVal) && arrVal is BsonArray arr)
                result[name] = arr.OfType<BsonDocument>().ToList();
        }
        return result;
    }
}
