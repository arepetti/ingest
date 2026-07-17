using Ingest.Core.Entities;

namespace Ingest.Core.Validation;

/// <summary>
/// How much a cadence's submission window is offset from its raw bucket (see
/// <see cref="CadenceCalculator.BucketFor"/>). <see cref="OpenOffsetHours"/> delays when the window
/// opens (relative to the bucket's start); <see cref="GraceHours"/> extends when it closes (relative
/// to the bucket's end). Both default to zero, in which case the window is exactly the bucket —
/// today's historical behaviour.
/// </summary>
/// <param name="OpenOffsetHours">Hours after the bucket's start before the window opens. Non-negative.</param>
/// <param name="GraceHours">Hours after the bucket's end during which the window stays open. Non-negative.</param>
public sealed record CadenceWindow(double OpenOffsetHours, double GraceHours)
{
    /// <summary>No offset, no grace — the window is exactly the bucket.</summary>
    public static readonly CadenceWindow None = new(0, 0);
}

/// <summary>
/// Resolved per-cadence <see cref="CadenceWindow"/> settings. Resolved from the optional overrides
/// on <c>AppConfiguration</c>; <see cref="Default"/> gives every cadence <see cref="CadenceWindow.None"/>
/// so a fresh or legacy deployment with no configuration behaves exactly as it always did.
/// </summary>
public sealed record CadenceWindows(
    CadenceWindow Daily,
    CadenceWindow Weekly,
    CadenceWindow Fortnightly,
    CadenceWindow Monthly,
    CadenceWindow Quarterly,
    CadenceWindow SemiAnnually,
    CadenceWindow Yearly)
{
    /// <summary>Every cadence resolves to <see cref="CadenceWindow.None"/> — window == bucket.</summary>
    public static readonly CadenceWindows Default = new(
        CadenceWindow.None, CadenceWindow.None, CadenceWindow.None, CadenceWindow.None,
        CadenceWindow.None, CadenceWindow.None, CadenceWindow.None);

    /// <summary>Look up the resolved window for a specific cadence.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cadence"/> is not a known enum value.</exception>
    public CadenceWindow For(Cadence cadence) => cadence switch
    {
        Cadence.Daily => Daily,
        Cadence.Weekly => Weekly,
        Cadence.Fortnightly => Fortnightly,
        Cadence.Monthly => Monthly,
        Cadence.Quarterly => Quarterly,
        Cadence.SemiAnnually => SemiAnnually,
        Cadence.Yearly => Yearly,
        _ => throw new ArgumentOutOfRangeException(nameof(cadence)),
    };
}
