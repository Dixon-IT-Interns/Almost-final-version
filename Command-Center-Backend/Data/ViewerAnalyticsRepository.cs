using System.Data;
using Dixon.CommandCenter.API.Models;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Data;

public sealed class ViewerAnalyticsRepository(SqlConnectionFactory connectionFactory)
{
    private const string ScopedLinesCte = """
        WITH AccessibleLines AS
        (
            SELECT DISTINCT
                c.CustomerId,
                c.CustomerName,
                p.PlantName,
                cat.CategoryId,
                cat.CategoryName,
                pl.LineId,
                pl.LineName
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
              AND (@Process = 'ALL' OR cat.CategoryName = @Process)
        )
        """;

    private const string RankedRowsCte = """
        , RankedReports AS
        (
            SELECT dr.*,
                ROW_NUMBER() OVER
                (
                    PARTITION BY dr.PlantId, dr.ReportDate, dr.CustomerId,
                                 dr.CategoryId, dr.LineId, dr.ModelId
                    ORDER BY CASE dr.Status
                                 WHEN 'APPROVED' THEN 0
                                 WHEN 'SUBMITTED' THEN 1
                                 ELSE 2
                             END,
                             COALESCE(dr.UpdatedAt, dr.CreatedAt) DESC,
                             dr.DailyReportId DESC
                ) AS RowNumber
            FROM DailyReport dr
            INNER JOIN AccessibleLines al
                ON al.CustomerId = dr.CustomerId
               AND al.CategoryId = dr.CategoryId
               AND al.LineId = dr.LineId
            WHERE dr.PlantId = @PlantId
              AND dr.ReportDate >= @PreviousStartDate
              AND dr.ReportDate <= @EndDate
              AND dr.IsActive = 1
              AND dr.ModelId IS NOT NULL
              AND dr.Status <> 'INACTIVE'
              AND (@CustomerIds IS NULL OR EXISTS
                   (SELECT 1 FROM STRING_SPLIT(@CustomerIds, ',') ids
                    WHERE TRY_CONVERT(int, ids.value) = dr.CustomerId))
              AND (@CategoryIds IS NULL OR EXISTS
                   (SELECT 1 FROM STRING_SPLIT(@CategoryIds, ',') ids
                    WHERE TRY_CONVERT(int, ids.value) = dr.CategoryId))
              AND (@LineIds IS NULL OR EXISTS
                   (SELECT 1 FROM STRING_SPLIT(@LineIds, ',') ids
                    WHERE TRY_CONVERT(int, ids.value) = dr.LineId))
              AND (@ModelIds IS NULL OR EXISTS
                   (SELECT 1 FROM STRING_SPLIT(@ModelIds, ',') ids
                    WHERE TRY_CONVERT(int, ids.value) = dr.ModelId))
        )
        """;

    private const string CreateAnalyticsRowsSql = """
        SELECT
            rr.ReportDate,
            al.PlantName,
            rr.CustomerId,
            al.CustomerName,
            rr.CategoryId,
            al.CategoryName,
            rr.LineId,
            al.LineName,
            rr.ModelId,
            m.ModelName,
            COALESCE(kd.ProductionActual, rr.ProductionActual) AS ProductionActual,
            COALESCE(kd.ProductionPlan, rr.ProductionPlan) AS ProductionTarget,
            COALESCE(kd.FPYActual, rr.FPYActual) AS FpyActual,
            COALESCE(kd.FPYTarget, rr.FPYTarget) AS FpyTarget,
            COALESCE(kd.FTYActual, rr.FTYActual) AS FtyActual,
            COALESCE(kd.FTYTarget, rr.FTYTarget) AS FtyTarget,
            COALESCE(kd.RTYActual, rr.RTYActual) AS RtyActual,
            COALESCE(kd.RTYTarget, rr.RTYTarget) AS RtyTarget
        INTO #ViewerAnalyticsRows
        FROM RankedReports rr
        INNER JOIN AccessibleLines al
            ON al.CustomerId = rr.CustomerId
           AND al.CategoryId = rr.CategoryId
           AND al.LineId = rr.LineId
        LEFT JOIN Model m ON m.ModelId = rr.ModelId
        LEFT JOIN KPI_Daily kd ON kd.DailyReportId = rr.DailyReportId
        WHERE rr.RowNumber = 1;
        """;

    private static readonly IReadOnlyDictionary<string, string> Dimensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = "CONVERT(nvarchar(10), ReportDate, 23)",
            ["period"] = "CONVERT(nvarchar(7), ReportDate, 126)",
            ["plant"] = "PlantName",
            ["customer"] = "CustomerName",
            ["category"] = "CategoryName",
            ["line"] = "LineName",
            ["model"] = "ModelName"
        };

    private static readonly IReadOnlyDictionary<string, string> Metrics =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["productionActual"] = "ProductionActual",
            ["productionTarget"] = "ProductionTarget",
            ["achievement"] = "CASE WHEN ProductionTarget > 0 THEN ProductionActual * 100.0 / ProductionTarget END",
            ["fpyActual"] = "FpyActual",
            ["fpyTarget"] = "FpyTarget",
            ["ftyActual"] = "FtyActual",
            ["ftyTarget"] = "FtyTarget",
            ["rtyActual"] = "RtyActual",
            ["rtyTarget"] = "RtyTarget"
        };

    private static readonly IReadOnlyDictionary<string, string> ExplorerSortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = "ReportDate",
            ["customer"] = "CustomerName",
            ["category"] = "CategoryName",
            ["line"] = "LineName",
            ["model"] = "ModelName",
            ["productionActual"] = "ProductionActual",
            ["productionTarget"] = "ProductionTarget",
            ["fpy"] = "FpyActual",
            ["fty"] = "FtyActual",
            ["rty"] = "RtyActual"
        };

    public async Task<ViewerAnalyticsOverview> GetOverviewAsync(
        int viewerId,
        int plantId,
        IReadOnlyCollection<int> customerIds,
        IReadOnlyCollection<int> categoryIds,
        IReadOnlyCollection<int> lineIds,
        IReadOnlyCollection<int> modelIds,
        string process,
        DateTime startDate,
        DateTime endDate,
        DateTime previousStartDate,
        CancellationToken cancellationToken)
    {
        var sql = ScopedLinesCte + RankedRowsCte + CreateAnalyticsRowsSql + """
            SELECT
                COALESCE(SUM(CASE WHEN ReportDate BETWEEN @StartDate AND @EndDate THEN ProductionActual END), 0),
                COALESCE(SUM(CASE WHEN ReportDate BETWEEN @StartDate AND @EndDate THEN ProductionTarget END), 0),
                AVG(CASE WHEN ReportDate BETWEEN @StartDate AND @EndDate THEN FpyActual END),
                AVG(CASE WHEN ReportDate BETWEEN @StartDate AND @EndDate THEN FtyActual END),
                AVG(CASE WHEN ReportDate BETWEEN @StartDate AND @EndDate THEN RtyActual END),
                COUNT(DISTINCT CASE WHEN ReportDate BETWEEN @StartDate AND @EndDate THEN LineId END),
                COUNT(DISTINCT CASE WHEN ReportDate BETWEEN @StartDate AND @EndDate THEN ModelId END),
                COALESCE(SUM(CASE WHEN ReportDate < @StartDate THEN ProductionActual END), 0),
                COALESCE(SUM(CASE WHEN ReportDate < @StartDate THEN ProductionTarget END), 0),
                AVG(CASE WHEN ReportDate < @StartDate THEN FpyActual END),
                AVG(CASE WHEN ReportDate < @StartDate THEN FtyActual END),
                AVG(CASE WHEN ReportDate < @StartDate THEN RtyActual END),
                COUNT(DISTINCT CASE WHEN ReportDate < @StartDate THEN LineId END),
                COUNT(DISTINCT CASE WHEN ReportDate < @StartDate THEN ModelId END)
            FROM #ViewerAnalyticsRows;

            SELECT
                ReportDate,
                SUM(ProductionActual),
                SUM(ProductionTarget),
                AVG(FpyActual),
                AVG(FtyActual),
                AVG(RtyActual)
            FROM #ViewerAnalyticsRows
            WHERE ReportDate BETWEEN @StartDate AND @EndDate
            GROUP BY ReportDate
            ORDER BY ReportDate;

            SELECT
                CustomerId,
                CustomerName,
                SUM(ProductionActual),
                SUM(ProductionTarget),
                AVG(FpyActual),
                AVG(FtyActual),
                AVG(RtyActual)
            FROM #ViewerAnalyticsRows
            WHERE ReportDate BETWEEN @StartDate AND @EndDate
            GROUP BY CustomerId, CustomerName
            ORDER BY SUM(ProductionActual) DESC, CustomerName;

            SELECT
                CategoryId,
                CategoryName,
                SUM(ProductionActual),
                SUM(ProductionTarget),
                AVG(FpyActual),
                AVG(FtyActual),
                AVG(RtyActual)
            FROM #ViewerAnalyticsRows
            WHERE ReportDate BETWEEN @StartDate AND @EndDate
            GROUP BY CategoryId, CategoryName
            ORDER BY CategoryName;

            SELECT
                LineId,
                LineName,
                SUM(ProductionActual),
                SUM(ProductionTarget),
                AVG(FpyActual),
                AVG(FtyActual),
                AVG(RtyActual)
            FROM #ViewerAnalyticsRows
            WHERE ReportDate BETWEEN @StartDate AND @EndDate
            GROUP BY LineId, LineName
            HAVING SUM(ProductionTarget) > 0
            ORDER BY SUM(ProductionActual) * 100.0 / NULLIF(SUM(ProductionTarget), 0) DESC,
                     LineName;

            DROP TABLE #ViewerAnalyticsRows;
            """;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddFilterParameters(command, viewerId, plantId, customerIds, categoryIds, lineIds, modelIds, process);
        AddDateParameters(command, startDate, endDate, previousStartDate);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The analytics summary query returned no result.");
        }

        var current = new ViewerAnalyticsSummary(
            GetDecimal(reader, 0) ?? 0,
            GetDecimal(reader, 1) ?? 0,
            Achievement(GetDecimal(reader, 0), GetDecimal(reader, 1)),
            GetDecimal(reader, 2),
            GetDecimal(reader, 3),
            GetDecimal(reader, 4),
            reader.GetInt32(5),
            reader.GetInt32(6));
        var previous = new ViewerAnalyticsSummary(
            GetDecimal(reader, 7) ?? 0,
            GetDecimal(reader, 8) ?? 0,
            Achievement(GetDecimal(reader, 7), GetDecimal(reader, 8)),
            GetDecimal(reader, 9),
            GetDecimal(reader, 10),
            GetDecimal(reader, 11),
            reader.GetInt32(12),
            reader.GetInt32(13));

        var trend = new List<ViewerAnalyticsTrendPoint>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            trend.Add(new ViewerAnalyticsTrendPoint(
                reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                GetDecimal(reader, 1) ?? 0,
                GetDecimal(reader, 2) ?? 0,
                GetDecimal(reader, 3),
                GetDecimal(reader, 4),
                GetDecimal(reader, 5)));
        }

        var customers = await ReadBreakdownAsync(reader, cancellationToken);
        var categories = await ReadBreakdownAsync(reader, cancellationToken);
        var lines = await ReadBreakdownAsync(reader, cancellationToken);
        var sortedLines = lines
            .OrderByDescending(line => line.Achievement)
            .ThenBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ViewerAnalyticsOverview(
            current,
            previous,
            previousStartDate.ToString("yyyy-MM-dd"),
            startDate.AddDays(-1).ToString("yyyy-MM-dd"),
            trend,
            customers,
            categories,
            sortedLines.Take(5).ToArray(),
            sortedLines.Reverse().Take(5).ToArray());
    }

    public async Task<ViewerAnalyticsFilterOptions> GetFilterOptionsAsync(
        int viewerId,
        int plantId,
        IReadOnlyCollection<int> customerIds,
        IReadOnlyCollection<int> categoryIds,
        IReadOnlyCollection<int> lineIds,
        string process,
        CancellationToken cancellationToken)
    {
        var sql = ScopedLinesCte + """
            SELECT * INTO #ViewerAnalyticsAccessibleLines
            FROM AccessibleLines;

            SELECT DISTINCT CustomerId, CustomerName
            FROM #ViewerAnalyticsAccessibleLines
            ORDER BY CustomerName;

            SELECT DISTINCT CategoryId, CategoryName
            FROM #ViewerAnalyticsAccessibleLines
            WHERE @CustomerIds IS NULL OR EXISTS
                  (SELECT 1 FROM STRING_SPLIT(@CustomerIds, ',') ids
                   WHERE TRY_CONVERT(int, ids.value) = CustomerId)
            ORDER BY CategoryName;

            SELECT DISTINCT LineId, LineName
            FROM #ViewerAnalyticsAccessibleLines
            WHERE (@CustomerIds IS NULL OR EXISTS
                   (SELECT 1 FROM STRING_SPLIT(@CustomerIds, ',') ids
                    WHERE TRY_CONVERT(int, ids.value) = CustomerId))
              AND (@CategoryIds IS NULL OR EXISTS
                   (SELECT 1 FROM STRING_SPLIT(@CategoryIds, ',') ids
                    WHERE TRY_CONVERT(int, ids.value) = CategoryId))
            ORDER BY LineName;

            SELECT DISTINCT m.ModelId, m.ModelName
            FROM #ViewerAnalyticsAccessibleLines al
            INNER JOIN Model m ON m.CustomerId = al.CustomerId AND m.IsActive = 1
            WHERE (@CustomerIds IS NULL OR EXISTS
                   (SELECT 1 FROM STRING_SPLIT(@CustomerIds, ',') ids
                    WHERE TRY_CONVERT(int, ids.value) = al.CustomerId))
              AND (@CategoryIds IS NULL OR EXISTS
                   (SELECT 1 FROM STRING_SPLIT(@CategoryIds, ',') ids
                    WHERE TRY_CONVERT(int, ids.value) = al.CategoryId))
              AND (@LineIds IS NULL OR EXISTS
                   (SELECT 1 FROM STRING_SPLIT(@LineIds, ',') ids
                    WHERE TRY_CONVERT(int, ids.value) = al.LineId))
            ORDER BY m.ModelName;

            DROP TABLE #ViewerAnalyticsAccessibleLines;
            """;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddFilterParameters(command, viewerId, plantId, customerIds, categoryIds, lineIds, [], process);

        var customers = new List<ViewerAnalyticsOption>();
        var categories = new List<ViewerAnalyticsOption>();
        var lines = new List<ViewerAnalyticsOption>();
        var models = new List<ViewerAnalyticsOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            customers.Add(new ViewerAnalyticsOption(reader.GetInt32(0), reader.GetString(1)));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(new ViewerAnalyticsOption(reader.GetInt32(0), reader.GetString(1)));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new ViewerAnalyticsOption(reader.GetInt32(0), reader.GetString(1)));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            models.Add(new ViewerAnalyticsOption(reader.GetInt32(0), reader.GetString(1)));
        }

        return new ViewerAnalyticsFilterOptions(customers, categories, lines, models);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetChartDataAsync(
        int viewerId,
        int plantId,
        IReadOnlyCollection<int> customerIds,
        IReadOnlyCollection<int> categoryIds,
        IReadOnlyCollection<int> lineIds,
        IReadOnlyCollection<int> modelIds,
        string process,
        DateTime startDate,
        DateTime endDate,
        string dimension,
        string? groupBy,
        IReadOnlyCollection<string> metrics,
        string aggregation,
        CancellationToken cancellationToken)
    {
        if (!Dimensions.TryGetValue(dimension, out var dimensionSql))
        {
            throw new ArgumentException("Unsupported chart dimension.", nameof(dimension));
        }

        if (metrics.Count == 0 || metrics.Count > Metrics.Count
            || metrics.Any(metric => !Metrics.ContainsKey(metric)))
        {
            throw new ArgumentException("One or more chart metrics are unsupported.", nameof(metrics));
        }

        if (aggregation is not ("SUM" or "AVG" or "MIN" or "MAX" or "COUNT"))
        {
            throw new ArgumentException("Unsupported chart aggregation.", nameof(aggregation));
        }

        var seriesSql = string.Empty;
        var seriesSelect = string.Empty;
        var groupSql = dimensionSql;
        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            if (!Dimensions.TryGetValue(groupBy, out var groupBySql))
            {
                throw new ArgumentException("Unsupported chart grouping.", nameof(groupBy));
            }

            if (!string.Equals(groupBy, dimension, StringComparison.OrdinalIgnoreCase))
            {
                seriesSql = groupBySql;
                seriesSelect = $", {seriesSql} AS SeriesLabel";
                groupSql = $"{dimensionSql}, {seriesSql}";
            }
        }

        var metricSelect = string.Join(", ", metrics.Select(metric =>
        {
            if (string.Equals(metric, "achievement", StringComparison.OrdinalIgnoreCase)
                && aggregation == "SUM")
            {
                return "CONVERT(decimal(38, 4), SUM(ProductionActual) * 100.0 / NULLIF(SUM(ProductionTarget), 0)) AS [achievement]";
            }

            var valueSql = Metrics[metric];
            var aggregate = aggregation == "COUNT" ? "COUNT" : aggregation;
            return $"CONVERT(decimal(38, 4), {aggregate}({valueSql})) AS [{metric}]";
        }));
        var sql = ScopedLinesCte + RankedRowsCte + CreateAnalyticsRowsSql + $"""
            SELECT TOP (500)
                {dimensionSql} AS DimensionLabel
                {seriesSelect},
                {metricSelect}
            FROM #ViewerAnalyticsRows
            WHERE ReportDate BETWEEN @StartDate AND @EndDate
            GROUP BY {groupSql}
            ORDER BY {dimensionSql}{(seriesSql.Length > 0 ? $", {seriesSql}" : string.Empty)};

            DROP TABLE #ViewerAnalyticsRows;
            """;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddFilterParameters(command, viewerId, plantId, customerIds, categoryIds, lineIds, modelIds, process);
        AddDateParameters(command, startDate, endDate, startDate);

        var results = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["label"] = reader.IsDBNull(0) ? null : reader.GetString(0)
            };
            var metricStart = 1;
            if (seriesSql.Length > 0)
            {
                row["series"] = reader.IsDBNull(1) ? null : reader.GetString(1);
                metricStart = 2;
            }

            for (var index = 0; index < metrics.Count; index++)
            {
                row[metrics.ElementAt(index)] = GetDecimal(reader, metricStart + index);
            }

            results.Add(row);
        }

        return results;
    }

    public async Task<ViewerAnalyticsExplorerResponse> GetExplorerRowsAsync(
        int viewerId,
        int plantId,
        IReadOnlyCollection<int> customerIds,
        IReadOnlyCollection<int> categoryIds,
        IReadOnlyCollection<int> lineIds,
        IReadOnlyCollection<int> modelIds,
        string process,
        DateTime startDate,
        DateTime endDate,
        string? search,
        string sortBy,
        string sortDirection,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!ExplorerSortColumns.TryGetValue(sortBy, out var sortColumn))
        {
            throw new ArgumentException("Unsupported explorer sort column.", nameof(sortBy));
        }

        if (sortDirection is not ("ASC" or "DESC"))
        {
            throw new ArgumentException("Sort direction must be ASC or DESC.", nameof(sortDirection));
        }

        var orderBy = new List<string> { $"{sortColumn} {sortDirection}" };
        foreach (var tieBreaker in new[]
                 {
                     "ReportDate DESC",
                     "CustomerName ASC",
                     "CategoryName ASC",
                     "LineName ASC",
                     "ModelName ASC"
                 })
        {
            var column = tieBreaker.Split(' ')[0];
            if (!string.Equals(sortColumn, column, StringComparison.OrdinalIgnoreCase))
            {
                orderBy.Add(tieBreaker);
            }
        }

        var sql = ScopedLinesCte + RankedRowsCte + CreateAnalyticsRowsSql + $"""
            SELECT COUNT_BIG(1),
                   COALESCE(SUM(ProductionActual), 0),
                   COALESCE(SUM(ProductionTarget), 0),
                   AVG(FpyActual),
                   AVG(FtyActual),
                   AVG(RtyActual)
            FROM #ViewerAnalyticsRows
            WHERE @Search IS NULL
               OR CustomerName LIKE '%' + @Search + '%'
               OR CategoryName LIKE '%' + @Search + '%'
               OR LineName LIKE '%' + @Search + '%'
               OR ModelName LIKE '%' + @Search + '%'
               OR CONVERT(nvarchar(10), ReportDate, 23) LIKE '%' + @Search + '%';

            SELECT ReportDate, CustomerName, CategoryName, LineName, ModelName,
                   ProductionActual, ProductionTarget, FpyActual, FtyActual, RtyActual
            FROM #ViewerAnalyticsRows
            WHERE @Search IS NULL
               OR CustomerName LIKE '%' + @Search + '%'
               OR CategoryName LIKE '%' + @Search + '%'
               OR LineName LIKE '%' + @Search + '%'
               OR ModelName LIKE '%' + @Search + '%'
               OR CONVERT(nvarchar(10), ReportDate, 23) LIKE '%' + @Search + '%'
            ORDER BY {string.Join(", ", orderBy)}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

            DROP TABLE #ViewerAnalyticsRows;
            """;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddFilterParameters(command, viewerId, plantId, customerIds, categoryIds, lineIds, modelIds, process);
        AddDateParameters(command, startDate, endDate, startDate);
        Add(command, "@Search", SqlDbType.NVarChar, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), 120);
        Add(command, "@Offset", SqlDbType.Int, checked((page - 1) * pageSize));
        Add(command, "@PageSize", SqlDbType.Int, pageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The analytics explorer summary query returned no result.");
        }

        var summary = new ViewerAnalyticsExplorerSummary(
            reader.GetInt64(0),
            GetDecimal(reader, 1) ?? 0,
            GetDecimal(reader, 2) ?? 0,
            GetDecimal(reader, 3),
            GetDecimal(reader, 4),
            GetDecimal(reader, 5));

        var rows = new List<ViewerAnalyticsExplorerRow>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ViewerAnalyticsExplorerRow(
                reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                GetString(reader, 4) ?? string.Empty,
                GetDecimal(reader, 5),
                GetDecimal(reader, 6),
                GetDecimal(reader, 7),
                GetDecimal(reader, 8),
                GetDecimal(reader, 9)));
        }

        return new ViewerAnalyticsExplorerResponse(summary, rows, page, pageSize);
    }

    public async Task<IReadOnlyList<ViewerSavedView>> GetSavedViewsAsync(
        int viewerId,
        CancellationToken cancellationToken)
    {
        var views = new List<ViewerSavedView>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT ViewerAnalyticsSavedViewId, Name, ConfigurationJson, CreatedAt, UpdatedAt
            FROM ViewerAnalyticsSavedView
            WHERE UserId = @UserId
            ORDER BY UpdatedAt DESC, ViewerAnalyticsSavedViewId DESC;
            """, connection);
        Add(command, "@UserId", SqlDbType.Int, viewerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            views.Add(ReadSavedView(reader));
        }

        return views;
    }

    public async Task<ViewerSavedView> CreateSavedViewAsync(
        int viewerId,
        string name,
        string configurationJson,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            INSERT INTO ViewerAnalyticsSavedView (UserId, Name, ConfigurationJson)
            OUTPUT INSERTED.ViewerAnalyticsSavedViewId, INSERTED.Name,
                   INSERTED.ConfigurationJson, INSERTED.CreatedAt, INSERTED.UpdatedAt
            VALUES (@UserId, @Name, @ConfigurationJson);
            """, connection);
        Add(command, "@UserId", SqlDbType.Int, viewerId);
        Add(command, "@Name", SqlDbType.NVarChar, name, 100);
        Add(command, "@ConfigurationJson", SqlDbType.NVarChar, configurationJson, -1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The saved analytics view could not be created.");
        }

        return ReadSavedView(reader);
    }

    public async Task<bool> UpdateSavedViewAsync(
        int viewerId,
        int id,
        string name,
        string configurationJson,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE ViewerAnalyticsSavedView
            SET Name = @Name,
                ConfigurationJson = @ConfigurationJson,
                UpdatedAt = SYSUTCDATETIME()
            WHERE ViewerAnalyticsSavedViewId = @Id
              AND UserId = @UserId;
            """, connection);
        Add(command, "@UserId", SqlDbType.Int, viewerId);
        Add(command, "@Id", SqlDbType.Int, id);
        Add(command, "@Name", SqlDbType.NVarChar, name, 100);
        Add(command, "@ConfigurationJson", SqlDbType.NVarChar, configurationJson, -1);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> DeleteSavedViewAsync(
        int viewerId,
        int id,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            DELETE FROM ViewerAnalyticsSavedView
            WHERE ViewerAnalyticsSavedViewId = @Id
              AND UserId = @UserId;
            """, connection);
        Add(command, "@UserId", SqlDbType.Int, viewerId);
        Add(command, "@Id", SqlDbType.Int, id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<IReadOnlyList<ViewerAnalyticsBreakdown>> ReadBreakdownAsync(
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var values = new List<ViewerAnalyticsBreakdown>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var actual = GetDecimal(reader, 2) ?? 0;
            var target = GetDecimal(reader, 3) ?? 0;
            values.Add(new ViewerAnalyticsBreakdown(
                reader.GetInt32(0),
                reader.GetString(1),
                actual,
                target,
                Achievement(actual, target),
                GetDecimal(reader, 4),
                GetDecimal(reader, 5),
                GetDecimal(reader, 6)));
        }

        return values;
    }

    private static decimal Achievement(decimal? actual, decimal? target) =>
        target > 0 ? decimal.Round(actual.GetValueOrDefault() / target.Value * 100m, 2) : 0;

    private static void AddFilterParameters(
        SqlCommand command,
        int viewerId,
        int plantId,
        IReadOnlyCollection<int> customerIds,
        IReadOnlyCollection<int> categoryIds,
        IReadOnlyCollection<int> lineIds,
        IReadOnlyCollection<int> modelIds,
        string process)
    {
        Add(command, "@ViewerId", SqlDbType.Int, viewerId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);
        Add(command, "@CustomerIds", SqlDbType.VarChar, ToCsv(customerIds), 4000);
        Add(command, "@CategoryIds", SqlDbType.VarChar, ToCsv(categoryIds), 4000);
        Add(command, "@LineIds", SqlDbType.VarChar, ToCsv(lineIds), 4000);
        Add(command, "@ModelIds", SqlDbType.VarChar, ToCsv(modelIds), 4000);
        Add(command, "@Process", SqlDbType.VarChar, process, 10);
    }

    private static void AddDateParameters(
        SqlCommand command,
        DateTime startDate,
        DateTime endDate,
        DateTime previousStartDate)
    {
        Add(command, "@StartDate", SqlDbType.Date, startDate.Date);
        Add(command, "@EndDate", SqlDbType.Date, endDate.Date);
        Add(command, "@PreviousStartDate", SqlDbType.Date, previousStartDate.Date);
    }

    private static string? ToCsv(IReadOnlyCollection<int> values) =>
        values.Count == 0 ? null : string.Join(',', values);

    private static ViewerSavedView ReadSavedView(SqlDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            reader.GetDateTime(3), reader.GetDateTime(4));

    private static decimal? GetDecimal(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static string? GetString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

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
}
