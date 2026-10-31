using System.Globalization;
using ClosedXML.Excel;

namespace Dixon.CommandCenter.API.Services;

internal static class ExcelImportCellReader
{
    private static readonly HashSet<string> EmptyNumericValues =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "",
            "-",
            "–",
            "—",
            "−",
            "NA",
            "N/A",
            "NP",
            "NO PLAN"
        };

    internal static IXLCell ResolveMergedCell(IXLCell cell)
    {
        var row = cell.Address.RowNumber;
        var column = cell.Address.ColumnNumber;

        foreach (var range in cell.Worksheet.MergedRanges)
        {
            var address = range.RangeAddress;
            if (row >= address.FirstAddress.RowNumber
                && row <= address.LastAddress.RowNumber
                && column >= address.FirstAddress.ColumnNumber
                && column <= address.LastAddress.ColumnNumber)
            {
                return cell.Worksheet.Cell(
                    address.FirstAddress.RowNumber,
                    address.FirstAddress.ColumnNumber);
            }
        }

        return cell;
    }

    internal static bool TryReadNumber(IXLCell cell, out decimal? value)
    {
        var sourceCell = ResolveMergedCell(cell);
        value = null;
        if (sourceCell.IsEmpty())
            return true;

        if (sourceCell.TryGetValue<decimal>(out var number))
        {
            value = number;
            return true;
        }

        var text = sourceCell.GetString().Trim();
        if (EmptyNumericValues.Contains(text))
            return true;

        if (!decimal.TryParse(
                text,
                NumberStyles.Number | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out number))
        {
            return false;
        }

        value = number;
        return true;
    }

    internal static bool TryReadNumberOrStatus(
        IXLCell cell,
        out decimal? value,
        out string? nonNumericValue)
    {
        var sourceCell = ResolveMergedCell(cell);
        value = null;
        nonNumericValue = null;
        var text = sourceCell.GetString().Trim();
        var marker = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        if (marker.Equals("N/P", StringComparison.OrdinalIgnoreCase)
            || marker.Equals("NP", StringComparison.OrdinalIgnoreCase))
        {
            nonNumericValue = "N/P";
            return true;
        }

        if (marker.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || marker.Equals("NA", StringComparison.OrdinalIgnoreCase))
        {
            nonNumericValue = "N/A";
            return true;
        }

        return TryReadNumber(sourceCell, out value);
    }

    internal static bool TryReadPercentageOrStatus(
        IXLCell cell,
        out decimal? value,
        out string? nonNumericValue)
    {
        if (!TryReadNumberOrStatus(cell, out value, out nonNumericValue))
            return false;

        if (value.HasValue)
            value *= 100m;

        return true;
    }

    internal static bool TryReadFATPNumber(IXLCell cell, out decimal? value)
    {
        var sourceCell = ResolveMergedCell(cell);
        if (string.Equals(sourceCell.GetString().Trim(), "No Plan", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        return TryReadNumber(sourceCell, out value);
    }
}
