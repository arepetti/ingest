namespace Ingest.Export.Xlsx;

/// <summary>Horizontal alignment options exposed by the XLSX proxy.</summary>
public enum XlsxAlign
{
    /// <summary>Default alignment.</summary>
    General,
    /// <summary>Left-aligned.</summary>
    Left,
    /// <summary>Centre-aligned.</summary>
    Center,
    /// <summary>Right-aligned.</summary>
    Right,
}

/// <summary>
/// A small, library-agnostic description of a cell's appearance. Callers build these instead of
/// touching the underlying spreadsheet library directly, so the export code stays simple and the
/// backing library can change without ripple.
/// </summary>
public sealed record XlsxCellStyle
{
    /// <summary>Render the cell text in bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Solid background colour as an RGB or ARGB hex string ("#" optional); null for none.</summary>
    public string? FillHex { get; init; }

    /// <summary>Horizontal alignment.</summary>
    public XlsxAlign Align { get; init; } = XlsxAlign.General;

    /// <summary>Wrap long text within the cell.</summary>
    public bool WrapText { get; init; }
}
