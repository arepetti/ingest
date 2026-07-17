using ClosedXML.Excel;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Export;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="SubmissionXlsxExportService"/>. Each test renders the workbook and reads it
/// back with ClosedXML to assert the grid: outermost-section column groups (tinted headers), area
/// band rows, the missing-value highlight, and the per-submission warning note.
/// </summary>
public class SubmissionXlsxExportServiceTests
{
    // Matches SubmissionXlsxExportService's first section colour and the missing-value fill.
    private static readonly XLColor FirstSectionFill = XLColor.FromHtml("#DDEBF7");
    private static readonly XLColor EmptyFill = XLColor.FromHtml("#FFF7CC");

    private static readonly Guid GarbageId = Guid.NewGuid();
    private static readonly Guid WaterId = Guid.NewGuid();
    private static readonly Guid AirId = Guid.NewGuid();
    private static readonly Guid GhostId = Guid.NewGuid();

    private static Schema BuildSchema() => new()
    {
        Name = "weekly",
        Label = "Weekly report",
        Enabled = true,
        Version = 1,
        Values = new List<SchemaValue>
        {
            new() { Name = "region", Label = "Region", Type = SchemaValueType.String },
            new() { Name = "employees", Label = "Employees", Type = SchemaValueType.Integer },
            new() { Name = "sick", Label = "Sick leave", Type = SchemaValueType.Integer },
        },
        Layout = new List<SchemaLayoutNode>
        {
            // A top-level value forms the (header-less) ungrouped block that leads the columns.
            new() { Kind = SchemaLayoutNodeKind.Value, ValueName = "region" },
            new()
            {
                Kind = SchemaLayoutNodeKind.Section, Caption = "Employment",
                Items = new List<SchemaLayoutNode>
                {
                    new() { Kind = SchemaLayoutNodeKind.Value, ValueName = "employees" },
                    new() { Kind = SchemaLayoutNodeKind.Value, ValueName = "sick" },
                },
            },
        },
    };

    private static Submission Sub(Guid serviceId, string serviceName, int day, IEnumerable<(string name, object? value)> values, params SubmissionWarning[] warnings) => new()
    {
        Id = Guid.NewGuid(),
        ServiceAccountId = serviceId,
        ServiceName = serviceName,
        SubmittedAt = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc),
        Warnings = warnings.ToList(),
        Samples = values
            .Where(v => v.value is not null)
            .Select(v => new Sample { SchemaName = "weekly", ValueName = v.name, Value = v.value, Timestamp = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc) })
            .ToList(),
    };

    private static SubmissionXlsxExportService BuildService()
    {
        var submissions = new List<Submission>
        {
            Sub(GarbageId, "Garbage", 2, new (string, object?)[] { ("region", "R1"), ("employees", 10L), ("sick", 2L) }),
            Sub(WaterId, "Water", 3, new (string, object?)[] { ("region", "R2"), ("employees", 5L), ("sick", null) },
                new SubmissionWarning("sick", "Peak too high"), new SubmissionWarning(null, "check data")),
            Sub(AirId, "Air", 4, new (string, object?)[] { ("region", "R3"), ("employees", 1L), ("sick", 1L) }),
            Sub(GhostId, "Ghost", 5, new (string, object?)[] { ("employees", 0L) }),
        };

        var accounts = new List<Account>
        {
            new() { Name = "garbage", Label = "Garbage", Kind = AccountKind.Application, Role = AccountRole.Service, Enabled = true, Area = "North area" },
            new() { Name = "water", Label = "Water", Kind = AccountKind.Application, Role = AccountRole.Service, Enabled = true, Area = "North area" },
            new() { Name = "air", Label = "Air", Kind = AccountKind.Application, Role = AccountRole.Service, Enabled = true, Area = "South area" },
            new() { Name = "ghost", Label = "Ghost", Kind = AccountKind.Application, Role = AccountRole.Service, Enabled = true, Area = null },
        };
        accounts[0].Id = GarbageId;
        accounts[1].Id = WaterId;
        accounts[2].Id = AirId;
        accounts[3].Id = GhostId;

        return new SubmissionXlsxExportService(
            new FakeSchemaRepo(BuildSchema()),
            new FakeSubmissionRepo(submissions),
            new FakeAccountRepo(accounts),
            new FakeClock());
    }

    [Fact]
    public async Task Export_lays_out_columns_grouped_by_outermost_section()
    {
        var doc = await BuildService().ExportSubmissionsAsync(new SubmissionExportFilter("weekly"));

        Assert.NotNull(doc);
        Assert.StartsWith("submissions-weekly-", doc!.FileName);
        Assert.EndsWith(".xlsx", doc.FileName);

        var ws = Read(doc);
        Assert.Equal("Service", ws.Cell(1, 1).GetString());
        Assert.Equal("Schema", ws.Cell(1, 2).GetString());

        // region is a top-level value → header-less leading column; employees/sick sit under the
        // "Employment" section header (merged across their two columns, tinted with the first colour).
        Assert.Equal("Region", ws.Cell(2, 3).GetString());
        Assert.Equal("Employment", ws.Cell(1, 4).GetString());
        Assert.Equal("Employees", ws.Cell(2, 4).GetString());
        Assert.Equal("Sick leave", ws.Cell(2, 5).GetString());
        Assert.Contains(ws.MergedRanges, r => r.RangeAddress.ToString() == "D1:E1");
        Assert.Equal(FirstSectionFill, ws.Cell(1, 4).Style.Fill.BackgroundColor);
    }

    [Fact]
    public async Task Export_bands_rows_by_area_and_omits_a_header_for_missing_areas()
    {
        var doc = await BuildService().ExportSubmissionsAsync(new SubmissionExportFilter("weekly"));
        var ws = Read(doc);

        var north = FindRow(ws, "North area");
        var south = FindRow(ws, "South area");
        Assert.True(north > 0 && south > 0 && north < south);
        // Area headers span the whole grid (Service..Sick leave = A..E).
        Assert.Contains(ws.MergedRanges, r => r.RangeAddress.ToString() == $"A{north}:E{north}");

        // The area-less service (Ghost) has no banner: the row directly above it is a data row.
        var ghost = FindRow(ws, "Ghost");
        Assert.True(ghost > 0);
        Assert.Equal("Air", ws.Cell(ghost - 1, 1).GetString());
    }

    [Fact]
    public async Task Export_highlights_missing_values_and_routes_warnings_to_the_right_cell()
    {
        var doc = await BuildService().ExportSubmissionsAsync(new SubmissionExportFilter("weekly"));
        var ws = Read(doc);

        var water = FindRow(ws, "Water");
        Assert.True(water > 0);
        Assert.Equal("Weekly report", ws.Cell(water, 2).GetString());

        // Water didn't report sick leave (column 5) → blank cell with the subtle-yellow highlight.
        var sick = ws.Cell(water, 5);
        Assert.True(sick.IsEmpty(XLCellsUsedOptions.Contents));
        Assert.Equal(EmptyFill, sick.Style.Fill.BackgroundColor);

        // A value-scoped warning (ValueName = "sick") rides along as a note on that value's own
        // cell — even though it's the empty/highlighted one.
        Assert.True(sick.HasComment);
        var sickNote = string.Concat(sick.GetComment().Select(rt => rt.Text));
        Assert.Contains("Peak too high", sickNote);

        // The submission-level warning (no value name) collects on the schema-label cell (column 2),
        // and the value-scoped one does not leak onto it.
        var schemaCell = ws.Cell(water, 2);
        Assert.True(schemaCell.HasComment);
        var schemaNote = string.Concat(schemaCell.GetComment().Select(rt => rt.Text));
        Assert.Contains("check data", schemaNote);
        Assert.DoesNotContain("Peak too high", schemaNote);
    }

    [Fact]
    public async Task Export_writes_numeric_values_as_numbers()
    {
        var doc = await BuildService().ExportSubmissionsAsync(new SubmissionExportFilter("weekly"));
        var ws = Read(doc);

        var garbage = FindRow(ws, "Garbage");
        Assert.Equal(10, ws.Cell(garbage, 4).GetDouble());
        Assert.Equal(2, ws.Cell(garbage, 5).GetDouble());
    }

    [Fact]
    public async Task Export_returns_null_for_an_unknown_schema()
    {
        var doc = await BuildService().ExportSubmissionsAsync(new SubmissionExportFilter("does-not-exist"));
        Assert.Null(doc);
    }

    [Fact]
    public async Task Export_requires_a_schema_name()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => BuildService().ExportSubmissionsAsync(new SubmissionExportFilter("  ")));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static IXLWorksheet Read(XlsxDocument? doc)
    {
        Assert.NotNull(doc);
        var wb = new XLWorkbook(new MemoryStream(doc!.Content));
        return wb.Worksheet("Weekly report");
    }

    private static int FindRow(IXLWorksheet ws, string col1Value)
    {
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= last; r++)
            if (string.Equals(ws.Cell(r, 1).GetString(), col1Value, StringComparison.Ordinal))
                return r;
        return -1;
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────────────────

    private sealed class FakeClock : IAuditContext
    {
        public string? UserName => null;
        public Guid? AccountId => null;
        public DateTime UtcNow => new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FakeSchemaRepo : ISchemaRepository
    {
        private readonly Schema _schema;
        public FakeSchemaRepo(Schema schema) => _schema = schema;

        public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(string.Equals(_schema.Name, name, StringComparison.OrdinalIgnoreCase) ? _schema : null);

        public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) => Task.FromResult<Schema?>(null);
        public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(Schema schema, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Schema schema, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeSubmissionRepo : ISubmissionRepository
    {
        private readonly List<Submission> _submissions;
        public FakeSubmissionRepo(List<Submission> submissions) => _submissions = submissions;

        public Task<PagedResult<Submission>> ListAsync(
            PageRequest request, Guid? serviceId = null, DateTime? from = null, DateTime? to = null,
            string? schemaName = null, ApprovalStatus? approvalStatus = null, bool? draft = null,
            IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Submission>(_submissions, _submissions.Count, request.Page, request.PageSize));

        public Task<Submission?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) => Task.FromResult<Submission?>(null);
        public Task<long> CountBySchemaAsync(string schemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CountByApprovalStatusAsync(ApprovalStatus status, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(Submission submission, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Submission submission, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Submission>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeAccountRepo : IAccountRepository
    {
        private readonly List<Account> _accounts;
        public FakeAccountRepo(List<Account> accounts) => _accounts = accounts;

        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Account>(_accounts, _accounts.Count, request.Page, request.PageSize));

        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(Account account, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Account account, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
