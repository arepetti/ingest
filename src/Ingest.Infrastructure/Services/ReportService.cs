using System.Text.RegularExpressions;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Reports;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IReportService"/>. Owns the parse-on-upload step (front
/// matter → entity fields), persists reports, and composes the data envelope each
/// <see cref="ReportType"/> requires before delegating to <see cref="IReportRenderer"/>.
/// </summary>
public sealed class ReportService : IReportService
{
    private static readonly Regex NameSlug = new("[^a-zA-Z0-9_-]+", RegexOptions.Compiled);

    private readonly IReportRepository _reports;
    private readonly ISchemaRepository _schemas;
    private readonly ISubmissionRepository _submissions;
    private readonly ISampleRepository _samples;
    private readonly IAccountRepository _accounts;
    private readonly IReportRenderer _renderer;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="ReportService"/>.</summary>
    /// <param name="reports">Report repository.</param>
    /// <param name="schemas">Schema repository (used to look up the target schema's definition for the envelope).</param>
    /// <param name="submissions">Submission repository (Single render data + Single-pick filter).</param>
    /// <param name="samples">Sample projection repository (Aggregate render data).</param>
    /// <param name="accounts">Account repository (denormalised service label/name on Single renders).</param>
    /// <param name="renderer">The Liquid renderer.</param>
    /// <param name="audit">Audit context (clock).</param>
    public ReportService(
        IReportRepository reports,
        ISchemaRepository schemas,
        ISubmissionRepository submissions,
        ISampleRepository samples,
        IAccountRepository accounts,
        IReportRenderer renderer,
        IAuditContext audit)
    {
        _reports = reports;
        _schemas = schemas;
        _submissions = submissions;
        _samples = samples;
        _accounts = accounts;
        _renderer = renderer;
        _audit = audit;
    }

    /// <inheritdoc />
    public Task<PagedResult<Report>> ListAsync(PageRequest request, CancellationToken ct = default) =>
        _reports.ListAsync(request, ct);

    /// <inheritdoc />
    public Task<Report?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct = default) =>
        _reports.GetByIdAsync(id, includeDeleted, ct);

    /// <inheritdoc />
    public Task<Report?> GetByNameAsync(string name, CancellationToken ct = default) =>
        _reports.GetByNameAsync(name, ct: ct);

    /// <inheritdoc />
    public async Task<Report> UploadAsync(string fileName, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ValidationException(new[] { "Report content is empty." });

        var meta = ReportMetadataParser.Parse(content);
        var name = !string.IsNullOrWhiteSpace(meta.Name)
            ? meta.Name!
            : NameFromFile(fileName);
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException(new[] { "Report name is required (set 'name:' in the front matter or supply a non-empty file name)." });

        if (await _reports.GetByNameAsync(name, includeDeleted: true, ct) is not null)
            throw new ConflictException($"Report '{name}' already exists.");

        var report = new Report
        {
            Name = name,
            Label = meta.Label,
            Description = meta.Description,
            Type = meta.Type ?? ReportType.Aggregate,
            TargetSchemaNames = meta.TargetSchemaNames.ToList(),
            Content = content,
            Template = meta.Template,
        };

        await _reports.AddAsync(report, ct);
        return report;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        _reports.SoftDeleteAsync(id, ct);

    /// <inheritdoc />
    public async Task<ReportRenderResult> RenderAsync(string name, ReportRenderRequest request, CancellationToken ct = default)
    {
        var report = await _reports.GetByNameAsync(name, ct: ct)
            ?? throw new NotFoundException($"Report '{name}'");

        // Resolve the date window. Default = "this calendar month so far" so the viewer always
        // has something useful to show without the user having to pick a range first.
        var to = request.To ?? _audit.UtcNow;
        var from = request.From ?? new DateTime(to.Year, to.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        if (from > to)
            throw new ValidationException(new[] { "'from' must be earlier than or equal to 'to'." });

        // Pick the schema this render is scoped to. When the report has exactly one target the
        // viewer doesn't need to pass anything; otherwise SchemaName is required.
        var schemaName = ResolveSchemaName(report, request);
        Schema? schema = null;
        if (schemaName is not null)
        {
            schema = await _schemas.GetByNameAsync(schemaName, ct: ct)
                ?? throw new NotFoundException($"Schema '{schemaName}'");
        }

        var model = report.Type switch
        {
            ReportType.Single => await BuildSingleModelAsync(report, schema, request, from, to, ct),
            ReportType.Aggregate => await BuildAggregateModelAsync(report, schema, from, to, ct),
            _ => throw new ValidationException(new[] { $"Unsupported report type '{report.Type}'." }),
        };

        var html = await _renderer.RenderAsync(report.Template, model, ct);
        return new ReportRenderResult(
            html,
            report.Name,
            report.Label,
            report.Type,
            schemaName,
            request.SubmissionId,
            from,
            to);
    }

    private static string? ResolveSchemaName(Report report, ReportRenderRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SchemaName))
        {
            // When the caller pinned a schema, make sure it's one the report says it supports
            // (or that the report is global — empty target list = any schema).
            if (report.TargetSchemaNames.Count > 0 &&
                !report.TargetSchemaNames.Contains(request.SchemaName!, StringComparer.OrdinalIgnoreCase))
            {
                throw new ValidationException(new[]
                {
                    $"Schema '{request.SchemaName}' is not in this report's target list.",
                });
            }
            return request.SchemaName;
        }
        if (report.TargetSchemaNames.Count == 1) return report.TargetSchemaNames[0];
        if (report.TargetSchemaNames.Count == 0) return null;
        throw new ValidationException(new[]
        {
            $"Report '{report.Name}' targets multiple schemas — pass 'schemaName' to pick one.",
        });
    }

    private async Task<object> BuildSingleModelAsync(
        Report report, Schema? schema, ReportRenderRequest request, DateTime from, DateTime to, CancellationToken ct)
    {
        if (!request.SubmissionId.HasValue)
            throw new ValidationException(new[] { "Single-type reports require a 'submissionId'." });

        var submission = await _submissions.GetByIdAsync(request.SubmissionId.Value, ct: ct)
            ?? throw new NotFoundException($"Submission '{request.SubmissionId.Value}'");

        // If the report is scoped to a schema, sanity-check the submission belongs to it. We
        // accept "no schema scoped" (global) too so an admin can write a report that simply
        // dumps any submission as a generic table.
        if (schema is not null && !submission.Samples.Any(s => string.Equals(s.SchemaName, schema.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException(new[]
            {
                $"Submission '{submission.Id}' has no samples for schema '{schema.Name}'.",
            });
        }

        var account = await _accounts.GetByIdAsync(submission.ServiceAccountId, includeDeleted: true, ct);
        var schemaModel = schema is not null ? SchemaToModel(schema) : null;

        // Build a value-name → schema-value lookup so the template can join the sample's value
        // name onto its label/unit/type without doing it manually for every iteration.
        var valuesByName = schema?.Values.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase)
                           ?? new Dictionary<string, SchemaValue>(StringComparer.OrdinalIgnoreCase);

        var sampleModels = submission.Samples
            .Where(sa => schema is null || string.Equals(sa.SchemaName, schema.Name, StringComparison.OrdinalIgnoreCase))
            .Select(sa => new
            {
                schemaName = sa.SchemaName,
                valueName = sa.ValueName,
                label = valuesByName.TryGetValue(sa.ValueName, out var def) ? def.Label : null,
                unit = valuesByName.TryGetValue(sa.ValueName, out var def2) ? def2.Unit : null,
                type = valuesByName.TryGetValue(sa.ValueName, out var def3) ? def3.Type.ToString() : null,
                value = sa.Value,
                timestamp = sa.Timestamp,
                note = sa.Note,
            })
            .ToList();

        return new
        {
            report = ReportToModel(report),
            range = new { from, to },
            schema = schemaModel,
            service = account is null ? null : new
            {
                id = account.Id,
                name = account.Name,
                label = account.Label,
            },
            submission = new
            {
                id = submission.Id,
                serviceAccountId = submission.ServiceAccountId,
                serviceName = submission.ServiceName,
                submittedAt = submission.SubmittedAt,
                replacedAt = submission.ReplacedAt,
                samples = sampleModels,
                createdAt = submission.CreatedAt,
                createdBy = submission.CreatedBy,
                modifiedAt = submission.ModifiedAt,
                modifiedBy = submission.ModifiedBy,
            },
        };
    }

    private async Task<object> BuildAggregateModelAsync(
        Report report, Schema? schema, DateTime from, DateTime to, CancellationToken ct)
    {
        if (schema is null)
            throw new ValidationException(new[] { "Aggregate-type reports require a 'schemaName'." });

        var allSamples = await _samples.GetAllForSchemaAsync(schema.Name, ct);
        // Trim to the requested window using the sample timestamp (sample → service report) so
        // the buckets line up with what the user asked for.
        var windowed = allSamples
            .Where(s => s.Timestamp >= from && s.Timestamp < to)
            .ToList();

        var valuesByName = schema.Values.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        var bucketsByValue = windowed
            .Where(s => valuesByName.ContainsKey(s.ValueName))
            .Select(s => new
            {
                s.ValueName,
                s.PeriodStart,
                s.PeriodEnd,
                NumericValue = s.NumberValue ?? (double?)s.IntegerValue,
                s.StringValue,
                s.DateValue,
                s.BooleanValue,
                s.ServiceAccountId,
                s.ServiceName,
                s.Timestamp,
                s.Note,
            })
            .GroupBy(x => (Name: x.ValueName, x.PeriodStart, x.PeriodEnd))
            .Select(g =>
            {
                var numerics = g.Where(x => x.NumericValue.HasValue).Select(x => x.NumericValue!.Value).ToList();
                return new
                {
                    valueName = g.Key.Name,
                    periodStart = g.Key.PeriodStart,
                    periodEnd = g.Key.PeriodEnd,
                    min = numerics.Count > 0 ? numerics.Min() : (double?)null,
                    max = numerics.Count > 0 ? numerics.Max() : (double?)null,
                    average = numerics.Count > 0 ? numerics.Average() : (double?)null,
                    sum = numerics.Count > 0 ? numerics.Sum() : (double?)null,
                    count = g.Count(),
                    services = g.Select(x => new { id = x.ServiceAccountId, name = x.ServiceName }).Distinct().ToList(),
                };
            })
            .ToLookup(b => b.valueName, StringComparer.OrdinalIgnoreCase);

        var valueModels = schema.Values.Select(v => new
        {
            name = v.Name,
            label = v.Label,
            type = v.Type.ToString(),
            unit = v.Unit,
            cadence = v.Cadence.ToString(),
            description = v.Description,
            required = v.Required,
            buckets = bucketsByValue[v.Name].OrderBy(b => b.periodStart).ToList(),
            // Flat list of all samples for this value over the window — handy for tables that
            // want to render one row per submission (service-level breakdown) instead of the
            // bucketed view.
            samples = windowed
                .Where(s => string.Equals(s.ValueName, v.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Timestamp)
                .Select(s => new
                {
                    serviceAccountId = s.ServiceAccountId,
                    serviceName = s.ServiceName,
                    timestamp = s.Timestamp,
                    periodStart = s.PeriodStart,
                    periodEnd = s.PeriodEnd,
                    value = (object?)s.NumberValue ?? s.IntegerValue ?? (object?)s.StringValue ?? s.DateValue ?? (object?)s.BooleanValue,
                    numeric = s.NumberValue ?? (double?)s.IntegerValue,
                    note = s.Note,
                })
                .ToList(),
        }).ToList();

        // A summary card across services keeps simple "global totals" templates short.
        var services = windowed
            .GroupBy(s => (s.ServiceAccountId, s.ServiceName))
            .Select(g => new { id = g.Key.ServiceAccountId, name = g.Key.ServiceName, sampleCount = g.Count() })
            .OrderBy(x => x.name)
            .ToList();

        return new
        {
            report = ReportToModel(report),
            range = new { from, to },
            schema = SchemaToModel(schema),
            services,
            // Bucket cadence is per-value; expose the schema's "majority" cadence so a template
            // that doesn't care about per-value cadence has a single value to label its axis.
            values = valueModels,
            totals = new
            {
                sampleCount = windowed.Count,
                serviceCount = services.Count,
            },
        };
    }

    private static object ReportToModel(Report r) => new
    {
        id = r.Id,
        name = r.Name,
        label = r.Label,
        description = r.Description,
        type = r.Type.ToString(),
        targetSchemaNames = r.TargetSchemaNames,
    };

    private static object SchemaToModel(Schema s) => new
    {
        id = s.Id,
        name = s.Name,
        label = s.Label,
        description = s.Description,
        version = s.Version,
        values = s.Values.Select(v => new
        {
            name = v.Name,
            label = v.Label,
            description = v.Description,
            type = v.Type.ToString(),
            unit = v.Unit,
            cadence = v.Cadence.ToString(),
            required = v.Required,
            min = v.Min,
            max = v.Max,
            minDate = v.MinDate,
            maxDate = v.MaxDate,
        }).ToList(),
    };

    private static string NameFromFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
        var bare = Path.GetFileNameWithoutExtension(fileName.Trim());
        // Drop any character that isn't safe for a URL segment; collapse runs of replacements.
        bare = NameSlug.Replace(bare, "_").Trim('_');
        return bare;
    }
}
