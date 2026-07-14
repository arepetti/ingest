using ClosedXML.Excel;

namespace Ingest.Export.Xlsx;

/// <summary>
/// A thin proxy over ClosedXML that exposes only the small surface the exports need
/// (sheets, text/number cells, fills, alignment, merges, column widths and hover notes).
/// Keeping callers on this facade means the underlying spreadsheet library is an implementation
/// detail that can be swapped without touching export logic.
/// </summary>
public sealed class XlsxWorkbook : IDisposable
{
    /// <summary>The MIME content type for an <c>.xlsx</c> file.</summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>The conventional file extension (including the dot).</summary>
    public const string FileExtension = ".xlsx";

    private readonly XLWorkbook _workbook = new();

    /// <summary>Add a worksheet with the given tab name (sanitised to Excel's constraints).</summary>
    public XlsxSheet AddSheet(string name) => new(_workbook.Worksheets.Add(Sanitize(name)));

    /// <summary>Serialise the workbook to <paramref name="stream"/>.</summary>
    public void Save(Stream stream) => _workbook.SaveAs(stream);

    /// <summary>Serialise the workbook to a new byte array.</summary>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        _workbook.SaveAs(ms);
        return ms.ToArray();
    }

    /// <inheritdoc />
    public void Dispose() => _workbook.Dispose();

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Sheet1";
        var cleaned = new string(name.Select(c => c is '[' or ']' or ':' or '*' or '?' or '/' or '\\' ? ' ' : c).ToArray()).Trim();
        if (cleaned.Length == 0) return "Sheet1";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
