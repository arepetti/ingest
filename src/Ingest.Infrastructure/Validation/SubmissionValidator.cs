using System.Text.RegularExpressions;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;
using MongoDB.Driver;
using Ingest.Infrastructure.Approvals;
using Ingest.Infrastructure.Mongo;
using Microsoft.Extensions.Options;

namespace Ingest.Infrastructure.Validation;

/// <summary>
/// Default implementation of <see cref="ISubmissionValidator"/>. Runs the full validation
/// pipeline in this order: schema visibility, schema/value enabled-state, EnabledIf/VisibleIf
/// (discards inactive samples with a warning), shape (type + min/max + length + regex),
/// value-level expression rule, cadence (single sample per bucket), modifiability on replacement,
/// the optional Warning expression, schema-level expression rules, and finally required-value
/// presence on create (scoped to the schemas the submission actually carries samples for, and
/// skipping values whose EnabledIf/VisibleIf is false in this context).
/// </summary>
public sealed class SubmissionValidator : ISubmissionValidator
{
    private readonly ISchemaRepository _schemas;
    private readonly MongoContext _ctx;
    private readonly IExpressionEvaluator _evaluator;
    private readonly IAuditContext _audit;
    private readonly bool _approvalEnabled;

    /// <summary>Marker embedded in the "already submitted for this period" cadence error.</summary>
    internal const string DuplicatePeriodMarker = "already submitted for this";

    /// <summary>Marker embedded in the "already has a submission awaiting approval" cadence error.</summary>
    internal const string PendingDuplicateMarker = "already has a submission awaiting approval";

    /// <summary>
    /// True when <paramref name="error"/> is a cadence-duplicate rejection — i.e. a sample that
    /// already has a live (or pending) submission in its reporting window. Callers that want
    /// idempotent behaviour (bulk import) use this to treat such a submission as "already there"
    /// rather than a genuine failure.
    /// </summary>
    internal static bool IsDuplicatePeriodError(string error) =>
        error.Contains(DuplicatePeriodMarker, StringComparison.Ordinal) ||
        error.Contains(PendingDuplicateMarker, StringComparison.Ordinal);

    /// <summary>Create a new <see cref="SubmissionValidator"/>.</summary>
    /// <param name="schemas">Schema repository used to fetch the caller's visible schemas.</param>
    /// <param name="ctx">Mongo context, used directly for the cadence lookup so it doesn't go through the generic repo.</param>
    /// <param name="evaluator">Expression evaluator for the user-provided validation rules.</param>
    /// <param name="audit">Audit context; not used today but injected to make per-rule logging trivial to add.</param>
    /// <param name="approvalOptions">Approval master switch; when on, the cadence check also considers pending (not-yet-approved) submissions so a window can't hold two.</param>
    public SubmissionValidator(
        ISchemaRepository schemas,
        MongoContext ctx,
        IExpressionEvaluator evaluator,
        IAuditContext audit,
        IOptions<ApprovalOptions> approvalOptions)
    {
        _schemas = schemas;
        _ctx = ctx;
        _evaluator = evaluator;
        _audit = audit;
        _approvalEnabled = approvalOptions.Value.Enabled;
    }

    /// <inheritdoc />
    public async Task<SubmissionValidationResult> ValidateAsync(
        Account service,
        Submission submission,
        bool isReplacement,
        Submission? existing,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var discarded = new HashSet<SampleRef>();
        var visible = (await _schemas.ListVisibleToAsync(service.Id, ct))
            .ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        // Build the per-schema value context up-front. EnabledIf/VisibleIf evaluation needs
        // it before any sample is processed (a rule can reference any sibling value), and the
        // schema-level rules need it too. The context only carries values for samples we still
        // intend to persist — we strip discarded entries after evaluating gating rules.
        var samplesBySchema = new Dictionary<string, List<(Sample sample, SchemaValue? def)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in submission.Samples)
        {
            if (!samplesBySchema.TryGetValue(sample.SchemaName, out var list))
                samplesBySchema[sample.SchemaName] = list = new();
            var def = visible.TryGetValue(sample.SchemaName, out var schema)
                ? schema.Values.FirstOrDefault(v => string.Equals(v.Name, sample.ValueName, StringComparison.OrdinalIgnoreCase))
                : null;
            list.Add((sample, def));
        }

        // Pass 1: gating (visibility + EnabledIf/VisibleIf). Populates `discarded` and emits the
        // associated warnings before any other rule looks at the samples.
        foreach (var sample in submission.Samples)
        {
            if (!visible.TryGetValue(sample.SchemaName, out var schema))
            {
                // No schema entity yet — best we can do is echo what the caller sent.
                errors.Add($"Schema '{sample.SchemaName}' is not assigned to this service.");
                continue;
            }
            if (!schema.Enabled)
            {
                errors.Add($"Schema '{Display(schema)}' is currently disabled.");
                continue;
            }

            var value = schema.Values.FirstOrDefault(v =>
                string.Equals(v.Name, sample.ValueName, StringComparison.OrdinalIgnoreCase));
            if (value is null)
            {
                errors.Add($"Value '{sample.ValueName}' is not defined in schema '{Display(schema)}'.");
                continue;
            }
            if (!value.Enabled)
            {
                errors.Add($"Value '{Display(schema, value)}' is currently disabled.");
                continue;
            }

            // EnabledIf / VisibleIf: false-y discards the sample with a warning. We evaluate
            // against the FULL submitted context (before pruning) so rules like
            // "VisibleIf: type == 'A'" see the sibling value 'type' regardless of order.
            var context = BuildRuleContext(schema, samplesBySchema.GetValueOrDefault(schema.Name) ?? new());
            if (IsGatingFalse(value.EnabledIf, "EnabledIf", schema, value, context, warnings, errors))
            {
                discarded.Add(new SampleRef(schema.Name, value.Name));
                continue;
            }
            if (IsGatingFalse(value.VisibleIf, "VisibleIf", schema, value, context, warnings, errors))
            {
                discarded.Add(new SampleRef(schema.Name, value.Name));
                continue;
            }
        }

        // Pass 2: per-sample shape + value rules + cadence + modifiability for surviving samples.
        // We cache the per-schema rule context: it only depends on the (pre-pruning) submitted
        // values for the schema, so it's identical for every sample of the same schema.
        var contextCache = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in submission.Samples)
        {
            if (!visible.TryGetValue(sample.SchemaName, out var schema)) continue;
            var value = schema.Values.FirstOrDefault(v =>
                string.Equals(v.Name, sample.ValueName, StringComparison.OrdinalIgnoreCase));
            if (value is null || !schema.Enabled || !value.Enabled) continue;
            if (discarded.Contains(new SampleRef(schema.Name, value.Name))) continue;

            if (!contextCache.TryGetValue(schema.Name, out var schemaContext))
            {
                schemaContext = BuildRuleContext(schema, samplesBySchema.GetValueOrDefault(schema.Name) ?? new());
                contextCache[schema.Name] = schemaContext;
            }

            ValidateValueShape(schema, value, sample, errors);
            EvaluateValueValidator(service, schema, value, sample, schemaContext, errors);
            await CheckCadenceAsync(service.Id, schema, value, sample, isReplacement, existing, errors, ct);

            var modifiable = schema.Modifiable && value.Modifiable;
            if (isReplacement && !modifiable)
            {
                var existingSample = existing?.Samples.FirstOrDefault(s =>
                    string.Equals(s.SchemaName, schema.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.ValueName, value.Name, StringComparison.OrdinalIgnoreCase));
                if (existingSample is not null && !SameSample(existingSample, sample))
                    errors.Add($"Value '{Display(schema, value)}' is not modifiable; existing sample cannot be changed.");
            }

            // Per-value Warning expression. Runs only on surviving samples that already passed
            // shape/value validation up to this point — there's no point reporting a "warning"
            // alongside an outright rejection.
            EvaluateValueWarning(service, schema, value, sample, schemaContext, warnings);
        }

        // Pass 3: schema-level submission validators. Evaluated against the surviving context so
        // rules don't trip on values their own EnabledIf/VisibleIf already filtered out.
        foreach (var (schemaName, samples) in samplesBySchema)
        {
            if (!visible.TryGetValue(schemaName, out var schema)) continue;
            if (schema.SubmissionValidations.Count == 0) continue;

            var survivors = samples
                .Where(t => t.def is not null && !discarded.Contains(new SampleRef(schemaName, t.def!.Name)))
                .ToList();

            // Same unified shape every other rule sees: each value by name, plus the
            // `[name.minimum]` / `[name.maximum]` bound keys for numeric values.
            var parameters = BuildRuleContext(schema, survivors);

            var customFns = new Dictionary<string, Func<object?[], object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["serviceName"] = _ => service.Name,
                ["schemaName"] = _ => schema.Name,
                ["sampleTimestamp"] = args => LookupSampleField(survivors, args, s => (object?)s.Timestamp),
                ["sampleNote"] = args => LookupSampleField(survivors, args, s => (object?)s.Note),
            };

            foreach (var expr in schema.SubmissionValidations)
            {
                if (string.IsNullOrWhiteSpace(expr)) continue;
                ExpressionValidation outcome;
                try { outcome = _evaluator.EvaluateValidation(expr, parameters, customFns); }
                catch (Exception ex)
                {
                    errors.Add($"Schema '{Display(schema)}' submission validation error: {ex.Message}");
                    continue;
                }

                if (!outcome.IsValid)
                    errors.Add($"Schema '{Display(schema)}' submission validation failed: " +
                               (outcome.ErrorMessage ?? expr));
            }
        }

        // Pass 4: required-value presence. Scoped to the schemas this submission actually
        // touches — a service assigned to multiple schemas wouldn't otherwise be able to file
        // a single-schema submission without the validator flagging every required value of
        // every *other* schema the service is wired up to. A value isn't "missing" if its own
        // EnabledIf/VisibleIf would discard it anyway — that's the whole point of conditional
        // values, so we re-evaluate gating against the surviving context per value.
        if (!isReplacement)
        {
            var presented = submission.Samples
                .Where(s => !discarded.Contains(new SampleRef(s.SchemaName, s.ValueName)))
                .Select(s => (s.SchemaName, s.ValueName))
                .ToHashSet(SchemaValueKeyComparer.Instance);

            var submittedSchemaNames = submission.Samples
                .Select(s => s.SchemaName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var schemaName in submittedSchemaNames)
            {
                if (!visible.TryGetValue(schemaName, out var schema)) continue;
                if (!schema.Enabled) continue;

                // Context for "is this value conditionally hidden?" uses only surviving samples.
                var schemaSamples = samplesBySchema.GetValueOrDefault(schema.Name) ?? new();
                var survivors = schemaSamples
                    .Where(t => t.def is not null && !discarded.Contains(new SampleRef(schema.Name, t.def!.Name)))
                    .ToList();
                var context = BuildRuleContext(schema, survivors);

                foreach (var v in schema.Values)
                {
                    if (!v.Enabled || !v.Required) continue;
                    if (presented.Contains((schema.Name, v.Name))) continue;
                    if (IsConditionFalseSilent(v.EnabledIf, context)) continue;
                    if (IsConditionFalseSilent(v.VisibleIf, context)) continue;
                    errors.Add($"Required value '{Display(schema, v)}' missing.");
                }
            }
        }

        return new SubmissionValidationResult(errors.Count == 0, errors, warnings, discarded);
    }

    /// <summary>
    /// Build the unified parameter dictionary every expression rule (per-value validation,
    /// Warning, EnabledIf/VisibleIf, schema-level <c>SubmissionValidations</c>) is evaluated
    /// against. Each schema value gets a top-level key bound to its submitted sample value
    /// (or <c>null</c> if absent); numeric values additionally contribute bracketed
    /// <c>[name.minimum]</c> / <c>[name.maximum]</c> keys for their configured bounds.
    /// </summary>
    /// <remarks>
    /// The <c>.</c> separator is illegal in <see cref="SchemaValue.Name"/> (enforced at schema
    /// save time) and in NCalc's plain identifier grammar, so the bound namespace cannot
    /// collide with any user-defined value. Schema authors reach a bound by writing
    /// <c>[tonnes_collected.maximum]</c> in their rules; the bare identifier
    /// <c>tonnes_collected</c> refers to the submitted value.
    /// </remarks>
    private static Dictionary<string, object?> BuildRuleContext(
        Schema schema,
        IReadOnlyList<(Sample sample, SchemaValue? def)> samples)
    {
        var byValue = samples
            .Where(t => t.def is not null)
            .GroupBy(t => t.def!.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (object?)g.First().sample.Value, StringComparer.OrdinalIgnoreCase);

        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in schema.Values)
        {
            parameters[v.Name] = byValue.TryGetValue(v.Name, out var x) ? x : null;
            if (v.Type is SchemaValueType.Integer or SchemaValueType.Number)
            {
                if (v.Min is { } m) parameters[$"{v.Name}.minimum"] = m;
                if (v.Max is { } M) parameters[$"{v.Name}.maximum"] = M;
            }
        }
        return parameters;
    }

    private bool IsGatingFalse(
        string? expression,
        string ruleName,
        Schema schema,
        SchemaValue value,
        IReadOnlyDictionary<string, object?> context,
        List<string> warnings,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;

        ExpressionValidation outcome;
        try { outcome = _evaluator.EvaluateValidation(expression, context); }
        catch (Exception ex)
        {
            // A broken gating rule shouldn't silently swallow data: surface as an error.
            errors.Add($"Value '{Display(schema, value)}' {ruleName} evaluation error: {ex.Message}");
            return false;
        }

        // ExpressionValidation: IsValid == false means "false-y" (false or a non-empty string).
        // For gating rules that means "discard". We synthesise a warning so the caller can see
        // why their value didn't make it.
        if (!outcome.IsValid)
        {
            warnings.Add(outcome.ErrorMessage is { Length: > 0 } msg
                ? $"Sample '{Display(schema, value)}' discarded: {msg}"
                : $"Sample '{Display(schema, value)}' discarded by {ruleName}.");
            return true;
        }
        return false;
    }

    private bool IsConditionFalseSilent(string? expression, IReadOnlyDictionary<string, object?> context)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;

        try
        {
            // The unified context already carries every value (null when not submitted). No
            // current-value alias is injected here — rules reference values by name.
            var outcome = _evaluator.EvaluateValidation(expression, context);
            return !outcome.IsValid;
        }
        catch
        {
            // A broken rule shouldn't make required values disappear from the error list.
            return false;
        }
    }

    private static object? LookupSampleField(
        IReadOnlyList<(Sample sample, SchemaValue? def)> samples,
        object?[] args,
        Func<Sample, object?> getter)
    {
        if (args.Length == 0 || args[0] is not string valueName) return null;
        var match = samples.FirstOrDefault(t =>
            t.def is not null &&
            string.Equals(t.def.Name, valueName, StringComparison.OrdinalIgnoreCase));
        return match.sample is null ? null : getter(match.sample);
    }

    private static bool SameSample(Sample a, Sample b) =>
        Equals(a.Value, b.Value) && a.Timestamp == b.Timestamp && a.Note == b.Note;

    private static void ValidateValueShape(Schema schema, SchemaValue def, Sample sample, List<string> errors)
    {
        var key = Display(schema, def);

        if (sample.Value is null)
        {
            if (def.Required) errors.Add($"Value '{key}' requires a value.");
            return;
        }

        switch (def.Type)
        {
            case SchemaValueType.String:
                if (sample.Value is not string str) { errors.Add($"Value '{key}' expects string."); return; }
                if (def.MinLength is { } min && str.Length < min) errors.Add($"Value '{key}' shorter than {min}.");
                if (def.MaxLength is { } max && str.Length > max) errors.Add($"Value '{key}' longer than {max}.");
                if (!string.IsNullOrWhiteSpace(def.RegexPattern) &&
                    !Regex.IsMatch(str, def.RegexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                    errors.Add($"Value '{key}' does not match regex.");
                break;

            case SchemaValueType.Integer:
                if (!TryToLong(sample.Value, out var l)) { errors.Add($"Value '{key}' expects integer."); return; }
                if (def.Min is { } imin && l < imin) errors.Add($"Value '{key}' below min ({imin}).");
                if (def.Max is { } imax && l > imax) errors.Add($"Value '{key}' above max ({imax}).");
                break;

            case SchemaValueType.Number:
                if (!TryToDouble(sample.Value, out var d)) { errors.Add($"Value '{key}' expects number."); return; }
                if (def.Min is { } nmin && d < nmin) errors.Add($"Value '{key}' below min ({nmin}).");
                if (def.Max is { } nmax && d > nmax) errors.Add($"Value '{key}' above max ({nmax}).");
                break;

            case SchemaValueType.Date:
                if (!TryToDate(sample.Value, out var dt)) { errors.Add($"Value '{key}' expects date."); return; }
                if (def.MinDate is { } dmin && dt < dmin) errors.Add($"Value '{key}' before {dmin:o}.");
                if (def.MaxDate is { } dmax && dt > dmax) errors.Add($"Value '{key}' after {dmax:o}.");
                break;

            case SchemaValueType.Boolean:
                if (sample.Value is not bool) errors.Add($"Value '{key}' expects boolean.");
                break;
        }
    }

    private void EvaluateValueValidator(
        Account service,
        Schema schema,
        SchemaValue def,
        Sample sample,
        IReadOnlyDictionary<string, object?> schemaContext,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(def.ValueValidation)) return;
        var key = Display(schema, def);

        var customFns = BuildValueLevelFunctions(service, schema, def, sample);

        ExpressionValidation outcome;
        try { outcome = _evaluator.EvaluateValidation(def.ValueValidation, schemaContext, customFns); }
        catch (Exception ex)
        {
            errors.Add($"Value '{key}' value-validation error: {ex.Message}");
            return;
        }

        if (!outcome.IsValid)
            errors.Add($"Value '{key}' value-validation failed: " + (outcome.ErrorMessage ?? "expression returned false"));
    }

    private void EvaluateValueWarning(
        Account service,
        Schema schema,
        SchemaValue def,
        Sample sample,
        IReadOnlyDictionary<string, object?> schemaContext,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(def.Warning)) return;
        var key = Display(schema, def);

        var customFns = BuildValueLevelFunctions(service, schema, def, sample);

        // The Warning rule fires on truthy / non-empty results — the inverse of validation
        // semantics — so we read the raw value rather than going through ExpressionValidation
        // (which would conflate "true" with "empty string"/"null").
        object? raw;
        try { raw = _evaluator.Evaluate(def.Warning, schemaContext, customFns); }
        catch (Exception ex)
        {
            warnings.Add($"Value '{key}' warning rule evaluation error: {ex.Message}");
            return;
        }

        switch (raw)
        {
            case null: return;
            case bool b when !b: return;
            case bool: warnings.Add($"Sample '{key}': warning rule triggered."); return;
            case string s when string.IsNullOrWhiteSpace(s): return;
            case string s: warnings.Add($"Sample '{key}': {s}"); return;
            default: return; // numbers / dates aren't meaningful here; quietly ignore
        }
    }

    private static Dictionary<string, Func<object?[], object?>> BuildValueLevelFunctions(
        Account service, Schema schema, SchemaValue def, Sample sample) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["serviceName"] = _ => service.Name,
            ["schemaName"] = _ => schema.Name,
            ["valueName"] = _ => def.Name,
            ["sampleTimestamp"] = _ => sample.Timestamp,
            ["sampleNote"] = _ => sample.Note,
        };

    private async Task CheckCadenceAsync(
        Guid serviceId,
        Schema schema,
        SchemaValue def,
        Sample sample,
        bool isReplacement,
        Submission? existing,
        List<string> errors,
        CancellationToken ct)
    {
        var (start, end) = CadenceCalculator.BucketFor(def.Cadence, sample.Timestamp);

        var filter = Builders<SampleProjection>.Filter.And(
            Builders<SampleProjection>.Filter.Eq(s => s.IsDeleted, false),
            Builders<SampleProjection>.Filter.Eq(s => s.ServiceAccountId, serviceId),
            Builders<SampleProjection>.Filter.Eq(s => s.SchemaName, schema.Name),
            Builders<SampleProjection>.Filter.Eq(s => s.ValueName, def.Name),
            Builders<SampleProjection>.Filter.Gte(s => s.Timestamp, start),
            Builders<SampleProjection>.Filter.Lt(s => s.Timestamp, end));

        if (isReplacement && existing is not null)
            filter = Builders<SampleProjection>.Filter.And(filter,
                Builders<SampleProjection>.Filter.Ne(s => s.SubmissionId, existing.Id));

        var existsInPeriod = await _ctx.Samples.Find(filter).AnyAsync(ct);
        if (existsInPeriod)
        {
            errors.Add($"Value '{Display(schema, def)}' {DuplicatePeriodMarker} {def.Cadence.ToString().ToLowerInvariant()} period.");
            return;
        }

        // Live (approved / not-required) submissions surface above through their projection. Pending
        // submissions have no projection yet, so when approval is enabled we additionally guard the
        // window against a second pending submission — a re-send must replace the existing one
        // (which resets its approval) rather than create a duplicate.
        if (_approvalEnabled)
        {
            var sampleInWindow = Builders<Sample>.Filter.And(
                Builders<Sample>.Filter.Eq(x => x.SchemaName, schema.Name),
                Builders<Sample>.Filter.Eq(x => x.ValueName, def.Name),
                Builders<Sample>.Filter.Gte(x => x.Timestamp, start),
                Builders<Sample>.Filter.Lt(x => x.Timestamp, end));
            var pendingFilter = Builders<Submission>.Filter.And(
                Builders<Submission>.Filter.Eq(s => s.IsDeleted, false),
                Builders<Submission>.Filter.Eq(s => s.ServiceAccountId, serviceId),
                Builders<Submission>.Filter.Eq(s => s.ApprovalStatus, ApprovalStatus.Pending),
                Builders<Submission>.Filter.ElemMatch(s => s.Samples, sampleInWindow));
            if (isReplacement && existing is not null)
                pendingFilter = Builders<Submission>.Filter.And(pendingFilter,
                    Builders<Submission>.Filter.Ne(s => s.Id, existing.Id));

            if (await _ctx.Submissions.Find(pendingFilter).AnyAsync(ct))
                errors.Add($"Value '{Display(schema, def)}' {PendingDuplicateMarker} for this {def.Cadence.ToString().ToLowerInvariant()} period; replace that submission instead.");
        }
    }

    /// <summary>Human-friendly schema name: label when set, machine name as fallback.</summary>
    private static string Display(Schema schema) =>
        string.IsNullOrWhiteSpace(schema.Label) ? schema.Name : schema.Label;

    /// <summary>Human-friendly value label.</summary>
    private static string Display(SchemaValue value) =>
        string.IsNullOrWhiteSpace(value.Label) ? value.Name : value.Label;

    /// <summary>
    /// Compact "schema / value" reference for error messages. Both halves fall back to their
    /// machine name when no label is set so the user always sees *something* identifiable.
    /// </summary>
    private static string Display(Schema schema, SchemaValue value) =>
        $"{Display(schema)} / {Display(value)}";

    private static bool TryToLong(object value, out long result)
    {
        switch (value)
        {
            case long l: result = l; return true;
            case int i: result = i; return true;
            case double d when Math.Floor(d) == d: result = (long)d; return true;
            case decimal m when Math.Floor(m) == m: result = (long)m; return true;
            case string s when long.TryParse(s, out var p): result = p; return true;
            default: result = 0; return false;
        }
    }

    private static bool TryToDouble(object value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case decimal m: result = (double)m; return true;
            case string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p): result = p; return true;
            default: result = 0; return false;
        }
    }

    private static bool TryToDate(object value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dt: result = DateTime.SpecifyKind(dt, DateTimeKind.Utc); return true;
            case DateTimeOffset dto: result = dto.UtcDateTime; return true;
            case string s when DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var p): result = p; return true;
            default: result = default; return false;
        }
    }

    private sealed class SchemaValueKeyComparer : IEqualityComparer<(string Schema, string Value)>
    {
        public static readonly SchemaValueKeyComparer Instance = new();

        public bool Equals((string Schema, string Value) x, (string Schema, string Value) y) =>
            string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Value, y.Value, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Schema, string Value) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Value));
    }
}
