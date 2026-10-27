using System.Data;
using System.Security.Claims;
using Dixon.CommandCenter.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Contributor")]
[Route("api/contributor")]
public sealed class ContributorController(SqlConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int days = 7, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(); if (userId <= 0) return Unauthorized();
        var plantId = GetPlantId(); if (plantId <= 0) return Unauthorized();
        days = Math.Clamp(days, 3, 30);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        const string summarySql = """
            SELECT
                SUM(CASE WHEN dr.Status='DRAFT' THEN 1 ELSE 0 END),
                SUM(CASE WHEN dr.Status='SUBMITTED' THEN 1 ELSE 0 END),
                SUM(CASE WHEN dr.Status='REJECTED' THEN 1 ELSE 0 END),
                SUM(CASE WHEN dr.Status='APPROVED' THEN 1 ELSE 0 END),
                COUNT(1)
            FROM DailyReport dr
            INNER JOIN UserCustomer uc ON uc.CustomerId = dr.CustomerId AND uc.UserId = @UserId
            INNER JOIN PlantCustomer pc ON pc.PlantId = dr.PlantId AND pc.CustomerId = dr.CustomerId
            WHERE dr.CreatedBy = @UserId
              AND dr.PlantId = @PlantId;
            """;
        await using var summary = new SqlCommand(summarySql, connection); Add(summary,"@UserId",SqlDbType.Int,userId); Add(summary,"@PlantId",SqlDbType.Int,plantId);
        await using var sr = await summary.ExecuteReaderAsync(cancellationToken);
        await sr.ReadAsync(cancellationToken);
        var counts = new { draft = GetInt(sr,0) ?? 0, submitted = GetInt(sr,1) ?? 0, rejected = GetInt(sr,2) ?? 0, approved = GetInt(sr,3) ?? 0, total = GetInt(sr,4) ?? 0 };
        await sr.CloseAsync();

        const string trendSql = """
            WITH Dates AS (
                SELECT CAST(DATEADD(day, -(@Days-1), CAST(GETDATE() AS date)) AS date) AS ReportDate
                UNION ALL
                SELECT DATEADD(day, 1, ReportDate) FROM Dates WHERE ReportDate < CAST(GETDATE() AS date)
            )
            SELECT d.ReportDate,
                   COUNT(CASE WHEN dr.Status='APPROVED' THEN 1 END) AS ApprovedReports,
                   COALESCE(SUM(CASE WHEN dr.Status='APPROVED' THEN kd.ProductionActual END),0) AS ProductionActual
            FROM Dates d
            LEFT JOIN DailyReport dr ON dr.ReportDate=d.ReportDate AND dr.CreatedBy=@UserId AND dr.PlantId=@PlantId
                AND EXISTS
                (
                    SELECT 1 FROM UserCustomer uc
                    INNER JOIN PlantCustomer pc ON pc.PlantId=@PlantId AND pc.CustomerId=uc.CustomerId
                    WHERE uc.UserId=@UserId AND uc.CustomerId=dr.CustomerId
                )
            LEFT JOIN KPI_Daily kd ON kd.DailyReportId=dr.DailyReportId
            GROUP BY d.ReportDate
            ORDER BY d.ReportDate OPTION (MAXRECURSION 31);
            """;
        await using var trend = new SqlCommand(trendSql, connection); Add(trend,"@UserId",SqlDbType.Int,userId); Add(trend,"@PlantId",SqlDbType.Int,plantId); Add(trend,"@Days",SqlDbType.Int,days);
        var trendRows=new List<object>(); await using var tr=await trend.ExecuteReaderAsync(cancellationToken);
        while(await tr.ReadAsync(cancellationToken)) trendRows.Add(new { date=tr.GetDateTime(0).ToString("yyyy-MM-dd"), approved=GetInt(tr,1)??0, production=GetDecimal(tr,2)??0 });
        await tr.CloseAsync();

        const string recentSql = """
            SELECT TOP (12) dr.DailyReportId, dr.ReportDate, c.CustomerName, cat.CategoryName, pl.LineName,
                   m.ModelName, dr.Status, dr.SubmittedAt, dr.RejectionReason
            FROM DailyReport dr
            INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
            INNER JOIN Customer c ON c.CustomerId=dr.CustomerId
            INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId
            INNER JOIN ProductionLine pl ON pl.LineId=dr.LineId
            LEFT JOIN Model m ON m.ModelId=dr.ModelId
            WHERE dr.CreatedBy=@UserId
              AND dr.PlantId=@PlantId
            ORDER BY COALESCE(dr.UpdatedAt,dr.CreatedAt) DESC;
            """;
        await using var recent=new SqlCommand(recentSql,connection); Add(recent,"@UserId",SqlDbType.Int,userId); Add(recent,"@PlantId",SqlDbType.Int,plantId);
        var recentRows=new List<object>(); await using var rr=await recent.ExecuteReaderAsync(cancellationToken);
        while(await rr.ReadAsync(cancellationToken)) recentRows.Add(new { dailyReportId=rr.GetInt32(0), reportDate=rr.GetDateTime(1).ToString("yyyy-MM-dd"), customerName=rr.GetString(2), categoryName=rr.GetString(3), lineName=rr.GetString(4), modelName=rr.IsDBNull(5)?null:rr.GetString(5), status=rr.GetString(6), submittedAt=rr.IsDBNull(7)?(DateTime?)null:rr.GetDateTime(7), rejectionReason=rr.IsDBNull(8)?null:rr.GetString(8) });
        return Ok(new { counts, trend=trendRows, recent=recentRows });
    }

    [HttpGet("submissions")]
    public async Task<IActionResult> Submissions([FromQuery] string? status = null, [FromQuery] int days = 60, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(); if (userId <= 0) return Unauthorized();
        var plantId = GetPlantId(); if (plantId <= 0) return Unauthorized();
        days = Math.Clamp(days, 7, 180);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = """
            SELECT TOP (250) dr.DailyReportId,dr.ReportDate,c.CustomerId,cat.CategoryId,c.CustomerName,cat.CategoryName,pl.LineName,pl.SAPLocation,
                   m.ModelName,dr.Status,dr.SubmittedAt,dr.UpdatedAt,dr.RejectionReason
            FROM DailyReport dr
            INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
            INNER JOIN Customer c ON c.CustomerId=dr.CustomerId
            INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId
            INNER JOIN ProductionLine pl ON pl.LineId=dr.LineId
            LEFT JOIN Model m ON m.ModelId=dr.ModelId
            WHERE dr.CreatedBy=@UserId
              AND dr.PlantId=@PlantId
              AND dr.IsActive=1
              AND dr.ModelId IS NOT NULL
              AND dr.ReportDate>=DATEADD(day,-@Days,CAST(GETDATE() AS date))
              AND (@Status IS NULL OR dr.Status=@Status)
            ORDER BY dr.ReportDate DESC,COALESCE(dr.UpdatedAt,dr.CreatedAt) DESC;
            """;
        await using var command=new SqlCommand(sql,connection); Add(command,"@UserId",SqlDbType.Int,userId);Add(command,"@PlantId",SqlDbType.Int,plantId);Add(command,"@Days",SqlDbType.Int,days);Add(command,"@Status",SqlDbType.VarChar,string.IsNullOrWhiteSpace(status)?null:status.Trim().ToUpperInvariant());
        var rows=new List<object>(); await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) rows.Add(new { dailyReportId=reader.GetInt32(0),reportDate=reader.GetDateTime(1).ToString("yyyy-MM-dd"),customerId=reader.GetInt32(2),categoryId=reader.GetInt32(3),customerName=reader.GetString(4),categoryName=reader.GetString(5),lineName=reader.GetString(6),sapLocation=reader.IsDBNull(7)?null:reader.GetString(7),modelName=reader.IsDBNull(8)?null:reader.GetString(8),status=reader.GetString(9),submittedAt=reader.IsDBNull(10)?(DateTime?)null:reader.GetDateTime(10),updatedAt=reader.IsDBNull(11)?(DateTime?)null:reader.GetDateTime(11),rejectionReason=reader.IsDBNull(12)?null:reader.GetString(12) });
        return Ok(rows);
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(
        [FromQuery] bool includeMissingReminder = false,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(); if (userId <= 0) return Unauthorized();
        var plantId = GetPlantId(); if (plantId <= 0) return Unauthorized();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        const string rejectedSql = """
            SELECT dr.DailyReportId, dr.ReportDate, c.CustomerName, cat.CategoryName,
                   pl.LineName, m.ModelName, dr.RejectionReason, aq.ReviewedAt
            FROM DailyReport dr
            INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
            INNER JOIN Customer c ON c.CustomerId=dr.CustomerId
            INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId
            INNER JOIN ProductionLine pl ON pl.LineId=dr.LineId
            LEFT JOIN Model m ON m.ModelId=dr.ModelId
            LEFT JOIN ApprovalQueue aq ON aq.DailyReportId=dr.DailyReportId
            WHERE dr.CreatedBy=@UserId AND dr.PlantId=@PlantId AND dr.Status='REJECTED'
            ORDER BY COALESCE(aq.ReviewedAt, dr.UpdatedAt, dr.CreatedAt) DESC;
            """;
        await using var rejectedCommand = new SqlCommand(rejectedSql, connection);
        Add(rejectedCommand,"@UserId",SqlDbType.Int,userId); Add(rejectedCommand,"@PlantId",SqlDbType.Int,plantId);
        var notifications = new List<object>();
        await using (var reader = await rejectedCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                notifications.Add(new {
                    id = $"rejected-{reader.GetInt32(0)}", type = "REJECTED",
                    reportDate = reader.GetDateTime(1).ToString("yyyy-MM-dd"),
                    customerName = reader.GetString(2), categoryName = reader.GetString(3),
                    lineName = reader.GetString(4), modelName = reader.IsDBNull(5) ? null : reader.GetString(5),
                    message = reader.IsDBNull(6) ? "Your report was rejected. Please review and resubmit it." : reader.GetString(6),
                    createdAt = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                    link = "/contributor/reports"
                });
            }
        }

        var reportingTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, reportingTimeZone);
        if (includeMissingReminder || localNow.TimeOfDay >= new TimeSpan(21, 0, 0))
        {
            var reportDate = localNow.Date.AddDays(-1);
            const string missingSql = """
                SELECT CASE WHEN EXISTS
                (
                    SELECT 1 FROM DailyReport dr
                    INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
                    INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
                    WHERE dr.CreatedBy=@UserId AND dr.PlantId=@PlantId AND dr.ReportDate=@ReportDate
                ) THEN 0 ELSE 1 END;
                """;
            await using var missingCommand = new SqlCommand(missingSql, connection);
            Add(missingCommand,"@UserId",SqlDbType.Int,userId); Add(missingCommand,"@PlantId",SqlDbType.Int,plantId);
            Add(missingCommand,"@ReportDate",SqlDbType.Date,reportDate);
            if (Convert.ToInt32(await missingCommand.ExecuteScalarAsync(cancellationToken)) == 1)
            {
                notifications.Add(new {
                    id = $"missing-{reportDate:yyyy-MM-dd}", type = "MISSING_REPORT",
                    reportDate = reportDate.ToString("yyyy-MM-dd"),
                    customerName = (string?)null, categoryName = (string?)null,
                    lineName = (string?)null, modelName = (string?)null,
                    message = "No report was created for this date. Please submit the missing daily report.",
                    createdAt = (DateTime?)null, link = "/contributor/brands"
                });
            }
        }

        return Ok(notifications);
    }

    private int GetUserId()=>int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:0;
    private int GetPlantId()=>int.TryParse(User.FindFirstValue("plantId"),out var plantId)?plantId:0;
    private static void Add(SqlCommand c,string n,SqlDbType t,object? v){var p=c.Parameters.Add(n,t);p.Value=v??DBNull.Value;}
    private static int? GetInt(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToInt32(r.GetValue(i));
    private static decimal? GetDecimal(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDecimal(r.GetValue(i));
}
