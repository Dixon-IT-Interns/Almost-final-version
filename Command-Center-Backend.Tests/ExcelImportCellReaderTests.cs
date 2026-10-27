using ClosedXML.Excel;
using Dixon.CommandCenter.API.Services;
using Xunit;

namespace Dixon.CommandCenter.API.Tests;

public sealed class ExcelImportCellReaderTests
{
    [Fact]
    public void ReadsMergedValueForEveryCellInsideMergedRange()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Quality");
        worksheet.Cell("A2").Value = "FA-01";
        worksheet.Cell("A3").Value = "FA-02";
        worksheet.Cell("A4").Value = "FA-03";
        worksheet.Cell("F2").Value = 0.985m;
        worksheet.Cell("G2").Value = 0.97m;
        worksheet.Cell("H2").Value = 0.95m;
        worksheet.Range("F2:F4").Merge();
        worksheet.Range("G2:G4").Merge();
        worksheet.Range("H2:H4").Merge();

        foreach (var row in new[] { 2, 3, 4 })
        {
            Assert.True(ExcelImportCellReader.TryReadNumber(
                worksheet.Cell(row, 6),
                out var value));
            Assert.Equal(0.985m, value);
            Assert.True(ExcelImportCellReader.TryReadNumber(
                worksheet.Cell(row, 7),
                out var fty));
            Assert.Equal(0.97m, fty);
            Assert.True(ExcelImportCellReader.TryReadNumber(
                worksheet.Cell(row, 8),
                out var rty));
            Assert.Equal(0.95m, rty);
        }
    }

    [Fact]
    public void DoesNotForwardFillBlankOutsideMergedRange()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Quality");
        worksheet.Cell("F2").Value = 0.985m;
        worksheet.Range("F2:F4").Merge();

        Assert.True(ExcelImportCellReader.TryReadNumber(worksheet.Cell("F5"), out var value));

        Assert.Null(value);
    }

    [Theory]
    [InlineData(0.29, "0.29")]
    [InlineData(0.985, "0.985")]
    [InlineData(24025.17, "24025.17")]
    [InlineData(131305, "131305")]
    [InlineData(2929.5, "2929.5")]
    public void PreservesNumericCellPrecision(double source, string expected)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Values");
        worksheet.Cell("A1").Value = source;

        Assert.True(ExcelImportCellReader.TryReadNumber(worksheet.Cell("A1"), out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Fact]
    public void PreservesUnderlyingPercentageValueWithoutScaling()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Quality");
        var cell = worksheet.Cell("A1");
        cell.Value = 0.985m;
        cell.Style.NumberFormat.Format = "0.00%";

        Assert.True(ExcelImportCellReader.TryReadNumber(cell, out var value));

        Assert.Equal(0.985m, value);
    }

    [Fact]
    public void PreservesCurrencyCellNumericValue()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Operations");
        var cell = worksheet.Cell("A1");
        cell.Value = 24025.17m;
        cell.Style.NumberFormat.Format = "\"₹\"#,##0.00";

        Assert.True(ExcelImportCellReader.TryReadNumber(cell, out var value));

        Assert.Equal(24025.17m, value);
    }

    [Fact]
    public void ParsesInvariantTextNumberAndLeavesBlankNull()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Values");
        worksheet.Cell("A1").Value = "24,025.17";

        Assert.True(ExcelImportCellReader.TryReadNumber(worksheet.Cell("A1"), out var value));
        Assert.Equal(24025.17m, value);
        Assert.True(ExcelImportCellReader.TryReadNumber(worksheet.Cell("A2"), out var blank));
        Assert.Null(blank);
    }

    [Fact]
    public void RejectsInvalidTextInsteadOfTurningItIntoZero()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Values");
        worksheet.Cell("A1").Value = "not-a-number";

        Assert.False(ExcelImportCellReader.TryReadNumber(worksheet.Cell("A1"), out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("N/P", "N/P")]
    [InlineData("n/p", "N/P")]
    [InlineData("NP", "N/P")]
    [InlineData("n p", "N/P")]
    [InlineData("N/A", "N/A")]
    [InlineData("n/a", "N/A")]
    [InlineData("NA", "N/A")]
    [InlineData("n a", "N/A")]
    public void ReadsSupportedNonNumericMarkers(string source, string expected)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Values");
        worksheet.Cell("A1").Value = source;

        Assert.True(ExcelImportCellReader.TryReadNumberOrStatus(
            worksheet.Cell("A1"),
            out var number,
            out var nonNumericValue));

        Assert.Null(number);
        Assert.Equal(expected, nonNumericValue);
    }

    [Fact]
    public void ReadsNonNumericMarkerFromMergedRangeOnly()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Values");
        worksheet.Cell("A1").Value = "N/P";
        worksheet.Range("A1:A2").Merge();

        Assert.True(ExcelImportCellReader.TryReadNumberOrStatus(
            worksheet.Cell("A2"),
            out var number,
            out var nonNumericValue));
        Assert.Null(number);
        Assert.Equal("N/P", nonNumericValue);

        Assert.True(ExcelImportCellReader.TryReadNumberOrStatus(
            worksheet.Cell("A3"),
            out var blankNumber,
            out var blankMarker));
        Assert.Null(blankNumber);
        Assert.Null(blankMarker);
    }
}
