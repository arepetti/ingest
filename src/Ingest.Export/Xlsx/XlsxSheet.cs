using ClosedXML.Excel;

namespace Ingest.Export.Xlsx;

/// <summary>
/// A worksheet in an <see cref="XlsxWorkbook"/>. Cells are addressed with 1-based row/column
/// indices (row 1, column 1 = A1). All spreadsheet-library types stay behind this facade.
/// </summary>
public sealed class XlsxSheet
{
    private readonly IXLWorksheet _worksheet;

    internal XlsxSheet(IXLWorksheet worksheet) => _worksheet = worksheet;

    /// <summary>Write a text value into a cell. A null/empty value still applies the style (e.g. a fill).</summary>
    public void SetText(int row, int col, string? text, XlsxCellStyle? style = null)
    {
        var cell = _worksheet.Cell(row, col);
        if (!string.IsNullOrEmpty(text)) cell.Value = text;
        Apply(cell, style);
    }

    /// <summary>Write a numeric value into a cell.</summary>
    public void SetNumber(int row, int col, double value, XlsxCellStyle? style = null)
    {
        var cell = _worksheet.Cell(row, col);
        cell.Value = value;
        Apply(cell, style);
    }

    /// <summary>Merge a rectangular range of cells (1-based inclusive bounds).</summary>
    public void Merge(int row1, int col1, int row2, int col2)
    {
        if (row1 < 1 || col1 < 1 || row2 < row1 || col2 < col1)
            throw new ArgumentException("Invalid merge range.");
        _worksheet.Range(row1, col1, row2, col2).Merge();
    }

    /// <summary>Attach a hover note/comment to a cell.</summary>
    public void AddNote(int row, int col, string text, string? author = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var comment = _worksheet.Cell(row, col).CreateComment();
        comment.Author = string.IsNullOrWhiteSpace(author) ? "Ingest" : author!;
        comment.AddText(text);
        // Auto-size avoids the "removed records: comments" repair prompt some Excel builds show.
        comment.Style.Size.SetAutomaticSize();
    }

    /// <summary>Set a column's width (in Excel character units).</summary>
    public void SetColumnWidth(int col, double width)
    {
        if (col < 1) throw new ArgumentOutOfRangeException(nameof(col));
        _worksheet.Column(col).Width = width;
    }

    private static void Apply(IXLCell cell, XlsxCellStyle? style)
    {
        if (style is null) return;
        if (style.Bold) cell.Style.Font.Bold = true;
        if (!string.IsNullOrWhiteSpace(style.FillHex))
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(ToHtmlColor(style.FillHex!));
        if (style.Align != XlsxAlign.General) cell.Style.Alignment.Horizontal = MapAlign(style.Align);
        if (style.WrapText) cell.Style.Alignment.WrapText = true;
    }

    private static string ToHtmlColor(string hex)
    {
        var h = hex.TrimStart('#').Trim();
        // Accept ARGB by dropping the alpha; ClosedXML's FromHtml wants #RRGGBB.
        if (h.Length == 8) h = h[2..];
        return "#" + h.ToUpperInvariant();
    }

    private static XLAlignmentHorizontalValues MapAlign(XlsxAlign align) => align switch
    {
        XlsxAlign.Left => XLAlignmentHorizontalValues.Left,
        XlsxAlign.Center => XLAlignmentHorizontalValues.Center,
        XlsxAlign.Right => XLAlignmentHorizontalValues.Right,
        _ => XLAlignmentHorizontalValues.General,
    };
}
