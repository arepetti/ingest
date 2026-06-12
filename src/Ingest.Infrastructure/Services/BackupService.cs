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

    /// <inheritdoc />
    public async Task<string> ExportAsync(CancellationToken ct = default)
    {
        var data = new Dictionary<string, IReadOnlyList<BsonDocument>>();
        foreach (var name in BackupFormat.Collections)
        {
            var coll = _ctx.Database.GetCollection<BsonDocument>(name);
            data[name] = await coll.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);
        }
        return BackupFormat.Write(data, _time.GetUtcNow().UtcDateTime);
    }

    /// <inheritdoc />
    public async Task<BackupImportResult> ImportAsync(string json, CancellationToken ct = default)
    {
        var data = BackupFormat.Read(json);

        var restored = new Dictionary<string, int>();
        foreach (var name in BackupFormat.Collections)
        {
            if (!data.TryGetValue(name, out var docs)) continue;

            var coll = _ctx.Database.GetCollection<BsonDocument>(name);
            await coll.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);
            if (docs.Count > 0)
                await coll.InsertManyAsync(docs, cancellationToken: ct);
            restored[name] = docs.Count;
        }

        return new BackupImportResult(restored);
    }
}
