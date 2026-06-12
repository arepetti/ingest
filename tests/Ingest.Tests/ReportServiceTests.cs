using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Reports;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

/// <summary>
/// End-to-end coverage for <see cref="ReportService"/> with in-process fake repositories. We
/// pair the real <see cref="FluidReportRenderer"/> with the service so the assertions exercise
/// the full upload → render path.
/// </summary>
public class ReportServiceTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Upload_parses_front_matter_and_stores_template_separately()
    {
        var (svc, repo, _, _, _, _) = NewService();
        var content = """
            ---
            name: monthly_summary
            label: "Monthly summary"
            type: Aggregate
            schemas: [waste_kpis]
            ---
            <h1>{{ schema.label }}</h1>
            """;
        var report = await svc.UploadAsync("ignored.html", content);

        Assert.Equal("monthly_summary", report.Name);
        Assert.Equal("Monthly summary", report.Label);
        Assert.Equal(ReportType.Aggregate, report.Type);
        Assert.Equal(new[] { "waste_kpis" }, report.TargetSchemaNames);
        // Template should be front-matter-free; Content keeps the original.
        Assert.DoesNotContain("---", report.Template);
        Assert.Contains("---", report.Content);
        Assert.Same(report, repo.Single());
    }

    [Fact]
    public async Task Upload_falls_back_to_file_name_for_report_name()
    {
        var (svc, _, _, _, _, _) = NewService();
        var report = await svc.UploadAsync("My Cool Report.html", "<p>x</p>");
        Assert.Equal("My_Cool_Report", report.Name);
    }

    [Fact]
    public async Task Upload_duplicate_name_throws_conflict()
    {
        var (svc, _, _, _, _, _) = NewService();
        await svc.UploadAsync("first.html", "<p>a</p>");
        await Assert.ThrowsAsync<ConflictException>(() => svc.UploadAsync("first.html", "<p>b</p>"));
    }

    [Fact]
    public async Task Single_render_requires_submission_id()
    {
        var (svc, _, _, _, _, _) = NewService();
        var content = "---\ntype: Single\n---\n<p>x</p>";
        await svc.UploadAsync("r.html", content);

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.RenderAsync("r", new ReportRenderRequest()));
    }

    [Fact]
    public async Task Aggregate_render_requires_schema_when_multi_target()
    {
        var (svc, _, _, _, _, _) = NewService();
        var content = "---\ntype: Aggregate\nschemas: [a, b]\n---\n<p>x</p>";
        await svc.UploadAsync("r.html", content);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.RenderAsync("r", new ReportRenderRequest()));
        Assert.Contains(ex.Errors, e => e.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Render_resolves_single_target_schema_implicitly()
    {
        var (svc, _, schemas, _, _, samples) = NewService();
        schemas.Add(new Schema { Name = "waste_kpis", Label = "Waste KPIs", Values = new() { new SchemaValue { Name = "tonnes", Type = SchemaValueType.Number, Cadence = Cadence.Weekly } } });
        // One sample inside the default window.
        samples.Add(new SampleProjection
        {
            SubmissionId = Guid.NewGuid(),
            ServiceAccountId = Guid.NewGuid(),
            ServiceName = "alpha",
            SchemaName = "waste_kpis",
            ValueName = "tonnes",
            ValueType = SchemaValueType.Number,
            NumberValue = 42,
            Timestamp = FixedNow.AddDays(-1),
            PeriodStart = FixedNow.AddDays(-7),
            PeriodEnd = FixedNow,
        });

        var content = """
            ---
            type: Aggregate
            schemas: [waste_kpis]
            ---
            Schema={{ schema.label }};Bucket count={{ values[0].buckets.size }}
            """;
        await svc.UploadAsync("r.html", content);

        var result = await svc.RenderAsync("r", new ReportRenderRequest());
        Assert.Equal("waste_kpis", result.SchemaName);
        Assert.Contains("Schema=Waste KPIs", result.Html);
        Assert.Contains("Bucket count=1", result.Html);
    }

    [Fact]
    public async Task Single_render_passes_submission_samples_into_template()
    {
        var (svc, _, schemas, submissions, _, _) = NewService();
        var schemaId = Guid.NewGuid();
        schemas.Add(new Schema
        {
            Id = schemaId,
            Name = "kpis",
            Label = "KPIs",
            Values = new()
            {
                new SchemaValue { Name = "tonnes", Label = "Tonnes", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Unit = "t" },
            },
        });
        var submissionId = Guid.NewGuid();
        submissions.Add(new Submission
        {
            Id = submissionId,
            ServiceAccountId = Guid.NewGuid(),
            ServiceName = "alpha",
            SubmittedAt = FixedNow.AddDays(-1),
            Samples = new()
            {
                new Sample { SchemaName = "kpis", ValueName = "tonnes", Value = 17.5, Timestamp = FixedNow.AddDays(-1) },
            },
        });

        var content = """
            ---
            type: Single
            schemas: [kpis]
            ---
            {% for s in submission.samples %}{{ s.label }}={{ s.value }} {{ s.unit }}{% endfor %}
            """;
        await svc.UploadAsync("r.html", content);

        var result = await svc.RenderAsync("r", new ReportRenderRequest(SubmissionId: submissionId));
        Assert.Equal(submissionId, result.SubmissionId);
        Assert.Contains("Tonnes=17.5 t", result.Html);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────────────────────

    private static (ReportService svc, FakeReportRepo repo, FakeSchemas schemas,
        FakeSubmissions submissions, FakeAccounts accounts, FakeSamples samples) NewService()
    {
        var repo = new FakeReportRepo();
        var schemas = new FakeSchemas();
        var submissions = new FakeSubmissions();
        var accounts = new FakeAccounts();
        var samples = new FakeSamples();
        var renderer = new FluidReportRenderer();
        var audit = new FixedClock(FixedNow);
        return (new ReportService(repo, schemas, submissions, samples, accounts, renderer, audit, new NoopAuditLogService()),
            repo, schemas, submissions, accounts, samples);
    }

    private sealed class FixedClock(DateTime now) : IAuditContext
    {
        public string? UserName => "test";
        public Guid? AccountId => null;
        public DateTime UtcNow => now;
    }

    private sealed class FakeReportRepo : IReportRepository
    {
        private readonly List<Report> _store = new();

        public Report Single() => _store.Single();

        public Task<Report?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(r => r.Id == id && (includeDeleted || !r.IsDeleted)));

        public Task<Report?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) &&
                (includeDeleted || !r.IsDeleted)));

        public Task<PagedResult<Report>> ListAsync(PageRequest request, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Report>(_store.ToList(), _store.Count, 1, _store.Count));

        public Task AddAsync(Report report, CancellationToken ct = default)
        {
            _store.Add(report);
            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
        {
            var hit = _store.FirstOrDefault(r => r.Id == id);
            if (hit is not null) hit.IsDeleted = true;
            return Task.CompletedTask;
        }

        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class FakeSchemas : ISchemaRepository, System.Collections.IEnumerable
    {
        private readonly List<Schema> _store = new();
        public void Add(Schema s) => _store.Add(s);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _store.GetEnumerator();

        public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(s => s.Id == id));
        public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)));
        public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Schema>>(_store.ToList());
        public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Schema>(_store.ToList(), _store.Count, 1, _store.Count));
        public Task AddAsync(Schema schema, CancellationToken ct = default) { _store.Add(schema); return Task.CompletedTask; }
        public Task UpdateAsync(Schema schema, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) { _store.RemoveAll(s => s.Id == id); return Task.CompletedTask; }
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class FakeSubmissions : ISubmissionRepository, System.Collections.IEnumerable
    {
        private readonly List<Submission> _store = new();
        public void Add(Submission s) => _store.Add(s);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _store.GetEnumerator();

        public Task<Submission?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(s => s.Id == id));
        public Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId = null, DateTime? from = null, DateTime? to = null, string? schemaName = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Submission>(_store.ToList(), _store.Count, 1, _store.Count));
        public Task AddAsync(Submission submission, CancellationToken ct = default) { _store.Add(submission); return Task.CompletedTask; }
        public Task UpdateAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Submission>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Submission>>(_store.Where(s => s.ServiceAccountId == serviceId && (includeDeleted || !s.IsDeleted)).ToList());
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class FakeAccounts : IAccountRepository
    {
        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<Account?>(new Account { Id = id, Name = "fake", Label = "Fake" });
        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<Account?>(new Account { Name = name, Label = name });
        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) =>
            Task.FromResult<Account?>(null);
        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Account>(Array.Empty<Account>(), 0, 1, 0));
        public Task AddAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class FakeSamples : ISampleRepository, System.Collections.IEnumerable
    {
        private readonly List<SampleProjection> _store = new();
        public void Add(SampleProjection s) => _store.Add(s);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _store.GetEnumerator();

        public Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SampleProjection>(_store.ToList(), _store.Count, 1, _store.Count));
        public Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default) =>
            Task.FromResult<SampleProjection?>(null);
        public Task<bool> ExistsInWindowAsync(Guid serviceId, string schemaName, string valueName, DateTime start, DateTime end, CancellationToken ct = default) =>
            Task.FromResult(_store.Any(s => s.ServiceAccountId == serviceId && s.SchemaName == schemaName && s.ValueName == valueName && s.Timestamp >= start && s.Timestamp < end));
        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(_store.Where(s => string.Equals(s.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult(_store.Any(s => string.Equals(s.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase)));
        public Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default) =>
            Task.FromResult(_store.Any(s => s.ServiceAccountId == serviceAccountId));
        public IQueryable<SampleProjection> AsQueryable() => _store.AsQueryable();
        public Task<IReadOnlyList<SampleProjection>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(_store.Where(s => s.ServiceAccountId == serviceId && (includeDeleted || !s.IsDeleted)).ToList());
        public Task<long> RedactByServiceAsync(Guid serviceId, string pseudonym, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }
}
