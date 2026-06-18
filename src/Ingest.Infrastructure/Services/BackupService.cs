using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IBackupService"/>. Reads/writes raw <see cref="BsonDocument"/>s straight from
/// the database so the dump is faithful to whatever is stored (hashed keys, audit fields, derived
/// projections) and survives schema evolution. A restore deletes the contents of each collection
/// and re-inserts the backed-up documents — indexes are left in place (we delete rows, not the
/// collection), so they keep enforcing uniqueness as the data goes back in.
///
/// This is deliberately simple and is <b>not</b> transactional across collections: see
/// <see cref="IBackupService"/> for the "small deployments only" caveat.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly MongoContext _ctx;
    private readonly TimeProvider _time;

    /// <summary>Create a new <see cref="BackupService"/>.</summary>
    /// <param name="ctx">Mongo context exposing the database handle.</param>
    /// <param name="time">Clock used to stamp the export timestamp.</param>
    public BackupService(MongoContext ctx, TimeProvider time)
    {
        _ctx = ctx;
        _time = time;
    }

    /// <summary>
    /// camelCase BSON element name of the encrypted secret on each secret-bearing configuration
    /// collection (see <see cref="MongoSetup"/>'s element-name convention). On a config import a
    /// stored value is preserved when the incoming document omits this field.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ConfigSecretFields = new Dictionary<string, string>
    {
        ["emailSettings"] = "passwordCipher",
        ["webhookEndpoints"] = "secretCipher",
        ["teamsConnectionSettings"] = "appPasswordCipher",
    };

    /// <inheritdoc />
    public async Task<string> ExportAsync(CancellationToken ct = default) =>
        BackupFormat.Write(await ReadCollectionsAsync(BackupFormat.Collections, ct), _time.GetUtcNow().UtcDateTime);

    /// <inheritdoc />
    public async Task<BackupImportResult> ImportAsync(string json, CancellationToken ct = default) =>
        await ImportCollectionsAsync(BackupFormat.Read(json), BackupFormat.Collections, preserveSecrets: false, ct);

    /// <inheritdoc />
    public async Task<string> ExportConfigAsync(CancellationToken ct = default) =>
        ConfigBackupFormat.Write(await ReadCollectionsAsync(ConfigBackupFormat.Collections, ct), _time.GetUtcNow().UtcDateTime);

    /// <inheritdoc />
    public async Task<BackupImportResult> ImportConfigAsync(string json, CancellationToken ct = default) =>
        await ImportCollectionsAsync(ConfigBackupFormat.Read(json), ConfigBackupFormat.Collections, preserveSecrets: true, ct);

    private async Task<Dictionary<string, IReadOnlyList<BsonDocument>>> ReadCollectionsAsync(
        IReadOnlyList<string> names, CancellationToken ct)
    {
        var data = new Dictionary<string, IReadOnlyList<BsonDocument>>();
        foreach (var name in names)
        {
            var coll = _ctx.Database.GetCollection<BsonDocument>(name);
            data[name] = await coll.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);
        }
        return data;
    }

    private async Task<BackupImportResult> ImportCollectionsAsync(
        IReadOnlyDictionary<string, List<BsonDocument>> data,
        IReadOnlyList<string> names,
        bool preserveSecrets,
        CancellationToken ct)
    {
        var restored = new Dictionary<string, int>();
        foreach (var name in names)
        {
            if (!data.TryGetValue(name, out var docs)) continue;

            var coll = _ctx.Database.GetCollection<BsonDocument>(name);

            if (preserveSecrets && ConfigSecretFields.TryGetValue(name, out var secretField))
                await PreserveSecretsAsync(coll, docs, secretField, ct);

            await coll.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);
            if (docs.Count > 0)
                await coll.InsertManyAsync(docs, cancellationToken: ct);
            restored[name] = docs.Count;
        }

        return new BackupImportResult(restored);
    }

    /// <summary>
    /// Fill in any missing/blank secret on the incoming documents from what is currently stored, so a
    /// configuration import that omits secrets never wipes a working one. Per-document collections
    /// (e.g. webhook endpoints) are matched by <c>_id</c>; singletons fall back to the single stored doc.
    /// </summary>
    private async Task PreserveSecretsAsync(
        IMongoCollection<BsonDocument> coll, List<BsonDocument> incoming, string secretField, CancellationToken ct)
    {
        if (incoming.Count == 0) return;
        if (incoming.All(d => HasSecret(d, secretField))) return;

        var existing = await coll.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);
        if (existing.Count == 0) return;

        var byId = existing
            .Where(d => d.Contains("_id"))
            .GroupBy(d => d["_id"])
            .ToDictionary(g => g.Key, g => g.First());
        var soleExisting = existing.Count == 1 ? existing[0] : null;

        foreach (var doc in incoming)
        {
            if (HasSecret(doc, secretField)) continue;

            BsonDocument? source = null;
            if (doc.TryGetValue("_id", out var id) && byId.TryGetValue(id, out var match)) source = match;
            source ??= soleExisting;

            if (source is not null && source.TryGetValue(secretField, out var secret) && !secret.IsBsonNull)
                doc[secretField] = secret;
        }
    }

    private static bool HasSecret(BsonDocument doc, string secretField) =>
        doc.TryGetValue(secretField, out var v) && !v.IsBsonNull &&
        !(v.IsString && string.IsNullOrEmpty(v.AsString));
}
