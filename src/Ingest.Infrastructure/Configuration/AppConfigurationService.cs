using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Configuration;

/// <summary>
/// Database-backed application configuration singleton. There is at most one document; an absent one
/// is treated as an empty configuration so fresh and legacy deployments are back-compatible.
/// </summary>
public sealed class AppConfigurationService : IAppConfigurationService
{
    private readonly MongoContext _ctx;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="AppConfigurationService"/>.</summary>
    public AppConfigurationService(MongoContext ctx, IAuditContext audit)
    {
        _ctx = ctx;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAreasAsync(CancellationToken ct = default)
    {
        var existing = await _ctx.AppConfiguration.Find(FilterDefinition<AppConfiguration>.Empty).FirstOrDefaultAsync(ct);
        return existing?.Areas ?? new List<string>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> UpdateAreasAsync(IReadOnlyList<string> areas, CancellationToken ct = default)
    {
        var normalized = Normalize(areas);

        var existing = await _ctx.AppConfiguration.Find(FilterDefinition<AppConfiguration>.Empty).FirstOrDefaultAsync(ct);
        var now = _audit.UtcNow;
        var config = existing ?? new AppConfiguration { CreatedAt = now, CreatedBy = _audit.UserName };
        config.Areas = normalized;
        config.ModifiedAt = now;
        config.ModifiedBy = _audit.UserName;

        await _ctx.AppConfiguration.ReplaceOneAsync(
            Builders<AppConfiguration>.Filter.Eq(c => c.Id, config.Id),
            config,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return config.Areas;
    }

    /// <summary>Trim, drop blanks and de-duplicate (case-insensitively) while preserving order.</summary>
    private static List<string> Normalize(IReadOnlyList<string>? areas)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in areas ?? Array.Empty<string>())
        {
            var trimmed = raw?.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (seen.Add(trimmed)) result.Add(trimmed);
        }
        return result;
    }
}
