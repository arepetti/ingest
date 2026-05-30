using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

/// <summary>
/// Focused tests for the new layout / versioning / clone / example surface on
/// <see cref="SchemaService"/>. We back the service with an in-process fake repo so the tests
/// exercise the real validator and timestamp logic without a Mongo dependency.
/// </summary>
public class SchemaServiceTests
{
    private static SchemaService NewService(out FakeSchemaRepo repo)
    {
        repo = new FakeSchemaRepo();
        return new SchemaService(repo, new FakeEmptySampleRepo());
    }

    private static SchemaService NewService(out FakeSchemaRepo repo, out FakeEmptySampleRepo samples)
    {
        repo = new FakeSchemaRepo();
        samples = new FakeEmptySampleRepo();
        return new SchemaService(repo, samples);
    }

    private static Schema NewSchema(string name = "demo", int version = 1) => new()
    {
        Name = name,
        Label = "Demo",
        Modifiable = true,
        Enabled = true,
        IsGlobal = true,
        Version = version,
        Values = new List<SchemaValue>
        {
            new() { Name = "tonnes", Label = "Tonnes", Type = SchemaValueType.Number, Cadence = Cadence.Weekly },
            new() { Name = "notes",  Label = "Notes",  Type = SchemaValueType.String, Cadence = Cadence.Weekly },
        },
    };

    // ── Version + VersionModifiedAt ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_stamps_version_modified_at()
    {
        var svc = NewService(out _);
        var before = DateTime.UtcNow.AddSeconds(-1);
        var created = await svc.CreateAsync(NewSchema());
        Assert.NotNull(created.VersionModifiedAt);
        Assert.True(created.VersionModifiedAt >= before);
    }

    [Fact]
    public async Task Update_without_version_change_keeps_version_modified_at()
    {
        var svc = NewService(out _);
        var created = await svc.CreateAsync(NewSchema());
        var originalStamp = created.VersionModifiedAt;

        // Allow at least a tick so any accidental restamp would be observable.
        await Task.Delay(20);

        var update = NewSchema();
        update.Label = "Demo (renamed)";
        var updated = await svc.UpdateAsync(created.Id, update);

        Assert.NotNull(updated);
        Assert.Equal(originalStamp, updated!.VersionModifiedAt);
    }

    [Fact]
    public async Task Update_bumping_version_restamps_version_modified_at()
    {
        var svc = NewService(out _);
        var created = await svc.CreateAsync(NewSchema());
        var originalStamp = created.VersionModifiedAt;
        await Task.Delay(20);

        var update = NewSchema(version: 2);
        var updated = await svc.UpdateAsync(created.Id, update);

        Assert.NotNull(updated);
        Assert.True(updated!.VersionModifiedAt > originalStamp);
    }

    [Fact]
    public async Task Update_rejects_version_downgrade()
    {
        var svc = NewService(out _);
        var created = await svc.CreateAsync(NewSchema(version: 3));
        var update = NewSchema(version: 2);

        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(created.Id, update));
    }

    [Fact]
    public async Task Create_rejects_negative_version()
    {
        var svc = NewService(out _);
        var s = NewSchema(version: -1);
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
    }

    // ── SinceVersion bounds ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_rejects_SinceVersion_greater_than_Version()
    {
        var svc = NewService(out _);
        var s = NewSchema(version: 1);
        s.Values[0].SinceVersion = 2; // > 1
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
    }

    [Fact]
    public async Task Create_accepts_null_SinceVersion()
    {
        var svc = NewService(out _);
        var s = NewSchema(version: 2);
        s.Values[0].SinceVersion = null;
        var created = await svc.CreateAsync(s);
        Assert.Null(created.Values[0].SinceVersion);
    }

    [Fact]
    public async Task Create_accepts_SinceVersion_equal_to_Version()
    {
        var svc = NewService(out _);
        var s = NewSchema(version: 2);
        s.Values[0].SinceVersion = 2;
        var created = await svc.CreateAsync(s);
        Assert.Equal(2, created.Values[0].SinceVersion);
    }

    // ── Value-name format ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("with.dot")]      // `.` is the bound-namespace separator — never legal
    [InlineData("with-hyphen")]   // would force NCalc's bracket form just to be referenced
    [InlineData("with space")]    // would force NCalc's bracket form just to be referenced
    [InlineData("1leading_digit")]
    [InlineData("")]              // empty/whitespace
    public async Task Create_rejects_value_name_that_is_not_a_c_identifier(string badName)
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Values.Add(new SchemaValue
        {
            Name = badName, Label = "X", Type = SchemaValueType.Number, Cadence = Cadence.Weekly,
        });
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
        Assert.Contains(ex.Errors, e => e.Contains("not a valid identifier"));
    }

    [Theory]
    [InlineData("snake_case")]
    [InlineData("_leading_underscore")]
    [InlineData("PascalCase")]
    [InlineData("mixed1_with_digit2")]
    public async Task Create_accepts_value_names_that_are_valid_c_identifiers(string goodName)
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Values.Clear();
        s.Values.Add(new SchemaValue
        {
            Name = goodName, Label = "X", Type = SchemaValueType.Number, Cadence = Cadence.Weekly,
        });
        var created = await svc.CreateAsync(s);
        Assert.Single(created.Values);
        Assert.Equal(goodName, created.Values[0].Name);
    }

    // ── Layout validation ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_accepts_nested_layout_with_mixed_nodes()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Layout = new List<SchemaLayoutNode>
        {
            new() { Kind = "value", ValueName = "tonnes" },
            new()
            {
                Kind = "section",
                Caption = "Notes section",
                Items = new List<SchemaLayoutNode>
                {
                    new() { Kind = "value", ValueName = "notes" },
                },
            },
        };
        var created = await svc.CreateAsync(s);
        Assert.Equal(2, created.Layout.Count);
    }

    [Fact]
    public async Task Create_rejects_layout_with_missing_value_reference()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Layout = new List<SchemaLayoutNode>
        {
            new() { Kind = "value", ValueName = "does_not_exist" },
        };
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
        Assert.Contains(ex.Errors, e => e.Contains("does_not_exist"));
    }

    [Fact]
    public async Task Create_rejects_layout_with_duplicate_value_reference()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Layout = new List<SchemaLayoutNode>
        {
            new() { Kind = "value", ValueName = "tonnes" },
            new()
            {
                Kind = "section",
                Caption = "Other",
                Items = new List<SchemaLayoutNode>
                {
                    new() { Kind = "value", ValueName = "tonnes" }, // dup
                },
            },
        };
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
        Assert.Contains(ex.Errors, e => e.Contains("more than once"));
    }

    [Fact]
    public async Task Create_rejects_layout_with_section_missing_caption()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Layout = new List<SchemaLayoutNode>
        {
            new() { Kind = "section", Caption = "", Items = new() },
        };
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
        Assert.Contains(ex.Errors, e => e.Contains("caption"));
    }

    [Fact]
    public async Task Create_rejects_layout_with_unknown_kind()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Layout = new List<SchemaLayoutNode>
        {
            new() { Kind = "bogus" },
        };
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
    }

    // ── Clone ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clone_picks_copy_suffix_then_numeric_suffix()
    {
        var svc = NewService(out _);
        var source = await svc.CreateAsync(NewSchema(name: "alpha"));

        var first = await svc.CloneAsync(source.Id);
        Assert.NotNull(first);
        Assert.Equal("alpha_copy", first!.Name);

        // Cloning the original a second time must pick the next free suffix.
        var second = await svc.CloneAsync(source.Id);
        Assert.NotNull(second);
        Assert.Equal("alpha_copy_2", second!.Name);
    }

    [Fact]
    public async Task Clone_resets_version_modified_at_and_copies_layout_and_values()
    {
        var svc = NewService(out _);
        var s = NewSchema(name: "alpha", version: 3);
        s.Values[0].SinceVersion = 3;
        s.Layout = new List<SchemaLayoutNode>
        {
            new()
            {
                Kind = "section",
                Caption = "Sec",
                Items = new() { new() { Kind = "value", ValueName = "tonnes" } },
            },
        };
        var source = await svc.CreateAsync(s);
        var sourceStamp = source.VersionModifiedAt;

        await Task.Delay(20);

        var clone = await svc.CloneAsync(source.Id);

        Assert.NotNull(clone);
        Assert.Equal("alpha_copy", clone!.Name);
        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal(source.Version, clone.Version);
        Assert.True(clone.VersionModifiedAt > sourceStamp);
        Assert.Equal(source.Values.Count, clone.Values.Count);
        Assert.Equal(source.Layout.Count, clone.Layout.Count);
        // Layout list is a fresh copy — mutating the clone must not affect the source.
        clone.Layout[0].Caption = "changed";
        Assert.Equal("Sec", source.Layout[0].Caption);
    }

    [Fact]
    public async Task Clone_returns_null_for_unknown_id()
    {
        var svc = NewService(out _);
        Assert.Null(await svc.CloneAsync(Guid.NewGuid()));
    }

    // ── Example submission ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Example_picks_type_appropriate_defaults()
    {
        var svc = NewService(out _);
        var s = new Schema
        {
            Name = "ex",
            IsGlobal = true,
            Values = new List<SchemaValue>
            {
                new() { Name = "txt",  Type = SchemaValueType.String,  Cadence = Cadence.Weekly },
                new() { Name = "num",  Type = SchemaValueType.Number,  Cadence = Cadence.Weekly, Min = 2.5 },
                new() { Name = "int",  Type = SchemaValueType.Integer, Cadence = Cadence.Weekly, Min = 7 },
                new() { Name = "date", Type = SchemaValueType.Date,    Cadence = Cadence.Weekly },
                new() { Name = "flag", Type = SchemaValueType.Boolean, Cadence = Cadence.Weekly },
            },
        };
        await svc.CreateAsync(s);

        var example = await svc.BuildExampleSubmissionAsync(Guid.NewGuid(), "ex");
        Assert.NotNull(example);
        Assert.Equal(5, example!.Samples.Count);

        var byName = example.Samples.ToDictionary(x => x.ValueName);
        Assert.Equal("", byName["txt"].Value!.Value.GetString());
        Assert.Equal(2.5, byName["num"].Value!.Value.GetDouble());
        Assert.Equal(7, byName["int"].Value!.Value.GetInt64());
        Assert.False(byName["flag"].Value!.Value.GetBoolean());
        // Date should round-trip an ISO-8601 timestamp; existence is enough.
        Assert.True(byName["date"].Value!.Value.GetString()!.Length > 0);
    }

    [Fact]
    public async Task Example_returns_null_for_non_visible_schema()
    {
        var svc = NewService(out _);
        var s = NewSchema(name: "restricted");
        s.IsGlobal = false;
        s.ServiceIds = new() { Guid.NewGuid() };
        await svc.CreateAsync(s);

        var notInAudience = Guid.NewGuid();
        var example = await svc.BuildExampleSubmissionAsync(notInAudience, "restricted");
        Assert.Null(example);
    }

    // ── Name reuse after soft-delete ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_succeeds_when_existing_schema_with_same_name_is_soft_deleted()
    {
        // Reproduces the user-reported bug: after a soft-delete, re-creating a schema with the
        // same name used to fail with "Schema 'X' already exists." The new contract is to drop
        // the tombstone and let the create through.
        var svc = NewService(out var repo);
        var first = await svc.CreateAsync(NewSchema(name: "finance_monthly_close"));
        await svc.DeleteAsync(first.Id); // soft-delete

        var replacement = await svc.CreateAsync(NewSchema(name: "finance_monthly_close"));

        Assert.NotEqual(first.Id, replacement.Id);
        Assert.False(replacement.IsDeleted);
        // The tombstone is gone.
        Assert.Null(await repo.GetByIdAsync(first.Id, includeDeleted: true));
    }

    [Fact]
    public async Task Create_still_rejects_when_a_live_schema_with_same_name_exists()
    {
        var svc = NewService(out _);
        await svc.CreateAsync(NewSchema(name: "demo"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(NewSchema(name: "demo")));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task Update_rename_succeeds_when_target_name_is_held_by_soft_deleted_schema()
    {
        var svc = NewService(out var repo);
        var tombstone = await svc.CreateAsync(NewSchema(name: "old_name"));
        await svc.DeleteAsync(tombstone.Id);

        var live = await svc.CreateAsync(NewSchema(name: "fresh"));

        var update = NewSchema(name: "old_name"); // reusing the tombstone's name
        update.Version = live.Version;
        var renamed = await svc.UpdateAsync(live.Id, update);

        Assert.NotNull(renamed);
        Assert.Equal("old_name", renamed!.Name);
        Assert.Null(await repo.GetByIdAsync(tombstone.Id, includeDeleted: true));
    }

    [Fact]
    public async Task Update_rename_rejects_when_target_name_is_held_by_live_schema()
    {
        var svc = NewService(out _);
        await svc.CreateAsync(NewSchema(name: "taken"));
        var moving = await svc.CreateAsync(NewSchema(name: "moving"));

        var update = NewSchema(name: "taken");
        update.Version = moving.Version;
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateAsync(moving.Id, update));
        Assert.Contains("already exists", ex.Message);
    }

    // ── Delete guard ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_refuses_when_schema_is_in_use_and_suggests_disabling()
    {
        var svc = NewService(out var repo, out var samples);
        var created = await svc.CreateAsync(NewSchema(name: "demo"));
        samples.SchemasInUse.Add(created.Name);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.DeleteAsync(created.Id));
        Assert.Contains("cannot be deleted", ex.Message);
        Assert.Contains("Disable", ex.Message);

        // The schema is still live in the repo.
        var after = await repo.GetByIdAsync(created.Id);
        Assert.NotNull(after);
        Assert.False(after!.IsDeleted);
    }

    [Fact]
    public async Task Delete_soft_deletes_when_schema_has_no_usage()
    {
        var svc = NewService(out var repo, out var samples);
        var created = await svc.CreateAsync(NewSchema(name: "demo"));
        Assert.DoesNotContain(created.Name, samples.SchemasInUse);

        await svc.DeleteAsync(created.Id);

        var after = await repo.GetByIdAsync(created.Id, includeDeleted: true);
        Assert.NotNull(after);
        Assert.True(after!.IsDeleted);
    }

    [Fact]
    public async Task Delete_unknown_schema_is_a_silent_noop()
    {
        var svc = NewService(out _);
        // No throw — matches the original soft-delete idempotency contract.
        await svc.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task Delete_uses_label_in_the_error_message_when_available()
    {
        var svc = NewService(out _, out var samples);
        var schema = NewSchema(name: "demo");
        schema.Label = "Demo schema";
        var created = await svc.CreateAsync(schema);
        samples.SchemasInUse.Add(created.Name);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.DeleteAsync(created.Id));
        Assert.Contains("Demo schema", ex.Message);
    }

    // ── In-memory repositories (just enough surface) ────────────────────────────────────────

    private sealed class FakeSchemaRepo : ISchemaRepository
    {
        private readonly List<Schema> _store = new();

        public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default)
        {
            var hit = _store.FirstOrDefault(s => s.Id == id && (includeDeleted || !s.IsDeleted));
            return Task.FromResult(hit);
        }

        public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default)
        {
            var hit = _store.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
                                                  && (includeDeleted || !s.IsDeleted));
            return Task.FromResult(hit);
        }

        public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default)
        {
            IReadOnlyList<Schema> hits = _store
                .Where(s => !s.IsDeleted && (s.IsGlobal || s.ServiceIds.Contains(serviceId)))
                .ToList();
            return Task.FromResult(hits);
        }

        public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Schema>(_store.Where(s => !s.IsDeleted).ToList(), _store.Count, 1, _store.Count));

        public Task AddAsync(Schema schema, CancellationToken ct = default)
        {
            schema.CreatedAt = DateTime.UtcNow;
            schema.ModifiedAt = schema.CreatedAt;
            _store.Add(schema);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Schema schema, CancellationToken ct = default)
        {
            schema.ModifiedAt = DateTime.UtcNow;
            // Same-reference update is fine for our purposes — the service mutates `existing`.
            var idx = _store.FindIndex(s => s.Id == schema.Id);
            if (idx >= 0) _store[idx] = schema;
            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
        {
            var hit = _store.FirstOrDefault(s => s.Id == id);
            if (hit is not null) { hit.IsDeleted = true; hit.DeletedAt = DateTime.UtcNow; }
            return Task.CompletedTask;
        }

        public Task HardDeleteAsync(Guid id, CancellationToken ct = default)
        {
            _store.RemoveAll(s => s.Id == id);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stub sample repo — the schema history path isn't exercised here, but the new delete
    /// guard needs <see cref="ISampleRepository.IsSchemaInUseAsync"/>, so we let tests opt
    /// schemas into the "in use" set explicitly.
    /// </summary>
    private sealed class FakeEmptySampleRepo : ISampleRepository
    {
        public HashSet<string> SchemasInUse { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SampleProjection>(Array.Empty<SampleProjection>(), 0, 1, 0));

        public Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default) =>
            Task.FromResult<SampleProjection?>(null);

        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());

        public Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult(SchemasInUse.Contains(schemaName));

        public Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public IQueryable<SampleProjection> AsQueryable() =>
            Array.Empty<SampleProjection>().AsQueryable();
    }
}
