namespace Ingest.Core.Abstractions;

/// <summary>
/// Read/write access to the server-wide application configuration singleton. Currently exposes the
/// list of selectable "areas". An absent document reads back as an empty list, so callers never
/// have to special-case a fresh deployment.
/// </summary>
public interface IAppConfigurationService
{
    /// <summary>Fetch the configured areas in display order (never <c>null</c>; empty when unset).</summary>
    Task<IReadOnlyList<string>> GetAreasAsync(CancellationToken ct = default);

    /// <summary>
    /// Replace the list of areas. Entries are trimmed, blanks dropped and duplicates removed
    /// (case-insensitively) while preserving order. Returns the stored list.
    /// </summary>
    Task<IReadOnlyList<string>> UpdateAreasAsync(IReadOnlyList<string> areas, CancellationToken ct = default);
}
