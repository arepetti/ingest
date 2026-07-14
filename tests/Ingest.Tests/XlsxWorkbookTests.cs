using ClosedXML.Excel;
using Ingest.Export.Xlsx;

namespace Ingest.Tests;

public class XlsxWorkbookTests
{
    private static XLWorkbook RoundTrip(XlsxWorkbook wb)
    {
        var ms = new MemoryStream(wb.ToBytes());
        return new XLWorkbook(ms);
    }

    [Fact]
    public void Writes_text_and_number_cells()
    {
        using var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Data");
        sheet.SetText(1, 1, "Name");
        sheet.SetNumber(2, 1, 42);

        using var read = RoundTrip(wb);
        var ws = read.Worksheet("Data");
        Assert.Equal("Name", ws.Cell(1, 1).GetString());
        Assert.Equal(42, ws.Cell(2, 1).GetDouble());
    }

    [Fact]
    public void Applies_fill_bold_and_alignment_styles()
    {
        using var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Data");
        sheet.SetText(1, 1, "Header", new XlsxCellStyle { Bold = true, FillHex = "#B7D7A8", Align = XlsxAlign.Center });
        // Empty but styled cell (the "missing value" yellow highlight).
        sheet.SetText(1, 2, null, new XlsxCellStyle { FillHex = "FFF6C0" });

        using var read = RoundTrip(wb);
        var ws = read.Worksheet("Data");

        var header = ws.Cell(1, 1);
        Assert.True(header.Style.Font.Bold);
        Assert.Equal(XLAlignmentHorizontalValues.Center, header.Style.Alignment.Horizontal);
        Assert.Equal(XLColor.FromHtml("#B7D7A8"), header.Style.Fill.BackgroundColor);

        var empty = ws.Cell(1, 2);
        Assert.True(empty.IsEmpty(XLCellsUsedOptions.Contents));
        Assert.Equal(XLColor.FromHtml("#FFF6C0"), empty.Style.Fill.BackgroundColor);
    }

    [Fact]
    public void Emits_merged_ranges()
    {
        using var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Data");
        sheet.SetText(1, 3, "Employment");
        sheet.Merge(1, 3, 1, 4);

        using var read = RoundTrip(wb);
        var ws = read.Worksheet("Data");
        Assert.Contains(ws.MergedRanges, r => r.RangeAddress.ToString() == "C1:D1");
    }

    [Fact]
    public void Attaches_hover_notes()
    {
        using var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Data");
        sheet.SetText(3, 2, "Weekly report");
        sheet.AddNote(3, 2, "Sample discarded by VisibleIf.");

        using var read = RoundTrip(wb);
        var ws = read.Worksheet("Data");
        var cell = ws.Cell(3, 2);
        Assert.True(cell.HasComment);
        var text = string.Concat(cell.GetComment().Select(rt => rt.Text));
        Assert.Contains("discarded", text);
    }

    [Fact]
    public void Sanitizes_sheet_names()
    {
        using var wb = new XlsxWorkbook();
        // Illegal chars and >31 chars get cleaned so ClosedXML accepts the name.
        var sheet = wb.AddSheet("Weekly/Report: 2026 [draft] *****************************");
        sheet.SetText(1, 1, "ok");

        using var read = RoundTrip(wb);
        Assert.Single(read.Worksheets);
        Assert.True(read.Worksheets.First().Name.Length <= 31);
    }
}
