namespace Ingest.Core.Common;

/// <summary>One page of results plus enough metadata for callers to render paging UI.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">The items in this page (never null; empty when the page is past the last result).</param>
/// <param name="Total">Total number of items across all pages, before paging is applied.</param>
/// <param name="Page">1-based page number that produced this result.</param>
/// <param name="PageSize">The requested (clamped) page size.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

/// <summary>Paging + sort + soft-delete-visibility parameters carried into repository queries.</summary>
/// <param name="Page">1-based page number; defaults to 1.</param>
/// <param name="PageSize">Page size; defaults to 50. Clamped between 1 and 500 at use-time (see <see cref="Take"/>).</param>
/// <param name="Sort">Optional sort hint; repositories interpret it as they see fit (e.g. <c>createdAt</c> = newest-first).</param>
/// <param name="IncludeDeleted">When true, soft-deleted rows are included in the result.</param>
public sealed record PageRequest(int Page = 1, int PageSize = 50, string? Sort = null, bool IncludeDeleted = false)
{
    /// <summary>Number of rows the query should skip (computed from <see cref="Page"/> and <see cref="PageSize"/>, never negative).</summary>
    public int Skip => Math.Max(0, (Page - 1) * PageSize);

    /// <summary>Effective page size, clamped to a safe range (1..500) to protect the database.</summary>
    public int Take => Math.Clamp(PageSize, 1, 500);
}
