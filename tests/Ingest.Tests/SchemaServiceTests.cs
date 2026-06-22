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
        return new SchemaService(repo, new FakeEmptySampleRepo(), new NoopAuditLogService(),
            new FakeVersionHistoryRepo(), new FakeSubmissionCountRepo(), new StubAccountRepo(), new ImmediateClock());
    }

    private static SchemaService NewService(out FakeSchemaRepo repo, out FakeEmptySampleRepo samples)
    {
        repo = new FakeSchemaRepo();
        samples = new FakeEmptySampleRepo();
        return new SchemaService(repo, samples, new NoopAuditLogService(),
            new FakeVersionHistoryRepo(), new FakeSubmissionCountRepo(), new StubAccountRepo(), new ImmediateClock());
    }

    private static SchemaService NewServiceWithVersions(out FakeSchemaRepo repo, out FakeVersionHistoryRepo versions)
    {
        repo = new FakeSchemaRepo();
        versions = new FakeVersionHistoryRepo();
        return new SchemaService(repo, new FakeEmptySampleRepo(), new NoopAuditLogService(),
            versions, new FakeSubmissionCountRepo(), new StubAccountRepo(), new ImmediateClock());
    }

    /// <summary>
    /// Minimal <see cref="IAccountRepository"/> stub. The schema service only consults it to validate
    /// approval-policy approver references; these tests never set an approval policy, so a stub that
    /// returns an account for any id (so any approver "exists") is sufficient.
    /// </summary>
    private sealed class StubAccountRepo : IAccountRepository
    {
        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<Account?>(new Account { Name = "approver", Kind = AccountKind.User, Role = AccountRole.Approver });
        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => Task.FromResult<Account?>(null);
        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) => Task.FromResult<Account?>(null);
        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Account>(Array.Empty<Account>(), 0, request.Page, request.PageSize));
        public Task AddAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
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

    // ── Version history snapshots ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_records_initial_history_snapshot()
    {
        var svc = NewServiceWithVersions(out _, out var versions);
        var created = await svc.CreateAsync(NewSchema());

        var entry = Assert.Single(versions.Entries);
        Assert.Null(entry.OldVersion);
        Assert.Equal(1, entry.NewVersion);
        Assert.False(entry.VersionBumped);
        Assert.True(entry.Enabled);
        Assert.Equal(0, entry.SubmissionCount);
        Assert.Equal(created.Id, entry.SchemaId);
        Assert.Equal(created.Values.Count, entry.Snapshot.Values.Count);
    }

    [Fact]
    public async Task Update_records_history_snapshot_with_old_and_new_version()
    {
        var svc = NewServiceWithVersions(out _, out var versions);
        var created = await svc.CreateAsync(NewSchema());

        var update = NewSchema(version: 2);
        await svc.UpdateAsync(created.Id, update);

        Assert.Equal(2, versions.Entries.Count);
        // Newest entry (the update) records the bump 1 → 2.
        var latest = versions.Entries.Last();
        Assert.Equal(1, latest.OldVersion);
        Assert.Equal(2, latest.NewVersion);
        Assert.True(latest.VersionBumped);
    }

    [Fact]
    public async Task Update_without_version_change_records_unbumped_history_entry()
    {
        var svc = NewServiceWithVersions(out _, out var versions);
        var created = await svc.CreateAsync(NewSchema());

        var update = NewSchema();
        update.Label = "Renamed";
        await svc.UpdateAsync(created.Id, update);

        var latest = versions.Entries.Last();
        Assert.Equal(1, latest.OldVersion);
        Assert.Equal(1, latest.NewVersion);
        Assert.False(latest.VersionBumped);
    }

    [Fact]
    public async Task Delete_version_entry_removes_only_that_snapshot()
    {
        var svc = NewServiceWithVersions(out _, out var versions);
        var created = await svc.CreateAsync(NewSchema());
        await svc.UpdateAsync(created.Id, NewSchema(version: 2));
        var target = versions.Entries.First();

        var removed = await svc.DeleteVersionEntryAsync(created.Name, target.Id, default);

        Assert.True(removed);
        Assert.Single(versions.Entries);
        Assert.DoesNotContain(versions.Entries, e => e.Id == target.Id);
    }

    [Fact]
    public async Task Delete_version_history_clears_all_snapshots_for_the_schema()
    {
        var svc = NewServiceWithVersions(out _, out var versions);
        var created = await svc.CreateAsync(NewSchema());
        await svc.UpdateAsync(created.Id, NewSchema(version: 2));

        var removed = await svc.DeleteVersionHistoryAsync(created.Name, default);

        Assert.Equal(2, removed);
        Assert.Empty(versions.Entries);
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

    // ── RAG target band ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_round_trips_full_rag_band()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Values[0].AmberMin = 0;
        s.Values[0].GreenMin = 10;
        s.Values[0].GreenMax = 90;
        s.Values[0].AmberMax = 100;
        var created = await svc.CreateAsync(s);
        Assert.Equal(0, created.Values[0].AmberMin);
        Assert.Equal(10, created.Values[0].GreenMin);
        Assert.Equal(90, created.Values[0].GreenMax);
        Assert.Equal(100, created.Values[0].AmberMax);
    }

    [Fact]
    public async Task Create_accepts_amber_only_band()
    {
        // The outer (acceptable) band may stand alone — no inner ideal range required.
        var svc = NewService(out _);
        var s = NewSchema();
        s.Values[0].AmberMin = 5;
        s.Values[0].AmberMax = 50;
        var created = await svc.CreateAsync(s);
        Assert.Equal(5, created.Values[0].AmberMin);
        Assert.Equal(50, created.Values[0].AmberMax);
        Assert.Null(created.Values[0].GreenMin);
        Assert.Null(created.Values[0].GreenMax);
    }

    [Fact]
    public async Task Create_accepts_one_sided_band()
    {
        // "Lower is better": only the upper edges are set (no minimums).
        var svc = NewService(out _);
        var s = NewSchema();
        s.Values[0].GreenMax = 5;
        s.Values[0].AmberMax = 10;
        var created = await svc.CreateAsync(s);
        Assert.Equal(5, created.Values[0].GreenMax);
        Assert.Equal(10, created.Values[0].AmberMax);
    }

    [Fact]
    public async Task Create_rejects_out_of_order_band()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Values[0].AmberMin = 0;
        s.Values[0].GreenMin = 90; // green min above green max
        s.Values[0].GreenMax = 10;
        s.Values[0].AmberMax = 100;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
        Assert.Contains(ex.Errors, e => e.Contains("out-of-order"));
    }

    [Fact]
    public async Task Create_rejects_green_above_amber_ceiling()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Values[0].GreenMax = 120; // ideal ceiling pokes outside the acceptable ceiling
        s.Values[0].AmberMax = 100;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
        Assert.Contains(ex.Errors, e => e.Contains("out-of-order"));
    }

    [Fact]
    public async Task Create_rejects_green_min_without_amber_min()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Values[0].GreenMin = 10;
        s.Values[0].GreenMax = 90;
        s.Values[0].AmberMax = 100; // amber max present, but no amber min for the green min
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
        Assert.Contains(ex.Errors, e => e.Contains("GreenMin without AmberMin"));
    }

    [Fact]
    public async Task Create_rejects_green_max_without_amber_max()
    {
        var svc = NewService(out _);
        var s = NewSchema();
        s.Values[0].AmberMin = 0;
        s.Values[0].GreenMin = 10;
        s.Values[0].GreenMax = 90; // green max present, but no amber max
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(s));
        Assert.Contains(ex.Errors, e => e.Contains("GreenMax without AmberMax"));
    }

    [Fact]
    public async Task Clone_copies_rag_band()
    {
        var svc = NewService(out _);
        var s = NewSchema(name: "alpha");
        s.Values[0].AmberMin = 0;
        s.Values[0].GreenMin = 5;
        s.Values[0].GreenMax = 25;
        s.Values[0].AmberMax = 30;
        var source = await svc.CreateAsync(s);

        var clone = await svc.CloneAsync(source.Id);

        Assert.NotNull(clone);
        Assert.Equal(0, clone!.Values[0].AmberMin);
        Assert.Equal(5, clone.Values[0].GreenMin);
        Assert.Equal(25, clone.Values[0].GreenMax);
        Assert.Equal(30, clone.Values[0].AmberMax);
    }

    [Fact]
    public async Task History_exposes_rag_band_on_value_timeline()
    {
        var svc = NewService(out _);
        var s = NewSchema(name: "banded");
        s.Values[0].AmberMin = 0;
        s.Values[0].GreenMin = 1;
        s.Values[0].GreenMax = 2;
        s.Values[0].AmberMax = 3;
        await svc.CreateAsync(s);

        var history = await svc.GetHistoryAsync("banded");

        Assert.NotNull(history);
        var tonnes = Assert.Single(history!.Values, v => v.ValueName == "tonnes");
        Assert.Equal(0, tonnes.AmberMin);
        Assert.Equal(1, tonnes.GreenMin);
        Assert.Equal(2, tonnes.GreenMax);
        Assert.Equal(3, tonnes.AmberMax);
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

        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
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

        public Task<bool> ExistsInWindowAsync(Guid serviceId, string schemaName, string valueName, DateTime start, DateTime end, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());

        public Task<IReadOnlyList<SampleProjection>> GetForExploreAsync(string schemaName, IReadOnlyList<string> valueNames, IReadOnlyList<Guid>? serviceIds, DateTime? from, DateTime? to, CancellationToken ct = default) =>
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

        public Task<IReadOnlyList<SampleProjection>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());
        public Task<long> RedactByServiceAsync(Guid serviceId, string pseudonym, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    /// <summary>In-memory version-history store exposing the captured snapshots to assertions.</summary>
    private sealed class FakeVersionHistoryRepo : ISchemaVersionHistoryRepository
    {
        public List<SchemaVersionHistory> Entries { get; } = new();

        public Task AddAsync(SchemaVersionHistory entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<PagedResult<SchemaVersionHistory>> ListAsync(string schemaName, PageRequest request, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
        {
            var hits = Entries.Where(e => e.SchemaName == schemaName).OrderByDescending(e => e.ChangeDate).ToList();
            return Task.FromResult(new PagedResult<SchemaVersionHistory>(hits, hits.Count, request.Page, request.PageSize));
        }

        public Task<SchemaVersionHistory?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Entries.RemoveAll(e => e.Id == id) > 0);

        public Task<long> DeleteAllForSchemaAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult((long)Entries.RemoveAll(e => e.SchemaName == schemaName));
    }

    /// <summary>Submission repo stub that only needs to answer the per-schema count (always 0 here).</summary>
    private sealed class FakeSubmissionCountRepo : ISubmissionRepository
    {
        public Task<long> CountBySchemaAsync(string schemaName, CancellationToken ct = default) => Task.FromResult(0L);

        public Task<Submission?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<Submission?>(null);
        public Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId = null, DateTime? from = null, DateTime? to = null, string? schemaName = null, ApprovalStatus? approvalStatus = null, bool? draft = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Submission>(Array.Empty<Submission>(), 0, 1, 0));
        public Task<long> CountByApprovalStatusAsync(ApprovalStatus status, CancellationToken ct = default) => Task.FromResult(0L);
        public Task AddAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Submission>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Submission>>(Array.Empty<Submission>());
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    /// <summary>Audit context that just reads the wall clock; no authenticated actor.</summary>
    private sealed class ImmediateClock : IAuditContext
    {
        public string? UserName => null;
        public Guid? AccountId => null;
        public DateTime UtcNow => DateTime.UtcNow;
    }
}

