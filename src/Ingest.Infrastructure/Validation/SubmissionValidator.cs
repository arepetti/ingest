using System.Text.RegularExpressions;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;
using MongoDB.Driver;
using Ingest.Infrastructure.Approvals;
using Ingest.Infrastructure.Mongo;
using Ingest.Infrastructure.Services;
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
    private readonly IAppConfigurationService _appConfig;
    private readonly bool _approvalEnabled;

    /// <summary>
    /// True when <paramref name="error"/> is a cadence-duplicate rejection — i.e. a sample that
    /// already has a live (or pending) submission in its reporting window. Callers that want
    /// idempotent behaviour (bulk import) use this to treat such a submission as "already there"
    /// rather than a genuine failure.
    /// </summary>
    internal static bool IsDuplicatePeriodError(Diagnostic error) =>
        error.Code is DiagnosticCodes.Submissions.DuplicatePeriod or
            DiagnosticCodes.Submissions.PendingDuplicatePeriod;

    /// <summary>Create a new <see cref="SubmissionValidator"/>.</summary>
    /// <param name="schemas">Schema repository used to fetch the caller's visible schemas.</param>
    /// <param name="ctx">Mongo context, used directly for the cadence lookup so it doesn't go through the generic repo.</param>
    /// <param name="evaluator">Expression evaluator for the user-provided validation rules.</param>
    /// <param name="audit">Audit context; not used today but injected to make per-rule logging trivial to add.</param>
    /// <param name="appConfig">Application configuration provider; supplies the cadence anchors used to bucket the duplicate-period and history checks.</param>
    /// <param name="approvalOptions">Approval master switch; when on, the cadence check also considers pending (not-yet-approved) submissions so a window can't hold two.</param>
    public SubmissionValidator(
        ISchemaRepository schemas,
        MongoContext ctx,
        IExpressionEvaluator evaluator,
        IAuditContext audit,
        IAppConfigurationService appConfig,
        IOptions<ApprovalOptions> approvalOptions)
    {
        _schemas = schemas;
        _ctx = ctx;
        _evaluator = evaluator;
        _audit = audit;
        _appConfig = appConfig;
        _approvalEnabled = approvalOptions.Value.Enabled;
    }

    /// <inheritdoc />
    public async Task<SubmissionValidationResult> ValidateAsync(
        Account service,
        Submission submission,
        bool isReplacement,
        Submission? existing,
        bool draft = false,
        SubmissionValidationOptions? options = null,
        CancellationToken ct = default)
    {
        var skipCadence = options?.SkipCadence == true;
        var errors = new List<Diagnostic>();
        var warnings = new List<SubmissionWarning>();
        var discarded = new HashSet<SampleRef>();
        var visible = (await _schemas.ListVisibleToAsync(service.Id, ct))
            .ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        // Fetched once per validation run and threaded into every cadence bucket lookup below
        // (duplicate-period check + latest()/previous() history) so they all agree on the same
        // configured alignment.
        var anchors = await _appConfig.GetCadenceAnchorsAsync(ct);

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

        // History lookup (latest()/previous()) is resolved lazily per schema and cached: the
        // queries are skipped entirely for schemas whose rules never reference the functions, and
        // run at most once per schema otherwise. NCalc custom functions are synchronous, so the
        // values have to be fetched up-front and handed to the evaluator as a ready map.
        var historyCache = new Dictionary<string, SchemaHistory>(StringComparer.OrdinalIgnoreCase);
        async Task<SchemaHistory> HistoryFor(Schema schema)
        {
            if (historyCache.TryGetValue(schema.Name, out var cached)) return cached;
            var history = SchemaUsesHistory(schema)
                ? await BuildSchemaHistoryAsync(
                    service.Id, schema, samplesBySchema.GetValueOrDefault(schema.Name) ?? new(), existing, anchors, ct)
                : SchemaHistory.Empty;
            historyCache[schema.Name] = history;
            return history;
        }

        // Pass 1: gating (visibility + EnabledIf/VisibleIf). Populates `discarded` and emits the
        // associated warnings before any other rule looks at the samples.
        foreach (var sample in submission.Samples)
        {
            if (!visible.TryGetValue(sample.SchemaName, out var schema))
            {
                // No schema entity yet — best we can do is echo what the caller sent.
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Submissions.SchemaNotAssigned,
                    $"Schema '{sample.SchemaName}' is not assigned to this service.",
                    ("schemaName", sample.SchemaName),
                    ("serviceId", service.Id)));
                continue;
            }
            if (!schema.Enabled)
            {
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Submissions.SchemaDisabled,
                    $"Schema '{Display(schema)}' is currently disabled.",
                    ("schemaName", schema.Name),
                    ("schemaLabel", schema.Label)));
                continue;
            }

            var value = schema.Values.FirstOrDefault(v =>
                string.Equals(v.Name, sample.ValueName, StringComparison.OrdinalIgnoreCase));
            if (value is null)
            {
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Submissions.ValueNotDefined,
                    $"Value '{sample.ValueName}' is not defined in schema '{Display(schema)}'.",
                    ("schemaName", schema.Name),
                    ("schemaLabel", schema.Label),
                    ("valueName", sample.ValueName)));
                continue;
            }
            if (value.IsCalculated)
            {
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Submissions.CalculatedValueSubmitted,
                    $"Value '{sample.ValueName}' is calculated and cannot be submitted.",
                    ("schemaName", schema.Name),
                    ("valueName", value.Name)));
                continue;
            }
            if (!value.Enabled)
            {
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Submissions.ValueDisabled,
                    $"Value '{Display(schema, value)}' is currently disabled.",
                    ("schemaName", schema.Name),
                    ("valueName", value.Name),
                    ("displayName", Display(schema, value))));
                continue;
            }

            // Draft mode keeps only the structural checks above (schema/value existence +
            // enabled-state); conditional-display gating is part of the relaxed pipeline that
            // runs on publish, so a half-filled draft never has its samples silently discarded.
            if (draft) continue;

            // EnabledIf / VisibleIf: false-y discards the sample with a warning. We evaluate
            // against the FULL submitted context (before pruning) so rules like
            // "VisibleIf: type == 'A'" see the sibling value 'type' regardless of order.
            var context = BuildRuleContext(schema, samplesBySchema.GetValueOrDefault(schema.Name) ?? new());
            var gatingHistory = await HistoryFor(schema);
            var gatingFns = BuildHistoryFunctions(gatingHistory.Latest, gatingHistory.Previous, value.Name);
            if (IsGatingFalse(value.EnabledIf, "EnabledIf", schema, value, context, gatingFns, warnings, errors))
            {
                discarded.Add(new SampleRef(schema.Name, value.Name));
                continue;
            }
            if (IsGatingFalse(value.VisibleIf, "VisibleIf", schema, value, context, gatingFns, warnings, errors))
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

            // The shape check (type + min/max + length + regex) runs in every mode: a draft must
            // still reject malformed or out-of-range *present* values so corrupt data can't be
            // parked in a draft and later slip through.
            ValidateValueShape(schema, value, sample, errors);

            // Everything below is a relaxed-in-draft rule (expression rules, cadence, modifiability,
            // warnings). They all re-run on publish through the full pipeline.
            if (draft) continue;

            if (!contextCache.TryGetValue(schema.Name, out var schemaContext))
            {
                schemaContext = BuildRuleContext(schema, samplesBySchema.GetValueOrDefault(schema.Name) ?? new());
                contextCache[schema.Name] = schemaContext;
            }
            var history = await HistoryFor(schema);

            EvaluateValueValidator(service, schema, value, sample, schemaContext, history, errors);
            // Cadence is a context-dependent check (depends on this service's live/pending history in
            // the sample's window); validate-only callers can opt out of it (e.g. CI shape checks).
            if (!skipCadence)
                await CheckCadenceAsync(service.Id, schema, value, sample, isReplacement, existing, anchors, errors, ct);

            var modifiable = schema.Modifiable && value.Modifiable;
            if (isReplacement && !modifiable)
            {
                var existingSample = existing?.Samples.FirstOrDefault(s =>
                    string.Equals(s.SchemaName, schema.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.ValueName, value.Name, StringComparison.OrdinalIgnoreCase));
                if (existingSample is not null && !SameSample(existingSample, sample))
                    errors.Add(Diagnostic.Create(
                        DiagnosticCodes.Submissions.ValueNotModifiable,
                        $"Value '{Display(schema, value)}' is not modifiable; existing sample cannot be changed.",
                        ("schemaName", schema.Name),
                        ("valueName", value.Name),
                        ("displayName", Display(schema, value))));
            }

            // Per-value Warning expression. Runs only on surviving samples that already passed
            // shape/value validation up to this point — there's no point reporting a "warning"
            // alongside an outright rejection.
            EvaluateValueWarning(service, schema, value, sample, schemaContext, history, warnings);
        }

        // Pass 3: schema-level submission validators. Evaluated against the surviving context so
        // rules don't trip on values their own EnabledIf/VisibleIf already filtered out. Skipped in
        // draft mode — a cross-value rule can't be satisfied while the set is still being filled in.
        foreach (var (schemaName, samples) in samplesBySchema)
        {
            if (draft) break;
            if (!visible.TryGetValue(schemaName, out var schema)) continue;
            if (schema.SubmissionValidations.Count == 0) continue;

            var survivors = samples
                .Where(t => t.def is not null && !discarded.Contains(new SampleRef(schemaName, t.def!.Name)))
                .ToList();

            // Same unified shape every other rule sees: each value by name, plus the
            // `[name.minimum]` / `[name.maximum]` bound keys for numeric values.
            var parameters = BuildRuleContext(schema, survivors);
            var history = await HistoryFor(schema);

            var customFns = new Dictionary<string, Func<object?[], object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["serviceName"] = _ => service.Name,
                ["schemaName"] = _ => schema.Name,
                ["sampleTimestamp"] = args => LookupSampleField(survivors, args, s => (object?)s.Timestamp),
                ["sampleNote"] = args => LookupSampleField(survivors, args, s => (object?)s.Note),
            };
            // Schema-level rules have no single "current value", so latest()/previous() require an
            // explicit value name (no-arg falls back to the supplied default / null).
            foreach (var (k, v) in BuildHistoryFunctions(history.Latest, history.Previous, currentValueName: null))
                customFns[k] = v;

            foreach (var expr in schema.SubmissionValidations)
            {
                if (string.IsNullOrWhiteSpace(expr)) continue;
                ExpressionValidation outcome;
                try { outcome = _evaluator.EvaluateValidation(expr, parameters, customFns); }
                catch (Exception ex)
                {
                    errors.Add(Diagnostic.Create(
                        DiagnosticCodes.Submissions.SchemaValidationError,
                        $"Schema '{Display(schema)}' submission validation error: {ex.Message}",
                        ("schemaName", schema.Name),
                        ("schemaLabel", schema.Label),
                        ("detail", ex.Message)));
                    continue;
                }

                if (!outcome.IsValid)
                {
                    var detail = outcome.ErrorMessage ?? expr;
                    errors.Add(Diagnostic.Create(
                        DiagnosticCodes.Submissions.SchemaValidationFailed,
                        $"Schema '{Display(schema)}' submission validation failed: {detail}",
                        ("schemaName", schema.Name),
                        ("schemaLabel", schema.Label),
                        ("detail", detail)));
                }
            }
        }

        // Pass 4: required-value presence. Scoped to the schemas this submission actually
        // touches — a service assigned to multiple schemas wouldn't otherwise be able to file
        // a single-schema submission without the validator flagging every required value of
        // every *other* schema the service is wired up to. A value isn't "missing" if its own
        // EnabledIf/VisibleIf would discard it anyway — that's the whole point of conditional
        // values, so we re-evaluate gating against the surviving context per value. Skipped in
        // draft mode: the whole point of a draft is to save a partially-filled submission.
        if (!isReplacement && !draft)
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
                var history = await HistoryFor(schema);

                foreach (var v in schema.Values)
                {
                    if (!v.Enabled || !v.Required || v.IsCalculated) continue;
                    if (presented.Contains((schema.Name, v.Name))) continue;
                    var fns = BuildHistoryFunctions(history.Latest, history.Previous, v.Name);
                    if (IsConditionFalseSilent(v.EnabledIf, context, fns)) continue;
                    if (IsConditionFalseSilent(v.VisibleIf, context, fns)) continue;
                    errors.Add(Diagnostic.Create(
                        DiagnosticCodes.Submissions.RequiredValueMissing,
                        $"Required value '{Display(schema, v)}' missing.",
                        ("schemaName", schema.Name),
                        ("valueName", v.Name),
                        ("displayName", Display(schema, v))));
                }
            }
        }

        return new SubmissionValidationResult(
            errors.Count == 0,
            errors.Select(x => x.Message).ToList(),
            warnings,
            discarded)
        {
            ErrorDetails = errors,
        };
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
    private Dictionary<string, object?> BuildRuleContext(
        Schema schema,
        IReadOnlyList<(Sample sample, SchemaValue? def)> samples)
    {
        var byValue = samples
            .Where(t => t.def is not null && !t.def.IsCalculated)
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

        DerivedValueCalculator.ComputeInto(schema, parameters, _evaluator);
        return parameters;
    }

    private bool IsGatingFalse(
        string? expression,
        string ruleName,
        Schema schema,
        SchemaValue value,
        IReadOnlyDictionary<string, object?> context,
        IReadOnlyDictionary<string, Func<object?[], object?>> customFns,
        List<SubmissionWarning> warnings,
        List<Diagnostic> errors)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;

        ExpressionValidation outcome;
        try { outcome = _evaluator.EvaluateValidation(expression, context, customFns); }
        catch (Exception ex)
        {
            // A broken gating rule shouldn't silently swallow data: surface as an error.
            errors.Add(Diagnostic.Create(
                DiagnosticCodes.Submissions.GatingEvaluationError,
                $"Value '{Display(schema, value)}' {ruleName} evaluation error: {ex.Message}",
                ("schemaName", schema.Name),
                ("valueName", value.Name),
                ("rule", ruleName),
                ("detail", ex.Message)));
            return false;
        }

        // ExpressionValidation: IsValid == false means "false-y" (false or a non-empty string).
        // For gating rules that means "discard". We synthesise a warning so the caller can see
        // why their value didn't make it.
        if (!outcome.IsValid)
        {
            var detail = outcome.ErrorMessage is { Length: > 0 } msg ? msg : null;
            var message = detail is not null
                ? $"Sample '{Display(schema, value)}' discarded: {detail}"
                : $"Sample '{Display(schema, value)}' discarded by {ruleName}.";
            var code = detail is not null
                ? DiagnosticCodes.Submissions.SampleDiscardedWithMessage
                : DiagnosticCodes.Submissions.SampleDiscardedByRule;
            warnings.Add(new SubmissionWarning(
                value.Name,
                message,
                code,
                Diagnostic.Create(
                    code,
                    message,
                    ("schemaName", schema.Name),
                    ("valueName", value.Name),
                    ("rule", ruleName),
                    ("detail", detail)).Params));
            return true;
        }
        return false;
    }

    private bool IsConditionFalseSilent(
        string? expression,
        IReadOnlyDictionary<string, object?> context,
        IReadOnlyDictionary<string, Func<object?[], object?>> customFns)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;

        try
        {
            // The unified context already carries every value (null when not submitted). No
            // current-value alias is injected here — rules reference values by name.
            var outcome = _evaluator.EvaluateValidation(expression, context, customFns);
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

    private static void ValidateValueShape(Schema schema, SchemaValue def, Sample sample, List<Diagnostic> errors)
    {
        var key = Display(schema, def);
        Diagnostic Shape(string code, string message, params (string Name, object? Value)[] parameters) =>
            Diagnostic.Create(
                code,
                message,
                new[]
                {
                    ("schemaName", (object?)schema.Name),
                    ("valueName", def.Name),
                    ("displayName", key),
                }.Concat(parameters).ToArray());

        if (sample.Value is null)
        {
            if (def.Required)
                errors.Add(Shape(
                    DiagnosticCodes.Submissions.ValueRequired,
                    $"Value '{key}' requires a value."));
            return;
        }

        switch (def.Type)
        {
            case SchemaValueType.String:
                if (sample.Value is not string str)
                {
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueType,
                        $"Value '{key}' expects string.",
                        ("expectedType", "string")));
                    return;
                }
                if (def.MinLength is { } min && str.Length < min)
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueMinimumLength,
                        $"Value '{key}' shorter than {min}.",
                        ("minimum", min),
                        ("actual", str.Length)));
                if (def.MaxLength is { } max && str.Length > max)
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueMaximumLength,
                        $"Value '{key}' longer than {max}.",
                        ("maximum", max),
                        ("actual", str.Length)));
                if (!string.IsNullOrWhiteSpace(def.RegexPattern) &&
                    !Regex.IsMatch(str, def.RegexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueRegex,
                        $"Value '{key}' does not match regex.",
                        ("pattern", def.RegexPattern)));
                break;

            case SchemaValueType.Integer:
                if (!TryToLong(sample.Value, out var l))
                {
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueType,
                        $"Value '{key}' expects integer.",
                        ("expectedType", "integer")));
                    return;
                }
                if (def.Min is { } imin && l < imin)
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueMinimum,
                        $"Value '{key}' below min ({imin}).",
                        ("minimum", imin),
                        ("actual", l)));
                if (def.Max is { } imax && l > imax)
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueMaximum,
                        $"Value '{key}' above max ({imax}).",
                        ("maximum", imax),
                        ("actual", l)));
                break;

            case SchemaValueType.Number:
                if (!TryToDouble(sample.Value, out var d))
                {
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueType,
                        $"Value '{key}' expects number.",
                        ("expectedType", "number")));
                    return;
                }
                if (def.Min is { } nmin && d < nmin)
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueMinimum,
                        $"Value '{key}' below min ({nmin}).",
                        ("minimum", nmin),
                        ("actual", d)));
                if (def.Max is { } nmax && d > nmax)
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueMaximum,
                        $"Value '{key}' above max ({nmax}).",
                        ("maximum", nmax),
                        ("actual", d)));
                break;

            case SchemaValueType.Date:
                if (!TryToDate(sample.Value, out var dt))
                {
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueType,
                        $"Value '{key}' expects date.",
                        ("expectedType", "date")));
                    return;
                }
                if (def.MinDate is { } dmin && dt < dmin)
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueBeforeMinimumDate,
                        $"Value '{key}' before {dmin:o}.",
                        ("minimum", dmin),
                        ("actual", dt)));
                if (def.MaxDate is { } dmax && dt > dmax)
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueAfterMaximumDate,
                        $"Value '{key}' after {dmax:o}.",
                        ("maximum", dmax),
                        ("actual", dt)));
                break;

            case SchemaValueType.Boolean:
                if (sample.Value is not bool)
                    errors.Add(Shape(
                        DiagnosticCodes.Submissions.ValueType,
                        $"Value '{key}' expects boolean.",
                        ("expectedType", "boolean")));
                break;
        }
    }

    private void EvaluateValueValidator(
        Account service,
        Schema schema,
        SchemaValue def,
        Sample sample,
        IReadOnlyDictionary<string, object?> schemaContext,
        SchemaHistory history,
        List<Diagnostic> errors)
    {
        if (string.IsNullOrWhiteSpace(def.ValueValidation)) return;
        var key = Display(schema, def);

        var customFns = BuildValueLevelFunctions(service, schema, def, sample, history);

        ExpressionValidation outcome;
        try { outcome = _evaluator.EvaluateValidation(def.ValueValidation, schemaContext, customFns); }
        catch (Exception ex)
        {
            errors.Add(Diagnostic.Create(
                DiagnosticCodes.Submissions.ValueValidationError,
                $"Value '{key}' value-validation error: {ex.Message}",
                ("schemaName", schema.Name),
                ("valueName", def.Name),
                ("displayName", key),
                ("detail", ex.Message)));
            return;
        }

        if (!outcome.IsValid)
        {
            var detail = outcome.ErrorMessage ?? "expression returned false";
            errors.Add(Diagnostic.Create(
                DiagnosticCodes.Submissions.ValueValidationFailed,
                $"Value '{key}' value-validation failed: {detail}",
                ("schemaName", schema.Name),
                ("valueName", def.Name),
                ("displayName", key),
                ("detail", detail)));
        }
    }

    private void EvaluateValueWarning(
        Account service,
        Schema schema,
        SchemaValue def,
        Sample sample,
        IReadOnlyDictionary<string, object?> schemaContext,
        SchemaHistory history,
        List<SubmissionWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(def.Warning)) return;
        var key = Display(schema, def);

        var customFns = BuildValueLevelFunctions(service, schema, def, sample, history);

        // The Warning rule fires on truthy / non-empty results — the inverse of validation
        // semantics — so we read the raw value rather than going through ExpressionValidation
        // (which would conflate "true" with "empty string"/"null").
        object? raw;
        try { raw = _evaluator.Evaluate(def.Warning, schemaContext, customFns); }
        catch (Exception ex)
        {
            var message = $"Value '{key}' warning rule evaluation error: {ex.Message}";
            warnings.Add(new SubmissionWarning(
                def.Name,
                message,
                DiagnosticCodes.Submissions.WarningRuleError,
                Diagnostic.Create(
                    DiagnosticCodes.Submissions.WarningRuleError,
                    message,
                    ("schemaName", schema.Name),
                    ("valueName", def.Name),
                    ("displayName", key),
                    ("detail", ex.Message)).Params));
            return;
        }

        switch (raw)
        {
            case null: return;
            case bool b when !b: return;
            case bool:
            {
                var message = $"Sample '{key}': warning rule triggered.";
                warnings.Add(new SubmissionWarning(
                    def.Name,
                    message,
                    DiagnosticCodes.Submissions.WarningRuleTriggered,
                    Diagnostic.Create(
                        DiagnosticCodes.Submissions.WarningRuleTriggered,
                        message,
                        ("schemaName", schema.Name),
                        ("valueName", def.Name),
                        ("displayName", key)).Params));
                return;
            }
            case string s when string.IsNullOrWhiteSpace(s): return;
            case string s:
            {
                var message = $"Sample '{key}': {s}";
                warnings.Add(new SubmissionWarning(
                    def.Name,
                    message,
                    DiagnosticCodes.Submissions.WarningRuleMessage,
                    Diagnostic.Create(
                        DiagnosticCodes.Submissions.WarningRuleMessage,
                        message,
                        ("schemaName", schema.Name),
                        ("valueName", def.Name),
                        ("displayName", key),
                        ("detail", s)).Params));
                return;
            }
            default: return; // numbers / dates aren't meaningful here; quietly ignore
        }
    }

    private static Dictionary<string, Func<object?[], object?>> BuildValueLevelFunctions(
        Account service, Schema schema, SchemaValue def, Sample sample, SchemaHistory history)
    {
        var fns = new Dictionary<string, Func<object?[], object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["serviceName"] = _ => service.Name,
            ["schemaName"] = _ => schema.Name,
            ["valueName"] = _ => def.Name,
            ["sampleTimestamp"] = _ => sample.Timestamp,
            ["sampleNote"] = _ => sample.Note,
        };
        // latest()/previous() with no argument default to the current value being validated.
        foreach (var (k, v) in BuildHistoryFunctions(history.Latest, history.Previous, def.Name))
            fns[k] = v;
        return fns;
    }

    /// <summary>
    /// Build the <c>latest(name [, fallback])</c> / <c>previous(name [, fallback])</c> functions
    /// over a pair of pre-fetched value-name to last-live-value maps. <paramref name="currentValueName"/>
    /// is the value a no-argument call resolves to (the value-level rule's own value; <c>null</c>
    /// for schema-level rules, where a name argument is required).
    /// </summary>
    internal static Dictionary<string, Func<object?[], object?>> BuildHistoryFunctions(
        IReadOnlyDictionary<string, object?> latest,
        IReadOnlyDictionary<string, object?> previous,
        string? currentValueName) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["latest"] = args => LookupHistory(latest, args, currentValueName),
            ["previous"] = args => LookupHistory(previous, args, currentValueName),
        };

    /// <summary>
    /// Resolve a history lookup. The first string argument is the value name; a non-string first
    /// argument (or no argument) falls back to <paramref name="currentValueName"/> and treats that
    /// first argument as the fallback default. Returns the stored value when present and non-null,
    /// otherwise the supplied fallback (default <c>null</c>).
    /// </summary>
    private static object? LookupHistory(
        IReadOnlyDictionary<string, object?> map, object?[] args, string? currentValueName)
    {
        string? name;
        object? fallback;
        if (args.Length > 0 && args[0] is string s)
        {
            name = s;
            fallback = args.Length > 1 ? args[1] : null;
        }
        else
        {
            name = currentValueName;
            fallback = args.Length > 0 ? args[0] : null;
        }

        if (name is not null && map.TryGetValue(name, out var value) && value is not null)
            return value;
        return fallback;
    }

    /// <summary>
    /// True when any rule on the schema references <c>latest(</c> or <c>previous(</c>. Used to skip
    /// the history queries entirely for the (common) case where no rule needs them.
    /// </summary>
    private static bool SchemaUsesHistory(Schema schema)
    {
        static bool Uses(string? rule) =>
            !string.IsNullOrEmpty(rule) &&
            (rule.Contains("latest(", StringComparison.OrdinalIgnoreCase) ||
             rule.Contains("previous(", StringComparison.OrdinalIgnoreCase));

        foreach (var v in schema.Values)
            if (Uses(v.ValueValidation) || Uses(v.Warning) || Uses(v.EnabledIf) || Uses(v.VisibleIf))
                return true;
        return schema.SubmissionValidations.Any(Uses);
    }

    /// <summary>
    /// Pre-fetch the last live (approved / not-required, i.e. projected) value of every schema value
    /// for this service: <c>latest</c> is the most recent across all periods, <c>previous</c> the
    /// value in the cadence bucket immediately before the one this submission targets. On a
    /// replacement the submission being edited is excluded so the rule compares against genuinely
    /// prior data rather than the value it is about to overwrite.
    /// </summary>
    private async Task<SchemaHistory> BuildSchemaHistoryAsync(
        Guid serviceId,
        Schema schema,
        IReadOnlyList<(Sample sample, SchemaValue? def)> schemaSamples,
        Submission? existing,
        CadenceAnchors anchors,
        CancellationToken ct)
    {
        var latest = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // For values not present in this submission, fall back to the newest submitted timestamp
        // (or now) to anchor the "previous bucket" maths.
        var fallbackTs = schemaSamples.Count > 0
            ? schemaSamples.Max(t => t.sample.Timestamp)
            : DateTime.UtcNow;

        foreach (var v in schema.Values)
        {
            var baseFilter = Builders<SampleProjection>.Filter.And(
                Builders<SampleProjection>.Filter.Eq(s => s.IsDeleted, false),
                Builders<SampleProjection>.Filter.Eq(s => s.ServiceAccountId, serviceId),
                Builders<SampleProjection>.Filter.Eq(s => s.SchemaName, schema.Name),
                Builders<SampleProjection>.Filter.Eq(s => s.ValueName, v.Name));
            if (existing is not null)
                baseFilter = Builders<SampleProjection>.Filter.And(baseFilter,
                    Builders<SampleProjection>.Filter.Ne(s => s.SubmissionId, existing.Id));

            var sort = Builders<SampleProjection>.Sort.Descending(s => s.Timestamp);

            var latestRow = await _ctx.Samples.Find(baseFilter).Sort(sort).FirstOrDefaultAsync(ct);
            if (latestRow is not null) latest[v.Name] = ProjectionValue(latestRow);

            var refTs = schemaSamples
                .FirstOrDefault(t => t.def is not null &&
                    string.Equals(t.def.Name, v.Name, StringComparison.OrdinalIgnoreCase))
                .sample?.Timestamp ?? fallbackTs;
            var (pStart, pEnd) = CadenceCalculator.PreviousBucketFor(v.Cadence, refTs, anchors);
            var prevFilter = Builders<SampleProjection>.Filter.And(baseFilter,
                Builders<SampleProjection>.Filter.Gte(s => s.Timestamp, pStart),
                Builders<SampleProjection>.Filter.Lt(s => s.Timestamp, pEnd));
            var prevRow = await _ctx.Samples.Find(prevFilter).Sort(sort).FirstOrDefaultAsync(ct);
            if (prevRow is not null) previous[v.Name] = ProjectionValue(prevRow);
        }

        return new SchemaHistory(latest, previous);
    }

    /// <summary>Unbox the single populated typed column of a projection back to its CLR value.</summary>
    private static object? ProjectionValue(SampleProjection p) => p.ValueType switch
    {
        SchemaValueType.String => p.StringValue,
        SchemaValueType.Integer => p.IntegerValue,
        SchemaValueType.Number => p.NumberValue,
        SchemaValueType.Date => p.DateValue,
        SchemaValueType.Boolean => p.BooleanValue,
        _ => null,
    };

    /// <summary>
    /// Pre-fetched last-live values for one schema, keyed by value name. <c>Latest</c> is the most
    /// recent value across all periods; <c>Previous</c> the value in the immediately preceding
    /// cadence bucket. Missing entries mean "no live history".
    /// </summary>
    internal sealed record SchemaHistory(
        IReadOnlyDictionary<string, object?> Latest,
        IReadOnlyDictionary<string, object?> Previous)
    {
        public static readonly SchemaHistory Empty = new(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    }

    private async Task CheckCadenceAsync(
        Guid serviceId,
        Schema schema,
        SchemaValue def,
        Sample sample,
        bool isReplacement,
        Submission? existing,
        CadenceAnchors anchors,
        List<Diagnostic> errors,
        CancellationToken ct)
    {
        var (start, end) = CadenceCalculator.BucketFor(def.Cadence, sample.Timestamp, anchors);

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
            var cadence = def.Cadence.ToString().ToLowerInvariant();
            errors.Add(Diagnostic.Create(
                DiagnosticCodes.Submissions.DuplicatePeriod,
                $"Value '{Display(schema, def)}' already submitted for this {cadence} period.",
                ("schemaName", schema.Name),
                ("valueName", def.Name),
                ("displayName", Display(schema, def)),
                ("cadence", cadence),
                ("periodStart", start),
                ("periodEnd", end)));
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
            {
                var cadence = def.Cadence.ToString().ToLowerInvariant();
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Submissions.PendingDuplicatePeriod,
                    $"Value '{Display(schema, def)}' already has a submission awaiting approval for this {cadence} period; replace that submission instead.",
                    ("schemaName", schema.Name),
                    ("valueName", def.Name),
                    ("displayName", Display(schema, def)),
                    ("cadence", cadence),
                    ("periodStart", start),
                    ("periodEnd", end)));
            }
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
