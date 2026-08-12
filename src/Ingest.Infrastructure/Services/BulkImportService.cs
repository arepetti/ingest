using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Infrastructure.Validation;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IBulkImportService"/>. Parses the file with <see cref="BulkImportParser"/>
/// and then replays each submission group through <see cref="ISubmissionService.AdminCreateAsync"/>
/// so imported submissions go through exactly the same validation, projection rebuild, and audit
/// trail as one created through the admin UI. The run is best-effort: a group that fails validation
/// is recorded as failed and the loop carries on. It is also idempotent — a group that already
/// exists (every sample rejected only because its reporting window is taken) is counted as skipped
/// rather than failed, so re-running the same file is a safe no-op.
/// </summary>
public sealed class BulkImportService : IBulkImportService
{
    private readonly ISubmissionService _submissions;
    private readonly IAppConfigurationService _appConfig;

    /// <summary>Create a new <see cref="BulkImportService"/>.</summary>
    /// <param name="submissions">Submission service used to persist each parsed group.</param>
    /// <param name="appConfig">Application configuration provider; supplies the ingestion kill switch.</param>
    public BulkImportService(ISubmissionService submissions, IAppConfigurationService appConfig)
    {
        _submissions = submissions;
        _appConfig = appConfig;
    }

    /// <inheritdoc />
    public async Task<BulkImportResult> ImportAsync(Guid serviceAccountId, BulkImportFormat format, string content, CancellationToken ct = default)
    {
        // Bulk import replays through AdminCreateAsync — the same path the admin UI uses for
        // remediation — so the kill switch can't be enforced there without also blocking admins.
        // Gate here instead, at the service-facing entry point.
        var status = await _appConfig.GetIngestionStatusAsync(ct);
        if (status.Closed)
        {
            var message = string.IsNullOrWhiteSpace(status.Message)
                ? "Submissions are temporarily closed."
                : status.Message!;
            throw new ServiceUnavailableException(Diagnostic.Create(
                DiagnosticCodes.Submissions.IngestionClosed,
                message,
                ("configuredMessage", !string.IsNullOrWhiteSpace(status.Message))));
        }

        var parsed = BulkImportParser.Parse(format, content);
        if (parsed.Errors.Count > 0)
            throw new ValidationException(parsed.ErrorDetails);
        if (parsed.Submissions.Count == 0)
            throw new ValidationException(new[]
            {
                new Diagnostic(DiagnosticCodes.Imports.NoSubmissions, "No submissions found in the file."),
            });

        var items = new List<BulkImportItemResult>(parsed.Submissions.Count);
        var succeeded = 0;
        var skipped = 0;
        var failed = 0;

        for (var i = 0; i < parsed.Submissions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var group = parsed.Submissions[i];
            var input = new AdminSubmissionInput(serviceAccountId, group.Samples.ToList());
            // Date the imported submission to its data, not to "now": use the first sample's
            // timestamp (samples in a group usually share one, but if they differ we just take the
            // first in document order).
            var submittedAt = group.Samples[0].Timestamp;
            try
            {
                var written = await _submissions.AdminCreateAsync(input, submittedAt: submittedAt, ct: ct);
                succeeded++;
                items.Add(new BulkImportItemResult(i, group.Group, true, false, written.Submission.Id, group.Samples.Count, Array.Empty<string>(), written.Warnings)
                {
                    ErrorDetails = Array.Empty<Diagnostic>(),
                    WarningDetails = written.WarningDetails,
                });
            }
            catch (ValidationException ex)
            {
                // Idempotency: a group whose every blocking error is a cadence-duplicate already
                // exists for its reporting window, so re-importing it is a no-op — count it as
                // skipped rather than failed.
                if (ex.ErrorDetails.Count > 0 && ex.ErrorDetails.All(SubmissionValidator.IsDuplicatePeriodError))
                {
                    skipped++;
                    items.Add(new BulkImportItemResult(i, group.Group, false, true, null, group.Samples.Count, Array.Empty<string>(), Array.Empty<string>())
                    {
                        ErrorDetails = Array.Empty<Diagnostic>(),
                        WarningDetails = Array.Empty<Diagnostic>(),
                    });
                }
                else
                {
                    failed++;
                    items.Add(new BulkImportItemResult(i, group.Group, false, false, null, group.Samples.Count, ex.Errors.ToList(), Array.Empty<string>())
                    {
                        ErrorDetails = ex.ErrorDetails,
                        WarningDetails = Array.Empty<Diagnostic>(),
                    });
                }
            }
            catch (NotFoundException ex)
            {
                failed++;
                items.Add(new BulkImportItemResult(i, group.Group, false, false, null, group.Samples.Count, new[] { ex.Message }, Array.Empty<string>())
                {
                    ErrorDetails = new[] { ex.Diagnostic },
                    WarningDetails = Array.Empty<Diagnostic>(),
                });
            }
        }

        return new BulkImportResult(parsed.Submissions.Count, succeeded, skipped, failed, items);
    }
}
