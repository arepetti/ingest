using System.Text.Json;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Wire-format ingestion input for a single sample. The <c>Value</c> arrives as raw JSON because
/// its concrete CLR type depends on the (schema, value) pair's declared <c>SchemaValueType</c> —
/// the submission service resolves it against the visible schemas before persisting.
/// </summary>
/// <param name="SchemaName">Machine-style schema name the sample belongs to.</param>
/// <param name="ValueName">Machine-style value name inside the schema.</param>
/// <param name="Value">The submitted value as raw JSON; <c>null</c> means "skip" for optional values.</param>
/// <param name="Timestamp">When the sample was measured (UTC).</param>
/// <param name="Note">Free-text note attached to this single sample, if any.</param>
public sealed record SampleInput(
    string SchemaName,
    string ValueName,
    JsonElement? Value,
    DateTime Timestamp,
    string? Note);

/// <summary>Service-facing submission payload — the owning account is taken from the bearer credential.</summary>
/// <param name="Samples">The samples to submit. All samples in a single payload must refer to the same schema.</param>
public sealed record SubmissionInput(List<SampleInput> Samples);

/// <summary>Admin-facing submission payload — the caller explicitly names the service being acted on behalf of.</summary>
/// <param name="ServiceAccountId">The id of the service account the submission should be attributed to.</param>
/// <param name="Samples">The samples to submit. All samples in a single payload must refer to the same schema.</param>
public sealed record AdminSubmissionInput(Guid ServiceAccountId, List<SampleInput> Samples);
