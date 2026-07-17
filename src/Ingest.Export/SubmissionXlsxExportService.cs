using System.Globalization;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Export.Xlsx;

namespace Ingest.Export;

/// <summary>
/// Default <see cref="ISubmissionExportService"/>. Lays a single schema's submissions out as a grid:
/// one row per submission, one column per schema value (columns grouped by the outermost layout
/// section and tinted per group), with rows grouped under per-area header rows. Empty values get a
/// subtle highlight; warnings ride along as cell notes — value-scoped warnings on the value's own
/// cell, submission-level ones on the schema-label cell.
/// </summary>
public sealed class SubmissionXlsxExportService : ISubmissionExportService
{
    // Subtle, non-yellow group tints, cycled per outermost section (yellow is reserved for the
    // "missing value" highlight so the two never clash).
    private static readonly string[] SectionPalette =
    {
        "#DDEBF7", // blue
        "#E2EFDA", // green
        "#FCE4EC", // pink
        "#EDE7F6", // purple
        "#E0F2F1", // teal
        "#FFE5CC", // orange
        "#F2F2F2", // grey
    };

    private const string EmptyFill = "#FFF7CC";       // subtle yellow for a missing value
    private const string AreaHeaderFill = "#D9E1F2";  // area band
    private const string FixedHeaderFill = "#F2F2F2";  // Service / Schema column headers

    private const int ServiceCol = 1;
    private const int SchemaCol = 2;
    private const int FirstValueCol = 3;

    private const int AccountPageSize = 500;

    private readonly ISchemaRepository _schemas;
    private readonly ISubmissionRepository _submissions;
    private readonly IAccountRepository _accounts;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="SubmissionXlsxExportService"/>.</summary>
    public SubmissionXlsxExportService(
        ISchemaRepository schemas,
        ISubmissionRepository submissions,
        IAccountRepository accounts,
        IAuditContext audit)
    {
        _schemas = schemas;
        _submissions = submissions;
        _accounts = accounts;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<XlsxDocument?> ExportSubmissionsAsync(SubmissionExportFilter filter, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filter.SchemaName))
            throw new ArgumentException("A schema name is required for the XLSX export.", nameof(filter));

        var schema = await _schemas.GetByNameAsync(filter.SchemaName, includeDeleted: true, ct: ct);
        if (schema is null) return null;

        var groups = BuildGroups(schema);
        var columns = groups.SelectMany(g => g.Values).ToList();
        var totalCols = FirstValueCol - 1 + columns.Count;

        var submissions = await LoadSubmissionsAsync(filter, ct);
        var areaByService = await LoadServiceAreasAsync(submissions, ct);

        // Rows: grouped by area (services without an area come last, unheaded), then by service, then
        // by submission time — so an area band covers a contiguous, deterministic run of rows.
        var ordered = submissions
            .OrderBy(s => Area(areaByService, s) is null ? 1 : 0)
            .ThenBy(s => Area(areaByService, s) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => ServiceName(s), StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SubmittedAt)
            .ToList();

        using var workbook = new XlsxWorkbook();
        var sheet = workbook.AddSheet(Display(schema.Label, schema.Name));

        WriteHeader(sheet, groups);
        WriteRows(sheet, schema, filter.SchemaName, columns, ordered, areaByService, totalCols);
        SetWidths(sheet, columns.Count);

        var bytes = workbook.ToBytes();
        var fileName = $"submissions-{Slug(schema.Name)}-{_audit.UtcNow:yyyyMMdd}{XlsxWorkbook.FileExtension}";
        return new XlsxDocument(bytes, fileName);
    }

    // ── Column plan ──────────────────────────────────────────────────────────────────────

    /// <summary>A contiguous block of columns sharing one (optional) outermost-section header.</summary>
    private sealed record ColumnGroup(string? Header, List<SchemaValue> Values);

    /// <summary>
    /// Group the schema's values by their outermost layout section. Values that sit directly in the
    /// layout (or aren't placed at all) form a leading header-less group; nested sections are
    /// flattened into their top-level ancestor's group.
    /// </summary>
    private static List<ColumnGroup> BuildGroups(Schema schema)
    {
        var ungrouped = new List<SchemaValue>();
        var groups = new List<ColumnGroup>();
        ColumnGroup? current = null;

        foreach (var item in SchemaLayoutFlattener.Flatten(schema))
        {
            if (item.Kind == SchemaLayoutFlattener.SectionKind)
            {
                // Only a top-level section starts a new column group; nested ones keep flowing into
                // the current top-level group.
                if (item.Depth == 0)
                {
                    var header = string.IsNullOrWhiteSpace(item.Caption) ? null : item.Caption!.Trim();
                    current = new ColumnGroup(header, new List<SchemaValue>());
                    groups.Add(current);
                }
            }
            else if (item.Value is { } value)
            {
                // A value's own depth is authoritative: depth 0 is always top-level (ungrouped),
                // anything deeper belongs to the current outermost section.
                if (item.Depth == 0 || current is null) ungrouped.Add(value);
                else current.Values.Add(value);
            }
        }

        var ordered = new List<ColumnGroup>();
        if (ungrouped.Count > 0) ordered.Add(new ColumnGroup(null, ungrouped));
        ordered.AddRange(groups.Where(g => g.Values.Count > 0));
        return ordered;
    }

    // ── Rendering ────────────────────────────────────────────────────────────────────────

    private static void WriteHeader(XlsxSheet sheet, List<ColumnGroup> groups)
    {
        var fixedStyle = new XlsxCellStyle { Bold = true, FillHex = FixedHeaderFill, Align = XlsxAlign.Center };
        sheet.SetText(1, ServiceCol, "Service", fixedStyle);
        sheet.Merge(1, ServiceCol, 2, ServiceCol);
        sheet.SetText(1, SchemaCol, "Schema", fixedStyle);
        sheet.Merge(1, SchemaCol, 2, SchemaCol);

        var col = FirstValueCol;
        var colorIndex = 0;
        foreach (var group in groups)
        {
            var first = col;
            var last = col + group.Values.Count - 1;

            string? fill = null;
            if (group.Header is not null)
            {
                fill = SectionPalette[colorIndex % SectionPalette.Length];
                colorIndex++;
                var headerStyle = new XlsxCellStyle { Bold = true, FillHex = fill, Align = XlsxAlign.Center, WrapText = true };
                sheet.SetText(1, first, group.Header, headerStyle);
                if (last > first) sheet.Merge(1, first, 1, last);
            }

            for (var i = 0; i < group.Values.Count; i++)
            {
                var v = group.Values[i];
                var labelStyle = new XlsxCellStyle { Bold = true, FillHex = fill, WrapText = true };
                sheet.SetText(2, col + i, Display(v.Label, v.Name), labelStyle);
            }

            col = last + 1;
        }
    }

    private static void WriteRows(
        XlsxSheet sheet,
        Schema schema,
        string schemaName,
        List<SchemaValue> columns,
        List<Submission> ordered,
        IReadOnlyDictionary<Guid, string?> areaByService,
        int totalCols)
    {
        var schemaLabel = Display(schema.Label, schema.Name);
        var emptyStyle = new XlsxCellStyle { FillHex = EmptyFill };
        var areaHeaderStyle = new XlsxCellStyle { Bold = true, FillHex = AreaHeaderFill };

        var row = 3;
        string? currentArea = null;
        var firstRow = true;

        foreach (var submission in ordered)
        {
            var area = Area(areaByService, submission);
            if (area is not null && (firstRow || !string.Equals(area, currentArea, StringComparison.OrdinalIgnoreCase)))
            {
                sheet.SetText(row, 1, area, areaHeaderStyle);
                if (totalCols > 1) sheet.Merge(row, 1, row, totalCols);
                row++;
            }
            currentArea = area;
            firstRow = false;

            sheet.SetText(row, ServiceCol, ServiceName(submission));
            sheet.SetText(row, SchemaCol, schemaLabel);

            // Group warnings by their associated value name. Value-scoped warnings ride along as a
            // note on that value's own cell; warnings with no value name (submission-level) — or
            // that reference a value not shown as a column — collect on the schema-label cell.
            var warningsByValue = submission.Warnings
                .Where(w => !string.IsNullOrWhiteSpace(w.Message))
                .GroupBy(w => w.ValueName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(w => w.Message).ToList(), StringComparer.OrdinalIgnoreCase);

            var columnNames = columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var schemaNotes = warningsByValue
                .Where(kv => kv.Key.Length == 0 || !columnNames.Contains(kv.Key))
                .SelectMany(kv => kv.Value)
                .ToList();
            if (schemaNotes.Count > 0) sheet.AddNote(row, SchemaCol, string.Join("\n", schemaNotes));

            var byName = submission.Samples
                .Where(s => string.Equals(s.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(s => s.ValueName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < columns.Count; i++)
            {
                var col = FirstValueCol + i;
                WriteValueCell(sheet, row, col, columns[i], byName.GetValueOrDefault(columns[i].Name), emptyStyle);
                if (warningsByValue.TryGetValue(columns[i].Name, out var valueNotes) && valueNotes.Count > 0)
                    sheet.AddNote(row, col, string.Join("\n", valueNotes));
            }

            row++;
        }
    }

    private static void WriteValueCell(XlsxSheet sheet, int row, int col, SchemaValue value, Sample? sample, XlsxCellStyle emptyStyle)
    {
        if (sample?.Value is not { } raw)
        {
            // Missing value: leave it blank but flag it so gaps are obvious at a glance.
            sheet.SetText(row, col, null, emptyStyle);
            return;
        }

        switch (value.Type)
        {
            case SchemaValueType.Integer:
            case SchemaValueType.Number:
                if (TryToDouble(raw, out var d)) sheet.SetNumber(row, col, d);
                else sheet.SetText(row, col, Convert.ToString(raw, CultureInfo.InvariantCulture));
                break;
            case SchemaValueType.Boolean:
                sheet.SetText(row, col, raw is bool b ? (b ? "Yes" : "No") : Convert.ToString(raw, CultureInfo.InvariantCulture));
                break;
            case SchemaValueType.Date:
                sheet.SetText(row, col, raw is DateTime dt
                    ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : Convert.ToString(raw, CultureInfo.InvariantCulture));
                break;
            default:
                sheet.SetText(row, col, Convert.ToString(raw, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void SetWidths(XlsxSheet sheet, int valueColumns)
    {
        sheet.SetColumnWidth(ServiceCol, 26);
        sheet.SetColumnWidth(SchemaCol, 24);
        for (var i = 0; i < valueColumns; i++)
            sheet.SetColumnWidth(FirstValueCol + i, 18);
    }

    // ── Data loading ─────────────────────────────────────────────────────────────────────

    private async Task<List<Submission>> LoadSubmissionsAsync(SubmissionExportFilter filter, CancellationToken ct)
    {
        var all = new List<Submission>();
        var page = 1;
        while (true)
        {
            var request = new PageRequest(page, AccountPageSize, "createdAt", filter.IncludeDeleted);
            var result = await _submissions.ListAsync(
                request, filter.ServiceId, filter.From, filter.To, filter.SchemaName,
                filter.ApprovalStatus, filter.Draft, filter.AllowedServiceIds, ct);
            all.AddRange(result.Items);
            if (result.Items.Count == 0 || all.Count >= result.Total) break;
            page++;
        }
        return all;
    }

    private async Task<IReadOnlyDictionary<Guid, string?>> LoadServiceAreasAsync(IReadOnlyList<Submission> submissions, CancellationToken ct)
    {
        var wanted = submissions.Select(s => s.ServiceAccountId).ToHashSet();
        var map = new Dictionary<Guid, string?>();
        if (wanted.Count == 0) return map;

        var page = 1;
        while (true)
        {
            var result = await _accounts.ListAsync(new PageRequest(page, AccountPageSize), null, null, ct);
            foreach (var a in result.Items)
                if (wanted.Contains(a.Id))
                    map[a.Id] = string.IsNullOrWhiteSpace(a.Area) ? null : a.Area!.Trim();
            if (result.Items.Count == 0 || map.Count >= wanted.Count || (page * AccountPageSize) >= result.Total) break;
            page++;
        }
        return map;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private static string? Area(IReadOnlyDictionary<Guid, string?> map, Submission s) =>
        map.TryGetValue(s.ServiceAccountId, out var a) ? a : null;

    private static string ServiceName(Submission s) =>
        !string.IsNullOrWhiteSpace(s.ServiceName) ? s.ServiceName! : s.ServiceAccountId.ToString();

    private static bool TryToDouble(object value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case short sh: result = sh; return true;
            case decimal m: result = (double)m; return true;
            default:
                return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }
    }

    private static string Display(string? label, string fallback) =>
        string.IsNullOrWhiteSpace(label) ? fallback : label!;

    private static string Slug(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var slug = new string(chars).Trim('_');
        return string.IsNullOrEmpty(slug) ? "schema" : slug;
    }
}
