using System.Globalization;
using System.Reflection;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Export;

/// <summary>
/// Default <see cref="IPdfExportService"/>. Builds a Liquid data envelope that mirrors the
/// read-only submission view, renders it to HTML with the shared <see cref="IReportRenderer"/>,
/// then hands the HTML to an <see cref="IPdfConverter"/> (Gotenberg) for the actual PDF.
/// Schema rules are rendered as plain English via <see cref="IExpressionTranslator.TranslateToEnglish"/>.
/// </summary>
public sealed class PdfExportService : IPdfExportService
{
    private static readonly string TemplateText = LoadTemplate("export.liquid");

    private readonly ISchemaRepository _schemas;
    private readonly ISubmissionRepository _submissions;
    private readonly IReportRenderer _renderer;
    private readonly IPdfConverter _converter;
    private readonly IExpressionTranslator _translator;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="PdfExportService"/>.</summary>
    public PdfExportService(
        ISchemaRepository schemas,
        ISubmissionRepository submissions,
        IReportRenderer renderer,
        IPdfConverter converter,
        IExpressionTranslator translator,
        IAuditContext audit)
    {
        _schemas = schemas;
        _submissions = submissions;
        _renderer = renderer;
        _converter = converter;
        _translator = translator;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<PdfDocument?> ExportSchemaAsync(string name, CancellationToken ct = default)
    {
        var schema = await _schemas.GetByNameAsync(name, ct: ct);
        if (schema is null) return null;

        var labels = BuildLabels(schema);
        var items = SchemaLayoutFlattener.Flatten(schema)
            .Select(i => BuildItem(i, schema.Enabled, labels, showData: false, sample: null))
            .ToList();

        var model = new
        {
            kind = "schema",
            showData = false,
            title = Display(schema.Label, schema.Name),
            meta = new List<object>
            {
                new { label = "Version", value = schema.Version.ToString(CultureInfo.InvariantCulture) },
                new { label = "Audience", value = schema.IsGlobal ? "Global" : "Restricted" },
            },
            description = NullIfBlank(schema.Description),
            submissionValidations = schema.SubmissionValidations
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => TranslateSafe(r, labels))
                .ToList(),
            items,
            generatedAt = FormatTimestamp(_audit.UtcNow),
        };

        var pdf = await RenderAsync(model, ct);
        return new PdfDocument(pdf, $"{Slug(schema.Name)}.pdf");
    }

    /// <inheritdoc />
    public async Task<PdfDocument?> ExportSubmissionAsync(Guid submissionId, CancellationToken ct = default)
    {
        var submission = await _submissions.GetByIdAsync(submissionId, ct: ct);
        if (submission is null) return null;

        // A submission's samples usually all belong to one schema; mirror the read-only view and
        // anchor on the first sample's schema.
        var schemaName = submission.Samples.FirstOrDefault()?.SchemaName;
        var schema = schemaName is null ? null : await _schemas.GetByNameAsync(schemaName, includeDeleted: true, ct: ct);

        var relevant = schemaName is null
            ? submission.Samples
            : submission.Samples.Where(s => string.Equals(s.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase)).ToList();
        var samplesByName = relevant
            .GroupBy(s => s.ValueName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var labels = schema is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : BuildLabels(schema);
        var schemaEnabled = schema?.Enabled ?? true;

        // Prefer the schema's layout; when the schema no longer exists, fall back to one row per
        // submitted sample so the data still renders.
        var flat = schema is not null
            ? SchemaLayoutFlattener.Flatten(schema)
            : relevant
                .Select(s => new SchemaLayoutFlattener.LayoutItem(
                    SchemaLayoutFlattener.ValueKind, 0, null, null, SyntheticValue(s)))
                .ToList();

        var items = flat
            .Select(i => BuildItem(
                i, schemaEnabled, labels, showData: true,
                sample: i.Value is null ? null : Lookup(samplesByName, i.Value.Name)))
            .ToList();

        var schemaLabel = schema is not null ? Display(schema.Label, schema.Name) : schemaName;
        var model = new
        {
            kind = "submission",
            showData = true,
            title = "Submission",
            subtitle = schemaLabel,
            meta = new List<object>
            {
                new { label = "Service", value = submission.ServiceName ?? submission.ServiceAccountId.ToString() },
                new { label = "Schema", value = schemaLabel ?? "(unknown)" },
                new { label = "Submitted", value = FormatTimestamp(submission.SubmittedAt) },
                new { label = "Submission ID", value = submission.Id.ToString() },
            },
            description = (string?)null,
            submissionValidations = new List<string>(),
            items,
            generatedAt = FormatTimestamp(_audit.UtcNow),
        };

        var pdf = await RenderAsync(model, ct);
        return new PdfDocument(pdf, $"submission-{submission.Id}.pdf");
    }

    private async Task<byte[]> RenderAsync(object model, CancellationToken ct)
    {
        var html = await _renderer.RenderAsync(TemplateText, model, ct);
        return await _converter.HtmlToPdfAsync(html, ct);
    }

    private object BuildItem(
        SchemaLayoutFlattener.LayoutItem item,
        bool schemaEnabled,
        IReadOnlyDictionary<string, string> labels,
        bool showData,
        Sample? sample)
    {
        if (item.Kind == SchemaLayoutFlattener.SectionKind)
        {
            return new
            {
                kind = "section",
                depth = item.Depth,
                indent = item.Depth * 16,
                caption = NullIfBlank(item.Caption),
                description = NullIfBlank(item.Description),
            };
        }

        var v = item.Value!;
        var disabled = !(schemaEnabled && v.Enabled);
        var hasValue = sample is not null && sample.Value is not null;

        return new
        {
            kind = "value",
            depth = item.Depth,
            indent = item.Depth * 16,
            caption = NullIfBlank(v.Caption),
            label = Display(v.Label, v.Name),
            required = v.Required,
            typeLabel = FriendlyType(v.Type),
            cadenceLabel = CadenceLabel(v.Cadence),
            calculated = v.IsCalculated,
            disabled,
            unit = NullIfBlank(v.Unit),
            description = NullIfBlank(v.Description),
            notes = NullIfBlank(v.Notes),
            constraints = BuildConstraints(v),
            rag = BuildRag(v),
            rules = BuildRules(v, labels),
            hasValue,
            valueText = hasValue ? FormatValue(sample!.Value, v.Type) : null,
            note = sample is null ? null : NullIfBlank(sample.Note),
        };
    }

    private List<object> BuildRules(SchemaValue v, IReadOnlyDictionary<string, string> labels)
    {
        var rules = new List<object>();
        void Add(string label, string? expr)
        {
            if (!string.IsNullOrWhiteSpace(expr))
                rules.Add(new { label, text = TranslateSafe(expr!, labels) });
        }

        if (v.IsCalculated) Add("Calculated as", v.Expression);
        Add("Visible when", v.VisibleIf);
        Add("Enabled when", v.EnabledIf);
        Add("Warns when", v.Warning);
        Add("Valid when", v.ValueValidation);
        return rules;
    }

    private string TranslateSafe(string expression, IReadOnlyDictionary<string, string> labels)
    {
        try
        {
            return _translator.TranslateToEnglish(expression, labels);
        }
        catch
        {
            // If a rule can't be parsed (e.g. authored against a newer dialect), fall back to the
            // raw source so the reader still sees something meaningful.
            return expression;
        }
    }

    private static List<string> BuildConstraints(SchemaValue v)
    {
        var hints = new List<string>();
        switch (v.Type)
        {
            case SchemaValueType.Integer:
            case SchemaValueType.Number:
                if (v.Min is { } min && v.Max is { } max) hints.Add($"Between {Num(min)} and {Num(max)}");
                else if (v.Min is { } lo) hints.Add($"Min {Num(lo)}");
                else if (v.Max is { } hi) hints.Add($"Max {Num(hi)}");
                break;
            case SchemaValueType.String:
                if (v.MinLength is { } mn && v.MaxLength is { } mx) hints.Add($"{mn}\u2013{mx} characters");
                else if (v.MinLength is { } smn) hints.Add($"Min {smn} characters");
                else if (v.MaxLength is { } smx) hints.Add($"Max {smx} characters");
                if (!string.IsNullOrWhiteSpace(v.RegexPattern)) hints.Add($"Pattern: {v.RegexPattern}");
                break;
            case SchemaValueType.Date:
                if (v.MinDate is { } d1 && v.MaxDate is { } d2) hints.Add($"Between {Date(d1)} and {Date(d2)}");
                else if (v.MinDate is { } df) hints.Add($"From {Date(df)}");
                else if (v.MaxDate is { } dt) hints.Add($"Until {Date(dt)}");
                break;
        }
        return hints;
    }

    private static string? BuildRag(SchemaValue v)
    {
        if (!v.HasTargetBand) return null;
        var parts = new List<string>();
        var green = FormatBand("green", v.GreenMin, v.GreenMax);
        var amber = FormatBand("amber", v.AmberMin, v.AmberMax);
        if (green is not null) parts.Add(green);
        if (amber is not null) parts.Add(amber);
        return parts.Count == 0 ? null : "Target band: " + string.Join(", ", parts);
    }

    private static string? FormatBand(string label, double? min, double? max)
    {
        if (min is null && max is null) return null;
        if (min is { } lo && max is { } hi) return $"{label} {Num(lo)}\u2013{Num(hi)}";
        if (min is { } l) return $"{label} \u2265 {Num(l)}";
        return $"{label} \u2264 {Num(max!.Value)}";
    }

    private static SchemaValue SyntheticValue(Sample s) => new()
    {
        Name = s.ValueName,
        Type = InferType(s.Value),
    };

    private static SchemaValueType InferType(object? value) => value switch
    {
        bool => SchemaValueType.Boolean,
        long or int or short => SchemaValueType.Integer,
        double or float or decimal => SchemaValueType.Number,
        DateTime => SchemaValueType.Date,
        _ => SchemaValueType.String,
    };

    private static Sample? Lookup(IReadOnlyDictionary<string, Sample> map, string name) =>
        map.TryGetValue(name, out var s) ? s : null;

    private static IReadOnlyDictionary<string, string> BuildLabels(Schema schema)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in schema.Values)
            if (!string.IsNullOrWhiteSpace(v.Label))
                labels[v.Name] = v.Label!;
        return labels;
    }

    private static string FriendlyType(SchemaValueType type) => type switch
    {
        SchemaValueType.String => "Text",
        SchemaValueType.Integer => "Whole number",
        SchemaValueType.Number => "Number",
        SchemaValueType.Date => "Date",
        SchemaValueType.Boolean => "Yes/No",
        _ => type.ToString(),
    };

    private static string CadenceLabel(Cadence cadence) => cadence switch
    {
        Cadence.Daily => "Daily",
        Cadence.Weekly => "Weekly",
        Cadence.Monthly => "Monthly",
        Cadence.Yearly => "Yearly",
        Cadence.Fortnightly => "Fortnightly",
        Cadence.Quarterly => "Quarterly",
        Cadence.SemiAnnually => "Semi-annually",
        _ => cadence.ToString(),
    };

    private static string FormatValue(object? value, SchemaValueType type)
    {
        if (value is null) return string.Empty;
        return type switch
        {
            SchemaValueType.Boolean => value is bool b ? (b ? "Yes" : "No") : value.ToString() ?? string.Empty,
            SchemaValueType.Date => value is DateTime dt
                ? (dt.TimeOfDay == TimeSpan.Zero ? Date(dt) : dt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture))
                : value.ToString() ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string Num(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Date(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Display(string? label, string fallback) =>
        string.IsNullOrWhiteSpace(label) ? fallback : label!;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Slug(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var slug = new string(chars).Trim('_');
        return string.IsNullOrEmpty(slug) ? "schema" : slug;
    }

    private static string LoadTemplate(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded PDF template '{fileName}' was not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
