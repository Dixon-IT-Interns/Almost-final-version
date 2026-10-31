using System.Security.Claims;
using System.Text.Json;
using ClosedXML.Excel;
using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Models;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Viewer")]
[Route("api/viewer/analytics")]
public sealed class ViewerAnalyticsController(
    ViewerAnalyticsRepository analytics,
    ViewerAccessRepository viewerAccess) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] int? plantId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int[]? customerIds,
        [FromQuery] int[]? categoryIds,
        [FromQuery] int[]? lineIds,
        [FromQuery] int[]? modelIds,
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFilters(customerIds, categoryIds, lineIds, modelIds, out var filters)
            || !TryParseRange(startDate, endDate, out var start, out var end))
        {
            return BadRequest(new { message = "A valid date range and positive filter identifiers are required." });
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        var scopeResult = await GetPlantScopeAsync(plantId, cancellationToken);
        if (scopeResult.Error is not null)
        {
            return scopeResult.Error;
        }

        if (!HasCustomerAccess(scopeResult.PlantId, filters.Customers, scopeResult.Scope!))
        {
            return Forbid();
        }

        var periodLength = (end - start).Days + 1;
        var previousStart = start.AddDays(-periodLength);
        return Ok(await analytics.GetOverviewAsync(
            scopeResult.ViewerId,
            scopeResult.PlantId,
            filters.Customers,
            filters.Categories,
            filters.Lines,
            filters.Models,
            process,
            start,
            end,
            previousStart,
            cancellationToken));
    }

    [HttpGet("filters")]
    public async Task<IActionResult> GetFilters(
        [FromQuery] int? plantId,
        [FromQuery] int[]? customerIds,
        [FromQuery] int[]? categoryIds,
        [FromQuery] int[]? lineIds,
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFilters(customerIds, categoryIds, lineIds, [], out var filters))
        {
            return BadRequest(new { message = "Filter identifiers must be positive integers." });
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        var scopeResult = await GetPlantScopeAsync(plantId, cancellationToken);
        if (scopeResult.Error is not null)
        {
            return scopeResult.Error;
        }

        if (!HasCustomerAccess(scopeResult.PlantId, filters.Customers, scopeResult.Scope!))
        {
            return Forbid();
        }

        return Ok(await analytics.GetFilterOptionsAsync(
            scopeResult.ViewerId,
            scopeResult.PlantId,
            filters.Customers,
            filters.Categories,
            filters.Lines,
            process,
            cancellationToken));
    }

    [HttpGet("chart-data")]
    public async Task<IActionResult> GetChartData(
        [FromQuery] int? plantId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int[]? customerIds,
        [FromQuery] int[]? categoryIds,
        [FromQuery] int[]? lineIds,
        [FromQuery] int[]? modelIds,
        [FromQuery] string dimension = "date",
        [FromQuery] string? groupBy = null,
        [FromQuery] string[]? metrics = null,
        [FromQuery] string aggregation = "SUM",
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFilters(customerIds, categoryIds, lineIds, modelIds, out var filters)
            || !TryParseRange(startDate, endDate, out var start, out var end))
        {
            return BadRequest(new { message = "A valid date range and positive filter identifiers are required." });
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        var scopeResult = await GetPlantScopeAsync(plantId, cancellationToken);
        if (scopeResult.Error is not null)
        {
            return scopeResult.Error;
        }

        if (!HasCustomerAccess(scopeResult.PlantId, filters.Customers, scopeResult.Scope!))
        {
            return Forbid();
        }

        var selectedMetrics = (metrics ?? []).Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selectedMetrics.Length is < 1 or > 9)
        {
            return BadRequest(new { message = "Select between one and nine chart metrics." });
        }

        try
        {
            return Ok(await analytics.GetChartDataAsync(
                scopeResult.ViewerId,
                scopeResult.PlantId,
                filters.Customers,
                filters.Categories,
                filters.Lines,
                filters.Models,
                process,
                start,
                end,
                dimension.Trim(),
                groupBy?.Trim(),
                selectedMetrics,
                aggregation.Trim().ToUpperInvariant(),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("data")]
    public async Task<IActionResult> GetExplorerData(
        [FromQuery] int? plantId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int[]? customerIds,
        [FromQuery] int[]? categoryIds,
        [FromQuery] int[]? lineIds,
        [FromQuery] int[]? modelIds,
        [FromQuery] string? search,
        [FromQuery] string sortBy = "date",
        [FromQuery] string sortDirection = "DESC",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFilters(customerIds, categoryIds, lineIds, modelIds, out var filters)
            || !TryParseRange(startDate, endDate, out var start, out var end))
        {
            return BadRequest(new { message = "A valid date range and positive filter identifiers are required." });
        }

        if (page is < 1 or > 100_000 || pageSize is not (10 or 25 or 50 or 100)
            || (search?.Length ?? 0) > 120)
        {
            return BadRequest(new { message = "Explorer pagination or search values are invalid." });
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        var scopeResult = await GetPlantScopeAsync(plantId, cancellationToken);
        if (scopeResult.Error is not null)
        {
            return scopeResult.Error;
        }

        if (!HasCustomerAccess(scopeResult.PlantId, filters.Customers, scopeResult.Scope!))
        {
            return Forbid();
        }

        try
        {
            return Ok(await analytics.GetExplorerRowsAsync(
                scopeResult.ViewerId,
                scopeResult.PlantId,
                filters.Customers,
                filters.Categories,
                filters.Lines,
                filters.Models,
                process,
                start,
                end,
                search,
                sortBy.Trim(),
                sortDirection.Trim().ToUpperInvariant(),
                page,
                pageSize,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportFilteredData(
        [FromQuery] int? plantId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int[]? customerIds,
        [FromQuery] int[]? categoryIds,
        [FromQuery] int[]? lineIds,
        [FromQuery] int[]? modelIds,
        [FromQuery] string? search,
        [FromQuery] string sortBy = "date",
        [FromQuery] string sortDirection = "DESC",
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFilters(customerIds, categoryIds, lineIds, modelIds, out var filters)
            || !TryParseRange(startDate, endDate, out var start, out var end)
            || (search?.Length ?? 0) > 120)
        {
            return BadRequest(new { message = "A valid date range, filters and search are required." });
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        var scopeResult = await GetPlantScopeAsync(plantId, cancellationToken);
        if (scopeResult.Error is not null)
        {
            return scopeResult.Error;
        }

        if (!HasCustomerAccess(scopeResult.PlantId, filters.Customers, scopeResult.Scope!))
        {
            return Forbid();
        }

        ViewerAnalyticsExplorerResponse result;
        try
        {
            result = await analytics.GetExplorerRowsAsync(
                scopeResult.ViewerId,
                scopeResult.PlantId,
                filters.Customers,
                filters.Categories,
                filters.Lines,
                filters.Models,
                process,
                start,
                end,
                search,
                sortBy.Trim(),
                sortDirection.Trim().ToUpperInvariant(),
                1,
                10_000,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }

        if (result.Summary.TotalRows > result.Rows.Count)
        {
            return BadRequest(new { message = "The filtered export exceeds 10,000 rows. Narrow the date range or filters and retry." });
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Analytics");
        var headers = new[] { "Reporting Date", "Customer", "Category", "Production Line", "Model",
            "Production Actual", "Production Target", "FPY Actual", "FTY Actual", "RTY Actual" };
        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
            sheet.Cell(1, column + 1).Style.Font.Bold = true;
        }

        for (var index = 0; index < result.Rows.Count; index++)
        {
            var item = result.Rows[index];
            var row = index + 2;
            sheet.Cell(row, 1).Value = item.Date;
            sheet.Cell(row, 2).Value = item.Customer;
            sheet.Cell(row, 3).Value = item.Category;
            sheet.Cell(row, 4).Value = item.ProductionLine;
            sheet.Cell(row, 5).Value = item.Model;
            sheet.Cell(row, 6).Value = item.ProductionActual;
            sheet.Cell(row, 7).Value = item.ProductionTarget;
            sheet.Cell(row, 8).Value = item.Fpy;
            sheet.Cell(row, 9).Value = item.Fty;
            sheet.Cell(row, 10).Value = item.Rty;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Dixon_Analytics_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx");
    }

    [HttpGet("saved-views")]
    public async Task<IActionResult> GetSavedViews(CancellationToken cancellationToken)
    {
        if (!await IsActiveViewerAsync(cancellationToken))
        {
            return Forbid();
        }

        return Ok(await analytics.GetSavedViewsAsync(GetViewerId(), cancellationToken));
    }

    [HttpPost("saved-views")]
    public async Task<IActionResult> CreateSavedView(
        [FromBody] SaveViewerSavedViewRequest request,
        CancellationToken cancellationToken)
    {
        if (!await IsActiveViewerAsync(cancellationToken))
        {
            return Forbid();
        }

        if (!TryValidateSavedView(request, out var name, out var configurationJson))
        {
            return BadRequest(new { message = "A name (up to 100 characters) and valid JSON configuration up to 64 KB are required." });
        }

        var created = await analytics.CreateSavedViewAsync(
            GetViewerId(), name, configurationJson, cancellationToken);
        return CreatedAtAction(nameof(GetSavedViews), new { id = created.Id }, created);
    }

    [HttpPut("saved-views/{id:int}")]
    public async Task<IActionResult> UpdateSavedView(
        int id,
        [FromBody] SaveViewerSavedViewRequest request,
        CancellationToken cancellationToken)
    {
        if (!await IsActiveViewerAsync(cancellationToken))
        {
            return Forbid();
        }

        if (id <= 0 || !TryValidateSavedView(request, out var name, out var configurationJson))
        {
            return BadRequest(new { message = "A valid saved view identifier, name and JSON configuration are required." });
        }

        var updated = await analytics.UpdateSavedViewAsync(
            GetViewerId(), id, name, configurationJson, cancellationToken);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("saved-views/{id:int}")]
    public async Task<IActionResult> DeleteSavedView(int id, CancellationToken cancellationToken)
    {
        if (!await IsActiveViewerAsync(cancellationToken))
        {
            return Forbid();
        }

        if (id <= 0)
        {
            return BadRequest(new { message = "A valid saved view identifier is required." });
        }

        var deleted = await analytics.DeleteSavedViewAsync(GetViewerId(), id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private async Task<PlantScopeResult> GetPlantScopeAsync(
        int? requestedPlantId,
        CancellationToken cancellationToken)
    {
        var viewerId = GetViewerId();
        var scope = await viewerAccess.GetScopeAsync(viewerId, cancellationToken);
        if (scope is null)
        {
            return PlantScopeResult.Failed(Forbid());
        }

        if (!int.TryParse(User.FindFirstValue("plantId"), out var assignedPlantId)
            || assignedPlantId <= 0
            || requestedPlantId is <= 0)
        {
            return PlantScopeResult.Failed(Unauthorized());
        }

        var selectedPlantId = requestedPlantId ?? assignedPlantId;
        if (!await viewerAccess.IsActivePlantAsync(selectedPlantId, scope, viewerId, cancellationToken))
        {
            return PlantScopeResult.Failed(Forbid());
        }

        return PlantScopeResult.Success(viewerId, selectedPlantId, scope);
    }

    private static bool HasCustomerAccess(
        int plantId,
        IReadOnlyCollection<int> customerIds,
        ViewerAccessScope scope) =>
        customerIds.All(customerId =>
            ViewerAccessScopePolicy.CanAccessCustomer(scope, plantId, customerId, true));

    private int GetViewerId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var viewerId)
            ? viewerId
            : 0;

    private async Task<bool> IsActiveViewerAsync(CancellationToken cancellationToken) =>
        await viewerAccess.GetScopeAsync(GetViewerId(), cancellationToken) is not null;

    private static bool TryParseFilters(
        int[]? customerIds,
        int[]? categoryIds,
        int[]? lineIds,
        int[]? modelIds,
        out AnalyticsFilters filters)
    {
        filters = new AnalyticsFilters([], [], [], []);
        if (!NormalizeIds(customerIds, out var customers)
            || !NormalizeIds(categoryIds, out var categories)
            || !NormalizeIds(lineIds, out var lines)
            || !NormalizeIds(modelIds, out var models))
        {
            return false;
        }

        filters = new AnalyticsFilters(customers, categories, lines, models);
        return true;
    }

    private static bool NormalizeIds(int[]? values, out IReadOnlyList<int> normalized)
    {
        normalized = [];
        if (values is null)
        {
            return true;
        }

        if (values.Length > 100 || values.Any(value => value <= 0))
        {
            return false;
        }

        normalized = values.Distinct().ToArray();
        return true;
    }

    private static bool TryParseRange(
        DateTime? startDate,
        DateTime? endDate,
        out DateTime start,
        out DateTime end)
    {
        start = startDate?.Date ?? default;
        end = endDate?.Date ?? default;
        return startDate.HasValue
            && endDate.HasValue
            && end >= start
            && (end - start).TotalDays < 366;
    }

    private static bool TryNormalizeProcess(string? value, out string process)
    {
        process = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return process is "ALL" or "FATP" or "SMT";
    }

    private static bool TryValidateSavedView(
        SaveViewerSavedViewRequest request,
        out string name,
        out string configurationJson)
    {
        name = request.Name?.Trim() ?? string.Empty;
        configurationJson = request.ConfigurationJson ?? string.Empty;
        if (name.Length is 0 or > 100 || configurationJson.Length is 0 or > 65_536)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(configurationJson);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record AnalyticsFilters(
        IReadOnlyList<int> Customers,
        IReadOnlyList<int> Categories,
        IReadOnlyList<int> Lines,
        IReadOnlyList<int> Models);

    private sealed record PlantScopeResult(
        int ViewerId,
        int PlantId,
        ViewerAccessScope? Scope,
        IActionResult? Error)
    {
        public static PlantScopeResult Failed(IActionResult error) => new(0, 0, null, error);
        public static PlantScopeResult Success(int viewerId, int plantId, ViewerAccessScope scope) =>
            new(viewerId, plantId, scope, null);
    }
}
