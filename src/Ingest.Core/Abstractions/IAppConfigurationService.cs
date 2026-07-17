using Ingest.Core.Validation;

namespace Ingest.Core.Abstractions;

/// <summary>
/// The global "close all submissions" kill switch state. Freezes service-facing ingestion
/// (service create/replace, bulk import, Teams inbound) while leaving reads, admin remediation
/// and every other operation available. See <see cref="IAppConfigurationService.GetIngestionStatusAsync"/>.
/// </summary>
/// <param name="Closed">When true, service-facing ingestion must be rejected.</param>
/// <param name="Message">Optional operator-facing message shown alongside the block/banner.</param>
public sealed record IngestionStatus(bool Closed, string? Message);

/// <summary>
/// Read/write access to the server-wide application configuration singleton: the list of
/// selectable "areas", the cadence period anchors, and the ingestion kill switch. An absent
/// document reads back as an empty/default configuration, so callers never have to special-case a
/// fresh deployment.
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

    /// <summary>
    /// Fetch the configured cadence bucket alignment points, resolving unset fields to the
    /// historical calendar defaults (see <see cref="CadenceAnchors.Default"/>).
    /// </summary>
    Task<CadenceAnchors> GetCadenceAnchorsAsync(CancellationToken ct = default);

    /// <summary>
    /// Replace the cadence bucket alignment points (clamped to valid ranges). Returns the stored
    /// (clamped) anchors.
    /// </summary>
    Task<CadenceAnchors> UpdateCadenceAnchorsAsync(CadenceAnchors anchors, CancellationToken ct = default);

    /// <summary>Fetch the global ingestion kill-switch state (closed = false, message = null when unset).</summary>
    Task<IngestionStatus> GetIngestionStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Update the global ingestion kill-switch state. A blank message is stored as <c>null</c>.
    /// Returns the stored state.
    /// </summary>
    Task<IngestionStatus> UpdateIngestionStatusAsync(bool closed, string? message, CancellationToken ct = default);

    /// <summary>
    /// Fetch the per-cadence submission window offsets, resolving every unset cadence/field to
    /// <see cref="CadenceWindow.None"/> (window == bucket, the historical behaviour).
    /// </summary>
    Task<CadenceWindows> GetCadenceWindowsAsync(CancellationToken ct = default);

    /// <summary>
    /// Replace the per-cadence submission window offsets (each hour value clamped to a sane range).
    /// Returns the stored (clamped) windows.
    /// </summary>
    Task<CadenceWindows> UpdateCadenceWindowsAsync(CadenceWindows windows, CancellationToken ct = default);
}
