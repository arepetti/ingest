using Ingest.Core.Abstractions;
using Ingest.Core.Common;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IBulkImportService"/>. Parses the file with <see cref="BulkImportParser"/>
/// and then replays each submission group through <see cref="ISubmissionService.AdminCreateAsync"/>
/// so imported submissions go through exactly the same validation, projection rebuild, and audit
/// trail as one created through the admin UI. The run is best-effort: a group that fails validation
/// is recorded as failed and the loop carries on.
/// </summary>
public sealed class BulkImportService : IBulkImportService
{
    private readonly ISubmissionService _submissions;

    /// <summary>Create a new <see cref="BulkImportService"/>.</summary>
    /// <param name="submissions">Submission service used to persist each parsed group.</param>
    public BulkImportService(ISubmissionService submissions) => _submissions = submissions;

    /// <inheritdoc />
    public async Task<BulkImportResult> ImportAsync(Guid serviceAccountId, BulkImportFormat format, string content, CancellationToken ct = default)
    {
        var parsed = BulkImportParser.Parse(format, content);
        if (parsed.Errors.Count > 0)
            throw new ValidationException(parsed.Errors);
        if (parsed.Submissions.Count == 0)
            throw new ValidationException(new[] { "No submissions found in the file." });

        var items = new List<BulkImportItemResult>(parsed.Submissions.Count);
        var succeeded = 0;
        var failed = 0;

        for (var i = 0; i < parsed.Submissions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var group = parsed.Submissions[i];
            var input = new AdminSubmissionInput(serviceAccountId, group.Samples.ToList());
            try
            {
                var written = await _submissions.AdminCreateAsync(input, ct);
                succeeded++;
                items.Add(new BulkImportItemResult(i, group.Group, true, written.Submission.Id, group.Samples.Count, Array.Empty<string>(), written.Warnings));
            }
            catch (ValidationException ex)
            {
                failed++;
                items.Add(new BulkImportItemResult(i, group.Group, false, null, group.Samples.Count, ex.Errors.ToList(), Array.Empty<string>()));
            }
            catch (NotFoundException ex)
            {
                failed++;
                items.Add(new BulkImportItemResult(i, group.Group, false, null, group.Samples.Count, new[] { ex.Message }, Array.Empty<string>()));
            }
        }

        return new BulkImportResult(parsed.Submissions.Count, succeeded, failed, items);
    }
}
