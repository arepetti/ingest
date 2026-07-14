using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Export;
using Ingest.Infrastructure.Reports;
using Ingest.Infrastructure.Validation;

namespace Ingest.Tests;

public class PdfExportServiceTests
{
    private static Schema BuildSchema() => new()
    {
        Name = "kpis",
        Label = "KPIs",
        Description = "Monthly KPIs",
        Enabled = true,
        Version = 2,
        SubmissionValidations = new List<string> { "peak >= average" },
        Values = new List<SchemaValue>
        {
            new()
            {
                Name = "average", Label = "Average", Type = SchemaValueType.Number, Cadence = Cadence.Monthly,
                Required = true, Unit = "%", Min = 0, Max = 100,
                GreenMin = 40, GreenMax = 60, AmberMin = 20, AmberMax = 80,
            },
            new()
            {
                Name = "peak", Label = "Peak", Type = SchemaValueType.Number, Cadence = Cadence.Monthly,
                VisibleIf = "average > 0", Warning = "peak > 90",
            },
            new()
            {
                Name = "ratio", Label = "Ratio", Type = SchemaValueType.Number,
                Kind = SchemaValueKind.Calculated, Expression = "peak / average",
            },
        },
        Layout = new List<SchemaLayoutNode>
        {
            new()
            {
                Kind = SchemaLayoutNodeKind.Section, Caption = "Core metrics",
                Items = new List<SchemaLayoutNode>
                {
                    new() { Kind = SchemaLayoutNodeKind.Value, ValueName = "average" },
                    new() { Kind = SchemaLayoutNodeKind.Value, ValueName = "peak" },
                },
            },
        },
    };

    private static PdfExportService Build(FakePdfConverter converter, Schema? schema, Submission? submission) =>
        new(
            new FakeSchemaRepo(schema),
            new FakeSubmissionRepo(submission),
            new FluidReportRenderer(),
            converter,
            new NCalcTranslator(),
            new FakeClock());

    [Fact]
    public async Task ExportSchema_renders_full_spec_sheet_without_data()
    {
        var converter = new FakePdfConverter();
        var svc = Build(converter, BuildSchema(), null);

        var doc = await svc.ExportSchemaAsync("kpis");

        Assert.NotNull(doc);
        Assert.Equal("kpis.pdf", doc!.FileName);
        Assert.Equal(converter.Pdf, doc.Content);

        var html = converter.LastHtml!;
        // Header + structure.
        Assert.Contains("KPIs", html);
        Assert.Contains("Core metrics", html);
        // Every value is present, including the unassigned calculated one.
        Assert.Contains("Average", html);
        Assert.Contains("Peak", html);
        Assert.Contains("Ratio", html);
        // Rules translated to English.
        Assert.Contains("Calculated as", html);
        Assert.Contains("is greater than", html);
        // RAG band surfaced.
        Assert.Contains("Target band", html);
        // Schema-level validators.
        Assert.Contains("Submission validation rules", html);
        // No data column in the schema export.
        Assert.DoesNotContain("class=\"data\"", html);
    }

    [Fact]
    public async Task ExportSchema_returns_null_when_missing()
    {
        var converter = new FakePdfConverter();
        var svc = Build(converter, null, null);

        Assert.Null(await svc.ExportSchemaAsync("nope"));
    }

    [Fact]
    public async Task ExportSubmission_renders_data_in_schema_layout()
    {
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            ServiceAccountId = Guid.NewGuid(),
            ServiceName = "Acme",
            SubmittedAt = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc),
            Samples = new List<Sample>
            {
                new() { SchemaName = "kpis", ValueName = "average", Value = 42.0, Timestamp = DateTime.UtcNow, Note = "looks good" },
                new() { SchemaName = "kpis", ValueName = "peak", Value = 90.0, Timestamp = DateTime.UtcNow },
            },
        };

        var converter = new FakePdfConverter();
        var svc = Build(converter, BuildSchema(), submission);

        var doc = await svc.ExportSubmissionAsync(submission.Id);

        Assert.NotNull(doc);
        Assert.Equal($"submission-{submission.Id}.pdf", doc!.FileName);

        var html = converter.LastHtml!;
        Assert.Contains("Acme", html);
        Assert.Contains("42", html);
        Assert.Contains("looks good", html);
        // Data column is present for submissions.
        Assert.Contains("class=\"data\"", html);
    }

    [Fact]
    public async Task ExportSubmission_returns_null_when_missing()
    {
        var converter = new FakePdfConverter();
        var svc = Build(converter, BuildSchema(), null);

        Assert.Null(await svc.ExportSubmissionAsync(Guid.NewGuid()));
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────────────────

    private sealed class FakePdfConverter : IPdfConverter
    {
        public string? LastHtml { get; private set; }
        public byte[] Pdf { get; } = "%PDF-1.4 fake"u8.ToArray();

        public Task<byte[]> HtmlToPdfAsync(string html, CancellationToken ct = default)
        {
            LastHtml = html;
            return Task.FromResult(Pdf);
        }
    }

    private sealed class FakeClock : IAuditContext
    {
        public string? UserName => null;
        public Guid? AccountId => null;
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FakeSchemaRepo : ISchemaRepository
    {
        private readonly Schema? _schema;
        public FakeSchemaRepo(Schema? schema) => _schema = schema;

        public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_schema is not null && string.Equals(_schema.Name, name, StringComparison.OrdinalIgnoreCase) ? _schema : null);

        public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<Schema?>(null);

        public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task AddAsync(Schema schema, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Schema schema, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeSubmissionRepo : ISubmissionRepository
    {
        private readonly Submission? _submission;
        public FakeSubmissionRepo(Submission? submission) => _submission = submission;

        public Task<Submission?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_submission is not null && _submission.Id == id ? _submission : null);

        public Task<PagedResult<Submission>> ListAsync(
            PageRequest request, Guid? serviceId = null, DateTime? from = null, DateTime? to = null,
            string? schemaName = null, ApprovalStatus? approvalStatus = null, bool? draft = null,
            IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<long> CountBySchemaAsync(string schemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CountByApprovalStatusAsync(ApprovalStatus status, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(Submission submission, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Submission submission, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Submission>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
