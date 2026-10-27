using System.Data;
using System.Text.Json;
using Dixon.CommandCenter.API.Models;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Data;

public sealed class ViewerRepository(SqlConnectionFactory connectionFactory)
{
    private const string AccessibleLines = """
        WITH AccessibleLines AS
        (
            SELECT DISTINCT
                c.CustomerId,
                c.CustomerName,
                cat.CategoryId,
                cat.CategoryName AS Process,
                pl.LineId,
                pl.LineName,
                pl.SAPLocation
            FROM ViewerAccess va
            INNER JOIN [User] u ON u.UserId = va.UserId AND u.IsActive = 1
            INNER JOIN Role r ON r.RoleId = u.RoleId AND r.RoleName = 'Viewer'
            INNER JOIN PlantCustomer pc ON pc.PlantId = @PlantId
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            INNER JOIN Plant p ON p.PlantId = pc.PlantId
            INNER JOIN Category cat
                ON cat.CustomerId = c.CustomerId
               AND cat.PlantId = pc.PlantId
               AND cat.IsActive = 1
            INNER JOIN ProductionLine pl
                ON pl.CategoryId = cat.CategoryId
               AND pl.PlantId = pc.PlantId
               AND pl.IsActive = 1
            WHERE va.UserId = @ViewerId
              AND c.IsActive = 1
              AND p.IsActive = 1
              AND (va.AllPlants = 1 OR EXISTS
                   (SELECT 1 FROM ViewerAccessPlant vap
                    WHERE vap.UserId = va.UserId AND vap.PlantId = pc.PlantId))
              AND (va.AllCustomers = 1
                   OR (va.AllPlants = 1 AND EXISTS
                       (SELECT 1 FROM ViewerAccessCustomer vac
                        WHERE vac.UserId = va.UserId AND vac.CustomerId = pc.CustomerId))
                   OR (va.AllPlants = 0 AND EXISTS
                       (SELECT 1 FROM ViewerAccessPlantCustomer vapc
                        WHERE vapc.UserId = va.UserId
                          AND vapc.PlantId = pc.PlantId
                          AND vapc.CustomerId = pc.CustomerId)))
              AND (@CustomerId IS NULL OR c.CustomerId = @CustomerId)
              AND (@Process = 'ALL' OR cat.CategoryName = @Process)
        )
        """;

    public async Task<IReadOnlyList<PlantResponse>> GetPlantsAsync(
        int viewerId,
        CancellationToken cancellationToken)
    {
        var plants = new List<PlantResponse>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT p.PlantId, p.PlantName, p.Location
            FROM Plant p
            INNER JOIN ViewerAccess va ON va.UserId = @ViewerId
            INNER JOIN [User] u ON u.UserId = va.UserId AND u.IsActive = 1
            INNER JOIN Role r ON r.RoleId = u.RoleId AND r.RoleName = 'Viewer'
            WHERE p.IsActive = 1
              AND (va.AllPlants = 1 OR EXISTS
                   (SELECT 1 FROM ViewerAccessPlant vap
                    WHERE vap.UserId = va.UserId AND vap.PlantId = p.PlantId))
            ORDER BY p.PlantName;
            """, connection);
        Add(command, "@ViewerId", SqlDbType.Int, viewerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            plants.Add(new PlantResponse(
                reader.GetInt32(0),
                reader.GetString(1),
                GetString(reader, 2)));
        }

        return plants;
    }

    public async Task<IReadOnlyList<ViewerCustomerResponse>> GetCustomersAsync(
        int viewerId,
        int plantId,
        CancellationToken cancellationToken)
    {
        var customers = new List<ViewerCustomerResponse>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT c.CustomerId, c.CustomerName
            FROM PlantCustomer pc
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            INNER JOIN Plant p ON p.PlantId = pc.PlantId
            INNER JOIN ViewerAccess va ON va.UserId = @ViewerId
            INNER JOIN [User] u ON u.UserId = va.UserId AND u.IsActive = 1
            INNER JOIN Role r ON r.RoleId = u.RoleId AND r.RoleName = 'Viewer'
            WHERE pc.PlantId = @PlantId
              AND c.IsActive = 1
              AND p.IsActive = 1
              AND (va.AllPlants = 1 OR EXISTS
                   (SELECT 1 FROM ViewerAccessPlant vap
                    WHERE vap.UserId = va.UserId AND vap.PlantId = pc.PlantId))
              AND (va.AllCustomers = 1
                   OR (va.AllPlants = 1 AND EXISTS
                       (SELECT 1 FROM ViewerAccessCustomer vac
                        WHERE vac.UserId = va.UserId AND vac.CustomerId = pc.CustomerId))
                   OR (va.AllPlants = 0 AND EXISTS
                       (SELECT 1 FROM ViewerAccessPlantCustomer vapc
                        WHERE vapc.UserId = va.UserId
                          AND vapc.PlantId = pc.PlantId
                          AND vapc.CustomerId = pc.CustomerId)))
            ORDER BY c.CustomerName;
            """, connection);
        Add(command, "@ViewerId", SqlDbType.Int, viewerId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            customers.Add(new ViewerCustomerResponse(reader.GetInt32(0), reader.GetString(1)));
        }

        return customers;
    }

    public async Task<ViewerProgressResponse> GetProgressAsync(
        int viewerId,
        int plantId,
        int? customerId,
        string process,
        DateTime reportDate,
        CancellationToken cancellationToken)
    {
        var lines = new List<ViewerLineProgress>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(AccessibleLines + """
            ,ReportRows AS
            (
                SELECT dr.*,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY dr.PlantId,dr.ReportDate,dr.CustomerId,dr.CategoryId,dr.LineId,dr.ModelId
                        ORDER BY CASE dr.Status WHEN 'APPROVED' THEN 0 WHEN 'SUBMITTED' THEN 1 ELSE 2 END,
                            COALESCE(dr.UpdatedAt,dr.CreatedAt) DESC,dr.DailyReportId DESC
                    ) AS RowNumber
                FROM DailyReport dr
                WHERE dr.PlantId=@PlantId
                  AND dr.ReportDate=@ReportDate
                  AND dr.IsActive=1
                  AND dr.ModelId IS NOT NULL
                  AND dr.Status<>'INACTIVE'
            )
            SELECT
                al.CustomerId,
                al.CustomerName,
                al.Process,
                al.LineId,
                al.LineName,
                al.SAPLocation,
                dr.ModelId,
                m.ModelName,
                dr.DailyReportId,
                dr.Status,
                COALESCE(kd.MonthPlan,dr.MonthPlan),
                COALESCE(kd.MonthActual,dr.MonthActual),
                COALESCE(kd.ProductionPlan,dr.ProductionPlan),
                COALESCE(kd.ProductionActual,dr.ProductionActual),
                COALESCE(kd.UPHTarget,dr.UPHTarget),
                COALESCE(kd.UPHActual,dr.UPHActual),
                COALESCE(kd.UPPHInstalled,dr.UPPHInstalled),
                COALESCE(kd.UPPHActual,dr.UPPHActual),
                COALESCE(kd.FPYTarget,dr.FPYTarget),
                COALESCE(kd.FPYActual,dr.FPYActual),
                COALESCE(kd.FTYTarget,dr.FTYTarget),
                COALESCE(kd.FTYActual,dr.FTYActual),
                COALESCE(kd.RTYTarget,dr.RTYTarget),
                COALESCE(kd.RTYActual,dr.RTYActual),
                COALESCE(kd.OSDReportingDateValue,dr.OSDReportingDateValue),
                COALESCE(kd.OSDReportingDatePercent,dr.OSDReportingDatePercent),
                COALESCE(kd.OSDMTDValue,dr.OSDMTDValue),
                COALESCE(kd.OSDMTDPercent,dr.OSDMTDPercent),
                COALESCE(kd.ActualOTManhours,dr.ActualOTManhours),
                COALESCE(kd.PlannedOTManhours,dr.PlannedOTManhours),
                COALESCE(kd.UnplannedOTManhours,dr.UnplannedOTManhours),
                COALESCE(kd.OpenWOQty,dr.OpenWOQty),
                COALESCE(kd.Over7DaysWOBalanceQty,dr.Over7DaysWOBalanceQty),
                COALESCE(kd.DailyTRCInQty,dr.DailyTRCInQty),
                COALESCE(kd.DailyTRCOutQty,dr.DailyTRCOutQty),
                COALESCE(kd.TRCOverallFailureInflowPercent,dr.TRCOverallFailureInflowPercent),
                COALESCE(kd.TRCLyingOver3DaysCr,dr.TRCLyingOver3DaysCr),
                COALESCE(kd.IssueDescription,dr.IssueDescription),
                COALESCE(kd.CPHTarget,dr.CPHTarget),
                COALESCE(kd.CPHActual,dr.CPHActual),
                COALESCE(kd.OSDTarget,dr.OSDTarget),
                COALESCE(kd.OTTarget,dr.OTTarget),
                dr.wip_lying_greater_3_days,
                dr.wip_lying_greater_7_days,
                COALESCE(kd.TextValuesJson,dr.TextValuesJson)
            FROM AccessibleLines al
            LEFT JOIN ReportRows dr
                ON dr.CustomerId = al.CustomerId
               AND dr.CategoryId = al.CategoryId
               AND dr.LineId = al.LineId
               AND dr.PlantId = @PlantId
               AND dr.ReportDate = @ReportDate
               AND dr.IsActive = 1
               AND dr.ModelId IS NOT NULL
               AND dr.RowNumber=1
            LEFT JOIN Model m ON m.ModelId = dr.ModelId
            LEFT JOIN KPI_Daily kd ON kd.DailyReportId=dr.DailyReportId
            ORDER BY
                CASE al.Process WHEN 'FATP' THEN 0 WHEN 'SMT' THEN 1 ELSE 2 END,
                al.CustomerName,
                al.LineName,
                m.ModelName;
            """, connection);

        AddScopeParameters(command, viewerId, plantId, customerId, process);
        Add(command, "@ReportDate", SqlDbType.Date, reportDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new ViewerLineProgress(
                CustomerId: reader.GetInt32(0),
                CustomerName: reader.GetString(1),
                Process: reader.GetString(2),
                LineId: reader.GetInt32(3),
                LineName: reader.GetString(4),
                SAPLocation: GetString(reader, 5),
                ModelId: GetInt(reader, 6),
                ModelName: GetString(reader, 7),
                DailyReportId: GetInt(reader, 8),
                Status: GetInt(reader, 8) is null ? "NOT_REPORTED" : reader.GetString(9),
                MonthPlan: GetDecimal(reader, 10),
                MonthActual: GetDecimal(reader, 11),
                ProductionPlan: GetDecimal(reader, 12),
                ProductionActual: GetDecimal(reader, 13),
                UPHTarget: GetDecimal(reader, 14),
                UPHActual: GetDecimal(reader, 15),
                UPPHInstalled: GetDecimal(reader, 16),
                UPPHActual: GetDecimal(reader, 17),
                FPYTarget: GetDecimal(reader, 18),
                FPYActual: GetDecimal(reader, 19),
                FTYTarget: GetDecimal(reader, 20),
                FTYActual: GetDecimal(reader, 21),
                RTYTarget: GetDecimal(reader, 22),
                RTYActual: GetDecimal(reader, 23),
                OSDReportingDateValue: GetDecimal(reader, 24),
                OSDReportingDatePercent: GetDecimal(reader, 25),
                OSDMTDValue: GetDecimal(reader, 26),
                OSDMTDPercent: GetDecimal(reader, 27),
                ActualOTManhours: GetDecimal(reader, 28),
                PlannedOTManhours: GetDecimal(reader, 29),
                UnplannedOTManhours: GetDecimal(reader, 30),
                OpenWOQty: GetInt(reader, 31),
                Over7DaysWOBalanceQty: GetInt(reader, 32),
                DailyTRCInQty: GetInt(reader, 33),
                DailyTRCOutQty: GetInt(reader, 34),
                TRCOverallFailureInflowPercent: GetDecimal(reader, 35),
                TRCLyingOver3DaysCr: GetDecimal(reader, 36),
                IssueDescription: GetString(reader, 37),
                CPHTarget: GetDecimal(reader, 38),
                CPHActual: GetDecimal(reader, 39),
                OSDTarget: GetDecimal(reader, 40),
                OTTarget: GetDecimal(reader, 41),
                WIPLyingGreater3Days: GetInt(reader, 42),
                WIPLyingGreater7Days: GetInt(reader, 43),
                TextValues: ParseTextValues(GetString(reader, 44))));
        }

        var totalPlan = lines.Sum(line => line.ProductionPlan ?? 0);
        var totalActual = lines.Sum(line => line.ProductionActual ?? 0);
        var summary = new ViewerSummary(
            totalPlan,
            totalActual,
            totalPlan > 0 ? decimal.Round(totalActual / totalPlan * 100, 1) : 0,
            lines.GroupBy(line => line.LineId)
                .Count(group => group.All(line => line.Status == "APPROVED")),
            lines.Select(line => line.LineId).Distinct().Count());

        return new ViewerProgressResponse(summary, lines);
    }

    public async Task<IReadOnlyList<ViewerTrendPoint>> GetTrendAsync(
        int viewerId,
        int plantId,
        int? customerId,
        string process,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var trend = new List<ViewerTrendPoint>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(AccessibleLines + """
            SELECT
                dr.ReportDate,
                COALESCE(SUM(dr.ProductionPlan), 0) AS ProductionPlan,
                COALESCE(SUM(dr.ProductionActual), 0) AS ProductionActual
            FROM AccessibleLines al
            INNER JOIN DailyReport dr
                ON dr.CustomerId = al.CustomerId
               AND dr.CategoryId = al.CategoryId
               AND dr.LineId = al.LineId
               AND dr.PlantId = @PlantId
               AND dr.ReportDate >= @StartDate
               AND dr.ReportDate <= @EndDate
               AND dr.IsActive = 1
               AND dr.ModelId IS NOT NULL
               AND dr.Status <> 'INACTIVE'
            GROUP BY dr.ReportDate
            ORDER BY dr.ReportDate;
            """, connection);

        AddScopeParameters(command, viewerId, plantId, customerId, process);
        Add(command, "@StartDate", SqlDbType.Date, startDate.Date);
        Add(command, "@EndDate", SqlDbType.Date, endDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            trend.Add(new ViewerTrendPoint(
                reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                reader.GetDecimal(1),
                reader.GetDecimal(2)));
        }

        return trend;
    }

    private static void AddScopeParameters(
        SqlCommand command,
        int viewerId,
        int plantId,
        int? customerId,
        string process)
    {
        Add(command, "@ViewerId", SqlDbType.Int, viewerId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);
        Add(command, "@CustomerId", SqlDbType.Int, customerId);
        Add(command, "@Process", SqlDbType.VarChar, process, 10);
    }

    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value,
        int? size = null)
    {
        var parameter = size is null
            ? command.Parameters.Add(name, type)
            : command.Parameters.Add(name, type, size.Value);
        parameter.Value = value ?? DBNull.Value;
    }

    private static decimal? GetDecimal(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static int? GetInt(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string? GetString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static IReadOnlyDictionary<string, string> ParseTextValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return values is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record ViewerSummary(
    decimal ProductionPlan,
    decimal ProductionActual,
    decimal Achievement,
    int CompletedLines,
    int TotalLines);

public sealed record ViewerLineProgress(
    int CustomerId,
    string CustomerName,
    string Process,
    int LineId,
    string LineName,
    string? SAPLocation,
    int? ModelId,
    string? ModelName,
    int? DailyReportId,
    string Status,
    decimal? MonthPlan,
    decimal? MonthActual,
    decimal? ProductionPlan,
    decimal? ProductionActual,
    decimal? UPHTarget,
    decimal? UPHActual,
    decimal? UPPHInstalled,
    decimal? UPPHActual,
    decimal? FPYTarget,
    decimal? FPYActual,
    decimal? FTYTarget,
    decimal? FTYActual,
    decimal? RTYTarget,
    decimal? RTYActual,
    decimal? OSDReportingDateValue,
    decimal? OSDReportingDatePercent,
    decimal? OSDMTDValue,
    decimal? OSDMTDPercent,
    decimal? ActualOTManhours,
    decimal? PlannedOTManhours,
    decimal? UnplannedOTManhours,
    int? OpenWOQty,
    int? Over7DaysWOBalanceQty,
    int? DailyTRCInQty,
    int? DailyTRCOutQty,
    decimal? TRCOverallFailureInflowPercent,
    decimal? TRCLyingOver3DaysCr,
    string? IssueDescription,
    decimal? CPHTarget,
    decimal? CPHActual,
    decimal? OSDTarget,
    decimal? OTTarget,
    int? WIPLyingGreater3Days,
    int? WIPLyingGreater7Days,
    IReadOnlyDictionary<string, string> TextValues)
{
    public decimal Achievement =>
        ProductionPlan is decimal plan && plan > 0
            ? decimal.Round(ProductionActual.GetValueOrDefault() / plan * 100, 1)
            : 0;
}

public sealed record ViewerProgressResponse(
    ViewerSummary Summary,
    IReadOnlyList<ViewerLineProgress> Lines);

public sealed record ViewerTrendPoint(
    string Date,
    decimal ProductionPlan,
    decimal ProductionActual);

public sealed record ViewerCustomerResponse(int CustomerId, string CustomerName);