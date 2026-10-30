using System.Security.Claims;
using ClosedXML.Excel;
using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Viewer")]
[Route("api/viewer")]
public sealed class ViewerController(
    ViewerRepository viewer,
    ViewerAccessRepository viewerAccess) : ControllerBase
{
    [HttpGet("progress")]
    public async Task<IActionResult> GetProgress(
        [FromQuery] DateTime? reportDate,
        [FromQuery] int? plantId,
        [FromQuery] int? customerId,
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (reportDate is null)
        {
            return BadRequest(new { message = "A reporting date is required." });
        }

        var viewerId = GetUserId();
        var scope = await viewerAccess.GetScopeAsync(viewerId, cancellationToken);
        if (scope is null)
        {
            return Forbid();
        }

        if (!TryGetScope(plantId, out var selectedPlantId))
        {
            return Unauthorized();
        }

        if (!await viewerAccess.IsActivePlantAsync(
                selectedPlantId, scope, viewerId, cancellationToken))
        {
            return Forbid();
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        if (customerId is not null
            && !await viewerAccess.HasCustomerAccessAsync(
                viewerId, selectedPlantId, customerId.Value, scope, cancellationToken))
        {
            return Forbid();
        }

        return Ok(await viewer.GetProgressAsync(
            viewerId, selectedPlantId, customerId, process, reportDate.Value, cancellationToken));
    }

    [HttpGet("plants")]
    public async Task<IActionResult> GetPlants(CancellationToken cancellationToken)
    {
        var viewerId = GetUserId();
        var scope = await viewerAccess.GetScopeAsync(viewerId, cancellationToken);
        if (scope is null)
        {
            return Forbid();
        }

        return Ok(await viewer.GetPlantsAsync(viewerId, cancellationToken));
    }

    [HttpGet("customers")]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] int? plantId,
        CancellationToken cancellationToken)
    {
        var viewerId = GetUserId();
        var scope = await viewerAccess.GetScopeAsync(viewerId, cancellationToken);
        if (scope is null)
        {
            return Forbid();
        }

        if (!TryGetScope(plantId, out var selectedPlantId))
        {
            return Unauthorized();
        }

        if (!await viewerAccess.IsActivePlantAsync(
                selectedPlantId, scope, viewerId, cancellationToken))
        {
            return Forbid();
        }

        return Ok(await viewer.GetCustomersAsync(viewerId, selectedPlantId, cancellationToken));
    }

    [HttpGet("trend")]
    public async Task<IActionResult> GetTrend(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int? plantId,
        [FromQuery] int? customerId,
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (startDate is null || endDate is null || endDate.Value.Date < startDate.Value.Date)
        {
            return BadRequest(new { message = "A valid start and end date range is required." });
        }

        if ((endDate.Value.Date - startDate.Value.Date).TotalDays > 365)
        {
            return BadRequest(new { message = "The trend range cannot exceed 366 days." });
        }

        var viewerId = GetUserId();
        var scope = await viewerAccess.GetScopeAsync(viewerId, cancellationToken);
        if (scope is null)
        {
            return Forbid();
        }

        if (!TryGetScope(plantId, out var selectedPlantId))
        {
            return Unauthorized();
        }

        if (!await viewerAccess.IsActivePlantAsync(
                selectedPlantId, scope, viewerId, cancellationToken))
        {
            return Forbid();
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        if (customerId is not null
            && !await viewerAccess.HasCustomerAccessAsync(
                viewerId, selectedPlantId, customerId.Value, scope, cancellationToken))
        {
            return Forbid();
        }

        return Ok(await viewer.GetTrendAsync(
            viewerId, selectedPlantId, customerId, process, startDate.Value, endDate.Value, cancellationToken));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] DateTime? reportDate,
        [FromQuery] int? plantId,
        [FromQuery] int? customerId,
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (reportDate is null)
        {
            return BadRequest(new { message = "A reporting date is required." });
        }

        var viewerId = GetUserId();
        var scope = await viewerAccess.GetScopeAsync(viewerId, cancellationToken);
        if (scope is null)
        {
            return Forbid();
        }

        if (!TryGetScope(plantId, out var selectedPlantId))
        {
            return Unauthorized();
        }

        if (!await viewerAccess.IsActivePlantAsync(
                selectedPlantId, scope, viewerId, cancellationToken))
        {
            return Forbid();
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        if (customerId is not null
            && !await viewerAccess.HasCustomerAccessAsync(
                viewerId, selectedPlantId, customerId.Value, scope, cancellationToken))
        {
            return Forbid();
        }

        var templatePath = Path.Combine(
            AppContext.BaseDirectory, "Templates", "DixonDailyReportTemplate.xlsx");
        if (!System.IO.File.Exists(templatePath))
        {
            return Problem(
                "The styled daily report template is not available on the server.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var progress = await viewer.GetProgressAsync(
            viewerId, selectedPlantId, customerId, process, reportDate.Value, cancellationToken);
        await using var templateFile = System.IO.File.OpenRead(templatePath);
        await using var templateStream = new MemoryStream();
        await templateFile.CopyToAsync(templateStream, cancellationToken);
        ExcelWorkbookCompatibility.RemoveUnsupportedInlineListValidations(templateStream);
        using var workbook = new XLWorkbook(templateStream);
        var sheet = workbook.Worksheet(1);
        sheet.Cell("B2").Value = reportDate.Value.Date;
        PopulateReportSection(sheet, progress.Lines, "FATP");
        PopulateReportSection(sheet, progress.Lines, "SMT");
        ClearTemplateSampleRows(workbook);
        workbook.Worksheet(3).Cell("D2").Value = reportDate.Value.Date;

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Dixon_Viewer_{reportDate.Value:yyyyMMdd}.xlsx");
    }

    private bool TryGetScope(int? requestedPlantId, out int plantId)
    {
        plantId = 0;
        if (!int.TryParse(User.FindFirstValue("plantId"), out var assignedPlantId)
            || assignedPlantId <= 0
            || requestedPlantId is <= 0)
        {
            return false;
        }

        plantId = requestedPlantId ?? assignedPlantId;
        return plantId > 0;
    }

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : 0;

    private static bool TryNormalizeProcess(string? value, out string process)
    {
        process = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return process is "ALL" or "FATP" or "SMT";
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Clear(XLClearOptions.Contents);
                break;
            case string text:
                cell.Value = text;
                break;
            case int number:
                cell.Value = number;
                break;
            case decimal number:
                cell.Value = number;
                break;
            default:
                throw new InvalidOperationException($"Unsupported Excel cell value type: {value.GetType().Name}.");
        }
    }

    private static void PopulateReportSection(
        IXLWorksheet sheet,
        IReadOnlyList<ViewerLineProgress> lines,
        string process)
    {
        var headerRow = Enumerable.Range(1, sheet.LastRowUsed()?.RowNumber() ?? 1)
            .FirstOrDefault(row =>
            {
                var isHeader = string.Equals(
                        sheet.Cell(row, 1).GetString().Trim(), "Line No.", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        sheet.Cell(row, 3).GetString().Trim(), "Customer", StringComparison.OrdinalIgnoreCase);
                var metric = sheet.Cell(row, 11).GetString();
                return isHeader && (process == "FATP"
                    ? metric.Contains("UPPH", StringComparison.OrdinalIgnoreCase)
                    : metric.Contains("CPH", StringComparison.OrdinalIgnoreCase));
            });
        if (headerRow == 0)
        {
            throw new InvalidOperationException($"The daily report template has no {process} section.");
        }

        var totalRow = Enumerable.Range(headerRow + 2, (sheet.LastRowUsed()?.RowNumber() ?? headerRow + 2) - headerRow - 1)
            .FirstOrDefault(row => string.Equals(
                sheet.Cell(row, 1).GetString().Trim(), "Total", StringComparison.OrdinalIgnoreCase));
        if (totalRow == 0)
        {
            throw new InvalidOperationException($"The daily report template has no {process} totals row.");
        }

        var dataStart = headerRow + 2;
        var capacity = totalRow - dataStart;
        var reportLines = lines
            .Where(line => line.DailyReportId.HasValue && line.Process == process)
            .ToArray();
        var additionalRows = Math.Max(0, reportLines.Length - capacity);
        const int lastColumn = 31;

        for (var row = dataStart; row < totalRow; row++)
        {
            for (var column = 1; column <= lastColumn; column++)
                sheet.Cell(row, column).Clear(XLClearOptions.Contents);
        }

        if (additionalRows > 0)
        {
            var styleSource = totalRow - 1;
            var firstInsertedRow = totalRow;
            var sourceHeight = sheet.Row(styleSource).Height;
            sheet.Row(totalRow).InsertRowsAbove(additionalRows);
            for (var row = firstInsertedRow; row < firstInsertedRow + additionalRows; row++)
            {
                sheet.Row(row).Height = sourceHeight;
                for (var column = 1; column <= lastColumn; column++)
                {
                    sheet.Cell(row, column).Clear(XLClearOptions.Contents);
                    sheet.Cell(row, column).Style = sheet.Cell(styleSource, column).Style;
                }
            }
            totalRow += additionalRows;
        }

        for (var index = 0; index < reportLines.Length; index++)
            WriteReportLine(sheet, dataStart + index, reportLines[index], process);

        for (var column = 1; column <= lastColumn; column++)
        {
            if (column != 1)
                sheet.Cell(totalRow, column).Clear(XLClearOptions.Contents);
        }

        SetTotal(sheet, totalRow, 5, reportLines, line => line.MonthPlan);
        SetTotal(sheet, totalRow, 6, reportLines, line => line.MonthActual);
        SetTotal(sheet, totalRow, 7, reportLines, line => line.ProductionPlan);
        SetTotal(sheet, totalRow, 8, reportLines, line => line.ProductionActual);
        SetTotal(sheet, totalRow, 19, reportLines, line => line.OSDReportingDateValue);
        SetTotal(sheet, totalRow, 21, reportLines, line => line.OSDMTDValue);
        SetTotal(sheet, totalRow, 23, reportLines, line => line.PlannedOTManhours);
        SetTotal(sheet, totalRow, 24, reportLines, line => line.UnplannedOTManhours);
        SetTotal(sheet, totalRow, 25, reportLines, line => line.OpenWOQty);
        SetTotal(sheet, totalRow, 26, reportLines, line => line.Over7DaysWOBalanceQty);
        SetTotal(sheet, totalRow, 27, reportLines, line => line.DailyTRCInQty);
        SetTotal(sheet, totalRow, 28, reportLines, line => line.DailyTRCOutQty);
        SetTotal(sheet, totalRow, 30, reportLines, line => line.TRCLyingOver3DaysCr);
    }

    private static void WriteReportLine(
        IXLWorksheet sheet,
        int row,
        ViewerLineProgress line,
        string process)
    {
        object? Value(string field, object? numericValue) =>
            numericValue ?? (line.TextValues.TryGetValue(field, out var textValue) ? textValue : null);
        object? Percentage(string field, decimal? numericValue)
        {
            var value = Value(field, numericValue);
            return value is decimal number ? number / 100m : value;
        }

        var values = new object?[]
        {
            line.LineName, line.SAPLocation, line.CustomerName, line.ModelName,
            Value("monthPlan", line.MonthPlan), Value("monthActual", line.MonthActual),
            Value("productionPlan", line.ProductionPlan), Value("productionActual", line.ProductionActual),
            Value("uphTarget", line.UPHTarget), Value("uphActual", line.UPHActual),
            Value(process == "FATP" ? "upphInstalled" : "cphTarget", process == "FATP" ? line.UPPHInstalled : line.CPHTarget),
            Value(process == "FATP" ? "upphActual" : "cphActual", process == "FATP" ? line.UPPHActual : line.CPHActual),
            Percentage("fpyTarget", line.FPYTarget), Percentage("fpyActual", line.FPYActual),
            Percentage("ftyTarget", line.FTYTarget), Percentage("ftyActual", line.FTYActual),
            Percentage("rtyTarget", line.RTYTarget), Percentage("rtyActual", line.RTYActual),
            Value("osdReportingDateValue", line.OSDReportingDateValue),
            Percentage("osdReportingDatePercent", line.OSDReportingDatePercent),
            Value("osdMtdValue", line.OSDMTDValue), Percentage("osdMtdPercent", line.OSDMTDPercent),
            Value("plannedOTManhours", line.PlannedOTManhours), Value("unplannedOTManhours", line.UnplannedOTManhours),
            Value("openWOQty", line.OpenWOQty), Value("over7DaysWOBalanceQty", line.Over7DaysWOBalanceQty),
            Value("dailyTRCInQty", line.DailyTRCInQty), Value("dailyTRCOutQty", line.DailyTRCOutQty),
            Percentage("trcOverallFailureInflowPercent", line.TRCOverallFailureInflowPercent),
            Value("trcLyingOver3DaysCr", line.TRCLyingOver3DaysCr), line.IssueDescription
        };

        for (var index = 0; index < values.Length; index++)
            SetCellValue(sheet.Cell(row, index + 1), values[index]);
    }

    private static void SetTotal(
        IXLWorksheet sheet,
        int row,
        int column,
        IReadOnlyList<ViewerLineProgress> lines,
        Func<ViewerLineProgress, decimal?> valueSelector)
    {
        var values = lines.Select(valueSelector).Where(value => value.HasValue).ToArray();
        SetCellValue(sheet.Cell(row, column), values.Length == 0 ? null : values.Sum(value => value!.Value));
    }

    private static void ClearTemplateSampleRows(XLWorkbook workbook)
    {
        var reportSheet = workbook.Worksheet(1);
        foreach (var picture in reportSheet.Pictures.ToList())
            picture.Delete();

        var qualitySheet = workbook.Worksheet(2);
        qualitySheet.Range("B5:Q8").Clear(XLClearOptions.Contents);
        qualitySheet.Range("I11:O13").Clear(XLClearOptions.Contents);
        qualitySheet.Range("B19:Q24").Clear(XLClearOptions.Contents);
        qualitySheet.Cell("P27").Clear(XLClearOptions.Contents);

        var reviewSheet = workbook.Worksheet(3);
        reviewSheet.Range("B7:P27").Clear(XLClearOptions.Contents);
        reviewSheet.Range("B31:P40").Clear(XLClearOptions.Contents);
    }
}