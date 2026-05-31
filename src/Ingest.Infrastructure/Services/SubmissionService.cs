using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="ISubmissionService"/>. Coordinates the read/write paths
/// for service- and admin-driven submissions: schema audience lookup, sample mapping, validation
/// (through <see cref="ISubmissionValidator"/>), persistence, projection rebuild, and the
/// Service-role cadence-window check that admin endpoints intentionally bypass.
/// </summary>
public sealed class SubmissionService : ISubmissionService
{
    private readonly ISubmissionRepository _submissions;
    private readonly ISampleRepository _samples;
    private readonly ISchemaRepository _schemas;
    private readonly ISubmissionValidator _validator;
    private readonly IAccountRepository _accounts;
    private readonly TimeProvider _time;

    /// <summary>Create a new <see cref="SubmissionService"/>.</summary>
    /// <param name="submissions">Submission repository.</param>
    /// <param name="samples">Sample projection repository (rebuilt per submission save).</param>
    /// <param name="schemas">Schema repository for visibility checks.</param>
    /// <param name="validator">Validator that runs the full rule pipeline.</param>
    /// <param name="accounts">Account repository for owner/service lookups.</param>
    /// <param name="time">Clock used to evaluate cadence windows on replacement.</param>
    public SubmissionService(
        ISubmissionRepository submissions,
        ISampleRepository samples,
        ISchemaRepository schemas,
        ISubmissionValidator validator,
        IAccountRepository accounts,
        TimeProvider time)
    {
        _submissions = submissions;
        _samples = samples;
        _schemas = schemas;
        _validator = validator;
        _accounts = accounts;
        _time = time;
    }

    // ── Service-facing ──

    /// <inheritdoc />
    public async Task<SubmissionWriteResult> CreateMineAsync(Guid callerAccountId, SubmissionInput input, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(callerAccountId, ct: ct)
            ?? throw new NotFoundException("Account");
        var visible = await LoadVisibleAsync(account.Id, ct);

        var submission = MapInput(account, input, visible);
        var validation = await ValidateOrThrow(account, submission, isReplacement: false, existing: null, ct);

        // Strip samples the validator told us to drop (EnabledIf/VisibleIf == false). The
        // associated warnings are already in validation.Warnings; the surviving samples are
        // what gets persisted and projected.
        submission.Samples = FilterDiscarded(submission.Samples, validation.DiscardedSamples);

        await PersistAsync(submission, visible, isReplacement: false, ct);
        return new SubmissionWriteResult(submission, validation.Warnings);
    }

    /// <inheritdoc />
    public async Task<SubmissionWriteResult> ReplaceMineAsync(Guid callerAccountId, Guid submissionId, SubmissionInput input, CancellationToken ct = default)
    {
        var existing = await _submissions.GetByIdAsync(submissionId, ct: ct)
            ?? throw new NotFoundException($"Submission '{submissionId}'");
        if (existing.ServiceAccountId != callerAccountId)
            throw new ForbiddenException("Submission belongs to a different account.");

        var account = await _accounts.GetByIdAsync(existing.ServiceAccountId, ct: ct)
            ?? throw new NotFoundException("Account");
        var visible = await LoadVisibleAsync(account.Id, ct);

        // Cadence window: a service can only revise samples whose cadence period is still open.
        // We evaluate against the EXISTING samples (each value's declared cadence + its recorded
        // timestamp) so that backdating new samples can't be used to circumvent the limit. Admins
        // wanting to override go through AdminReplaceAsync instead.
        if (ClosedCadenceError(existing, visible) is { } cadenceError)
            throw new ForbiddenException(cadenceError);

        var replacement = MapInput(account, input, visible);
        replacement.Id = existing.Id;
        var validation = await ValidateOrThrow(account, replacement, isReplacement: true, existing, ct);

        existing.Samples = FilterDiscarded(replacement.Samples, validation.DiscardedSamples);
        await PersistAsync(existing, visible, isReplacement: true, ct);
        return new SubmissionWriteResult(existing, validation.Warnings);
    }

    /// <inheritdoc />
    public async Task<Submission?> GetMineAsync(Guid callerAccountId, Guid submissionId, CancellationToken ct = default)
    {
        var s = await _submissions.GetByIdAsync(submissionId, ct: ct);
        if (s is null) return null;
        if (s.ServiceAccountId != callerAccountId)
            throw new ForbiddenException("Submission belongs to a different account.");
        return s;
    }

    /// <inheritdoc />
    public Task<PagedResult<Submission>> ListMineAsync(Guid callerAccountId, PageRequest request, DateTime? from, DateTime? to, string? schemaName, CancellationToken ct = default) =>
        _submissions.ListAsync(request, callerAccountId, from, to, schemaName, ct);

    // ── Admin-facing ──

    /// <inheritdoc />
    public Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId, DateTime? from, DateTime? to, string? schemaName, CancellationToken ct = default) =>
        _submissions.ListAsync(request, serviceId, from, to, schemaName, ct);

    /// <inheritdoc />
    public Task<Submission?> GetAsync(Guid submissionId, bool includeDeleted, CancellationToken ct = default) =>
        _submissions.GetByIdAsync(submissionId, includeDeleted, ct);

    /// <inheritdoc />
    public async Task<SubmissionWriteResult> AdminCreateAsync(AdminSubmissionInput input, CancellationToken ct = default)
    {
        var service = await _accounts.GetByIdAsync(input.ServiceAccountId, ct: ct)
            ?? throw new NotFoundException($"Service '{input.ServiceAccountId}'");
        var visible = await LoadVisibleAsync(service.Id, ct);

        var submission = MapInput(service, new SubmissionInput(input.Samples), visible);
        var validation = await ValidateOrThrow(service, submission, isReplacement: false, existing: null, ct);

        submission.Samples = FilterDiscarded(submission.Samples, validation.DiscardedSamples);
        await PersistAsync(submission, visible, isReplacement: false, ct);
        return new SubmissionWriteResult(submission, validation.Warnings);
    }

    /// <inheritdoc />
    public async Task<SubmissionWriteResult> AdminReplaceAsync(Guid submissionId, AdminSubmissionInput input, CancellationToken ct = default)
    {
        var existing = await _submissions.GetByIdAsync(submissionId, ct: ct)
            ?? throw new NotFoundException($"Submission '{submissionId}'");

        // The service of an existing submission is immutable; the input value is ignored if it differs.
        var service = await _accounts.GetByIdAsync(existing.ServiceAccountId, ct: ct)
            ?? throw new NotFoundException($"Service '{existing.ServiceAccountId}'");
        var visible = await LoadVisibleAsync(service.Id, ct);

        var replacement = MapInput(service, new SubmissionInput(input.Samples), visible);
        replacement.Id = existing.Id;
        var validation = await ValidateOrThrow(service, replacement, isReplacement: true, existing, ct);

        existing.Samples = FilterDiscarded(replacement.Samples, validation.DiscardedSamples);
        await PersistAsync(existing, visible, isReplacement: true, ct);
        return new SubmissionWriteResult(existing, validation.Warnings);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid submissionId, CancellationToken ct = default)
    {
        await _submissions.SoftDeleteAsync(submissionId, ct);
        await _samples.SoftDeleteForSubmissionAsync(submissionId, ct);
    }

    // ── Internals ──

    private async Task<IReadOnlyDictionary<string, Schema>> LoadVisibleAsync(Guid accountId, CancellationToken ct)
    {
        var visible = await _schemas.ListVisibleToAsync(accountId, ct);
        return visible.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static Submission MapInput(Account account, SubmissionInput input, IReadOnlyDictionary<string, Schema> visible)
    {
        var samples = new List<Sample>(input.Samples.Count);
        foreach (var s in input.Samples)
        {
            // Look up the declared SchemaValueType so JsonValueMapper can coerce the raw
            // JsonElement into the right CLR type (Double, Long, DateTime, bool, string).
            SchemaValueType? type = null;
            if (visible.TryGetValue(s.SchemaName, out var schema))
            {
                var def = schema.Values.FirstOrDefault(v =>
                    string.Equals(v.Name, s.ValueName, StringComparison.OrdinalIgnoreCase));
                type = def?.Type;
            }
            var value = JsonValueMapper.MapValue(s.Value, type);
            samples.Add(new Sample
            {
                SchemaName = s.SchemaName,
                ValueName = s.ValueName,
                Value = value,
                Timestamp = DateTime.SpecifyKind(s.Timestamp, DateTimeKind.Utc),
                Note = s.Note,
            });
        }

        return new Submission
        {
            ServiceAccountId = account.Id,
            ServiceName = account.Name,
            Samples = samples,
        };
    }

    private async Task<SubmissionValidationResult> ValidateOrThrow(Account account, Submission submission, bool isReplacement, Submission? existing, CancellationToken ct)
    {
        var result = await _validator.ValidateAsync(account, submission, isReplacement, existing, ct);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);
        return result;
    }

    private static List<Sample> FilterDiscarded(IEnumerable<Sample> samples, IReadOnlySet<SampleRef> discarded)
    {
        if (discarded.Count == 0) return samples.ToList();
        return samples
            .Where(s => !discarded.Contains(new SampleRef(s.SchemaName, s.ValueName)))
            .ToList();
    }

    private async Task PersistAsync(Submission submission, IReadOnlyDictionary<string, Schema> visible, bool isReplacement, CancellationToken ct)
    {
        if (isReplacement)
            await _submissions.UpdateAsync(submission, ct);
        else
            await _submissions.AddAsync(submission, ct);

        var projections = SampleProjectionBuilder.Build(submission, visible);
        await _samples.ReplaceForSubmissionAsync(submission.Id, projections, ct);
    }

    /// <summary>
    /// Returns null when every sample in the submission still falls inside its (per-value) cadence
    /// period; otherwise returns a human-readable message naming the first closed (schema, value)
    /// pair. Samples whose schema/value can no longer be resolved are ignored — they're already in
    /// a broken state that the validator will flag separately.
    /// </summary>
    private string? ClosedCadenceError(Submission existing, IReadOnlyDictionary<string, Schema> visible)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var sample in existing.Samples)
        {
            if (!visible.TryGetValue(sample.SchemaName, out var schema)) continue;
            var def = schema.Values.FirstOrDefault(v =>
                string.Equals(v.Name, sample.ValueName, StringComparison.OrdinalIgnoreCase));
            if (def is null) continue;

            var (_, end) = CadenceCalculator.BucketFor(def.Cadence, sample.Timestamp);
            if (end <= now)
            {
                return $"Submission can no longer be modified: the {def.Cadence.ToString().ToLowerInvariant()} " +
                       $"period for '{sample.SchemaName}.{sample.ValueName}' closed on {end:u}. " +
                       $"Ask an administrator to amend it on your behalf.";
            }
        }
        return null;
    }
}
