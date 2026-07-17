using Ingest.Core.Abstractions;
using Ingest.Core.Validation;

namespace Ingest.Tests;

/// <summary>
/// Minimal in-memory <see cref="IAppConfigurationService"/> for unit tests that don't care about
/// configuration persistence — anchors default to the historical calendar alignment and ingestion
/// starts open, both mutable via the setters below when a test needs to exercise a non-default
/// value (e.g. a custom fiscal year start, or the kill switch).
/// </summary>
public sealed class FakeAppConfigurationService : IAppConfigurationService
{
    public List<string> AreasValue { get; set; } = new();
    public CadenceAnchors Anchors { get; set; } = CadenceAnchors.Default;
    public IngestionStatus Status { get; set; } = new(false, null);
    public CadenceWindows Windows { get; set; } = CadenceWindows.Default;

    public Task<IReadOnlyList<string>> GetAreasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(AreasValue);

    public Task<IReadOnlyList<string>> UpdateAreasAsync(IReadOnlyList<string> areas, CancellationToken ct = default)
    {
        AreasValue = areas.ToList();
        return Task.FromResult<IReadOnlyList<string>>(AreasValue);
    }

    public Task<CadenceAnchors> GetCadenceAnchorsAsync(CancellationToken ct = default) => Task.FromResult(Anchors);

    public Task<CadenceAnchors> UpdateCadenceAnchorsAsync(CadenceAnchors anchors, CancellationToken ct = default)
    {
        Anchors = anchors;
        return Task.FromResult(Anchors);
    }

    public Task<IngestionStatus> GetIngestionStatusAsync(CancellationToken ct = default) => Task.FromResult(Status);

    public Task<IngestionStatus> UpdateIngestionStatusAsync(bool closed, string? message, CancellationToken ct = default)
    {
        Status = new IngestionStatus(closed, message);
        return Task.FromResult(Status);
    }

    public Task<CadenceWindows> GetCadenceWindowsAsync(CancellationToken ct = default) => Task.FromResult(Windows);

    public Task<CadenceWindows> UpdateCadenceWindowsAsync(CadenceWindows windows, CancellationToken ct = default)
    {
        Windows = windows;
        return Task.FromResult(Windows);
    }
}
