using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>Persistence boundary for <see cref="Report"/> aggregates.</summary>
public interface IReportRepository
{
    /// <summary>Fetch a report by id.</summary>
    /// <param name="id">Report id.</param>
    /// <param name="includeDeleted">When true, soft-deleted reports are also considered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report, or <c>null</c> if no match.</returns>
    Task<Report?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default);

    /// <summary>Fetch a report by its unique machine-style name.</summary>
    /// <param name="name">Report name (case-insensitive).</param>
    /// <param name="includeDeleted">When true, soft-deleted reports are also considered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report, or <c>null</c> if no match.</returns>
    Task<Report?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default);

    /// <summary>Page through every report in the catalogue.</summary>
    /// <param name="request">Paging + sort parameters; <c>IncludeDeleted</c> opts soft-deleted entries in.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of reports with the total count.</returns>
    Task<PagedResult<Report>> ListAsync(PageRequest request, CancellationToken ct = default);

    /// <summary>Insert a new report.</summary>
    /// <param name="report">Report to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(Report report, CancellationToken ct = default);

    /// <summary>Flip the soft-delete flag on a report. Idempotent.</summary>
    /// <param name="id">Report id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}
