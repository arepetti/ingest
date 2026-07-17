using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Key identifying a single (schema, value) pair inside a submission. Used by the validator to
/// report which samples were discarded so the persistence layer can filter them out.
/// </summary>
/// <param name="SchemaName">Machine-style schema name.</param>
/// <param name="ValueName">Machine-style value name inside the schema.</param>
public readonly record struct SampleRef(string SchemaName, string ValueName);

/// <summary>Outcome of a submission's validation pass.</summary>
/// <param name="IsValid">True when no per-value or schema-level rule rejected the input.</param>
/// <param name="Errors">Human-readable error strings, one per rejected rule. Empty when <paramref name="IsValid"/> is true.</param>
/// <param name="Warnings">
/// Non-blocking diagnostics surfaced alongside a successful submission: triggered <c>Warning</c>
/// expressions and notices that one or more samples were discarded because their
/// <c>EnabledIf</c>/<c>VisibleIf</c> rule rendered them inactive. Each carries the machine name
/// of the value it relates to (or <c>null</c> for submission-level diagnostics). Returned to the
/// caller as part of the response.
/// </param>
/// <param name="DiscardedSamples">
/// Samples that should NOT be persisted because their <c>EnabledIf</c> or <c>VisibleIf</c> rule
/// evaluated to false. The submission service filters these out before saving.
/// </param>
public sealed record SubmissionValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<SubmissionWarning> Warnings,
    IReadOnlySet<SampleRef> DiscardedSamples);

/// <summary>
/// Optional toggles that relax parts of the validation pipeline. The default ("run everything")
/// is what every real write uses. Intended for the validate-only endpoints — e.g. an API client
/// running shape checks in CI that wants to ignore the context-dependent cadence duplicate check.
/// Designed to grow: add a flag here (and a token to the endpoint's <c>omit</c> parser) per check.
/// </summary>
/// <param name="SkipCadence">
/// When true, skip the cadence one-per-window duplicate checks (both the live-projection check and
/// the pending-submission check). Everything else still runs.
/// </param>
public sealed record SubmissionValidationOptions(bool SkipCadence = false)
{
    /// <summary>Run the full pipeline. The default for every real submission write.</summary>
    public static readonly SubmissionValidationOptions Full = new();
}

/// <summary>
/// Runs every validation rule attached to a submission's schema: per-value checks (type, min/max,
/// required), value-level expression rules, and schema-level rules that see all samples at once
/// and can compare values to each other.
/// </summary>
public interface ISubmissionValidator
{
    /// <summary>Validate a submission about to be persisted.</summary>
    /// <param name="service">The owning service account; available to rules as context.</param>
    /// <param name="submission">The submission being validated (already mapped onto domain entities).</param>
    /// <param name="isReplacement">True when this is an update on an existing submission, false for a create.</param>
    /// <param name="existing">The previous submission when <paramref name="isReplacement"/> is true; <c>null</c> on create.</param>
    /// <param name="draft">
    /// True for a work-in-progress draft save. In draft mode only the structural checks run
    /// (schema/value existence + enabled-state, JSON→type coercion, and per-value shape/range);
    /// required-value presence, cadence one-per-window duplicates, conditional-display discards,
    /// value- and schema-level expression rules, and warning rules are all skipped. Publishing
    /// re-runs the full pipeline, so nothing partial reaches the live model.
    /// </param>
    /// <param name="options">
    /// Optional pipeline toggles. <c>null</c> (the default) runs the full pipeline — what every real
    /// write uses. Validate-only callers can pass a relaxed set (e.g. <c>SkipCadence</c>).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The aggregated validation outcome, including any discarded samples and warnings.</returns>
    Task<SubmissionValidationResult> ValidateAsync(
        Account service,
        Submission submission,
        bool isReplacement,
        Submission? existing,
        bool draft = false,
        SubmissionValidationOptions? options = null,
        CancellationToken ct = default);
}
