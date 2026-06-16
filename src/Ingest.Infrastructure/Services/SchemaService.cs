using System.Text.Json;
using System.Text.RegularExpressions;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="ISchemaService"/>. Adds the visibility filter (global or
/// in <c>ServiceIds</c>) to the service-facing read paths, enforces name uniqueness and
/// structural validation on create, applies PUT-style replacement on update, manages the version
/// timestamp, builds example submissions for the import/export UI, and aggregates the per-value
/// history for the admin-facing charts.
/// </summary>
public sealed class SchemaService : ISchemaService
{
    /// <summary>
    /// Safety cap on layout nesting depth. The UI doesn't need more than a handful of levels and
    /// allowing arbitrary depth opens the door to malformed input blowing the stack on the
    /// recursive walks. The cap is intentionally generous (admins will not hit it organically).
    /// </summary>
    private const int MaxLayoutDepth = 32;

    private readonly ISchemaRepository _schemas;
    private readonly ISampleRepository _samples;
    private readonly IAuditLogService _audit;
    private readonly ISchemaVersionHistoryRepository _versions;
    private readonly ISubmissionRepository _submissions;
    private readonly IAuditContext _auditContext;

    /// <summary>Create a new <see cref="SchemaService"/>.</summary>
    /// <param name="schemas">Schema repository.</param>
    /// <param name="samples">Sample projection repository (used only by the history aggregation).</param>
    /// <param name="audit">Audit log used to record create/edit/delete changes.</param>
    /// <param name="versions">Version-history repository — receives a full snapshot on every save.</param>
    /// <param name="submissions">Submission repository, used to snapshot the submission count at save time.</param>
    /// <param name="auditContext">Ambient who/when context for stamping the snapshot author + timestamp.</param>
    public SchemaService(
        ISchemaRepository schemas,
        ISampleRepository samples,
        IAuditLogService audit,
        ISchemaVersionHistoryRepository versions,
        ISubmissionRepository submissions,
        IAuditContext auditContext)
    {
        _schemas = schemas;
        _samples = samples;
        _audit = audit;
        _versions = versions;
        _submissions = submissions;
        _auditContext = auditContext;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceAccountId, CancellationToken ct = default) =>
        _schemas.ListVisibleToAsync(serviceAccountId, ct);

    /// <inheritdoc />
    public async Task<Schema?> GetVisibleAsync(Guid serviceAccountId, string name, CancellationToken ct = default)
    {
        var s = await _schemas.GetByNameAsync(name, ct: ct);
        if (s is null) return null;
        // Hide non-visible schemas behind 'not found' from the caller's POV: they shouldn't even
        // learn the schema exists if they're not in its audience.
        if (!s.IsGlobal && !s.ServiceIds.Contains(serviceAccountId)) return null;
        return s;
    }

    /// <inheritdoc />
    public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) =>
        _schemas.ListAsync(request, ct);

    /// <inheritdoc />
    public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct = default) =>
        _schemas.GetByIdAsync(id, includeDeleted, ct);

    /// <inheritdoc />
    public async Task<Schema> CreateAsync(Schema input, CancellationToken ct = default)
    {
        // Soft-deleted records still hold their name slot in the unique index. If the caller is
        // recreating a schema with the same name as one that was previously deleted, hard-delete
        // the old row first so the fresh insert can take the slot — there's no business value in
        // keeping the tombstone around, and the existing samples that referenced it are filtered
        // out of every read path by their own IsDeleted flag (cadence checks, status, reports,
        // OData feed all exclude soft-deleted samples by default).
        var collision = await _schemas.GetByNameAsync(input.Name, includeDeleted: true, ct);
        if (collision is not null)
        {
            if (!collision.IsDeleted)
                throw new ConflictException($"Schema '{input.Name}' already exists.");

            await _schemas.HardDeleteAsync(collision.Id, ct);
        }

        ValidateStructure(input);

        // CreatedAt is stamped by the repository — set VersionModifiedAt to "now" so it aligns
        // with the moment the schema was first persisted. The repo will overwrite CreatedAt on
        // its way through but the relative ordering matches what we want.
        input.VersionModifiedAt = DateTime.UtcNow;

        await _schemas.AddAsync(input, ct);
        await _audit.RecordAsync(AuditTargetType.Schema, AuditChangeType.Create, input.Id, input.Name, ct);
        // A brand-new schema cannot have any live submissions yet (any tombstoned ones were
        // soft-deleted), so the snapshot's submission count is 0 and there is no "old" version.
        await RecordHistoryAsync(input, oldVersion: null, versionBumped: false, submissionCount: 0, ct);
        return input;
    }

    /// <inheritdoc />
    public async Task<Schema?> UpdateAsync(Guid id, Schema input, CancellationToken ct = default)
    {
        var existing = await _schemas.GetByIdAsync(id, ct: ct);
        if (existing is null) return null;

        // Monotonic check before touching anything else so we don't half-apply on rejection.
        if (input.Version < existing.Version)
            throw new ValidationException(new[]
            {
                $"Schema version cannot be decreased (was {existing.Version}, got {input.Version}).",
            });

        // Capture the version before we overwrite it so the history snapshot can record old → new.
        var oldVersion = existing.Version;

        // Rename collision: same treatment as Create. If the new name is held by a live schema
        // we reject; if it's held by a soft-deleted one we hard-delete the tombstone so the
        // rename can succeed against the unique index.
        if (!string.Equals(existing.Name, input.Name, StringComparison.OrdinalIgnoreCase))
        {
            var collision = await _schemas.GetByNameAsync(input.Name, includeDeleted: true, ct);
            if (collision is not null && collision.Id != id)
            {
                if (!collision.IsDeleted)
                    throw new ConflictException($"Schema '{input.Name}' already exists.");
                await _schemas.HardDeleteAsync(collision.Id, ct);
            }
        }

        // Build a candidate so we can validate structural rules with the new values & layout
        // in the same shape they'll have once persisted.
        existing.Name = input.Name;
        existing.Label = input.Label;
        existing.Description = input.Description;
        existing.Notes = input.Notes;
        existing.Modifiable = input.Modifiable;
        existing.Enabled = input.Enabled;
        existing.SubmissionValidations = input.SubmissionValidations ?? new();
        existing.IsGlobal = input.IsGlobal;
        existing.ServiceIds = input.ServiceIds ?? new();
        existing.Values = input.Values ?? new();
        existing.Layout = input.Layout ?? new();

        var versionChanged = input.Version != existing.Version;
        existing.Version = input.Version;
        if (versionChanged) existing.VersionModifiedAt = DateTime.UtcNow;

        ValidateStructure(existing);

        await _schemas.UpdateAsync(existing, ct);
        await _audit.RecordAsync(AuditTargetType.Schema, AuditChangeType.Edit, existing.Id, existing.Name, ct);
        var submissionCount = await _submissions.CountBySchemaAsync(existing.Name, ct);
        await RecordHistoryAsync(existing, oldVersion, versionChanged, submissionCount, ct);
        return existing;
    }

    /// <summary>
    /// Persist a full snapshot of <paramref name="schema"/> to the version history. Called after a
    /// successful create/update so the admin "version history" page can show who saved what, when,
    /// the version before/after, whether it was Published (Enabled) or Draft, and how many
    /// submissions existed at that point — and so "view this version" can reconstruct the schema.
    /// </summary>
    private Task RecordHistoryAsync(Schema schema, int? oldVersion, bool versionBumped, long submissionCount, CancellationToken ct)
    {
        var entry = new SchemaVersionHistory
        {
            SchemaId = schema.Id,
            SchemaName = schema.Name,
            ChangeDate = _auditContext.UtcNow,
            AuthorId = _auditContext.AccountId,
            AuthorName = _auditContext.UserName,
            OldVersion = oldVersion,
            NewVersion = schema.Version,
            VersionBumped = versionBumped,
            Enabled = schema.Enabled,
            SubmissionCount = submissionCount,
            Snapshot = CloneSchema(schema),
        };
        return _versions.AddAsync(entry, ct);
    }

    /// <summary>
    /// Deep-copy a schema for an immutable history snapshot. The live entity keeps being mutated on
    /// later saves, so the snapshot must not share its <see cref="Schema.Values"/> /
    /// <see cref="Schema.Layout"/> lists.
    /// </summary>
    private static Schema CloneSchema(Schema s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Label = s.Label,
        Description = s.Description,
        Notes = s.Notes,
        Modifiable = s.Modifiable,
        Enabled = s.Enabled,
        SubmissionValidations = s.SubmissionValidations.ToList(),
        IsGlobal = s.IsGlobal,
        ServiceIds = s.ServiceIds.ToList(),
        Values = s.Values.Select(CloneValue).ToList(),
        Layout = s.Layout.Select(CloneLayoutNode).ToList(),
        Version = s.Version,
        VersionModifiedAt = s.VersionModifiedAt,
        CreatedAt = s.CreatedAt,
        CreatedBy = s.CreatedBy,
        ModifiedAt = s.ModifiedAt,
        ModifiedBy = s.ModifiedBy,
        IsDeleted = s.IsDeleted,
        DeletedAt = s.DeletedAt,
        DeletedBy = s.DeletedBy,
    };

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // Refuse to delete schemas that are still referenced by live submissions. Soft-deleting
        // the schema would leave orphan samples that the OData feed and the history charts can't
        // resolve back to a definition — the safer move is to disable the schema so it stops
        // accepting new data while history stays browseable.
        var existing = await _schemas.GetByIdAsync(id, ct: ct);
        if (existing is null) return; // idempotent: nothing to delete

        if (await _samples.IsSchemaInUseAsync(existing.Name, ct))
            throw new ConflictException(
                $"Schema '{existing.Label ?? existing.Name}' is referenced by one or more submissions and cannot be deleted. " +
                "Disable it instead to stop accepting new data while keeping the history intact.");

        await _schemas.SoftDeleteAsync(id, ct);
        await _audit.RecordAsync(AuditTargetType.Schema, AuditChangeType.Delete, existing.Id, existing.Name, ct);
    }

    /// <inheritdoc />
    public async Task<Schema?> CloneAsync(Guid id, CancellationToken ct = default)
    {
        var source = await _schemas.GetByIdAsync(id, ct: ct);
        if (source is null) return null;

        var newName = await AllocateCloneNameAsync(source.Name, ct);
        var now = DateTime.UtcNow;

        var clone = new Schema
        {
            Name = newName,
            Label = source.Label,
            Description = source.Description,
            Notes = source.Notes,
            Modifiable = source.Modifiable,
            Enabled = source.Enabled,
            SubmissionValidations = source.SubmissionValidations.ToList(),
            IsGlobal = source.IsGlobal,
            ServiceIds = source.ServiceIds.ToList(),
            Values = source.Values.Select(CloneValue).ToList(),
            Layout = source.Layout.Select(CloneLayoutNode).ToList(),
            Version = source.Version,
            VersionModifiedAt = now,
        };

        await _schemas.AddAsync(clone, ct);
        await _audit.RecordAsync(AuditTargetType.Schema, AuditChangeType.Create, clone.Id, clone.Name, ct);
        return clone;
    }

    /// <inheritdoc />
    public async Task<SubmissionInput?> BuildExampleSubmissionAsync(Guid serviceAccountId, string name, CancellationToken ct = default)
    {
        var schema = await GetVisibleAsync(serviceAccountId, name, ct);
        if (schema is null) return null;

        var now = DateTime.UtcNow;
        var samples = schema.Values
            .Select(v => new SampleInput(schema.Name, v.Name, BuildDefaultValue(v, now), now, null))
            .ToList();
        return new SubmissionInput(samples);
    }

    /// <summary>
    /// Pick a fresh, non-colliding name for a clone. Walks <c>{source}_copy</c>,
    /// <c>{source}_copy_2</c>, <c>{source}_copy_3</c> until the repository confirms no entry —
    /// including soft-deleted entries — owns that name.
    /// </summary>
    private async Task<string> AllocateCloneNameAsync(string sourceName, CancellationToken ct)
    {
        var baseName = $"{sourceName}_copy";
        var candidate = baseName;
        var n = 2;
        while (await _schemas.GetByNameAsync(candidate, includeDeleted: true, ct) is not null)
        {
            candidate = $"{baseName}_{n++}";
        }
        return candidate;
    }

    /// <summary>
    /// Pick a sensible default sample value for a <see cref="SchemaValue"/>, honouring the
    /// value's lower bounds when set. Returns a <see cref="JsonElement"/> shaped like what the
    /// submission API expects on the wire.
    /// </summary>
    private static JsonElement BuildDefaultValue(SchemaValue v, DateTime now)
    {
        return v.Type switch
        {
            SchemaValueType.String => ToJsonElement(string.Empty),
            SchemaValueType.Integer => ToJsonElement((long)(v.Min ?? 0d)),
            SchemaValueType.Number => ToJsonElement(v.Min ?? 0d),
            SchemaValueType.Date => ToJsonElement((v.MinDate ?? now.Date).ToString("o", System.Globalization.CultureInfo.InvariantCulture)),
            SchemaValueType.Boolean => ToJsonElement(false),
            _ => ToJsonElement((object?)null),
        };
    }

    private static JsonElement ToJsonElement(object? value)
    {
        // Serialise then re-parse — cheap, and avoids hand-building JsonElement instances.
        var json = JsonSerializer.Serialize(value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Cross-cutting structural validator. Runs on every create and update; throws a single
    /// <see cref="ValidationException"/> aggregating all detected issues so the SPA can show a
    /// useful "everything that's wrong" summary instead of revealing one problem per round-trip.
    /// </summary>
    private static void ValidateStructure(Schema schema)
    {
        var errors = new List<string>();

        if (schema.Version < 0)
            errors.Add("Schema version must be greater than or equal to 0.");

        // Value-name format: every name must be a valid C-style identifier so it can be used
        // unbracketed in NCalc rules AND so it doesn't collide with the bracketed
        // `[name.minimum]` / `[name.maximum]` bound namespace. A name containing `.`, `-`, or
        // whitespace would either need NCalc's bracket form to be referenced at all or would
        // make `[foo.bar.maximum]` ambiguous between "value `foo.bar`'s maximum" and "value
        // `foo.bar.maximum`".
        foreach (var v in schema.Values)
        {
            if (!IsValidValueName(v.Name))
                errors.Add(
                    $"Value name '{v.Name}' is not a valid identifier. " +
                    "Names must start with a letter or underscore and contain only letters, " +
                    "digits, and underscores (no dots, hyphens, spaces, or other punctuation).");
        }

        // Per-value SinceVersion bounds. The wire contract treats null as 1, so we apply the
        // same coercion here.
        foreach (var v in schema.Values)
        {
            var since = v.SinceVersion ?? 1;
            if (since < 0)
                errors.Add($"Value '{v.Name}' has SinceVersion < 0.");
            if (since > schema.Version)
                errors.Add($"Value '{v.Name}' has SinceVersion {since} greater than the schema's version {schema.Version}.");
        }

        // Layout: every value-ref resolves; no value is referenced more than once; section nodes
        // have a non-empty caption; nesting is bounded.
        var valueNames = new HashSet<string>(schema.Values.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        var seenRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateLayoutNodes(schema.Layout, depth: 0, errors, valueNames, seenRefs);

        if (errors.Count > 0) throw new ValidationException(errors);
    }

    /// <summary>
    /// C-style identifier rule: first character is a letter or underscore, subsequent
    /// characters add digits. Lines up with NCalc's plain identifier grammar, C# field
    /// naming, and JavaScript variable naming all at once. Empty / whitespace names are
    /// also rejected here.
    /// </summary>
    private static readonly Regex _identifierPattern =
        new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static bool IsValidValueName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _identifierPattern.IsMatch(name);

    private static void ValidateLayoutNodes(
        IReadOnlyList<SchemaLayoutNode> nodes,
        int depth,
        List<string> errors,
        HashSet<string> valueNames,
        HashSet<string> seenRefs)
    {
        if (depth > MaxLayoutDepth)
        {
            errors.Add($"Layout exceeds the maximum nesting depth of {MaxLayoutDepth}.");
            return;
        }

        foreach (var node in nodes)
        {
            if (node.Kind == SchemaLayoutNodeKind.Value)
            {
                if (string.IsNullOrWhiteSpace(node.ValueName))
                {
                    errors.Add("Layout has a value node without a valueName.");
                    continue;
                }
                if (!valueNames.Contains(node.ValueName))
                {
                    errors.Add($"Layout references unknown value '{node.ValueName}'.");
                    continue;
                }
                if (!seenRefs.Add(node.ValueName))
                {
                    errors.Add($"Value '{node.ValueName}' is referenced more than once in the layout.");
                }
            }
            else if (node.Kind == SchemaLayoutNodeKind.Section)
            {
                if (string.IsNullOrWhiteSpace(node.Caption))
                {
                    errors.Add("Layout has a section node without a caption.");
                }
                if (node.Items is { Count: > 0 })
                {
                    ValidateLayoutNodes(node.Items, depth + 1, errors, valueNames, seenRefs);
                }
            }
            else
            {
                errors.Add($"Layout has a node with unknown kind '{node.Kind}'. Expected '{SchemaLayoutNodeKind.Value}' or '{SchemaLayoutNodeKind.Section}'.");
            }
        }
    }

    private static SchemaValue CloneValue(SchemaValue v) => new()
    {
        Name = v.Name,
        Label = v.Label,
        Description = v.Description,
        Notes = v.Notes,
        Caption = v.Caption,
        Type = v.Type,
        Unit = v.Unit,
        Cadence = v.Cadence,
        Required = v.Required,
        Modifiable = v.Modifiable,
        Enabled = v.Enabled,
        Min = v.Min,
        Max = v.Max,
        MinDate = v.MinDate,
        MaxDate = v.MaxDate,
        MinLength = v.MinLength,
        MaxLength = v.MaxLength,
        RegexPattern = v.RegexPattern,
        ValueValidation = v.ValueValidation,
        EnabledIf = v.EnabledIf,
        VisibleIf = v.VisibleIf,
        Warning = v.Warning,
        SinceVersion = v.SinceVersion,
    };

    private static SchemaLayoutNode CloneLayoutNode(SchemaLayoutNode n) => new()
    {
        Kind = n.Kind,
        ValueName = n.ValueName,
        Caption = n.Caption,
        Description = n.Description,
        Items = n.Items.Select(CloneLayoutNode).ToList(),
    };

    /// <inheritdoc />
    public async Task<SchemaHistory?> GetHistoryAsync(string name, CancellationToken ct = default)
    {
        var schema = await _schemas.GetByNameAsync(name, ct: ct);
        if (schema is null) return null;

        var numericValues = schema.Values
            .Where(v => v.Type is SchemaValueType.Number or SchemaValueType.Integer)
            .ToList();

        if (numericValues.Count == 0)
            return new SchemaHistory(schema.Name, schema.Label, Array.Empty<SchemaValueHistory>());

        var numericNames = numericValues
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var all = await _samples.GetAllForSchemaAsync(name, ct);

        // Period boundaries are pre-computed at submission time according to each value's cadence,
        // so submissions from different services within the same cadence window naturally collapse.
        var bucketsByValue = all
            .Where(s => numericNames.Contains(s.ValueName))
            .Select(s => new
            {
                s.ValueName,
                s.PeriodStart,
                s.PeriodEnd,
                // NumberValue carries Number; IntegerValue carries Integer. Cast to a common double.
                Value = s.NumberValue ?? (double?)s.IntegerValue,
            })
            .Where(x => x.Value.HasValue)
            .GroupBy(x => (Name: x.ValueName, x.PeriodStart, x.PeriodEnd))
            .Select(g => new
            {
                g.Key.Name,
                g.Key.PeriodStart,
                g.Key.PeriodEnd,
                Min = g.Min(x => x.Value!.Value),
                Max = g.Max(x => x.Value!.Value),
                Avg = g.Average(x => x.Value!.Value),
                Count = g.Count(),
            })
            .ToLookup(b => b.Name, StringComparer.OrdinalIgnoreCase);

        var valueHistories = numericValues.Select(v => new SchemaValueHistory(
            v.Name, v.Label, v.Type, v.Cadence, v.Unit,
            bucketsByValue[v.Name]
                .OrderBy(b => b.PeriodStart)
                .Select(b => new HistoryBucket(b.PeriodStart, b.PeriodEnd, b.Min, b.Max, b.Avg, b.Count))
                .ToList())).ToList();

        return new SchemaHistory(schema.Name, schema.Label, valueHistories);
    }

    /// <inheritdoc />
    public Task<PagedResult<SchemaVersionHistory>> GetVersionHistoryAsync(
        string name, PageRequest request, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) =>
        _versions.ListAsync(name, request, from, to, ct);

    /// <inheritdoc />
    public Task<SchemaVersionHistory?> GetVersionSnapshotAsync(Guid entryId, CancellationToken ct = default) =>
        _versions.GetByIdAsync(entryId, ct);

    /// <inheritdoc />
    public async Task<bool> DeleteVersionEntryAsync(string name, Guid entryId, CancellationToken ct = default)
    {
        var entry = await _versions.GetByIdAsync(entryId, ct);
        // Guard against deleting an entry that belongs to a different schema (defensive — the
        // route already scopes by name).
        if (entry is null || !string.Equals(entry.SchemaName, name, StringComparison.Ordinal))
            return false;

        var removed = await _versions.DeleteAsync(entryId, ct);
        if (removed)
            await _audit.RecordAsync(AuditTargetType.SchemaHistory, AuditChangeType.Delete, entry.SchemaId, name, ct);
        return removed;
    }

    /// <inheritdoc />
    public async Task<long> DeleteVersionHistoryAsync(string name, CancellationToken ct = default)
    {
        // Resolve the schema id for the audit entry; fall back to the live schema lookup so the
        // audit row still points at the right object even if history rows were already gone.
        var schema = await _schemas.GetByNameAsync(name, includeDeleted: true, ct);
        var removed = await _versions.DeleteAllForSchemaAsync(name, ct);
        if (removed > 0)
            await _audit.RecordAsync(AuditTargetType.SchemaHistory, AuditChangeType.Delete, schema?.Id ?? Guid.Empty, name, ct);
        return removed;
    }
}
